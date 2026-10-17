using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Transcripts;

/// <summary>
/// Configuration options for <see cref="VerbatimTranscriptChatClient"/>.
/// </summary>
public class VerbatimTranscriptOptions
{
    /// <summary>Directory where markdown transcripts are automatically saved.</summary>
    public string? OutputDirectory { get; set; }

    /// <summary>Direct file path override for the transcript markdown file.</summary>
    public string? FilePath { get; set; }

    /// <summary>Optional session or scenario title embedded into the markdown header.</summary>
    public string? SessionTitle { get; set; }

    /// <summary>Whether to embed system instructions in a collapsible details section.</summary>
    public bool IncludeSystemPrompt { get; set; } = true;

    /// <summary>Whether to embed declared tool definitions and JSON schemas.</summary>
    public bool IncludeToolSchemas { get; set; } = true;

    /// <summary>Whether to record turn latencies and ISO timestamps.</summary>
    public bool IncludeTimestamps { get; set; } = true;

    /// <summary>Whether to summarize input, output, and total token usage.</summary>
    public bool IncludeTokenUsage { get; set; } = true;

    /// <summary>
    /// Whether to automatically write the markdown file on disposal.
    /// The client implements <see cref="IAsyncDisposable"/> (preferred); synchronous Dispose performs a blocking flush.
    /// </summary>
    public bool AutoSaveOnDispose { get; set; } = true;

    /// <summary>
    /// When true, flushes and writes the transcript to disk after each completed turn.
    /// Recommended for long-running agent workflows to protect against process crashes.
    /// Default: false (saves on dispose or explicit call to minimize disk I/O).
    /// </summary>
    public bool FlushAfterEachTurn { get; set; } = false;

    /// <summary>Optional callback invoked whenever a transcript is saved or updated.</summary>
    public Action<string>? OnTranscriptGenerated { get; set; }

    /// <summary>Suffix added to the file name (e.g. "audit" / "wire") so two instances in one pipeline never write the same file.</summary>
    public string? Label { get; set; }

    /// <summary>Resolves the session a request belongs to (default: ChatOptions.ConversationId, then "default").</summary>
    public Func<ChatOptions?, string>? SessionIdResolver { get; set; }

    /// <summary>Upper bound on in-memory transcript size per session; oldest tool-result bodies are elided beyond it. Default: 5,000,000 characters.</summary>
    public int MaxTranscriptCharacters { get; set; } = 5_000_000;

    /// <summary>Optional redaction hook applied to every text before it is recorded (secrets, tokens, PII).</summary>
    public Func<string, string>? Redactor { get; set; }
}

/// <summary>
/// IChatClient middleware capturing verbatim turns, developer/system instructions, reasoning traces,
/// tool calls/results, timestamps, and token usage into standalone GitHub-Flavored Markdown transcripts.
/// Trim-safe and Native AOT compatible.
/// </summary>
public class VerbatimTranscriptChatClient : DelegatingChatClient, IAsyncDisposable
{
    private readonly VerbatimTranscriptOptions _options;
    private readonly ConcurrentDictionary<string, SessionTranscriptState> _sessions = new(StringComparer.Ordinal);
    private bool _disposed;

    private sealed class SessionTranscriptState
    {
        public readonly object Lock = new();
        public string? SystemPrompt;
        public string? ToolSchemasJson;
        public readonly List<TranscriptTurn> Turns = [];
        public long InputTokens;
        public long OutputTokens;
        public long TotalTokens;
        public int TurnCounter;
        public int TotalEstimatedChars;
    }

    private sealed record TranscriptTurn(
        int TurnNumber,
        DateTimeOffset Timestamp,
        ChatRole Role,
        string? Text,
        string? Reasoning,
        IReadOnlyList<ToolCallRecord>? ToolCalls,
        IReadOnlyList<ToolResultRecord>? ToolResults,
        long DurationMs);

    private sealed record ToolCallRecord(string Id, string Name, string ArgumentsJson);
    private sealed record ToolResultRecord(string CallId, string ResultText);

