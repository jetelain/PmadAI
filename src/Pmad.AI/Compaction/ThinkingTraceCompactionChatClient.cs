using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Compaction;

/// <summary>
/// Configuration options for <see cref="ThinkingTraceCompactor"/> and <see cref="ThinkingTraceCompactionChatClient"/>.
/// </summary>
public class ThinkingTraceCompactionOptions : CompactionOptionsBase
{
    public ThinkingTraceCompactionOptions()
    {
        KeepVerbatimCount = 5;
        FallbackCharacterLimit = 1500;
        FallbackPrefixLength = 1000;
        FallbackSuffixLength = 400;
    }

    /// <summary>Optional background SLM summarizer.</summary>
    public ISpeculativeSummarizer? Summarizer { get; set; }

}

/// <summary>
/// Conversation history optimizer that trims verbose reasoning traces outside the active turn window.
/// </summary>
public class ThinkingTraceCompactor
{
    private readonly ThinkingTraceCompactionOptions _options;

    public ThinkingTraceCompactor(ThinkingTraceCompactionOptions? options = null)
    {
        _options = options ?? new ThinkingTraceCompactionOptions();
        if (_options.FallbackCharacterLimit < _options.FallbackPrefixLength + _options.FallbackSuffixLength)
        {
            throw new ArgumentException($"FallbackCharacterLimit ({_options.FallbackCharacterLimit}) must be >= FallbackPrefixLength ({_options.FallbackPrefixLength}) + FallbackSuffixLength ({_options.FallbackSuffixLength}).");
        }
    }

    public ThinkingTraceCompactor(
        ISpeculativeSummarizer? summarizer = null,
        int keepVerbatimCount = 5,
        int fallbackCharacterLimit = 1500,
        int fallbackPrefixLength = 1000,
        int fallbackSuffixLength = 400)
    {
        if (fallbackCharacterLimit < fallbackPrefixLength + fallbackSuffixLength)
        {
            throw new ArgumentException($"fallbackCharacterLimit ({fallbackCharacterLimit}) must be >= fallbackPrefixLength ({fallbackPrefixLength}) + fallbackSuffixLength ({fallbackSuffixLength}).");
        }

        _options = new ThinkingTraceCompactionOptions
        {
            Summarizer = summarizer,
            KeepVerbatimCount = keepVerbatimCount,
            FallbackPrefixLength = fallbackPrefixLength,
            FallbackSuffixLength = fallbackSuffixLength,
            FallbackCharacterLimit = fallbackCharacterLimit
        };
    }

    /// <summary>
    /// Compacts conversation history, pruning verbose reasoning traces outside the active turn window.
    /// </summary>
    public IList<ChatMessage> Compact(IList<ChatMessage> messages, string? sessionId = null)
    {
        var effectiveSessionId = sessionId ?? "default";

        // Identify assistant messages containing TextReasoningContent
        var reasoningIndices = new List<int>();
        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            if (msg.Role == ChatRole.Assistant && msg.Contents.OfType<TextReasoningContent>().Any())
            {
                reasoningIndices.Add(i);
            }
        }

        if (reasoningIndices.Count <= _options.KeepVerbatimCount)
        {
            return messages;
        }

        var toCompact = new HashSet<int>(reasoningIndices.Take(reasoningIndices.Count - _options.KeepVerbatimCount));

