using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Pmad.AI.Healing;

/// <summary>
/// Strategy used by <see cref="ToolCallHealingChatClient"/> when streaming responses.
/// </summary>
public enum StreamingHealingMode
{
    /// <summary>Only buffers stream chunks when potential pseudo-tool tag prefixes are encountered, preserving TTFT for prose.</summary>
    PrefixTriggered,

    /// <summary>Buffers entire stream until completion before parsing (100% extraction rate at the cost of streaming latency).</summary>
    FullBuffer,

    /// <summary>Disables streaming healing; passes streaming updates untouched for maximum performance.</summary>
    Disabled
}

/// <summary>
/// Configuration options for <see cref="ToolCallHealingChatClient"/>.
/// </summary>
public class ToolCallHealingOptions
{
    /// <summary>Streaming mode strategy. Defaults to PrefixTriggered.</summary>
    public StreamingHealingMode StreamingMode { get; set; } = StreamingHealingMode.PrefixTriggered;

    /// <summary>
    /// Prefix triggers that activate stream chunk buffering.
    /// Prefixes may be split across chunk boundaries (e.g. "&lt;tool" + "_call&gt;"); the buffer matches partial prefixes at the tail of the accumulated text.
    /// A bare "&lt;" is intentionally NOT a trigger (HTML, generics and markdown would defeat the TTFT guarantee).
    /// </summary>
    public string[] TagPrefixTriggers { get; set; } = ["<tool_call", "<function_call"];

    /// <summary>Optional logger.</summary>
    public ILogger? Logger { get; set; }
}

/// <summary>
/// Chat client middleware wrapping <see cref="IChatClient"/> (streaming + non-streaming) that promotes
/// pseudo &lt;tool_call&gt; and &lt;function_call&gt; tags and ReAct action blocks into <see cref="FunctionCallContent"/>,
/// strips code fences from argument JSON, and repairs trailing commas or unescaped control characters.
/// </summary>
public class ToolCallHealingChatClient : DelegatingChatClient
{
    private readonly ToolCallHealingOptions _options;

    public ToolCallHealingChatClient(IChatClient innerClient, ToolCallHealingOptions? options = null)
        : base(innerClient)
    {
        _options = options ?? new ToolCallHealingOptions();
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(chatMessages, options, cancellationToken);
        return ToolCallHealingTransform.HealChatResponse(response, _options.Logger);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        switch (_options.StreamingMode)
        {
            case StreamingHealingMode.Disabled:
                await foreach (var update in base.GetStreamingResponseAsync(chatMessages, options, cancellationToken))
                {
                    yield return update;
                }
                yield break;

            case StreamingHealingMode.FullBuffer:
                await foreach (var update in StreamWithFullBufferAsync(chatMessages, options, cancellationToken))
                {
                    yield return update;
                }
                yield break;

            case StreamingHealingMode.PrefixTriggered:
            default:
                await foreach (var update in StreamWithPrefixTriggeredAsync(chatMessages, options, cancellationToken))
                {
                    yield return update;
                }
                yield break;
        }
    }

    private async IAsyncEnumerable<ChatResponseUpdate> StreamWithFullBufferAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var textBuilder = new StringBuilder();
        var allContents = new List<AIContent>();
        ChatRole? role = null;
        ChatFinishReason? finishReason = null;
        string? responseId = null;
        DateTimeOffset? createdAt = null;
        string? modelId = null;

        await foreach (var update in base.GetStreamingResponseAsync(chatMessages, options, cancellationToken))
        {
            role ??= update.Role;
            finishReason ??= update.FinishReason;
            responseId ??= update.ResponseId;
            createdAt ??= update.CreatedAt;
            modelId ??= update.ModelId;

            if (update.Contents != null)
            {
                allContents.AddRange(update.Contents);
            }

            if (!string.IsNullOrEmpty(update.Text))
            {
                textBuilder.Append(update.Text);
            }
        }

