using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Compaction;

/// <summary>
/// Configuration options for <see cref="ToolResultCompactor"/> and <see cref="ToolResultCompactionChatClient"/>.
/// </summary>
public class ToolResultCompactionOptions : CompactionOptionsBase
{
    public ToolResultCompactionOptions()
    {
        KeepVerbatimCount = 20;
    }
}

/// <summary>
/// Conversation history optimizer that trims bulky tool outputs outside the active turn window.
/// </summary>
public class ToolResultCompactor
{
    private readonly ToolResultCompactionOptions _options;

    public ToolResultCompactor(ToolResultCompactionOptions? options = null)
    {
        _options = options ?? new ToolResultCompactionOptions();
        if (_options.FallbackCharacterLimit < _options.FallbackPrefixLength + _options.FallbackSuffixLength)
        {
            throw new ArgumentException($"FallbackCharacterLimit ({_options.FallbackCharacterLimit}) must be >= FallbackPrefixLength ({_options.FallbackPrefixLength}) + FallbackSuffixLength ({_options.FallbackSuffixLength}).");
        }
    }

    public ToolResultCompactor(
        ISpeculativeSummarizer? summarizer,
        int keepVerbatimCount = 20,
        int fallbackCharacterLimit = 1750,
        int fallbackPrefixLength = 1500,
        int fallbackSuffixLength = 250)
    {
        if (fallbackCharacterLimit < fallbackPrefixLength + fallbackSuffixLength)
        {
            throw new ArgumentException($"fallbackCharacterLimit ({fallbackCharacterLimit}) must be >= fallbackPrefixLength ({fallbackPrefixLength}) + fallbackSuffixLength ({fallbackSuffixLength}).");
        }

        _options = new ToolResultCompactionOptions
        {
            Summarizer = summarizer,
            KeepVerbatimCount = keepVerbatimCount,
            FallbackPrefixLength = fallbackPrefixLength,
            FallbackSuffixLength = fallbackSuffixLength,
            FallbackCharacterLimit = fallbackCharacterLimit
        };
    }

    /// <summary>
    /// Compacts conversation history, pruning bulky tool outputs outside the active turn window.
    /// </summary>
    public IList<ChatMessage> Compact(IList<ChatMessage> messages, string? sessionId = null)
    {
        var effectiveSessionId = sessionId ?? "default";

        // Identify tool result messages
        var toolIndices = new List<int>();
        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            if (msg.Role == ChatRole.Tool || msg.Contents.Any(c => c is FunctionResultContent))
            {
                toolIndices.Add(i);
            }
        }

        if (toolIndices.Count <= _options.KeepVerbatimCount)
        {
            return messages;
        }

