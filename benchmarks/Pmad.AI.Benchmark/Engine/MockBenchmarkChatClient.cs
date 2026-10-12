using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Benchmark.Engine;

/// <summary>
/// Deterministic mock implementation of <see cref="IChatClient"/> for benchmarking scenarios,
/// verifying tool calling mechanics, assertions, and reporting without downloading large model weights.
/// </summary>
public sealed class MockBenchmarkChatClient : IChatClient
{
    private readonly string _modelId;
    private readonly ChatClientMetadata _metadata;
    private readonly int _simulatedDelayMs;
    private readonly bool _enableReasoning;
    private readonly string _backend;

    public MockBenchmarkChatClient(
        string modelId = "mock-qwen-3.5",
        int? simulatedDelayMs = null,
        bool enableReasoning = false,
        string backend = "Vulkan")
    {
        _modelId = modelId;
        _enableReasoning = enableReasoning;
        _backend = backend;
        _simulatedDelayMs = simulatedDelayMs ?? (backend.Equals("CPU", StringComparison.OrdinalIgnoreCase) ? 35 : 10);
        _metadata = new ChatClientMetadata("mock", new Uri("http://localhost/mock"), modelId);
    }

    public ChatClientMetadata Metadata => _metadata;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_simulatedDelayMs > 0)
        {
            await Task.Delay(_simulatedDelayMs, cancellationToken);
        }

        var messageList = messages.ToList();
        var (contents, finishReason, promptTokens, outputTokens) = GenerateMockResponse(messageList, options);

        if (_enableReasoning)
        {
            contents.Insert(0, new TextReasoningContent("Evaluating user request and planning optimal tool actions..."));
            outputTokens += 40;
        }

        var assistantMsg = new ChatMessage(ChatRole.Assistant, contents);
        return new ChatResponse([assistantMsg])
        {
            FinishReason = finishReason,
            Usage = new UsageDetails
            {
                InputTokenCount = promptTokens,
                OutputTokenCount = outputTokens,
                TotalTokenCount = promptTokens + outputTokens
            }
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        var msg = response.Messages[0];

        foreach (var content in msg.Contents)
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [content],
                FinishReason = response.FinishReason
            };
        }
    }

    private (List<AIContent> Contents, ChatFinishReason FinishReason, int PromptTokens, int OutputTokens) GenerateMockResponse(
        List<ChatMessage> history,
        ChatOptions? options)
    {
        var last = history.LastOrDefault();
        var toolsAvailable = options?.Tools != null && options.Tools.Count > 0;

        // 1. If last message is a Tool message (FunctionResultContent), model synthesizes final output
        if (last?.Role == ChatRole.Tool)
        {
            // Inspect the tool results or user context
            var originalUser = history.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;

            if (originalUser.Contains("Paris", StringComparison.OrdinalIgnoreCase))
            {
                return (
                    [new TextContent("The current weather in Paris is 18°C (Partly Cloudy) and in Tokyo it is 22°C (Sunny).")],
                    ChatFinishReason.Stop,
                    PromptTokens: 145,
                    OutputTokens: 28
                );
            }

            if (originalUser.Contains("discount", StringComparison.OrdinalIgnoreCase) || originalUser.Contains("jackets", StringComparison.OrdinalIgnoreCase))
            {
                return (
                    [new TextContent("After computing the subtotal of $178.97 and applying the 15% discount, the final total price is $152.12.")],
                    ChatFinishReason.Stop,
                    PromptTokens: 130,
                    OutputTokens: 25
                );
            }

            if (originalUser.Contains("track", StringComparison.OrdinalIgnoreCase))
            {
                return (
                    [new TextContent("Your package is out for delivery with DHL (tracking DHL-88319) and estimated to arrive Today by 4:00 PM.")],
                    ChatFinishReason.Stop,
                    PromptTokens: 160,
                    OutputTokens: 26
                );
            }

            if (originalUser.Contains("ORD-9921", StringComparison.OrdinalIgnoreCase))
            {
                return (
                    [new TextContent("Order ORD-9921 has been shipped via DHL with tracking code DHL-88319.")],
                    ChatFinishReason.Stop,
                    PromptTokens: 120,
                    OutputTokens: 20
                );
            }

            return (
                [new TextContent("Task completed successfully based on tool results.")],
                ChatFinishReason.Stop,
                PromptTokens: 100,
                OutputTokens: 12
            );
        }

        // 2. If user message:
        var userText = history.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;

        // Weather Scenario - Turn 1 (Tool call)
        if (toolsAvailable && userText.Contains("Paris and Tokyo", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [
                    new FunctionCallContent("call_1", "get_weather", new Dictionary<string, object?> { ["city"] = "Paris", ["unit"] = "celsius" }),
                    new FunctionCallContent("call_2", "get_weather", new Dictionary<string, object?> { ["city"] = "Tokyo", ["unit"] = "celsius" })
                ],
                ChatFinishReason.ToolCalls,
                PromptTokens: 120,
                OutputTokens: 42
            );
        }

        // Weather Scenario - Turn 2 (Contextual follow-up, no tools needed)
        if (userText.Contains("warmer", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new TextContent("Tokyo is warmer than Paris by 4 degrees celsius (22°C vs 18°C).")],
                ChatFinishReason.Stop,
                PromptTokens: 180,
                OutputTokens: 18
            );
        }

        // Calculator Scenario (Tool call)
        if (toolsAvailable && (userText.Contains("jackets", StringComparison.OrdinalIgnoreCase) || userText.Contains("discount", StringComparison.OrdinalIgnoreCase)))
        {
            return (
                [
                    new FunctionCallContent("call_calc", "calculate", new Dictionary<string, object?>
                    {
                        ["expression"] = "(3 * 49.99 + 2 * 14.50) * 0.85"
                    })
                ],
                ChatFinishReason.ToolCalls,
                PromptTokens: 110,
                OutputTokens: 30
            );
        }

        // Customer Support - Turn 1 (Lookup order)
        if (toolsAvailable && userText.Contains("ORD-9921", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [
                    new FunctionCallContent("call_ord", "lookup_order", new Dictionary<string, object?>
                    {
                        ["order_id"] = "ORD-9921"
                    })
                ],
                ChatFinishReason.ToolCalls,
                PromptTokens: 105,
                OutputTokens: 22
            );
        }

        // Customer Support - Turn 2 (Track package)
        if (toolsAvailable && userText.Contains("track that DHL package", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [
                    new FunctionCallContent("call_track", "track_package", new Dictionary<string, object?>
                    {
                        ["carrier"] = "DHL",
                        ["tracking_code"] = "DHL-88319"
                    })
                ],
                ChatFinishReason.ToolCalls,
                PromptTokens: 150,
                OutputTokens: 25
            );
        }

        // Direct Knowledge (Negative test - No tools)
        if (userText.Contains("capital of France", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new TextContent("The capital of France is Paris.")],
                ChatFinishReason.Stop,
                PromptTokens: 85,
                OutputTokens: 8
            );
        }

        // Default fallback response
        return (
            [new TextContent("I have processed your request.")],
            ChatFinishReason.Stop,
            PromptTokens: 50,
            OutputTokens: 7
        );
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(MockBenchmarkChatClient) ? this : null;

    public void Dispose()
    {
    }
}
