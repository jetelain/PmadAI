using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Pmad.AI.Benchmark.Scenarios;

namespace Pmad.AI.Benchmark.Engine;

/// <summary>
/// Executes benchmark scenarios against an <see cref="IChatClient"/> instance,
/// tracking latencies, token throughput, tool calling accuracy, and conversational continuity.
/// </summary>
public sealed class BenchmarkRunner
{
    private readonly IChatClient _client;
    private readonly string _modelId;
    private readonly bool _verbose;
    private readonly Action<string>? _logAction;

    public BenchmarkRunner(IChatClient client, string modelId, bool verbose = false, Action<string>? logAction = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _modelId = modelId ?? throw new ArgumentNullException(nameof(modelId));
        _verbose = verbose;
        _logAction = logAction;
    }

    /// <summary>
    /// Executes all provided scenarios and calculates aggregated model summary metrics.
    /// </summary>
    public async Task<ModelBenchmarkSummary> RunAllAsync(
        IReadOnlyList<BenchmarkScenario> scenarios,
        CancellationToken cancellationToken = default)
    {
        var summary = new ModelBenchmarkSummary
        {
            ModelId = _modelId,
            ModelName = _client.GetService<ChatClientMetadata>()?.DefaultModelId ?? _modelId,
            PeakMemoryMb = GetCurrentWorkingSetMb()
        };

        foreach (var scenario in scenarios)
        {
            var result = await RunScenarioAsync(scenario, cancellationToken);
            summary.ScenarioResults.Add(result);
            summary.ScenariosRun++;

            if (result.Status == BenchmarkStatus.Passed)
            {
                summary.ScenariosPassed++;
            }
            else
            {
                summary.ScenariosFailed++;
            }

            summary.TotalTimeMs += result.TotalDurationMs;
            summary.TotalPromptTokens += result.TotalPromptTokens;
            summary.TotalOutputTokens += result.TotalOutputTokens;
            summary.TotalToolCalls += result.TotalToolCalls;

            if (result.PeakMemoryMb > summary.PeakMemoryMb)
            {
                summary.PeakMemoryMb = result.PeakMemoryMb;
            }
        }

        var currentMem = GetCurrentWorkingSetMb();
        if (currentMem > summary.PeakMemoryMb)
        {
            summary.PeakMemoryMb = currentMem;
        }

        if (summary.TotalTimeMs > 0)
        {
            summary.AverageGenerationTokensPerSecond = summary.TotalOutputTokens / (summary.TotalTimeMs / 1000.0);
        }

        return summary;
    }