        var toCompact = new HashSet<int>(toolIndices.Take(toolIndices.Count - _options.KeepVerbatimCount));

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
        for (int i = 0; i < message.Contents.Count; i++)
        {
            var content = message.Contents[i];
            if (content is FunctionResultContent frc)
            {
                var rawStr = frc.Result as string ?? frc.Result?.ToString();
                if (!string.IsNullOrEmpty(rawStr) && rawStr.Length > _options.FallbackCharacterLimit)
                {
                    var itemKey = CompactionItemKeyResolver.ResolveItemKey(message, SummarizationTarget.ToolResult, rawStr, frc.CallId);
                    var compacted = ResolveCompactedResult(rawStr, sessionId, itemKey);
                    message.Contents[i] = new FunctionResultContent(frc.CallId, compacted);
                }
            }
            else if (content is TextContent tc && message.Role == ChatRole.Tool)
            {
                if (!string.IsNullOrEmpty(tc.Text) && tc.Text.Length > _options.FallbackCharacterLimit)
                {
                    var itemKey = CompactionItemKeyResolver.ResolveItemKey(message, SummarizationTarget.ToolResult, tc.Text);
                    var compacted = ResolveCompactedResult(tc.Text, sessionId, itemKey);
                    message.Contents[i] = new TextContent(compacted);
                }
            }
        }
    }

    private ChatMessage CloneAndCompactMessage(ChatMessage message, string sessionId)
    {
        var newContents = new List<AIContent>(message.Contents.Count);
        bool modified = false;

        foreach (var content in message.Contents)
        {
            if (content is FunctionResultContent frc)
            {
                var rawStr = frc.Result as string ?? frc.Result?.ToString();
                if (!string.IsNullOrEmpty(rawStr) && rawStr.Length > _options.FallbackCharacterLimit)
                {
                    var itemKey = CompactionItemKeyResolver.ResolveItemKey(message, SummarizationTarget.ToolResult, rawStr, frc.CallId);
                    var compacted = ResolveCompactedResult(rawStr, sessionId, itemKey);
                    newContents.Add(new FunctionResultContent(frc.CallId, compacted));
                    modified = true;
                    continue;
                }
            }
            else if (content is TextContent tc && message.Role == ChatRole.Tool)
            {
                if (!string.IsNullOrEmpty(tc.Text) && tc.Text.Length > _options.FallbackCharacterLimit)
                {
                    var itemKey = CompactionItemKeyResolver.ResolveItemKey(message, SummarizationTarget.ToolResult, tc.Text);
                    var compacted = ResolveCompactedResult(tc.Text, sessionId, itemKey);
                    newContents.Add(new TextContent(compacted));
                    modified = true;
                    continue;
                }
            }

            newContents.Add(content);
        }

        if (!modified) return message;

        return new ChatMessage(message.Role, newContents)
        {
            AuthorName = message.AuthorName,
            RawRepresentation = message.RawRepresentation,
            AdditionalProperties = message.AdditionalProperties != null
                ? new AdditionalPropertiesDictionary(message.AdditionalProperties)
                : null
        };
    }

    private string ResolveCompactedResult(string rawText, string sessionId, string itemKey)
    {
        // Try speculative summary
        if (_options.Summarizer != null && _options.Summarizer.TryGetSummary(sessionId, itemKey, out var summary) && !string.IsNullOrWhiteSpace(summary))
        {
            var trimmed = summary.Trim();
            var compacted = (trimmed.StartsWith("[TOOL_RESULT_SUMMARY]", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("[Semantic Summary:", StringComparison.OrdinalIgnoreCase))
                ? trimmed
                : $"[Semantic Summary: {trimmed}]";

            RecordCompactionEvent(sessionId, itemKey, rawText.Length, compacted.Length, wasSpeculativeHit: true, CompactionMethods.SemanticSummary);
            return compacted;
        }

        // Deterministic prefix + truncation marker + suffix
        var prefix = rawText[.._options.FallbackPrefixLength];
        var suffix = rawText[^_options.FallbackSuffixLength..];
        var truncatedChars = rawText.Length - _options.FallbackPrefixLength - _options.FallbackSuffixLength;
        var marker = string.Format(_options.FallbackTruncationMarker, truncatedChars);
        var fallback = prefix + marker + suffix;

        RecordCompactionEvent(sessionId, itemKey, rawText.Length, fallback.Length, wasSpeculativeHit: false, CompactionMethods.PrefixSuffixTruncation);
        return fallback;
    }

    private void RecordCompactionEvent(string sessionId, string itemKey, int origChars, int compactedChars, bool wasSpeculativeHit, string method)
    {
        if (_options.Telemetry == null && _options.OnCompaction == null) return;
        var evt = new CompactionEvent(
            DateTimeOffset.UtcNow,
            sessionId,
            SummarizationTarget.ToolResult,
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
/// IChatClient middleware compacting bulky historical tool results outside the active turn window.
/// </summary>
public class ToolResultCompactionChatClient : DelegatingChatClient
{
    private readonly ToolResultCompactionOptions _options;
    private readonly ToolResultCompactor _compactor;

    public ToolResultCompactionChatClient(IChatClient innerClient, ToolResultCompactionOptions? options = null)
        : base(innerClient)
    {
        _options = options ?? new ToolResultCompactionOptions();
        _compactor = new ToolResultCompactor(_options);
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(ToolResultCompactionChatClient)) return this;
        if (serviceType == typeof(ToolResultCompactor)) return _compactor;
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

        // Enqueue from the original text before compaction (compaction may mutate in place or truncate).
        TriggerSpeculativeEnqueue(msgList, sessionId);

        var compacted = _compactor.Compact(msgList, sessionId);

        return await base.GetResponseAsync(compacted, options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionId = CompactionItemKeyResolver.ResolveSessionId(options, _options.SessionIdResolver);
        var msgList = chatMessages as IList<ChatMessage> ?? chatMessages.ToList();

        // Enqueue from the original text before compaction (compaction may mutate in place or truncate).
        TriggerSpeculativeEnqueue(msgList, sessionId);

        var compacted = _compactor.Compact(msgList, sessionId);

        await foreach (var update in base.GetStreamingResponseAsync(compacted, options, cancellationToken))
        {
            yield return update;
        }
    }

    private void TriggerSpeculativeEnqueue(IList<ChatMessage> messages, string sessionId)
    {
        if (_options.Summarizer == null) return;

        var toolMsgs = messages.Where(m => m.Role == ChatRole.Tool || m.Contents.Any(c => c is FunctionResultContent)).ToList();
        int threshold = Math.Max(0, toolMsgs.Count - _options.KeepVerbatimCount);

        for (int i = 0; i <= threshold && i < toolMsgs.Count; i++)
        {
            var msg = toolMsgs[i];
            foreach (var frc in msg.Contents.OfType<FunctionResultContent>())
            {
                var rawStr = frc.Result as string ?? frc.Result?.ToString();
                if (!string.IsNullOrEmpty(rawStr) && rawStr.Length > _options.FallbackCharacterLimit)
                {
                    var itemKey = CompactionItemKeyResolver.ResolveItemKey(msg, SummarizationTarget.ToolResult, rawStr, frc.CallId);
                    _options.Summarizer.EnqueueSummarization(sessionId, itemKey, SummarizationTarget.ToolResult, rawStr);
                }
            }

            if (msg.Role == ChatRole.Tool)
            {
                foreach (var tc in msg.Contents.OfType<TextContent>())
                {
                    if (!string.IsNullOrEmpty(tc.Text) && tc.Text.Length > _options.FallbackCharacterLimit)
                    {
                        var itemKey = CompactionItemKeyResolver.ResolveItemKey(msg, SummarizationTarget.ToolResult, tc.Text);
                        _options.Summarizer.EnqueueSummarization(sessionId, itemKey, SummarizationTarget.ToolResult, tc.Text);
                    }
                }
            }
        }
    }
}