    public VerbatimTranscriptChatClient(IChatClient innerClient, VerbatimTranscriptOptions? options = null)
        : base(innerClient)
    {
        _options = options ?? new VerbatimTranscriptOptions();
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sessionId = ResolveSessionId(options);
        var state = GetOrCreateState(sessionId);

        CapturePristineInputs(state, chatMessages, options);

        var sw = Stopwatch.StartNew();
        ChatResponse response;
        try
        {
            response = await base.GetResponseAsync(chatMessages, options, cancellationToken);
        }
        finally
        {
            sw.Stop();
        }

        CaptureResponse(state, response, sw.ElapsedMilliseconds);

        if (_options.FlushAfterEachTurn)
        {
            await SaveTranscriptAsync(null, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionId = ResolveSessionId(options);
        var state = GetOrCreateState(sessionId);

        CapturePristineInputs(state, chatMessages, options);

        var sw = Stopwatch.StartNew();
        var reasoningBuilder = new StringBuilder();
        var textBuilder = new StringBuilder();
        var toolCalls = new List<ToolCallRecord>();
        UsageDetails? finalUsage = null;

        IAsyncEnumerator<ChatResponseUpdate> enumerator;
        try
        {
            enumerator = base.GetStreamingResponseAsync(chatMessages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
        }
        catch
        {
            sw.Stop();
            throw;
        }

        try
        {
            while (true)
            {
                ChatResponseUpdate update;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }
                    update = enumerator.Current;
                }
                catch
                {
                    sw.Stop();
                    throw;
                }

                if (!string.IsNullOrEmpty(update.Text))
                {
                    textBuilder.Append(update.Text);
                }

                if (update.Contents != null)
                {
                    foreach (var content in update.Contents)
                    {
                        if (content is TextReasoningContent reasoning && !string.IsNullOrEmpty(reasoning.Text))
                        {
                            reasoningBuilder.Append(reasoning.Text);
                        }
                        else if (content is FunctionCallContent fcc)
                        {
                            var argsJson = FormatArgumentsJson(fcc.Arguments);
                            toolCalls.Add(new ToolCallRecord(fcc.CallId ?? string.Empty, fcc.Name, argsJson));
                        }
                        else if (content is UsageContent uc && uc.Details != null)
                        {
                            finalUsage = uc.Details;
                        }
                    }
                }

                yield return update;
            }
        }
        finally
        {
            sw.Stop();
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        CaptureStreamingTurn(state, textBuilder.ToString(), reasoningBuilder.ToString(), toolCalls, finalUsage, sw.ElapsedMilliseconds);

        if (_options.FlushAfterEachTurn)
        {
            await SaveTranscriptAsync(null, cancellationToken).ConfigureAwait(false);
        }
    }

    private string ResolveSessionId(ChatOptions? options)
    {
        if (_options.SessionIdResolver != null)
        {
            var resolved = _options.SessionIdResolver(options);
            if (!string.IsNullOrWhiteSpace(resolved)) return resolved;
        }

        if (!string.IsNullOrWhiteSpace(options?.ConversationId))
        {
            return options.ConversationId;
        }

        if (options?.AdditionalProperties != null &&
            options.AdditionalProperties.TryGetValue("pmad.sessionId", out var sid) &&
            sid != null)
        {
            return sid.ToString() ?? "default";
        }

        return "default";
    }

    private SessionTranscriptState GetOrCreateState(string sessionId)
    {
        return _sessions.GetOrAdd(sessionId, _ => new SessionTranscriptState());
    }

    private void CapturePristineInputs(SessionTranscriptState state, IEnumerable<ChatMessage> chatMessages, ChatOptions? options)
    {
        lock (state.Lock)
        {
            if (state.SystemPrompt == null && options?.Instructions != null)
            {
                state.SystemPrompt = Redact(options.Instructions);
            }

            if (state.ToolSchemasJson == null && options?.Tools != null && options.Tools.Count > 0)
            {
                state.ToolSchemasJson = FormatToolSchemas(options.Tools);
            }

            int existingUserTurns = state.Turns.Count(t => t.Role == ChatRole.User);
            int existingToolTurns = state.Turns.Count(t => t.Role == ChatRole.Tool);

            int userIndex = 0;
            int toolIndex = 0;

            foreach (var msg in chatMessages)
            {
                if (msg.Role == ChatRole.System)
                {
                    state.SystemPrompt ??= Redact(msg.Text ?? string.Empty);
                    continue;
                }

                if (msg.Role == ChatRole.User)
                {
                    if (userIndex < existingUserTurns)
                    {
                        userIndex++;
                        continue;
                    }

                    userIndex++;
                    state.TurnCounter++;
                    var turn = new TranscriptTurn(
                        state.TurnCounter,
                        DateTimeOffset.UtcNow,
                        ChatRole.User,
                        Redact(msg.Text ?? string.Empty),
                        null,
                        null,
                        null,
                        0);
                    AddTurn(state, turn);
                }
                else if (msg.Role == ChatRole.Tool || msg.Contents.OfType<FunctionResultContent>().Any())
                {
                    if (toolIndex < existingToolTurns)
                    {
                        toolIndex++;
                        continue;
                    }

                    toolIndex++;
                    var results = new List<ToolResultRecord>();
                    foreach (var frc in msg.Contents.OfType<FunctionResultContent>())
                    {
                        var resStr = frc.Result as string ?? frc.Result?.ToString() ?? string.Empty;
                        results.Add(new ToolResultRecord(frc.CallId ?? string.Empty, Redact(resStr)));
                    }

                    if (results.Count == 0 && msg.Role == ChatRole.Tool && !string.IsNullOrEmpty(msg.Text))
                    {
                        results.Add(new ToolResultRecord(string.Empty, Redact(msg.Text)));
                    }

                    if (results.Count > 0)
                    {
                        state.TurnCounter++;
                        var turn = new TranscriptTurn(
                            state.TurnCounter,
                            DateTimeOffset.UtcNow,
                            ChatRole.Tool,
                            null,
                            null,
                            null,
                            results,
                            0);
                        AddTurn(state, turn);
                    }
                }
            }
        }
    }

    private void CaptureResponse(SessionTranscriptState state, ChatResponse response, long durationMs)
    {
        lock (state.Lock)
        {
            if (response.Usage != null)
            {
                state.InputTokens += response.Usage.InputTokenCount ?? 0;
                state.OutputTokens += response.Usage.OutputTokenCount ?? 0;
                state.TotalTokens += response.Usage.TotalTokenCount ?? ((response.Usage.InputTokenCount ?? 0) + (response.Usage.OutputTokenCount ?? 0));
            }

            if (response.Messages == null) return;

            foreach (var msg in response.Messages)
            {
                var text = msg.Text;
                var reasoning = string.Join("\n", msg.Contents.OfType<TextReasoningContent>().Select(r => r.Text).Where(t => !string.IsNullOrEmpty(t)));
                var toolCalls = new List<ToolCallRecord>();

                foreach (var fcc in msg.Contents.OfType<FunctionCallContent>())
                {
                    var args = FormatArgumentsJson(fcc.Arguments);
                    toolCalls.Add(new ToolCallRecord(fcc.CallId ?? string.Empty, fcc.Name, args));
                }

                state.TurnCounter++;
                var turn = new TranscriptTurn(
                    state.TurnCounter,
                    DateTimeOffset.UtcNow,
                    ChatRole.Assistant,
                    string.IsNullOrEmpty(text) ? null : Redact(text),
                    string.IsNullOrEmpty(reasoning) ? null : Redact(reasoning),
                    toolCalls.Count > 0 ? toolCalls : null,
                    null,
                    durationMs);
                AddTurn(state, turn);
            }
        }
    }

    private void CaptureStreamingTurn(
        SessionTranscriptState state,
        string text,
        string reasoning,
        IReadOnlyList<ToolCallRecord> toolCalls,
        UsageDetails? usage,
        long durationMs)
    {
        lock (state.Lock)
        {
            if (usage != null)
            {
                state.InputTokens += usage.InputTokenCount ?? 0;
                state.OutputTokens += usage.OutputTokenCount ?? 0;
                state.TotalTokens += usage.TotalTokenCount ?? ((usage.InputTokenCount ?? 0) + (usage.OutputTokenCount ?? 0));
            }

            state.TurnCounter++;
            var turn = new TranscriptTurn(
                state.TurnCounter,
                DateTimeOffset.UtcNow,
                ChatRole.Assistant,
                string.IsNullOrEmpty(text) ? null : Redact(text),
                string.IsNullOrEmpty(reasoning) ? null : Redact(reasoning),
                toolCalls.Count > 0 ? toolCalls : null,
                null,
                durationMs);
            AddTurn(state, turn);
        }
    }

    private void AddTurn(SessionTranscriptState state, TranscriptTurn turn)
    {
        state.Turns.Add(turn);
        int turnLength = (turn.Text?.Length ?? 0) + (turn.Reasoning?.Length ?? 0);
        if (turn.ToolResults != null)
        {
            foreach (var tr in turn.ToolResults) turnLength += tr.ResultText.Length;
        }
        state.TotalEstimatedChars += turnLength;

        if (state.TotalEstimatedChars > _options.MaxTranscriptCharacters)
        {
            for (int i = 0; i < state.Turns.Count && state.TotalEstimatedChars > _options.MaxTranscriptCharacters; i++)
            {
                var t = state.Turns[i];
                if (t.ToolResults != null && t.ToolResults.Count > 0)
                {
                    var elidedResults = t.ToolResults.Select(r =>
                        r.ResultText.Length > 200
                            ? new ToolResultRecord(r.CallId, r.ResultText[..200] + "\n... [Elided to maintain transcript memory bound] ...")
                            : r).ToList();
                    state.Turns[i] = t with { ToolResults = elidedResults };
                }
            }
        }
    }

    private string Redact(string text)
    {
        if (string.IsNullOrEmpty(text) || _options.Redactor == null)
        {
            return text;
        }
        return _options.Redactor(text);
    }

    private static string FormatToolSchemas(IList<AITool> tools)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[");
        for (int i = 0; i < tools.Count; i++)
        {
            var t = tools[i];
            if (i > 0) sb.AppendLine(",");
            if (t is AIFunction fn)
            {
                sb.Append("  {\n    \"name\": \"").Append(fn.Name).Append("\",\n");
                if (!string.IsNullOrEmpty(fn.Description))
                {
                    sb.Append("    \"description\": \"").Append(fn.Description.Replace("\"", "\\\"")).Append("\",\n");
                }
                sb.Append("    \"parameters\": ").Append(fn.JsonSchema.GetRawText()).Append("\n  }");
            }
            else
            {
                sb.Append("  { \"type\": \"").Append(t.GetType().Name).Append("\" }");
            }
        }
        sb.AppendLine("\n]");
        return sb.ToString();
    }

    private static string FormatArgumentsJson(IDictionary<string, object?>? arguments)
    {
        if (arguments == null || arguments.Count == 0) return "{}";
        var sb = new StringBuilder();
        FormatObjectAsJson(arguments, sb);
        return sb.ToString();
    }

    private static void FormatObjectAsJson(object? value, StringBuilder sb)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                break;
            case string s:
                sb.Append('"').Append(s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t")).Append('"');
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
            case JsonElement je:
                sb.Append(je.GetRawText());
                break;
            case JsonNode jn:
                sb.Append(jn.ToJsonString());
                break;
            case IDictionary<string, object?> dict:
                sb.Append('{');
                bool first = true;
                foreach (var kvp in dict)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append('"').Append(kvp.Key).Append("\": ");
                    FormatObjectAsJson(kvp.Value, sb);
                }
                sb.Append('}');
                break;
            case System.Collections.IEnumerable enumerable:
                sb.Append('[');
                bool firstItem = true;
                foreach (var item in enumerable)
                {
                    if (!firstItem) sb.Append(", ");
                    firstItem = false;
                    FormatObjectAsJson(item, sb);
                }
                sb.Append(']');
                break;
            default:
                sb.Append('"').Append(value.ToString()?.Replace("\"", "\\\"") ?? "").Append('"');
                break;
        }
    }