    /// <summary>
    /// Executes a single benchmark scenario.
    /// </summary>
    public async Task<BenchmarkScenarioResult> RunScenarioAsync(
        BenchmarkScenario scenario,
        CancellationToken cancellationToken = default)
    {
        Log($"\n>>> Running Scenario: [{scenario.Id}] \"{scenario.Name}\"");

        var scenarioResult = new BenchmarkScenarioResult
        {
            ScenarioId = scenario.Id,
            ScenarioName = scenario.Name,
            ModelId = _modelId,
            PeakMemoryMb = GetCurrentWorkingSetMb()
        };

        var scenarioStopwatch = Stopwatch.StartNew();
        var history = new List<ChatMessage>();
        var chatOptions = new ChatOptions
        {
            Tools = scenario.Tools.ToList()
        };

        int expectedToolCallMatches = 0;
        int totalExpectedToolCalls = 0;

        for (int turnIdx = 0; turnIdx < scenario.Turns.Count; turnIdx++)
        {
            var turn = scenario.Turns[turnIdx];
            totalExpectedToolCalls += turn.ExpectedToolNames.Count;

            Log($"  [Turn {turnIdx + 1}/{scenario.Turns.Count}] User: \"{turn.UserPrompt}\"");

            var turnResult = new TurnResult
            {
                TurnIndex = turnIdx + 1,
                UserPrompt = turn.UserPrompt,
                Status = BenchmarkStatus.Passed
            };

            var turnStopwatch = Stopwatch.StartNew();
            history.Add(new ChatMessage(ChatRole.User, turn.UserPrompt));

            int toolIterations = 0;
            bool turnCompleted = false;

            while (toolIterations < scenario.MaxToolIterationsPerTurn && !turnCompleted)
            {
                toolIterations++;
                ChatResponse response;

                try
                {
                    response = await _client.GetResponseAsync(history, chatOptions, cancellationToken);
                }
                catch (Exception ex)
                {
                    turnResult.Status = BenchmarkStatus.Failed;
                    turnResult.ValidationMessage = $"Client exception during GetResponseAsync: {ex.Message}";
                    scenarioResult.ErrorMessage = ex.Message;
                    break;
                }

                if (response.Usage != null)
                {
                    turnResult.PromptTokens += (int)(response.Usage.InputTokenCount ?? 0);
                    turnResult.OutputTokens += (int)(response.Usage.OutputTokenCount ?? 0);
                }

                var assistantMessage = response.Messages.FirstOrDefault()
                    ?? new ChatMessage(ChatRole.Assistant, [new TextContent(string.Empty)]);

                history.Add(assistantMessage);

                // Collect reasoning
                foreach (var reasoning in assistantMessage.Contents.OfType<TextReasoningContent>())
                {
                    turnResult.AssistantReasoning = reasoning.Text;
                    if (_verbose)
                    {
                        Log($"    [Reasoning] {reasoning.Text}");
                    }
                }

                // Check for function calls
                var functionCalls = assistantMessage.Contents.OfType<FunctionCallContent>().ToList();
                if (functionCalls.Count > 0)
                {
                    turnResult.ToolCalls.AddRange(functionCalls);
                    scenarioResult.TotalToolCalls += functionCalls.Count;

                    var toolResultContents = new List<AIContent>();

                    foreach (var call in functionCalls)
                    {
                        Log($"    [Model requested tool] {call.Name}({JsonSerializer.Serialize(call.Arguments)})");

                        // Execute mock tool handler
                        object? execResult;
                        try
                        {
                            execResult = turn.ToolExecutor(call);
                        }
                        catch (Exception ex)
                        {
                            execResult = new { error = $"Tool execution error: {ex.Message}" };
                        }

                        var resultJson = execResult is string strRes ? strRes : JsonSerializer.Serialize(execResult);
                        Log($"    [Tool executed -> result] {resultJson}");

                        toolResultContents.Add(new FunctionResultContent(call.CallId ?? Guid.NewGuid().ToString("N"), resultJson));
                    }

                    // Feed tool outputs back to conversation
                    history.Add(new ChatMessage(ChatRole.Tool, toolResultContents));
                }
                else
                {
                    // No further tool calls, assistant provided direct answer
                    turnResult.AssistantFinalText = assistantMessage.Text ?? string.Empty;
                    Log($"    [Assistant]: \"{turnResult.AssistantFinalText}\"");
                    turnCompleted = true;
                }
            }

            turnStopwatch.Stop();
            turnResult.DurationMs = turnStopwatch.Elapsed.TotalMilliseconds;

            // Validate Turn Expectations
            // 1. Tool name matching
            if (turn.ExpectedToolNames.Count > 0)
            {
                var actualToolNames = turnResult.ToolCalls.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var expectedName in turn.ExpectedToolNames)
                {
                    if (actualToolNames.Contains(expectedName))
                    {
                        expectedToolCallMatches++;
                    }
                    else
                    {
                        turnResult.Status = BenchmarkStatus.Failed;
                        turnResult.ValidationMessage = $"Expected tool call '{expectedName}' was not invoked by model.";
                    }
                }
            }
            else if (turnResult.ToolCalls.Count > 0)
            {
                // Negative test: expected NO tool calls, but some were made
                turnResult.Status = BenchmarkStatus.Failed;
                turnResult.ValidationMessage = $"Model invoked {turnResult.ToolCalls.Count} tool call(s) when 0 were expected.";
            }

            // 2. Custom Validator
            if (turnResult.Status == BenchmarkStatus.Passed && turn.CustomValidator != null)
            {
                var valCtx = new TurnValidationContext(
                    TurnIndex: turnIdx + 1,
                    UserPrompt: turn.UserPrompt,
                    ActualToolCalls: turnResult.ToolCalls,
                    AssistantFinalText: turnResult.AssistantFinalText,
                    AssistantReasoning: turnResult.AssistantReasoning,
                    CompletedWithoutError: turnCompleted
                );

                var validationOutcome = turn.CustomValidator(valCtx);
                if (!validationOutcome.IsSuccess)
                {
                    turnResult.Status = BenchmarkStatus.Failed;
                    turnResult.ValidationMessage = validationOutcome.Message ?? "Turn custom validation failed.";
                }
            }

            scenarioResult.TurnResults.Add(turnResult);
            scenarioResult.TotalPromptTokens += turnResult.PromptTokens;
            scenarioResult.TotalOutputTokens += turnResult.OutputTokens;

            if (turnResult.Status == BenchmarkStatus.Failed)
            {
                scenarioResult.Status = BenchmarkStatus.Failed;
                Log($"  ❌ [Turn {turnIdx + 1} FAILED]: {turnResult.ValidationMessage}");
            }
            else
            {
                Log($"  ✅ [Turn {turnIdx + 1} PASSED] ({turnResult.DurationMs:F0} ms, {turnResult.OutputTokens} tokens)");
            }

            var currentMem = GetCurrentWorkingSetMb();
            if (currentMem > scenarioResult.PeakMemoryMb)
            {
                scenarioResult.PeakMemoryMb = currentMem;
            }
        }

        scenarioStopwatch.Stop();
        scenarioResult.TotalDurationMs = scenarioStopwatch.Elapsed.TotalMilliseconds;

        var endMem = GetCurrentWorkingSetMb();
        if (endMem > scenarioResult.PeakMemoryMb)
        {
            scenarioResult.PeakMemoryMb = endMem;
        }

        if (totalExpectedToolCalls > 0)
        {
            scenarioResult.ToolAccuracyPercentage = Math.Round(expectedToolCallMatches * 100.0 / totalExpectedToolCalls, 1);
        }

        Log($"--- Scenario [{scenario.Id}] Completed: {scenarioResult.Status} in {scenarioResult.TotalDurationMs:F0} ms (Gen: {scenarioResult.GenerationTokensPerSecond:F1} tok/s, RAM: {FormatMemory(scenarioResult.PeakMemoryMb)}) ---");

        return scenarioResult;
    }

    private static double GetCurrentWorkingSetMb()
    {
        try
        {
            using var proc = Process.GetCurrentProcess();
            proc.Refresh();
            return Math.Round(proc.WorkingSet64 / (1024.0 * 1024.0), 1);
        }
        catch
        {
            return 0;
        }
    }

    public static string FormatMemory(double megabytes)
    {
        if (megabytes >= 1024.0)
        {
            return $"{megabytes / 1024.0:F2} GB";
        }
        return $"{megabytes:F0} MB";
    }

    private void Log(string message)
    {
        if (_logAction != null)
        {
            _logAction(message);
        }
        else
        {
            Console.WriteLine(message);
        }
    }
}
