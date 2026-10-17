using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Compaction;

/// <summary>
/// Configuration options for <see cref="ThinkingTraceCompactionChatClient"/>.
/// </summary>
public class ThinkingTraceCompactionOptions
{
    /// <summary>Number of recent assistant reasoning turns to retain verbatim. Default: 10.</summary>
    public int KeepVerbatimCount { get; set; } = 10;

    /// <summary>Maximum character length of the fallback deterministic reasoning stub. Default: 120.</summary>
    public int FallbackCharacterLimit { get; set; } = 120;

    /// <summary>Whether to truncate fallback text at the first newline if shorter than FallbackCharacterLimit. Default: true.</summary>
    public bool FallbackTruncateAtFirstLine { get; set; } = true;

    /// <summary>Format string for deterministic fallback stub. Default: "[Reasoned: {0}...]"</summary>
    public string FallbackFormat { get; set; } = "[Reasoned: {0}...]";

    /// <summary>
    /// When true, modifies the passed ChatMessage list and instances in-place (zero-allocations).
    /// When false (default), returns a shallow copy with modified messages, ensuring caller's
    /// conversation history remains pristine and immutable.
    /// </summary>
    public bool MutateInPlace { get; set; } = false;

    /// <summary>Optional background SLM summarizer.</summary>
    public ISpeculativeSummarizer? Summarizer { get; set; }

    /// <summary>Resolves the session id used to scope summaries. (default: ChatOptions.ConversationId, then "default").</summary>
    public Func<ChatOptions?, string>? SessionIdResolver { get; set; }

    /// <summary>Optional compaction telemetry instance to collect compaction events.</summary>
    public CompactionTelemetry? Telemetry { get; set; }

    /// <summary>Optional callback invoked whenever a thinking trace compaction event occurs.</summary>
    public Action<CompactionEvent>? OnCompaction { get; set; }
}

/// <summary>
/// IChatClient middleware compacting historical <see cref="TextReasoningContent"/> outside the active turn window.
/// </summary>
public class ThinkingTraceCompactionChatClient : DelegatingChatClient
{
    private readonly ThinkingTraceCompactionOptions _options;

    public ThinkingTraceCompactionChatClient(IChatClient innerClient, ThinkingTraceCompactionOptions? options = null)
        : base(innerClient)
    {
        _options = options ?? new ThinkingTraceCompactionOptions();
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(ThinkingTraceCompactionChatClient)) return this;
        if (serviceType == typeof(CompactionTelemetry) && _options.Telemetry != null) return _options.Telemetry;
        return base.GetService(serviceType, serviceKey);
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sessionId = CompactionItemKeyResolver.ResolveSessionId(options, _options.SessionIdResolver);
        var msgList = chatMessages as IList<ChatMessage> ?? chatMessages.ToList();

        // Enqueue from original uncompacted messages before compaction
        TriggerSpeculativeEnqueue(msgList, sessionId);

        var effectiveMessages = CompactMessages(msgList, sessionId);

        var response = await base.GetResponseAsync(effectiveMessages, options, cancellationToken);

        if (response?.Messages != null)
        {
            EnqueueResponseReasoning(response.Messages, sessionId);
        }

        return response!;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionId = CompactionItemKeyResolver.ResolveSessionId(options, _options.SessionIdResolver);
        var msgList = chatMessages as IList<ChatMessage> ?? chatMessages.ToList();

        // Enqueue from original uncompacted messages before compaction
        TriggerSpeculativeEnqueue(msgList, sessionId);

        var effectiveMessages = CompactMessages(msgList, sessionId);