        var fullText = textBuilder.ToString();
        var extractedCalls = new List<FunctionCallContent>();
        if (!string.IsNullOrEmpty(fullText))
        {
            ToolCallHealingTransform.TryExtractToolCalls(ref fullText, out extractedCalls, _options.Logger);
        }

        // Sanitize any existing FunctionCallContent items
        for (int i = 0; i < allContents.Count; i++)
        {
            if (allContents[i] is FunctionCallContent fcc)
            {
                var callId = string.IsNullOrWhiteSpace(fcc.CallId) ? $"call_{Guid.NewGuid():N}"[..12] : fcc.CallId;
                var arguments = fcc.Arguments;
                if (arguments is { Count: 1 } && arguments.TryGetValue("input", out var inputVal) && inputVal is string strInput &&
                    (strInput.TrimStart().StartsWith('{') || strInput.TrimStart().StartsWith("```")))
                {
                    var parsed = ToolCallHealingTransform.ParseArgumentsJson(strInput, _options.Logger, fcc.Name);
                    if (parsed.Count > 0)
                    {
                        arguments = parsed;
                    }
                }
                allContents[i] = new FunctionCallContent(callId, fcc.Name, arguments);
            }
        }

        if (extractedCalls.Count > 0)
        {
            foreach (var call in extractedCalls)
            {
                allContents.Add(call);
            }
            finishReason = ChatFinishReason.ToolCalls;
        }

        var finalUpdate = new ChatResponseUpdate
        {
            Role = role,
            FinishReason = finishReason,
            ResponseId = responseId,
            CreatedAt = createdAt,
            ModelId = modelId
        };

        if (!string.IsNullOrEmpty(fullText))
        {
            finalUpdate.Contents.Add(new TextContent(fullText));
        }

        foreach (var c in allContents)
        {
            if (c is not TextContent)
            {
                finalUpdate.Contents.Add(c);
            }
        }

