using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Transcripts;

/// <summary>
/// Controls how reasoning traces (thoughts) are formatted in OpenAI JSONL transcripts.
/// </summary>
public enum OpenAiJsonlReasoningMode
{
    /// <summary>Reasoning traces are omitted (default, conforming strictly to standard OpenAI chat/fine-tuning format).</summary>
    None = 0,

    /// <summary>Emits a separate "reasoning_content" property on assistant messages (used by DeepSeek / vLLM / Ollama OpenAI-compatible endpoints).</summary>
    ReasoningContent = 1,

    /// <summary>Prepends &lt;think&gt;...&lt;/think&gt; blocks to the message text content (used by Qwen and DeepSeek-R1 fine-tuning datasets).</summary>
    ThinkTags = 2
}

/// <summary>
/// Configuration options for <see cref="OpenAiJsonlTranscriptChatClient"/>.
/// </summary>
public class OpenAiJsonlTranscriptOptions
{
    /// <summary>Target file path where completed conversation JSONL lines are appended.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Path to the staging temp file used during the conversation.
    /// If null or whitespace, a unique temp file is automatically generated.
    /// In the event of an unhandled crash, this file remains on disk for post-mortem forensics.
    /// </summary>
    public string? TempFilePath { get; set; }

    /// <summary>
    /// Whether to delete the staging temp file once the conversation has been successfully flushed to <see cref="FilePath"/>.
    /// Default: true (leaves the temp file only if a crash/failure prevented completion).
    /// </summary>
    public bool DeleteTempFileOnSuccess { get; set; } = true;

    /// <summary>Whether to embed system instructions in the messages array. Default: true.</summary>
    public bool IncludeSystemPrompt { get; set; } = true;

    /// <summary>Whether to embed declared tool definitions in a root "tools" field in the JSONL line. Default: false.</summary>
    public bool IncludeTools { get; set; } = false;

    /// <summary>Controls how reasoning traces (thoughts) are emitted. Default: None.</summary>
    public OpenAiJsonlReasoningMode ReasoningMode { get; set; } = OpenAiJsonlReasoningMode.None;

    /// <summary>Optional redaction hook applied to every text before it is recorded (secrets, tokens, PII).</summary>
    public Func<string, string>? Redactor { get; set; }

    /// <summary>
    /// Whether to automatically flush and append the conversation line to <see cref="FilePath"/> on disposal.
    /// Default: true.
    /// </summary>
    public bool AutoSaveOnDispose { get; set; } = true;
}

/// <summary>
/// Alias options for <see cref="JsonlTranscriptChatClient"/>.
/// </summary>
public class JsonlTranscriptOptions : OpenAiJsonlTranscriptOptions
{
}

/// <summary>
/// IChatClient middleware capturing verbatim multi-turn conversations into single-line OpenAI JSONL dataset format.
/// Employs write-ahead temp-file staging: on each turn, inputs are streamed and flushed to disk prior to invocation
/// for crash forensics. When the conversation ends, the temp file is atomically appended as a single JSONL line
/// to the master dataset file.
/// Fully Native AOT compatible and trim-safe with SemaphoreSlim-based asynchronous I/O and flat O(1) memory overhead.
/// </summary>
public class OpenAiJsonlTranscriptChatClient : DelegatingChatClient, IAsyncDisposable
{
    private static readonly JavaScriptEncoder RelaxedEncoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    private static readonly SemaphoreSlim FileAppendSemaphore = new(1, 1);

    private readonly OpenAiJsonlTranscriptOptions _options;
    private readonly SemaphoreSlim _turnSemaphore = new(1, 1);
    private readonly object _stateLock = new();
    private readonly Dictionary<string, string> _toolCallNames = new(StringComparer.Ordinal);
    private string? _resolvedTempFilePath;
    private string? _systemPrompt;
    private IList<AITool>? _tools;
    private bool _isDirty;
    private bool _disposed;

    private sealed record ToolCallRecord(string Id, string Name, string ArgumentsJson);

    public OpenAiJsonlTranscriptChatClient(IChatClient innerClient, string filePath, string? tempFilePath = null)
        : this(innerClient, new OpenAiJsonlTranscriptOptions { FilePath = filePath, TempFilePath = tempFilePath })
    {
    }

    public OpenAiJsonlTranscriptChatClient(IChatClient innerClient, OpenAiJsonlTranscriptOptions? options = null)
        : base(innerClient)
    {
        _options = options ?? new OpenAiJsonlTranscriptOptions();
    }

    /// <summary>
    /// Gets the options configured for this transcript client.
    /// </summary>
    public OpenAiJsonlTranscriptOptions Options => _options;

