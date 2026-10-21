using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Pmad.AI.Tools;

namespace Pmad.AI.Guardrails;

/// <summary>
/// <see cref="DelegatingChatClient"/> middleware that monitors tool invocations and streaming text
/// for runaway loops, ping-pong repetitions on unchanged state, and repetitive tokens.
/// When tripped, halts the loop and replaces the call with a diagnostic circuit-breaker message.
/// </summary>
public class ToolLoopCircuitBreakerChatClient : DelegatingChatClient
{
    private readonly ToolLoopCircuitBreaker _breaker;
    private readonly LenientToolDispatcher? _dispatcher;
    private ChatMessage? _lastProcessedToolEndMessage;
    private string? _lastProcessedSessionId;

    public ToolLoopCircuitBreakerChatClient(
        IChatClient innerClient,
        ToolLoopCircuitBreaker? breaker = null,
        LenientToolDispatcher? dispatcher = null)
        : base(innerClient)
    {
        _breaker = breaker ?? new ToolLoopCircuitBreaker();
        _dispatcher = dispatcher;
        _breaker.OnReset += () =>
        {
            _lastProcessedToolEndMessage = null;
        };
    }

    public ToolLoopCircuitBreaker Breaker => _breaker;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (options?.ConversationId != null)
        {
            if (!string.Equals(_lastProcessedSessionId, options.ConversationId, StringComparison.Ordinal))
            {
                _lastProcessedSessionId = options.ConversationId;
                _lastProcessedToolEndMessage = null;
            }
            _breaker.Options.SessionId = options.ConversationId;
        }

        var dispatcher = _dispatcher ?? options.GetToolDispatcher() ?? (GetService(typeof(LenientToolDispatcher)) as LenientToolDispatcher);

        InspectChatMessages(chatMessages, dispatcher);

        var response = await base.GetResponseAsync(chatMessages, options, cancellationToken);
        if (response?.Messages != null)
        {
            foreach (var message in response.Messages)
            {
                var functionCalls = message.Contents.OfType<FunctionCallContent>().ToList();
                if (functionCalls.Count > 0)
                {
                    foreach (var call in functionCalls)
                    {
                        if (string.IsNullOrWhiteSpace(call.Name))
                        {
                            continue;
                        }
                        string toolName = call.Name;
                        IDictionary<string, object?>? toolArgs = call.Arguments;

                        if (dispatcher != null)
                        {
                            try
                            {
                                var dispatched = dispatcher.NormalizeCall(call.Name, call.Arguments ?? new Dictionary<string, object?>());
                                toolName = dispatched.CanonicalToolName;
                                toolArgs = dispatched.Arguments;
                            }
                            catch (KeyNotFoundException)
                            {
                                // If normalization fails due to missing template parameters, fallback to raw invocation
                            }
                        }

                        var check = _breaker.EvaluateToolInvocation(toolName, toolArgs);
                        if (check.IsLoopDetected)
                        {
                            message.Contents.Clear();
                            message.Contents.Add(new TextContent($"[Circuit Breaker Tripped] {check.DiagnosticMessage}"));
                            return new ChatResponse([message])
                            {
                                FinishReason = ChatFinishReason.Stop,
                                Usage = response.Usage
                            };
                        }
                    }
                }
            }
        }
        return response!;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (options?.ConversationId != null)
        {
            if (!string.Equals(_lastProcessedSessionId, options.ConversationId, StringComparison.Ordinal))
            {
                _lastProcessedSessionId = options.ConversationId;
                _lastProcessedToolEndMessage = null;
            }
            _breaker.Options.SessionId = options.ConversationId;
        }

        var dispatcher = _dispatcher ?? options.GetToolDispatcher() ?? (GetService(typeof(LenientToolDispatcher)) as LenientToolDispatcher);

        InspectChatMessages(chatMessages, dispatcher);

        var sb = new StringBuilder();
        await foreach (var update in base.GetStreamingResponseAsync(chatMessages, options, cancellationToken))
        {
            if (update.Contents != null)
            {
                foreach (var content in update.Contents)
                {
                    if (content is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                    {
                        sb.Append(tc.Text);
                        var check = _breaker.DetectStreamingRepetition(sb.ToString());
                        if (check.IsLoopDetected)
                        {
                            yield return new ChatResponseUpdate
                            {
                                Role = ChatRole.Assistant,
                                Contents = [new TextContent($"\n[Circuit Breaker Tripped] {check.DiagnosticMessage}")],
                                FinishReason = ChatFinishReason.Stop
                            };
                            yield break;
                        }
                    }
                }
            }
            yield return update;
        }
    }