        yield return finalUpdate;
    }

    private async IAsyncEnumerable<ChatResponseUpdate> StreamWithPrefixTriggeredAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var processor = new StreamPrefixProcessor(
            _options.TagPrefixTriggers ?? ["<tool_call", "<function_call"],
            _options.Logger);

        var outputList = new List<ChatResponseUpdate>();

        await foreach (var update in base.GetStreamingResponseAsync(chatMessages, options, cancellationToken))
        {
            outputList.Clear();
            processor.ProcessUpdate(update, outputList);
            foreach (var item in outputList)
            {
                yield return item;
            }
        }

        outputList.Clear();
        processor.FlushEnd(outputList);
        foreach (var item in outputList)
        {
            yield return item;
        }
    }

    private sealed class StreamPrefixProcessor
    {
        private readonly string[] _triggers;
        private readonly ILogger? _logger;
        private string _heldPrefix = string.Empty;
        private bool _isBufferingTag;
        private string? _activeClosingTag;
        private readonly StringBuilder _tagBuffer = new();

        public StreamPrefixProcessor(string[] triggers, ILogger? logger)
        {
            _triggers = triggers;
            _logger = logger;
        }

        public void ProcessUpdate(ChatResponseUpdate update, List<ChatResponseUpdate> output)
        {
            var updateText = update.Text;
            var otherContents = update.Contents?.Where(c => c is not TextContent).ToList();

            if (_isBufferingTag)
            {
                if (!string.IsNullOrEmpty(updateText))
                {
                    _tagBuffer.Append(updateText);
                }

                if (otherContents != null && otherContents.Count > 0)
                {
                    var otherUpdate = new ChatResponseUpdate
                    {
                        Role = update.Role,
                        FinishReason = update.FinishReason,
                        ResponseId = update.ResponseId,
                        CreatedAt = update.CreatedAt,
                        ModelId = update.ModelId
                    };
                    foreach (var c in otherContents)
                    {
                        otherUpdate.Contents.Add(c);
                    }
                    output.Add(otherUpdate);
                }

                if (_activeClosingTag != null && _tagBuffer.ToString().Contains(_activeClosingTag, StringComparison.OrdinalIgnoreCase))
                {
                    var bufferedStr = _tagBuffer.ToString();
                    var closeIdx = bufferedStr.IndexOf(_activeClosingTag, StringComparison.OrdinalIgnoreCase);
                    var fullTag = bufferedStr.Substring(0, closeIdx + _activeClosingTag.Length);
                    var remainder = bufferedStr.Substring(closeIdx + _activeClosingTag.Length);

                    _tagBuffer.Clear();
                    _isBufferingTag = false;
                    _activeClosingTag = null;

                    var textRemaining = fullTag;
                    if (ToolCallHealingTransform.TryExtractToolCalls(ref textRemaining, out var extracted, _logger) && extracted.Count > 0)
                    {
                        var promotedUpdate = new ChatResponseUpdate
                        {
                            Role = update.Role,
                            FinishReason = ChatFinishReason.ToolCalls
                        };
                        if (!string.IsNullOrEmpty(textRemaining))
                        {
                            promotedUpdate.Contents.Add(new TextContent(textRemaining));
                        }
                        foreach (var call in extracted)
                        {
                            promotedUpdate.Contents.Add(call);
                        }
                        output.Add(promotedUpdate);
                    }
                    else
                    {
                        var textUpdate = new ChatResponseUpdate { Role = update.Role };
                        textUpdate.Contents.Add(new TextContent(fullTag));
                        output.Add(textUpdate);
                    }

                    if (!string.IsNullOrEmpty(remainder))
                    {
                        ProcessText(remainder, update, output);
                    }
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(updateText))
                {
                    var chunkToProcess = _heldPrefix + updateText;
                    _heldPrefix = string.Empty;
                    ProcessText(chunkToProcess, update, output);
                }
                else if (otherContents != null && otherContents.Count > 0)
                {
                    if (!string.IsNullOrEmpty(_heldPrefix))
                    {
                        var heldUpdate = new ChatResponseUpdate { Role = update.Role };
                        heldUpdate.Contents.Add(new TextContent(_heldPrefix));
                        output.Add(heldUpdate);
                        _heldPrefix = string.Empty;
                    }

                    output.Add(update);
                }
                else
                {
                    output.Add(update);
                }
            }
        }

        private void ProcessText(string text, ChatResponseUpdate prototype, List<ChatResponseUpdate> output)
        {
            while (!string.IsNullOrEmpty(text) && !_isBufferingTag)
            {
                int earliestTriggerPos = -1;
                string? matchedTrigger = null;

                foreach (var trig in _triggers)
                {
                    var idx = text.IndexOf(trig, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0 && (earliestTriggerPos < 0 || idx < earliestTriggerPos))
                    {
                        earliestTriggerPos = idx;
                        matchedTrigger = trig;
                    }
                }

                if (earliestTriggerPos >= 0 && matchedTrigger != null)
                {
                    if (earliestTriggerPos > 0)
                    {
                        var preUpdate = new ChatResponseUpdate { Role = prototype.Role };
                        preUpdate.Contents.Add(new TextContent(text.Substring(0, earliestTriggerPos)));
                        output.Add(preUpdate);
                    }

                    _isBufferingTag = true;
                    _activeClosingTag = ResolveClosingTag(matchedTrigger);
                    _tagBuffer.Clear();
                    _tagBuffer.Append(text.Substring(earliestTriggerPos));
                    text = string.Empty;

                    if (_activeClosingTag != null && _tagBuffer.ToString().Contains(_activeClosingTag, StringComparison.OrdinalIgnoreCase))
                    {
                        var bufferedStr = _tagBuffer.ToString();
                        var closeIdx = bufferedStr.IndexOf(_activeClosingTag, StringComparison.OrdinalIgnoreCase);
                        var fullTag = bufferedStr.Substring(0, closeIdx + _activeClosingTag.Length);
                        var remainder = bufferedStr.Substring(closeIdx + _activeClosingTag.Length);

                        _tagBuffer.Clear();
                        _isBufferingTag = false;
                        _activeClosingTag = null;

                        var textRemaining = fullTag;
                        if (ToolCallHealingTransform.TryExtractToolCalls(ref textRemaining, out var extracted, _logger) && extracted.Count > 0)
                        {
                            var promoted = new ChatResponseUpdate
                            {
                                Role = prototype.Role,
                                FinishReason = ChatFinishReason.ToolCalls
                            };
                            if (!string.IsNullOrEmpty(textRemaining))
                            {
                                promoted.Contents.Add(new TextContent(textRemaining));
                            }
                            foreach (var call in extracted)
                            {
                                promoted.Contents.Add(call);
                            }
                            output.Add(promoted);
                        }
                        else
                        {
                            var fallbackUpdate = new ChatResponseUpdate { Role = prototype.Role };
                            fallbackUpdate.Contents.Add(new TextContent(fullTag));
                            output.Add(fallbackUpdate);
                        }

                        text = remainder;
                    }
                }
                else
                {
                    int partialLen = FindLongestPartialPrefixMatch(text, _triggers);
                    if (partialLen >= 2)
                    {
                        var emitLen = text.Length - partialLen;
                        if (emitLen > 0)
                        {
                            var preUpdate = new ChatResponseUpdate { Role = prototype.Role };
                            preUpdate.Contents.Add(new TextContent(text.Substring(0, emitLen)));
                            output.Add(preUpdate);
                        }
                        _heldPrefix = text.Substring(emitLen);
                        text = string.Empty;
                    }
                    else
                    {
                        var directUpdate = new ChatResponseUpdate { Role = prototype.Role };
                        directUpdate.Contents.Add(new TextContent(text));
                        output.Add(directUpdate);
                        text = string.Empty;
                    }
                }
            }
        }

        public void FlushEnd(List<ChatResponseUpdate> output)
        {
            if (_isBufferingTag && _tagBuffer.Length > 0)
            {
                var raw = _tagBuffer.ToString();
                var textRemaining = raw;
                if (ToolCallHealingTransform.TryExtractToolCalls(ref textRemaining, out var extracted, _logger) && extracted.Count > 0)
                {
                    var finalUpdate = new ChatResponseUpdate
                    {
                        FinishReason = ChatFinishReason.ToolCalls
                    };
                    if (!string.IsNullOrEmpty(textRemaining))
                    {
                        finalUpdate.Contents.Add(new TextContent(textRemaining));
                    }
                    foreach (var call in extracted)
                    {
                        finalUpdate.Contents.Add(call);
                    }
                    output.Add(finalUpdate);
                }
                else
                {
                    var update = new ChatResponseUpdate();
                    update.Contents.Add(new TextContent(raw));
                    output.Add(update);
                }
            }
            else if (!string.IsNullOrEmpty(_heldPrefix))
            {
                var update = new ChatResponseUpdate();
                update.Contents.Add(new TextContent(_heldPrefix));
                output.Add(update);
                _heldPrefix = string.Empty;
            }
        }

        private static string ResolveClosingTag(string openingTrigger)
        {
            var trimmed = openingTrigger.TrimStart('<');
            var spaceIdx = trimmed.IndexOfAny([' ', '>', '\n', '\r', '\t']);
            var tagName = spaceIdx > 0 ? trimmed.Substring(0, spaceIdx) : trimmed;
            return $"</{tagName}>";
        }

        private static int FindLongestPartialPrefixMatch(string text, string[] triggers)
        {
            for (int len = Math.Min(text.Length, 32); len >= 2; len--)
            {
                var tail = text.Substring(text.Length - len);
                foreach (var trig in triggers)
                {
                    if (trig.StartsWith(tail, StringComparison.OrdinalIgnoreCase))
                    {
                        return len;
                    }
                }
            }
            return 0;
        }
    }
}
