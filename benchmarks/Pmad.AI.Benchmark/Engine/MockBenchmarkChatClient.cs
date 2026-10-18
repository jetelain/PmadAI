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

        // Compaction Trajectory Scenarios
        if (options?.ConversationId?.StartsWith("compaction", StringComparison.OrdinalIgnoreCase) == true)
        {
            var compactionTrajectories = new[]
            {
                Scenarios.Compaction.CompactionTrajectoryLoader.LoadLargeToolTrajectory(),
                Scenarios.Compaction.CompactionTrajectoryLoader.LoadDeepContextTrajectory()
            };

            foreach (var traj in compactionTrajectories)
            {
                var matchedTurn = traj.Turns.FirstOrDefault(t =>
                    !string.IsNullOrEmpty(t.UserPrompt) &&
                    (string.Equals(t.UserPrompt.Trim(), userText.Trim(), StringComparison.OrdinalIgnoreCase) ||
                     userText.Contains(t.UserPrompt, StringComparison.OrdinalIgnoreCase)));

                if (matchedTurn != null)
                {
                    if (!string.IsNullOrEmpty(matchedTurn.ExpectedToolName) && toolsAvailable)
                    {
                        var args = new Dictionary<string, object?>
                        {
                            ["target"] = $"step_{matchedTurn.TurnIndex}",
                            ["query"] = matchedTurn.UserPrompt
                        };
                        return (
                            [new FunctionCallContent($"call_compaction_{matchedTurn.TurnIndex}", matchedTurn.ExpectedToolName, args)],
                            ChatFinishReason.ToolCalls,
                            PromptTokens: promptTokens,
                            OutputTokens: 25
                        );
                    }

                    return (
                        [new TextContent(matchedTurn.ExpectedCompletionSubstring != null
                            ? $"Completed task: root cause resolved and verified under {matchedTurn.ExpectedCompletionSubstring}"
                            : "Diagnostics complete.")],
                        ChatFinishReason.Stop,
                        PromptTokens: promptTokens,
                        OutputTokens: 20
                    );
                }
            }
        }

        // RULER Scenarios
        if (toolsAvailable && userText.Contains("fetch the system architecture and audit logs for cluster 'omega-9'", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new FunctionCallContent("call_audit_omega9", "fetch_audit_logs", new Dictionary<string, object?> { ["partition"] = "omega-9" })],
                ChatFinishReason.ToolCalls,
                PromptTokens: promptTokens,
                OutputTokens: 20
            );
        }

        if (userText.Contains("answer the following three questions", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new TextContent(
                    "1. The emergency rollback authorization secret key is ROLLBACK-SEC-99042.\n" +
                    "2. The migrated target IP address for DB-NODE-ALPHA is 10.240.18.42.\n" +
                    "3. The maximum allowable connection timeout for cluster 'omega-9' is 850 milliseconds.")],
                ChatFinishReason.Stop,
                PromptTokens: promptTokens,
                OutputTokens: 45
            );
        }

        // BFCL Multi-Turn Scenario
        if (toolsAvailable && userText.Contains("sarah_dev", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new FunctionCallContent("call_mt_1", "get_customer_profile", new Dictionary<string, object?> { ["username"] = "sarah_dev" })],
                ChatFinishReason.ToolCalls,
                PromptTokens: promptTokens,
                OutputTokens: 25
            );
        }
        if (toolsAvailable && userText.Contains("Sarah's default wallet", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new FunctionCallContent("call_mt_2", "get_wallet_balance", new Dictionary<string, object?> { ["wallet_id"] = "wal_55410" })],
                ChatFinishReason.ToolCalls,
                PromptTokens: promptTokens,
                OutputTokens: 25
            );
        }
        if (toolsAvailable && userText.Contains("Authorize a payment of 350.00 USD", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new FunctionCallContent("call_mt_3", "authorize_payment", new Dictionary<string, object?>
                {
                    ["wallet_id"] = "wal_55410",
                    ["vendor_name"] = "AWS-INFRA",
                    ["amount"] = 350.00,
                    ["currency"] = "USD"
                })],
                ChatFinishReason.ToolCalls,
                PromptTokens: promptTokens,
                OutputTokens: 35
            );
        }
        if (toolsAvailable && userText.Contains("Commit the authorized payment auth_99182a", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new FunctionCallContent("call_mt_4", "commit_payment_and_invoice", new Dictionary<string, object?> { ["auth_code"] = "auth_99182a" })],
                ChatFinishReason.ToolCalls,
                PromptTokens: promptTokens,
                OutputTokens: 25
            );
        }
        if (userText.Contains("Please summarize the invoice number and whether the payment is settled", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new TextContent("The payment has been settled successfully under invoice number INV-2026-4401.")],
                ChatFinishReason.Stop,
                PromptTokens: promptTokens,
                OutputTokens: 20
            );
        }
        if (toolsAvailable && userText.Contains("ticket for service 'auth-gateway'", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new FunctionCallContent("call_mt_5", "get_active_incidents", new Dictionary<string, object?> { ["service_name"] = "auth-gateway" })],
                ChatFinishReason.ToolCalls,
                PromptTokens: promptTokens,
                OutputTokens: 25
            );
        }
        if (toolsAvailable && userText.Contains("primary pod", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new FunctionCallContent("call_mt_6", "get_pod_diagnostics", new Dictionary<string, object?> { ["pod_name"] = "auth-gateway-pod-x89", ["include_logs"] = true })],
                ChatFinishReason.ToolCalls,
                PromptTokens: promptTokens,
                OutputTokens: 25
            );
        }
        if (toolsAvailable && userText.Contains("Patch deployment memory limit for incident INC-7704", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new FunctionCallContent("call_mt_7", "patch_deployment_resource", new Dictionary<string, object?>
                {
                    ["incident_id"] = "INC-7704",
                    ["resource_type"] = "memory_mb",
                    ["new_value"] = 2048
                })],
                ChatFinishReason.ToolCalls,
                PromptTokens: promptTokens,
                OutputTokens: 30
            );
        }
        if (toolsAvailable && userText.Contains("Resolve incident INC-7704", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new FunctionCallContent("call_mt_8", "resolve_incident", new Dictionary<string, object?>
                {
                    ["incident_id"] = "INC-7704",
                    ["resolution_notes"] = "Scaled memory limit to 2048MB"
                })],
                ChatFinishReason.ToolCalls,
                PromptTokens: promptTokens,
                OutputTokens: 30
            );
        }

        // BFCL Strict Scenario
        if (toolsAvailable && userText.Contains("Kubernetes production workload 'api-gateway'", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new FunctionCallContent("call_strict_1", "deploy_kubernetes_workload", new Dictionary<string, object?>
                {
                    ["spec"] = new Dictionary<string, object?>
                    {
                        ["name"] = "api-gateway",
                        ["replicas"] = 3,
                        ["resources"] = new Dictionary<string, object?>
                        {
                            ["cpu_cores"] = 2.5,
                            ["memory_gib"] = 4.0
                        },
                        ["environment"] = "production"
                    }
                })],
                ChatFinishReason.ToolCalls,
                PromptTokens: promptTokens,
                OutputTokens: 40
            );
        }
        if (toolsAvailable && userText.Contains("alert_cpu_high", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new FunctionCallContent("call_strict_2", "configure_alert_rule", new Dictionary<string, object?>
                {
                    ["alert_id"] = "alert_cpu_high",
                    ["threshold"] = 95.5,
                    ["severity"] = "CRITICAL",
                    ["notification"] = new Dictionary<string, object?>
                    {
                        ["channel"] = "slack",
                        ["target"] = "#ops-alerts",
                        ["cooldown_minutes"] = 15
                    }
                })],
                ChatFinishReason.ToolCalls,
                PromptTokens: promptTokens,
                OutputTokens: 45
            );
        }
        if (toolsAvailable && userText.Contains("payment transaction for 199.99 USD", StringComparison.OrdinalIgnoreCase))
        {
            return (
                [new FunctionCallContent("call_strict_3", "create_payment_transaction", new Dictionary<string, object?>
                {
                    ["amount"] = 199.99,
                    ["currency"] = "USD",
                    ["metadata"] = new Dictionary<string, object?>
                    {
                        ["order_id"] = "ord_8842",
                        ["customer"] = new Dictionary<string, object?>
                        {
                            ["account_number"] = 100982,
                            ["tax_id"] = "US-TAX-4491"
                        }
                    }
                })],
                ChatFinishReason.ToolCalls,
                PromptTokens: promptTokens,
                OutputTokens: 45
            );
        }

        // BFCL Scenarios (dynamic lookup from loaded fixtures)
        var bfclEntries = Scenarios.Bfcl.BfclDatasetLoader.LoadEntries("all");
        var availableTools = options?.Tools?.Select(t => t.Name).Where(n => !string.IsNullOrEmpty(n)).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var matchedBfcl = bfclEntries.FirstOrDefault(e =>
            (string.Equals(e.Question.Trim(), userText.Trim(), StringComparison.OrdinalIgnoreCase) ||
             userText.Contains(e.Question, StringComparison.OrdinalIgnoreCase)) &&
            (availableTools.Count == 0 || e.Tools.Any(t => availableTools.Contains(t.Name))))
            ?? bfclEntries.FirstOrDefault(e =>
            string.Equals(e.Question.Trim(), userText.Trim(), StringComparison.OrdinalIgnoreCase) ||
            userText.Contains(e.Question, StringComparison.OrdinalIgnoreCase));

        if (matchedBfcl != null)
        {
            if (matchedBfcl.GroundTruth.Count == 0)
            {
                return (
                    [new TextContent($"Direct answer to: {matchedBfcl.Question}")],
                    ChatFinishReason.Stop,
                    PromptTokens: promptTokens,
                    OutputTokens: 15
                );
            }

            if (toolsAvailable)
            {
                var calls = new List<AIContent>();
                int callIdx = 1;
                foreach (var gt in matchedBfcl.GroundTruth)
                {
                    calls.Add(new FunctionCallContent(
                        $"call_{callIdx++}",
                        gt.Name,
                        new Dictionary<string, object?>(gt.Arguments)));
                }

                return (
                    calls,
                    ChatFinishReason.ToolCalls,
                    PromptTokens: promptTokens,
                    OutputTokens: 25 * calls.Count
                );
            }
        }

        // SWE-bench Trajectory Scenarios (dynamic lookup from loaded fixtures)
        var sweTrajectories = Scenarios.SweBench.SweBenchTrajectoryLoader.LoadAllArchetypes();
        foreach (var traj in sweTrajectories)
        {
            var matchedTurn = traj.Turns.FirstOrDefault(t =>
                !string.IsNullOrEmpty(t.UserPrompt) &&
                (string.Equals(t.UserPrompt.Trim(), userText.Trim(), StringComparison.OrdinalIgnoreCase) ||
                 userText.Contains(t.UserPrompt, StringComparison.OrdinalIgnoreCase)));

            if (matchedTurn != null)
            {
                if (!string.IsNullOrEmpty(matchedTurn.ExpectedToolName) && toolsAvailable)
                {
                    var args = matchedTurn.GetArgumentsDictionary();
                    return (
                        [new FunctionCallContent($"call_{matchedTurn.TurnIndex}", matchedTurn.ExpectedToolName, args)],
                        ChatFinishReason.ToolCalls,
                        PromptTokens: promptTokens,
                        OutputTokens: 25
                    );
                }

                return (
                    [new TextContent(matchedTurn.ExpectedCompletionSubstring ?? "Task completed successfully based on trajectory step.")],
                    ChatFinishReason.Stop,
                    PromptTokens: promptTokens,
                    OutputTokens: 20
                );
            }
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
