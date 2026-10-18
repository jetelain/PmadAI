using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Guardrails;

/// <summary>
/// <see cref="DelegatingChatClient"/> middleware that monitors tool invocations and streaming text
/// for runaway loops, ping-pong repetitions on unchanged state, and repetitive tokens.
/// When tripped, halts the loop and replaces the call with a diagnostic circuit-breaker message.
/// </summary>
public class ToolLoopCircuitBreakerChatClient : DelegatingChatClient
{
    private readonly ToolLoopCircuitBreaker _breaker;

    public ToolLoopCircuitBreakerChatClient(IChatClient innerClient, ToolLoopCircuitBreaker? breaker = null)
        : base(innerClient)
    {
        _breaker = breaker ?? new ToolLoopCircuitBreaker();
    }

    public ToolLoopCircuitBreaker Breaker => _breaker;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        InspectChatMessages(chatMessages);

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
                        var check = _breaker.EvaluateToolInvocation(call.Name, call.Arguments);
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
        InspectChatMessages(chatMessages);

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

    private void InspectChatMessages(IEnumerable<ChatMessage>? chatMessages)
    {
        if (chatMessages == null) return;

        var messagesList = chatMessages as IList<ChatMessage> ?? chatMessages.ToList();

        // 1. Inspect for new guiding prompt from user
        var lastUserMsg = messagesList.LastOrDefault(m => m.Role == ChatRole.User);
        if (lastUserMsg != null && !string.IsNullOrWhiteSpace(lastUserMsg.Text))
        {
            _breaker.RecordUserPrompt(lastUserMsg.Text);
        }

        // 2. Inspect for tool observation in message history (aggregate all trailing Tool messages)
        var sb = new StringBuilder();
        for (int i = messagesList.Count - 1; i >= 0; i--)
        {
            var msg = messagesList[i];
            if (msg.Role == ChatRole.Tool)
            {
                if (msg.Contents != null)
                {
                    foreach (var content in msg.Contents)
                    {
                        if (content is FunctionResultContent frc && frc.Result != null)
                        {
                            sb.Append(frc.Result);
                        }
                        else if (content is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                        {
                            sb.Append(tc.Text);
                        }
                    }
                }
            }
            else
            {
                break;
            }
        }
        if (sb.Length > 0)
        {
            _breaker.RecordObservation(sb.ToString());
        }
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(ToolLoopCircuitBreakerChatClient)) return this;
        if (serviceType == typeof(ToolLoopCircuitBreaker)) return _breaker;
        return base.GetService(serviceType, serviceKey);
    }
}
