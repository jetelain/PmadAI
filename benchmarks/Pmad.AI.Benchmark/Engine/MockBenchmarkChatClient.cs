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
        var promptTokens = CalculatePromptTokens(history);

        // 1. If last message is a Tool message (FunctionResultContent), model synthesizes final output
        if (last?.Role == ChatRole.Tool)
        {
            // Inspect the tool results or user context
            var originalUser = history.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;

            if (originalUser.Contains("[LONG-ACTION", StringComparison.Ordinal))
            {
                return (
                    [new TextContent("Step completed; results noted.")],
                    ChatFinishReason.Stop,
                    PromptTokens: promptTokens,
                    OutputTokens: 8
                );
            }

            if (originalUser.Contains("Paris", StringComparison.OrdinalIgnoreCase))
            {
                return (
                    [new TextContent("The current weather in Paris is 18°C (Partly Cloudy) and in Tokyo it is 22°C (Sunny).")],
                    ChatFinishReason.Stop,
                    PromptTokens: promptTokens,
                    OutputTokens: 28
                );
            }

            if (originalUser.Contains("discount", StringComparison.OrdinalIgnoreCase) || originalUser.Contains("jackets", StringComparison.OrdinalIgnoreCase))
            {
                return (
                    [new TextContent("After computing the subtotal of $178.97 and applying the 15% discount, the final total price is $152.12.")],
                    ChatFinishReason.Stop,
                    PromptTokens: promptTokens,
                    OutputTokens: 25
                );
            }

            if (originalUser.Contains("track", StringComparison.OrdinalIgnoreCase))
            {
                return (
                    [new TextContent("Your package is out for delivery with DHL (tracking DHL-88319) and estimated to arrive Today by 4:00 PM.")],
                    ChatFinishReason.Stop,
                    PromptTokens: promptTokens,
                    OutputTokens: 26
                );
            }

            if (originalUser.Contains("ORD-9921", StringComparison.OrdinalIgnoreCase))
            {
                return (
                    [new TextContent("Order ORD-9921 has been shipped via DHL with tracking code DHL-88319.")],
                    ChatFinishReason.Stop,
                    PromptTokens: promptTokens,
                    OutputTokens: 20
                );
            }

            if (originalUser.Contains("AuthHandler", StringComparison.OrdinalIgnoreCase))
            {
                return (
                    [new TextContent("AuthHandler.cs contains the structure and 50 methods.")],
                    ChatFinishReason.Stop,
                    PromptTokens: promptTokens,
                    OutputTokens: 15
                );
            }

            if (originalUser.Contains("unit tests", StringComparison.OrdinalIgnoreCase))
            {
                return (
                    [new TextContent("The unit tests failed with ExitCode: 101, Reason: NullReferenceException in AuthHandler.cs:L42.")],
                    ChatFinishReason.Stop,
                    PromptTokens: promptTokens,
                    OutputTokens: 25
                );
            }

            if (originalUser.Contains("TokenValidator", StringComparison.OrdinalIgnoreCase))
            {
                return (
                    [new TextContent("Found match for TokenValidator in the module.")],
                    ChatFinishReason.Stop,
                    PromptTokens: promptTokens,
                    OutputTokens: 15
                );
            }

            return (
                [new TextContent("Task completed successfully based on tool results.")],
                ChatFinishReason.Stop,
                PromptTokens: promptTokens,
                OutputTokens: 12
            );
        }

        // 2. If user message:
        var userText = history.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;

        // Long-horizon scenarios (50 / 100 turns)
        if (userText.Contains("[LONG-PROBE]", StringComparison.Ordinal))
        {
            return (
                [new TextContent(
                    "Recalled facts: ERR-9021-AUTH-EXPIRED; datetime2(7) DEFAULT SYSUTCDATETIME(); TestExpiredTokenIsRejected_ReturnsUnauthorized; " +
                    "CVE-2026-4412 fixed in v2.4.5; ConnectionPoolManager.cs:L142; TEST_TOKEN_SECRET_98812; throughput 18,900 ops/sec.")],
                ChatFinishReason.Stop,
                PromptTokens: promptTokens,
                OutputTokens: 60
            );
        }

        var longAction = System.Text.RegularExpressions.Regex.Match(userText, @"\[LONG-ACTION:([A-Za-z0-9_]+)\] Step (\d+)");
        if (toolsAvailable && longAction.Success)
        {
            return (
                [new FunctionCallContent($"call_long_{longAction.Groups[2].Value}", longAction.Groups[1].Value,
                    new Dictionary<string, object?> { ["target"] = $"module_{longAction.Groups[2].Value}" })],
                ChatFinishReason.ToolCalls,
                PromptTokens: promptTokens,
                OutputTokens: 20
            );
        }

        // Weather Scenario - Turn 1 (Tool call)
        if (toolsAvailable && userText.Contains("Paris and Tokyo", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [
                    new FunctionCallContent("call_1", "get_weather", new Dictionary<string, object?> { ["city"] = "Paris", ["unit"] = "celsius" }),
                    new FunctionCallContent("call_2", "get_weather", new Dictionary<string, object?> { ["city"] = "Tokyo", ["unit"] = "celsius" })
                ],
                ChatFinishReason.ToolCalls,
                PromptTokens: promptTokens,
                OutputTokens: 42
            );
        }

        // Weather Scenario - Turn 2 (Contextual follow-up, no tools needed)
        if (userText.Contains("warmer", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new TextContent("Tokyo is warmer than Paris by 4 degrees celsius (22°C vs 18°C).")],
                ChatFinishReason.Stop,
                PromptTokens: promptTokens,
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
                PromptTokens: promptTokens,
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
                PromptTokens: promptTokens,
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
                PromptTokens: promptTokens,
                OutputTokens: 25
            );
        }

        // AgentMultiTurnCompactionScenario
        if (toolsAvailable && userText.Contains("AuthHandler.cs and tell me its structure", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new FunctionCallContent("call_read", "read_file", new Dictionary<string, object?> { ["path"] = "src/AuthHandler.cs" })],
                ChatFinishReason.ToolCalls,
                PromptTokens: promptTokens,
                OutputTokens: 25
            );
        }

        if (toolsAvailable && userText.Contains("Run the unit tests", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new FunctionCallContent("call_test", "execute_tests", new Dictionary<string, object?> { ["filter"] = "all" })],
                ChatFinishReason.ToolCalls,
                PromptTokens: promptTokens,
                OutputTokens: 25
            );
        }

        if (toolsAvailable && userText.Contains("TokenValidator_", StringComparison.OrdinalIgnoreCase))
        {
            var match = System.Text.RegularExpressions.Regex.Match(userText, @"TokenValidator_(\d+)");
            var query = match.Success ? $"TokenValidator_{match.Groups[1].Value}" : "TokenValidator";
            return (
                [new FunctionCallContent($"call_search_{query}", "search_code", new Dictionary<string, object?> { ["query"] = query })],
                ChatFinishReason.ToolCalls,
                PromptTokens: promptTokens,
                OutputTokens: 25
            );
        }

        if (userText.Contains("what was the exact exit code and failure reason", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new TextContent("In our initial test run in turn 2, the exit code was 101 with failure reason: NullReferenceException in AuthHandler.cs:L42.")],
                ChatFinishReason.Stop,
                PromptTokens: promptTokens,
                OutputTokens: 35
            );
        }

        // ToolCallHealingRecoveryScenario
        if (toolsAvailable && userText.Contains("Apply this patch wrapped in markdown fences", StringComparison.OrdinalIgnoreCase))
        {
            // Returns pseudo XML with markdown code fences inside string argument.
            // If ToolCallHealingChatClient is active, it heals it to FunctionCallContent("apply_patch", ...).
            // In baseline raw mode, this stays as raw TextContent, so turn validation fails as expected!
            return (
                [new TextContent("<tool_call>{\"name\":\"apply_patch\",\"arguments\":\"```json\\n{\\\"content\\\":\\\"var fixedCode = 42;\\\"}\\n```\"}</tool_call>")],
                ChatFinishReason.Stop,
                PromptTokens: promptTokens,
                OutputTokens: 25
            );
        }

        if (toolsAvailable && userText.Contains("Perform a search for 'SessionToken'", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new TextContent("Action: search\nAction Input: {\"query\":\"SessionToken\"}")],
                ChatFinishReason.Stop,
                PromptTokens: promptTokens,
                OutputTokens: 20
            );
        }

        if (toolsAvailable && userText.Contains("Run query_database with trailing commas", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new TextContent("<tool_call>{\"name\":\"query_database\",\"arguments\":{\"sql\":\"SELECT * FROM sessions\",}}</tool_call>")],
                ChatFinishReason.Stop,
                PromptTokens: promptTokens,
                OutputTokens: 25
            );
        }

        // StreamingRunawayAndPingPongScenario
        if (toolsAvailable && userText.Contains("Check status of auth-service", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new FunctionCallContent("call_status", "check_status", new Dictionary<string, object?> { ["service"] = "auth-service" })],
                ChatFinishReason.ToolCalls,
                PromptTokens: promptTokens,
                OutputTokens: 20
            );
        }

        if (toolsAvailable && userText.Contains("Read same.cs repeatedly", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new FunctionCallContent("call_read_same", "read", new Dictionary<string, object?> { ["path"] = "same.cs" })],
                ChatFinishReason.ToolCalls,
                PromptTokens: promptTokens,
                OutputTokens: 20
            );
        }

        // Direct Knowledge (Negative test - No tools)
        if (userText.Contains("capital of France", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new TextContent("The capital of France is Paris.")],
                ChatFinishReason.Stop,
                PromptTokens: promptTokens,
                OutputTokens: 8
            );
        }

        // Default fallback response
        return (
            [new TextContent("I have processed your request.")],
            ChatFinishReason.Stop,
            PromptTokens: promptTokens,
            OutputTokens: 7
        );
    }

    private static int CalculatePromptTokens(List<ChatMessage> history)
    {
        int tokens = 50;
        foreach (var msg in history)
        {
            tokens += 4;
            foreach (var c in msg.Contents)
            {
                if (c is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                    tokens += Pmad.AI.Tokens.HeuristicTokenEstimator.Default.Estimate(tc.Text);
                else if (c is TextReasoningContent tr && !string.IsNullOrEmpty(tr.Text))
                    tokens += Pmad.AI.Tokens.HeuristicTokenEstimator.Default.Estimate(tr.Text);
                else if (c is FunctionCallContent fc)
                    tokens += 20 + (fc.Arguments != null ? fc.Arguments.Count * 10 : 0);
                else if (c is FunctionResultContent fr && fr.Result != null)
                    tokens += Pmad.AI.Tokens.HeuristicTokenEstimator.Default.Estimate(fr.Result.ToString() ?? string.Empty);
            }
        }
        return tokens;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(MockBenchmarkChatClient) ? this : null;

    public void Dispose()
    {
    }
}