        await foreach (var update in base.GetStreamingResponseAsync(effectiveMessages, options, cancellationToken))
        {
            yield return update;
        }
    }

    private IEnumerable<ChatMessage> CompactMessages(IEnumerable<ChatMessage> messages, string sessionId)
    {
        var msgList = messages as IList<ChatMessage> ?? messages.ToList();

        // 1. Identify assistant messages containing TextReasoningContent
        var reasoningIndices = new List<int>();
        for (int i = 0; i < msgList.Count; i++)
        {
            var msg = msgList[i];
            if (msg.Role == ChatRole.Assistant && msg.Contents.OfType<TextReasoningContent>().Any())
            {
                reasoningIndices.Add(i);
            }
        }

        if (reasoningIndices.Count <= _options.KeepVerbatimCount)
        {
            return msgList;
        }

        // Indices outside the protected verbatim window (oldest first)
        var toCompact = new HashSet<int>(reasoningIndices.Take(reasoningIndices.Count - _options.KeepVerbatimCount));

        if (_options.MutateInPlace)
        {
            foreach (var idx in toCompact)
            {
                CompactMessageInPlace(msgList[idx], sessionId);
            }
            return msgList;
        }
        else
        {
            var result = new List<ChatMessage>(msgList.Count);
            for (int i = 0; i < msgList.Count; i++)
            {
                var msg = msgList[i];
                if (toCompact.Contains(i))
                {
                    result.Add(CloneAndCompactMessage(msg, sessionId));
                }
                else
                {
                    result.Add(msg);
                }
            }
            return result;
        }
    }

    private void CompactMessageInPlace(ChatMessage message, string sessionId)
    {
        var reasoningList = message.Contents.OfType<TextReasoningContent>().ToList();
        if (reasoningList.Count == 0) return;

        var rawReasoning = string.Concat(reasoningList.Select(r => r.Text));
        var itemKey = CompactionItemKeyResolver.ResolveItemKey(message, SummarizationTarget.ThinkingMonologue, rawReasoning);

        var compactedText = ResolveCompactedText(rawReasoning, sessionId, itemKey);

        // Remove existing TextReasoningContent items
        for (int i = message.Contents.Count - 1; i >= 0; i--)
        {
            if (message.Contents[i] is TextReasoningContent)
            {
                message.Contents.RemoveAt(i);
            }
        }

        message.Contents.Add(new TextReasoningContent(compactedText));
    }

    private ChatMessage CloneAndCompactMessage(ChatMessage message, string sessionId)
    {
        var reasoningList = message.Contents.OfType<TextReasoningContent>().ToList();
        if (reasoningList.Count == 0) return message;

        var rawReasoning = string.Concat(reasoningList.Select(r => r.Text));
        var itemKey = CompactionItemKeyResolver.ResolveItemKey(message, SummarizationTarget.ThinkingMonologue, rawReasoning);

        var compactedText = ResolveCompactedText(rawReasoning, sessionId, itemKey);

        var newContents = new List<AIContent>(message.Contents.Count);
        newContents.Add(new TextReasoningContent(compactedText));

        foreach (var c in message.Contents)
        {
            if (c is not TextReasoningContent)
            {
                newContents.Add(c);
            }
        }

        return new ChatMessage(message.Role, newContents)
        {
            AuthorName = message.AuthorName,
            RawRepresentation = message.RawRepresentation,
            AdditionalProperties = message.AdditionalProperties != null
                ? new AdditionalPropertiesDictionary(message.AdditionalProperties)
                : null
        };
    }

    private string ResolveCompactedText(string rawReasoning, string sessionId, string itemKey)
    {
        // Try speculative summary
        if (_options.Summarizer != null && _options.Summarizer.TryGetSummary(sessionId, itemKey, out var summary) && !string.IsNullOrWhiteSpace(summary))
        {
            var trimmed = summary.Trim();
            var compacted = (trimmed.StartsWith("[THINKING_SUMMARY]", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("[Reasoned Summary:", StringComparison.OrdinalIgnoreCase))
                ? trimmed
                : $"[Reasoned Summary: {trimmed}]";

            RecordCompactionEvent(sessionId, itemKey, rawReasoning.Length, compacted.Length, wasSpeculativeHit: true, CompactionMethods.SemanticSummary);
            return compacted;
        }

        // Deterministic fallback stub
        var firstLine = rawReasoning.Trim();
        if (_options.FallbackTruncateAtFirstLine)
        {
            var newlineIdx = firstLine.IndexOfAny(['\r', '\n']);
            if (newlineIdx >= 0)
            {
                firstLine = firstLine.Substring(0, newlineIdx).Trim();
            }
        }

        if (firstLine.Length > _options.FallbackCharacterLimit)
        {
            int maxChars = Math.Max(0, _options.FallbackCharacterLimit - 3);
            firstLine = firstLine.Substring(0, maxChars) + "...";
        }

        var fallback = string.Format(_options.FallbackFormat, firstLine);
        RecordCompactionEvent(sessionId, itemKey, rawReasoning.Length, fallback.Length, wasSpeculativeHit: false, CompactionMethods.DeterministicStub);
        return fallback;
    }

    private void RecordCompactionEvent(string sessionId, string itemKey, int origChars, int compactedChars, bool wasSpeculativeHit, string method)
    {
        if (_options.Telemetry == null && _options.OnCompaction == null) return;
        var evt = new CompactionEvent(
            DateTimeOffset.UtcNow,
            sessionId,
            SummarizationTarget.ThinkingMonologue,
            itemKey,
            origChars,
            compactedChars,
            wasSpeculativeHit,
            method);
        _options.Telemetry?.Record(evt);
        _options.OnCompaction?.Invoke(evt);
    }

    private void TriggerSpeculativeEnqueue(IList<ChatMessage> msgList, string sessionId)
    {
        if (_options.Summarizer == null) return;

        var reasoningMsgs = msgList.Where(m => m.Role == ChatRole.Assistant && m.Contents.OfType<TextReasoningContent>().Any()).ToList();

        // Enqueue items approaching or outside boundary
        int threshold = Math.Max(0, reasoningMsgs.Count - _options.KeepVerbatimCount);
        for (int i = 0; i <= threshold && i < reasoningMsgs.Count; i++)
        {
            var msg = reasoningMsgs[i];
            var raw = string.Concat(msg.Contents.OfType<TextReasoningContent>().Select(r => r.Text));
            if (!string.IsNullOrEmpty(raw))
            {
                var itemKey = CompactionItemKeyResolver.ResolveItemKey(msg, SummarizationTarget.ThinkingMonologue, raw);
                _options.Summarizer.EnqueueSummarization(sessionId, itemKey, SummarizationTarget.ThinkingMonologue, raw);
            }
        }
    }

    private void EnqueueResponseReasoning(IEnumerable<ChatMessage> messages, string sessionId)
    {
        if (_options.Summarizer == null) return;

        foreach (var msg in messages.Where(m => m.Role == ChatRole.Assistant && m.Contents.OfType<TextReasoningContent>().Any()))
        {
            var raw = string.Concat(msg.Contents.OfType<TextReasoningContent>().Select(r => r.Text));
            if (!string.IsNullOrEmpty(raw))
            {
                var itemKey = CompactionItemKeyResolver.ResolveItemKey(msg, SummarizationTarget.ThinkingMonologue, raw);
                _options.Summarizer.EnqueueSummarization(sessionId, itemKey, SummarizationTarget.ThinkingMonologue, raw);
            }
        }
    }
}