    /// <summary>
    /// Generates complete GitHub-flavored Markdown text of the recorded session.
    /// </summary>
    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        var sessions = _sessions.ToArray();

        foreach (var (sessionId, state) in sessions)
        {
            lock (state.Lock)
            {
                // Frontmatter
                sb.AppendLine("---");
                if (!string.IsNullOrEmpty(_options.SessionTitle))
                {
                    sb.AppendLine($"session_title: \"{_options.SessionTitle}\"");
                }
                sb.AppendLine($"session_id: \"{sessionId}\"");
                if (!string.IsNullOrEmpty(_options.Label))
                {
                    sb.AppendLine($"label: \"{_options.Label}\"");
                }
                if (_options.IncludeTimestamps)
                {
                    sb.AppendLine($"generated_at: {DateTimeOffset.UtcNow:O}");
                }
                sb.AppendLine($"turns_count: {state.Turns.Count}");
                if (_options.IncludeTokenUsage)
                {
                    sb.AppendLine($"input_tokens: {state.InputTokens}");
                    sb.AppendLine($"output_tokens: {state.OutputTokens}");
                    sb.AppendLine($"total_tokens: {state.TotalTokens}");
                }
                sb.AppendLine("---");
                sb.AppendLine();

                // Title
                var title = _options.SessionTitle ?? $"Session {sessionId}";
                sb.AppendLine($"# Transcript: {title}");
                sb.AppendLine();

                // System instructions and tool declarations collapsible section
                if ((_options.IncludeSystemPrompt && !string.IsNullOrEmpty(state.SystemPrompt)) ||
                    (_options.IncludeToolSchemas && !string.IsNullOrEmpty(state.ToolSchemasJson)))
                {
                    sb.AppendLine("<details>");
                    sb.AppendLine("<summary>⚙️ System Instructions & Declared Tools</summary>");
                    sb.AppendLine();

                    if (_options.IncludeSystemPrompt && !string.IsNullOrEmpty(state.SystemPrompt))
                    {
                        sb.AppendLine("#### System Instructions");
                        sb.AppendLine("````markdown");
                        sb.AppendLine(state.SystemPrompt);
                        sb.AppendLine("````");
                        sb.AppendLine();
                    }

                    if (_options.IncludeToolSchemas && !string.IsNullOrEmpty(state.ToolSchemasJson))
                    {
                        sb.AppendLine("#### Declared Tools & Schemas");
                        sb.AppendLine("````json");
                        sb.AppendLine(state.ToolSchemasJson);
                        sb.AppendLine("````");
                        sb.AppendLine();
                    }

                    sb.AppendLine("</details>");
                    sb.AppendLine();
                }

                // Turns
                foreach (var turn in state.Turns)
                {
                    var timeStr = _options.IncludeTimestamps ? $" - {turn.Timestamp:HH:mm:ss}" : "";

                    switch (turn.Role)
                    {
                        case ChatRole user when user == ChatRole.User:
                            sb.AppendLine($"### 👤 User (Turn {turn.TurnNumber}{timeStr})");
                            sb.AppendLine();
                            sb.AppendLine(turn.Text);
                            sb.AppendLine();
                            break;

                        case ChatRole tool when tool == ChatRole.Tool:
                            sb.AppendLine($"### 📥 Tool Results (Turn {turn.TurnNumber}{timeStr})");
                            sb.AppendLine();
                            if (turn.ToolResults != null)
                            {
                                foreach (var tr in turn.ToolResults)
                                {
                                    sb.AppendLine($"#### Result for `{tr.CallId}`");
                                    sb.AppendLine("````");
                                    sb.AppendLine(tr.ResultText);
                                    sb.AppendLine("````");
                                    sb.AppendLine();
                                }
                            }
                            break;

                        case ChatRole assistant when assistant == ChatRole.Assistant:
                        default:
                            var durStr = turn.DurationMs > 0 ? $" - {turn.DurationMs}ms" : "";
                            sb.AppendLine($"### 🤖 Assistant (Turn {turn.TurnNumber}{timeStr}{durStr})");
                            sb.AppendLine();

                            if (!string.IsNullOrEmpty(turn.Reasoning))
                            {
                                sb.AppendLine("> 💭 **Reasoning:**");
                                foreach (var line in turn.Reasoning.Split('\n'))
                                {
                                    sb.AppendLine($"> {line.TrimEnd()}");
                                }
                                sb.AppendLine();
                            }

                            if (!string.IsNullOrEmpty(turn.Text))
                            {
                                sb.AppendLine(turn.Text);
                                sb.AppendLine();
                            }

                            if (turn.ToolCalls != null && turn.ToolCalls.Count > 0)
                            {
                                foreach (var tc in turn.ToolCalls)
                                {
                                    var idPart = !string.IsNullOrEmpty(tc.Id) ? $" (id: `{tc.Id}`)" : "";
                                    sb.AppendLine($"#### 🛠️ Tool Call: `{tc.Name}`{idPart}");
                                    sb.AppendLine("````json");
                                    sb.AppendLine(tc.ArgumentsJson);
                                    sb.AppendLine("````");
                                    sb.AppendLine();
                                }
                            }
                            break;
                    }
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Flushes and saves the generated transcript to disk.
    /// </summary>
    public async Task<string> SaveTranscriptAsync(string? destinationPath = null, CancellationToken ct = default)
    {
        var markdown = ToMarkdown();

        string targetPath;
        if (!string.IsNullOrWhiteSpace(destinationPath))
        {
            targetPath = destinationPath;
        }
        else if (!string.IsNullOrWhiteSpace(_options.FilePath))
        {
            targetPath = _options.FilePath;
        }
        else
        {
            var dir = !string.IsNullOrWhiteSpace(_options.OutputDirectory)
                ? _options.OutputDirectory
                : Path.Combine(Directory.GetCurrentDirectory(), "docs", "transcripts");

            Directory.CreateDirectory(dir);

            var labelPart = !string.IsNullOrWhiteSpace(_options.Label) ? $"-{_options.Label}" : "";
            var titlePart = !string.IsNullOrWhiteSpace(_options.SessionTitle)
                ? $"-{SanitizeFileName(_options.SessionTitle)}"
                : "";

            var fileName = $"transcript{titlePart}{labelPart}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.md";
            targetPath = Path.Combine(dir, fileName);
        }

        var parentDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        await File.WriteAllTextAsync(targetPath, markdown, Encoding.UTF8, ct).ConfigureAwait(false);

        _options.OnTranscriptGenerated?.Invoke(markdown);

        return targetPath;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(invalid.Contains(c) || c == ' ' ? '-' : c);
        }
        return sb.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_options.AutoSaveOnDispose &&
            (!string.IsNullOrWhiteSpace(_options.OutputDirectory) || !string.IsNullOrWhiteSpace(_options.FilePath)))
        {
            try
            {
                await SaveTranscriptAsync().ConfigureAwait(false);
            }
            catch
            {
                // Suppress I/O errors during disposal
            }
        }

        GC.SuppressFinalize(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing && _options.AutoSaveOnDispose &&
            (!string.IsNullOrWhiteSpace(_options.OutputDirectory) || !string.IsNullOrWhiteSpace(_options.FilePath)))
        {
            try
            {
                SaveTranscriptAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Suppress I/O errors during disposal
            }
        }

        base.Dispose(disposing);
    }
}