    private void InspectChatMessages(IEnumerable<ChatMessage>? chatMessages, LenientToolDispatcher? dispatcher)
    {
        if (chatMessages == null) return;

        var messagesList = chatMessages as IList<ChatMessage> ?? chatMessages.ToList();
        if (messagesList.Count == 0) return;

        // 1. Inspect for new guiding prompt from user
        var lastUserMsg = messagesList.LastOrDefault(m => m.Role == ChatRole.User);
        if (lastUserMsg != null && !string.IsNullOrWhiteSpace(lastUserMsg.Text))
        {
            _breaker.RecordUserPrompt(lastUserMsg.Text);
        }

        // 2. Locate the latest contiguous block of Tool messages (skipping trailing User or System messages)
        int endIdx = messagesList.Count - 1;
        while (endIdx >= 0 && (messagesList[endIdx].Role == ChatRole.User || messagesList[endIdx].Role == ChatRole.System))
        {
            endIdx--;
        }

        if (endIdx < 0 || messagesList[endIdx].Role != ChatRole.Tool)
        {
            return;
        }

        // If this exact tool block end message was already processed in this session, skip re-processing
        if (ReferenceEquals(_lastProcessedToolEndMessage, messagesList[endIdx]))
        {
            return;
        }

        int startIdx = endIdx;
        while (startIdx > 0 && messagesList[startIdx - 1].Role == ChatRole.Tool)
        {
            startIdx--;
        }

        // Build mapping of CallId -> ToolName from preceding Assistant message(s)
        var callIdToToolName = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < startIdx; i++)
        {
            var msg = messagesList[i];
            if (msg.Role == ChatRole.Assistant && msg.Contents != null)
            {
                foreach (var call in msg.Contents.OfType<FunctionCallContent>())
                {
                    if (!string.IsNullOrWhiteSpace(call.CallId) && !string.IsNullOrWhiteSpace(call.Name))
                    {
                        var canonicalName = call.Name;
                        if (dispatcher != null)
                        {
                            try
                            {
                                var dispatched = dispatcher.NormalizeCall(call.Name, call.Arguments ?? new Dictionary<string, object?>());
                                canonicalName = dispatched.CanonicalToolName;
                            }
                            catch
                            {
                                // Keep raw name if normalization fails
                            }
                        }
                        callIdToToolName[call.CallId] = canonicalName;
                    }
                }
            }
        }

        // Process each tool message in the contiguous block
        bool recordedAny = false;
        var aggregatedFallback = new StringBuilder();
        var seenNonErrorObservationsInBlock = new HashSet<string>(StringComparer.Ordinal);

        for (int i = startIdx; i <= endIdx; i++)
        {
            var msg = messagesList[i];
            if (msg.Contents != null)
            {
                foreach (var content in msg.Contents)
                {
                    if (content is FunctionResultContent frc)
                    {
                        string? toolName = null;
                        if (frc.CallId != null && callIdToToolName.TryGetValue(frc.CallId, out var tn))
                        {
                            toolName = tn;
                        }

                        var resultStr = frc.Result?.ToString() ?? string.Empty;
                        bool? isError = frc.Exception != null ? true : (bool?)null;
                        bool treatedAsError = isError ?? ToolLoopCircuitBreaker.IsErrorObservation(resultStr);

                        // If multiple parallel calls within the same turn block return identical successful (non-error) observations,
                        // only record the first one so parallel executions are not misattributed as temporal retry streaks.
                        if (!treatedAsError && !seenNonErrorObservationsInBlock.Add(resultStr))
                        {
                            continue;
                        }

                        _breaker.RecordObservation(resultStr, toolName, treatedAsError);
                        recordedAny = true;
                    }
                    else if (content is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                    {
                        aggregatedFallback.Append(tc.Text);
                    }
                }
            }
        }

        if (!recordedAny && aggregatedFallback.Length > 0)
        {
            _breaker.RecordObservation(aggregatedFallback.ToString());
        }

        _lastProcessedToolEndMessage = messagesList[endIdx];
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(ToolLoopCircuitBreakerChatClient)) return this;
        if (serviceType == typeof(ToolLoopCircuitBreaker)) return _breaker;
        if (serviceType == typeof(CircuitBreakerTelemetry) && _breaker.Options.Telemetry != null) return _breaker.Options.Telemetry;
        if (serviceType == typeof(LenientToolDispatcher) && _dispatcher != null) return _dispatcher;
        return base.GetService(serviceType, serviceKey);
    }
}