        if (_options.MutateInPlace)
        {
            foreach (var idx in toCompact)
            {
                CompactMessageInPlace(messages[idx], effectiveSessionId);
            }
            return messages;
        }
        else
        {
            var result = new List<ChatMessage>(messages.Count);
            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                if (toCompact.Contains(i))
                {
                    result.Add(CloneAndCompactMessage(msg, effectiveSessionId));
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
        if (string.IsNullOrEmpty(rawReasoning) || rawReasoning.Length <= _options.FallbackCharacterLimit) return;

        var itemKey = CompactionItemKeyResolver.ResolveItemKey(message, CompactionTarget.ThinkingMonologue, rawReasoning);

        var compactedText = ResolveCompactedResult(rawReasoning, sessionId, itemKey);

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
        if (string.IsNullOrEmpty(rawReasoning) || rawReasoning.Length <= _options.FallbackCharacterLimit) return message;

        var itemKey = CompactionItemKeyResolver.ResolveItemKey(message, CompactionTarget.ThinkingMonologue, rawReasoning);

        var compactedText = ResolveCompactedResult(rawReasoning, sessionId, itemKey);

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

    private string ResolveCompactedResult(string rawReasoning, string sessionId, string itemKey)
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

        // Deterministic prefix + truncation marker + suffix
        var truncatedChars = rawReasoning.Length - _options.FallbackPrefixLength - _options.FallbackSuffixLength;
        var marker = string.Format(_options.FallbackTruncationMarker, truncatedChars);
        var fallback = string.Concat(
            rawReasoning.AsSpan(0, _options.FallbackPrefixLength),
            marker.AsSpan(),
            rawReasoning.AsSpan(rawReasoning.Length - _options.FallbackSuffixLength, _options.FallbackSuffixLength));

        RecordCompactionEvent(sessionId, itemKey, rawReasoning.Length, fallback.Length, wasSpeculativeHit: false, CompactionMethods.PrefixSuffixTruncation);
        return fallback;
    }

    private void RecordCompactionEvent(string sessionId, string itemKey, int origChars, int compactedChars, bool wasSpeculativeHit, string method)
    {
        if (_options.Telemetry == null && _options.OnCompaction == null) return;
        var evt = new CompactionEvent(
            DateTimeOffset.UtcNow,
            sessionId,
            CompactionTarget.ThinkingMonologue,
            itemKey,
            origChars,
            compactedChars,
            wasSpeculativeHit,
            method);
        _options.Telemetry?.Record(evt);
        _options.OnCompaction?.Invoke(evt);
    }
}

/// <summary>
/// IChatClient middleware compacting historical <see cref="TextReasoningContent"/> outside the active turn window.
/// </summary>
public class ThinkingTraceCompactionChatClient : DelegatingChatClient
{
    private readonly ThinkingTraceCompactionOptions _options;
    private readonly ThinkingTraceCompactor _compactor;

    public ThinkingTraceCompactionChatClient(IChatClient innerClient, ThinkingTraceCompactionOptions? options = null)
        : base(innerClient)
    {
        _options = options ?? new ThinkingTraceCompactionOptions();
        _compactor = new ThinkingTraceCompactor(_options);
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(ThinkingTraceCompactionChatClient)) return this;
        if (serviceType == typeof(ThinkingTraceCompactor)) return _compactor;
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

        var effectiveMessages = _compactor.Compact(msgList, sessionId);

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

        var effectiveMessages = _compactor.Compact(msgList, sessionId);

        System.Text.StringBuilder? reasoningBuilder = null;
        await foreach (var update in base.GetStreamingResponseAsync(effectiveMessages, options, cancellationToken))
        {
            if (_options.Summarizer != null && update.Contents != null)
            {
                foreach (var content in update.Contents)
                {
                    if (content is TextReasoningContent trc && !string.IsNullOrEmpty(trc.Text))
                    {
                        reasoningBuilder ??= new System.Text.StringBuilder();
                        reasoningBuilder.Append(trc.Text);
                    }
                }
            }
            yield return update;
        }

        if (reasoningBuilder != null && reasoningBuilder.Length > _options.FallbackCharacterLimit)
        {
            var raw = reasoningBuilder.ToString();
            var syntheticMsg = new ChatMessage(ChatRole.Assistant, [new TextReasoningContent(raw)]);
            var itemKey = CompactionItemKeyResolver.ResolveItemKey(syntheticMsg, CompactionTarget.ThinkingMonologue, raw);
            _options.Summarizer!.EnqueueSummarization(sessionId, itemKey, raw);
        }
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
            if (!string.IsNullOrEmpty(raw) && raw.Length > _options.FallbackCharacterLimit)
            {
                var itemKey = CompactionItemKeyResolver.ResolveItemKey(msg, CompactionTarget.ThinkingMonologue, raw);
                _options.Summarizer.EnqueueSummarization(sessionId, itemKey, raw);
            }
        }
    }

    private void EnqueueResponseReasoning(IEnumerable<ChatMessage> messages, string sessionId)
    {
        if (_options.Summarizer == null) return;

        foreach (var msg in messages.Where(m => m.Role == ChatRole.Assistant && m.Contents.OfType<TextReasoningContent>().Any()))
        {
            var raw = string.Concat(msg.Contents.OfType<TextReasoningContent>().Select(r => r.Text));
            if (!string.IsNullOrEmpty(raw) && raw.Length > _options.FallbackCharacterLimit)
            {
                var itemKey = CompactionItemKeyResolver.ResolveItemKey(msg, CompactionTarget.ThinkingMonologue, raw);
                _options.Summarizer.EnqueueSummarization(sessionId, itemKey, raw);
            }
        }
    }
}