    /// <summary>
    /// Gets the resolved temporary staging file path used for write-ahead crash forensics.
    /// </summary>
    public string TempFilePath => GetOrCreateTempFilePath();

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(OpenAiJsonlTranscriptChatClient) ||
            serviceType == typeof(JsonlTranscriptChatClient))
        {
            return this;
        }

        return base.GetService(serviceType, serviceKey);
    }

    private string GetOrCreateTempFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_resolvedTempFilePath))
        {
            return _resolvedTempFilePath;
        }

        lock (_stateLock)
        {
            if (!string.IsNullOrWhiteSpace(_resolvedTempFilePath))
            {
                return _resolvedTempFilePath;
            }

            if (!string.IsNullOrWhiteSpace(_options.TempFilePath))
            {
                _resolvedTempFilePath = _options.TempFilePath;
            }
            else if (!string.IsNullOrWhiteSpace(_options.FilePath))
            {
                var dir = Path.GetDirectoryName(_options.FilePath);
                var fileName = Path.GetFileNameWithoutExtension(_options.FilePath);
                var ext = Path.GetExtension(_options.FilePath);
                var tempName = $"{fileName}.{Guid.NewGuid():N}.tmp";
                _resolvedTempFilePath = string.IsNullOrEmpty(dir) ? tempName : Path.Combine(dir, tempName);
            }
            else
            {
                _resolvedTempFilePath = Path.Combine(Path.GetTempPath(), $"pmad_staging_{Guid.NewGuid():N}.tmp");
            }

            return _resolvedTempFilePath;
        }
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await _turnSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tempPath = GetOrCreateTempFilePath();
            EnsureDirectory(tempPath);

            // 1. Stage input chat messages to temp file and flush before invocation for crash forensics
            var (fileStream, writer) = OpenAndBeginStaging(tempPath, chatMessages, options);

            try
            {
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);

                ChatResponse response;
                try
                {
                    response = await base.GetResponseAsync(chatMessages, options, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Close scopes so temp file remains valid JSON on unhandled exception
                    await CloseScopesSafelyAsync(writer, fileStream).ConfigureAwait(false);
                    throw;
                }

                // 2. Append assistant response, close JSON, and flush
                if (response.Messages != null)
                {
                    foreach (var msg in response.Messages)
                    {
                        WriteAssistantMessage(writer, msg);
                    }
                }

                await FinishStagingAsync(writer, fileStream, cancellationToken).ConfigureAwait(false);
                lock (_stateLock)
                {
                    _isDirty = true;
                }

                return response;
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
                await fileStream.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _turnSemaphore.Release();
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await _turnSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        var tempPath = GetOrCreateTempFilePath();
        EnsureDirectory(tempPath);

        // 1. Stage input chat messages to temp file and flush before invocation for crash forensics
        var (fileStream, writer) = OpenAndBeginStaging(tempPath, chatMessages, options);

        var textBuilder = new StringBuilder();
        var reasoningBuilder = new StringBuilder();
        var toolCalls = new List<ToolCallRecord>();

        IAsyncEnumerator<ChatResponseUpdate> enumerator;
        try
        {
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);

            enumerator = base.GetStreamingResponseAsync(chatMessages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
        }
        catch
        {
            await CloseScopesSafelyAsync(writer, fileStream).ConfigureAwait(false);
            await writer.DisposeAsync().ConfigureAwait(false);
            await fileStream.DisposeAsync().ConfigureAwait(false);
            _turnSemaphore.Release();
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
                    await CloseScopesSafelyAsync(writer, fileStream).ConfigureAwait(false);
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
                            var callId = fcc.CallId ?? string.Empty;
                            lock (_stateLock)
                            {
                                _toolCallNames[callId] = fcc.Name;
                            }
                            toolCalls.Add(new ToolCallRecord(callId, fcc.Name, argsJson));
                        }
                    }
                }

                yield return update;
            }

            // 2. Append completed streaming assistant turn, close JSON, and flush
            WriteAssistantPayload(writer, textBuilder.ToString(), reasoningBuilder.ToString(), toolCalls);
            await FinishStagingAsync(writer, fileStream, cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                _isDirty = true;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
            await writer.DisposeAsync().ConfigureAwait(false);
            await fileStream.DisposeAsync().ConfigureAwait(false);
            _turnSemaphore.Release();
        }
    }

    private (FileStream Stream, Utf8JsonWriter Writer) OpenAndBeginStaging(
        string tempPath,
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options)
    {
        lock (_stateLock)
        {
            if (_systemPrompt == null && options?.Instructions != null)
            {
                _systemPrompt = options.Instructions;
            }
            if (_tools == null && options?.Tools != null && options.Tools.Count > 0)
            {
                _tools = options.Tools;
            }
        }

        var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
        var writer = new Utf8JsonWriter(fileStream, new JsonWriterOptions
        {
            Encoder = RelaxedEncoder,
            Indented = false
        });

        writer.WriteStartObject();
        writer.WriteStartArray("messages");

        // 1. System Prompt
        if (_options.IncludeSystemPrompt)
        {
            string? systemPrompt = null;
            lock (_stateLock)
            {
                systemPrompt = _systemPrompt;
            }

            if (string.IsNullOrEmpty(systemPrompt))
            {
                var sysMsg = chatMessages.FirstOrDefault(m => m.Role == ChatRole.System);
                if (sysMsg != null)
                {
                    systemPrompt = sysMsg.Text;
                }
            }

            if (!string.IsNullOrEmpty(systemPrompt))
            {
                writer.WriteStartObject();
                writer.WriteString("role", "system");
                writer.WriteString("content", Redact(systemPrompt));
                writer.WriteEndObject();
            }
        }

        // 2. Prior Conversation Messages
        foreach (var msg in chatMessages)
        {
            if (msg.Role == ChatRole.System)
            {
                continue;
            }

            if (msg.Role == ChatRole.User)
            {
                writer.WriteStartObject();
                writer.WriteString("role", "user");
                writer.WriteString("content", Redact(msg.Text ?? string.Empty));
                writer.WriteEndObject();
            }
            else if (msg.Role == ChatRole.Assistant)
            {
                WriteAssistantMessage(writer, msg);
            }
            else if (msg.Role == ChatRole.Tool || msg.Contents.OfType<FunctionResultContent>().Any())
            {
                bool wroteResult = false;
                foreach (var frc in msg.Contents.OfType<FunctionResultContent>())
                {
                    var callId = frc.CallId ?? string.Empty;
                    string? toolName = null;
                    lock (_stateLock)
                    {
                        if (_toolCallNames.TryGetValue(callId, out var mappedName))
                        {
                            toolName = mappedName;
                        }
                    }
                    if (string.IsNullOrEmpty(toolName))
                    {
                        toolName = msg.AuthorName ?? "tool";
                    }

                    var resStr = frc.Result as string ?? frc.Result?.ToString() ?? string.Empty;

                    writer.WriteStartObject();
                    writer.WriteString("role", "tool");
                    writer.WriteString("tool_call_id", callId);
                    writer.WriteString("name", toolName);
                    writer.WriteString("content", Redact(resStr));
                    writer.WriteEndObject();
                    wroteResult = true;
                }

                if (!wroteResult && msg.Role == ChatRole.Tool && !string.IsNullOrEmpty(msg.Text))
                {
                    writer.WriteStartObject();
                    writer.WriteString("role", "tool");
                    writer.WriteString("tool_call_id", string.Empty);
                    writer.WriteString("name", msg.AuthorName ?? "tool");
                    writer.WriteString("content", Redact(msg.Text));
                    writer.WriteEndObject();
                }
            }
        }

        return (fileStream, writer);
    }

    private void WriteAssistantMessage(Utf8JsonWriter writer, ChatMessage msg)
    {
        var text = msg.Text;
        var reasoning = string.Join("\n", msg.Contents.OfType<TextReasoningContent>().Select(r => r.Text).Where(t => !string.IsNullOrEmpty(t)));
        var toolCalls = new List<ToolCallRecord>();

        foreach (var fcc in msg.Contents.OfType<FunctionCallContent>())
        {
            var args = FormatArgumentsJson(fcc.Arguments);
            var callId = fcc.CallId ?? string.Empty;
            lock (_stateLock)
            {
                _toolCallNames[callId] = fcc.Name;
            }
            toolCalls.Add(new ToolCallRecord(callId, fcc.Name, args));
        }

        WriteAssistantPayload(writer, text, reasoning, toolCalls);
    }

    private void WriteAssistantPayload(
        Utf8JsonWriter writer,
        string? text,
        string? reasoning,
        IReadOnlyList<ToolCallRecord>? toolCalls)
    {
        writer.WriteStartObject();
        writer.WriteString("role", "assistant");

        if (_options.ReasoningMode == OpenAiJsonlReasoningMode.ReasoningContent && !string.IsNullOrEmpty(reasoning))
        {
            writer.WriteString("reasoning_content", Redact(reasoning));
        }

        string? content = text;
        if (_options.ReasoningMode == OpenAiJsonlReasoningMode.ThinkTags && !string.IsNullOrEmpty(reasoning))
        {
            content = $"<think>\n{Redact(reasoning)}\n</think>\n" + (content ?? string.Empty);
        }

        if (toolCalls != null && toolCalls.Count > 0 && string.IsNullOrEmpty(content))
        {
            writer.WriteNull("content");
        }
        else
        {
            writer.WriteString("content", string.IsNullOrEmpty(content) ? string.Empty : Redact(content));
        }

        if (toolCalls != null && toolCalls.Count > 0)
        {
            writer.WriteStartArray("tool_calls");
            foreach (var tc in toolCalls)
            {
                writer.WriteStartObject();
                writer.WriteString("id", tc.Id);
                writer.WriteString("type", "function");
                writer.WriteStartObject("function");
                writer.WriteString("name", tc.Name);
                writer.WriteString("arguments", tc.ArgumentsJson);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private async Task FinishStagingAsync(Utf8JsonWriter writer, FileStream stream, CancellationToken cancellationToken)
    {
        writer.WriteEndArray(); // messages

        IList<AITool>? tools;
        lock (_stateLock)
        {
            tools = _tools;
        }

        if (_options.IncludeTools && tools != null && tools.Count > 0)
        {
            writer.WriteStartArray("tools");
            foreach (var tool in tools)
            {
                if (tool is AIFunction fn)
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", "function");
                    writer.WriteStartObject("function");
                    writer.WriteString("name", fn.Name);
                    if (!string.IsNullOrEmpty(fn.Description))
                    {
                        writer.WriteString("description", fn.Description);
                    }
                    writer.WritePropertyName("parameters");
                    writer.WriteRawValue(fn.JsonSchema.GetRawText(), true);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();
        }

        writer.WriteEndObject(); // root object
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task CloseScopesSafelyAsync(Utf8JsonWriter writer, FileStream stream)
    {
        try
        {
            while (writer.CurrentDepth > 0)
            {
                if (writer.CurrentDepth == 2)
                {
                    writer.WriteEndArray();
                }
                else
                {
                    writer.WriteEndObject();
                }
            }
            await writer.FlushAsync().ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best effort to preserve forensic file on disk
        }
    }

    /// <summary>
    /// Asynchronously returns the current serialized single-line JSONL representation of the conversation from the staging file.
    /// </summary>
    public async Task<string> ToJsonlAsync(CancellationToken cancellationToken = default)
    {
        var tempPath = GetOrCreateTempFilePath();
        if (File.Exists(tempPath))
        {
            await using var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        return "{\"messages\":[]}";
    }


    /// <summary>
    /// Flushes the complete conversation from the staging temp file by appending it as a single line
    /// to <see cref="OpenAiJsonlTranscriptOptions.FilePath"/>.
    /// Concurrency-safe across multiple concurrent conversations and threads using SemaphoreSlim.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        string? tempFile;
        string masterFile;

        lock (_stateLock)
        {
            if (!_isDirty || string.IsNullOrWhiteSpace(_resolvedTempFilePath) || !File.Exists(_resolvedTempFilePath))
            {
                return;
            }

            tempFile = _resolvedTempFilePath;
            masterFile = _options.FilePath;
            _isDirty = false;
        }

        if (!string.IsNullOrWhiteSpace(masterFile))
        {
            EnsureDirectory(masterFile);

            await FileAppendSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var masterFs = new FileStream(masterFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
                await using var tempFs = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
                await tempFs.CopyToAsync(masterFs, cancellationToken).ConfigureAwait(false);
                masterFs.WriteByte((byte)'\n');
                await masterFs.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                FileAppendSemaphore.Release();
            }
        }

        if (_options.DeleteTempFileOnSuccess && !string.IsNullOrEmpty(tempFile))
        {
            try
            {
                File.Delete(tempFile);
            }
            catch
            {
                // Best effort
            }
        }
    }


    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
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
                bool firstElem = true;
                foreach (var item in enumerable)
                {
                    if (!firstElem) sb.Append(", ");
                    firstElem = false;
                    FormatObjectAsJson(item, sb);
                }
                sb.Append(']');
                break;
            default:
                sb.Append('"').Append(value.ToString()?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "").Append('"');
                break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_options.AutoSaveOnDispose && !string.IsNullOrWhiteSpace(_options.FilePath))
        {
            try
            {
                await FlushAsync().ConfigureAwait(false);
            }
            catch
            {
                // Suppress I/O errors during disposal
            }
        }

        _turnSemaphore.Dispose();

        GC.SuppressFinalize(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing && _options.AutoSaveOnDispose && !string.IsNullOrWhiteSpace(_options.FilePath))
        {
            try
            {
                FlushAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Suppress I/O errors during disposal
            }
        }

        if (disposing)
        {
            _turnSemaphore.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Alias for <see cref="OpenAiJsonlTranscriptChatClient"/>.
/// </summary>
public class JsonlTranscriptChatClient : OpenAiJsonlTranscriptChatClient
{
    public JsonlTranscriptChatClient(IChatClient innerClient, string filePath, string? tempFilePath = null)
        : base(innerClient, filePath, tempFilePath)
    {
    }

    public JsonlTranscriptChatClient(IChatClient innerClient, OpenAiJsonlTranscriptOptions? options = null)
        : base(innerClient, options)
    {
    }
}
