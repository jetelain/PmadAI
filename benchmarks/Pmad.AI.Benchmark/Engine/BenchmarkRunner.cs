using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Pmad.AI.Benchmark.Scenarios;
using Pmad.AI.Compaction;
using Pmad.AI.Guardrails;
using Pmad.AI.Healing;
using Pmad.AI.Tools;
using Pmad.AI.Transcripts;

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
    private readonly string? _backend;
    private readonly int _gpuLayers;
    private readonly bool? _reasoningEnabled;
    private readonly string? _reasoningEffort;
    private readonly string? _configurationLabel;
    private readonly string? _optimizationConfigId;
    private readonly string? _optimizationConfigLabel;
    private readonly string? _transcriptPath;
    private readonly float? _temperature;
    private readonly long? _seed;
    private readonly TimeSpan _turnTimeout;

    public BenchmarkRunner(
        IChatClient client,
        string modelId,
        bool verbose = false,
        Action<string>? logAction = null,
        string? backend = null,
        int gpuLayers = 0,
        bool? reasoningEnabled = null,
        string? reasoningEffort = null,
        string? configurationLabel = null,
        string? optimizationConfigId = null,
        string? optimizationConfigLabel = null,
        string? transcriptPath = null,
        float? temperature = null,
        long? seed = null,
        TimeSpan? turnTimeout = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _modelId = modelId ?? throw new ArgumentNullException(nameof(modelId));
        _verbose = verbose;
        _logAction = logAction;
        _backend = backend;
        _gpuLayers = gpuLayers;
        _reasoningEnabled = reasoningEnabled;
        _reasoningEffort = reasoningEffort;
        _configurationLabel = configurationLabel;
        _optimizationConfigId = optimizationConfigId;
        _optimizationConfigLabel = optimizationConfigLabel;
        _transcriptPath = transcriptPath;
        _temperature = temperature;
        _seed = seed;
        _turnTimeout = turnTimeout ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Executes all provided scenarios and calculates aggregated model summary metrics.
    /// </summary>
    public async Task<ModelBenchmarkSummary> RunAllAsync(
        IReadOnlyList<BenchmarkScenario> scenarios,
        Func<BenchmarkScenarioResult, ModelBenchmarkSummary, Task>? onScenarioCompleted = null,
        IReadOnlyList<BenchmarkScenarioResult>? resumeScenarioResults = null,
        CancellationToken cancellationToken = default)
    {
        var isGpu = _gpuLayers > 0 || string.Equals(_backend, "Vulkan", StringComparison.OrdinalIgnoreCase);
        var baselineVram = isGpu ? (NvidiaSmiMonitor.GetUsedVramMb() ?? 0) : 0;

        var summary = new ModelBenchmarkSummary
        {
            ModelId = _modelId,
            ModelName = !string.IsNullOrWhiteSpace(_modelId) ? _modelId : (_client.GetService<ChatClientMetadata>()?.DefaultModelId ?? "Unknown"),
            Backend = _backend,
            GpuLayers = _gpuLayers,
            ReasoningEnabled = _reasoningEnabled,
            ReasoningEffort = _reasoningEffort,
            ConfigurationLabel = _configurationLabel,
            OptimizationConfigId = _optimizationConfigId,
            OptimizationConfigLabel = _optimizationConfigLabel,
            TranscriptPath = _transcriptPath,
            PeakMemoryMb = GetCurrentWorkingSetMb(),
            BaselineVramMb = baselineVram,
            PeakVramMb = baselineVram
        };

        foreach (var scenario in scenarios)
        {
            var alreadyDoneScenario = resumeScenarioResults?.FirstOrDefault(r =>
                string.Equals(r.ScenarioId, scenario.Id, StringComparison.OrdinalIgnoreCase));

            BenchmarkScenarioResult result;
            if (alreadyDoneScenario != null)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"\n>>> Skipping Scenario (Already Completed): [{scenario.Id}] \"{scenario.Name}\" ({alreadyDoneScenario.Score:F0}% score)");
                Console.ResetColor();
                result = alreadyDoneScenario;
            }
            else
            {
                try
                {
                    result = await RunScenarioAsync(scenario, cancellationToken);
                }
                catch (Exception ex)
                {
                    _client.GetService<VerbatimTranscriptChatClient>()?.AddNote($"> [!CAUTION]\n> **SCENARIO ERROR:** {ex.Message}", scenario.Id);
                    result = new BenchmarkScenarioResult
                    {
                        ScenarioId = scenario.Id,
                        ScenarioName = scenario.Name,
                        ModelId = _modelId,
                        Status = BenchmarkStatus.Failed,
                        TaskSuccess = false,
                        StepAccuracy = 0.0,
                        Score = 0.0,
                        Weight = scenario.Weight,
                        TotalTurns = scenario.Turns.Count,
                        TurnsPassed = 0,
                        ErrorMessage = $"Scenario unhandled exception: {ex.Message}"
                    };
                }
            }

            summary.ScenarioResults.Add(result);
            summary.ScenariosRun++;

            if (result.Status == BenchmarkStatus.Passed)
            {
                summary.ScenariosPassed++;
            }
            else if (result.Status == BenchmarkStatus.Partial)
            {
                summary.ScenariosPartial++;
            }
            else
            {
                summary.ScenariosFailed++;
            }

            summary.TotalTimeMs += result.TotalDurationMs;
            summary.TotalPromptTokens += result.TotalPromptTokens;
            summary.TotalOutputTokens += result.TotalOutputTokens;
            summary.TotalReasoningTokens += result.TotalReasoningTokens;
            summary.TotalToolCalls += result.TotalToolCalls;

            if (result.PeakContextTokens > summary.PeakContextTokens)
            {
                summary.PeakContextTokens = result.PeakContextTokens;
            }

            if (result.PeakMemoryMb > summary.PeakMemoryMb)
            {
                summary.PeakMemoryMb = result.PeakMemoryMb;
            }

            if (result.PeakVramMb > summary.PeakVramMb)
            {
                summary.PeakVramMb = result.PeakVramMb;
            }

            if (onScenarioCompleted != null && alreadyDoneScenario == null)
            {
                try
                {
                    await onScenarioCompleted(result, summary);
                }
                catch
                {
                    // Best effort checkpoint callback
                }
            }
        }

        var currentMem = GetCurrentWorkingSetMb();
        if (currentMem > summary.PeakMemoryMb)
        {
            summary.PeakMemoryMb = currentMem;
        }

        if (isGpu)
        {
            var currentVram = NvidiaSmiMonitor.GetUsedVramMb() ?? 0;
            if (currentVram > summary.PeakVramMb)
            {
                summary.PeakVramMb = currentVram;
            }
        }

        if (summary.TotalTimeMs > 0)
        {
            summary.AverageGenerationTokensPerSecond = summary.TotalOutputTokens / (summary.TotalTimeMs / 1000.0);
        }

        var compactionTelemetry = _client.GetService<CompactionTelemetry>();
        var healingTelemetry = _client.GetService<ToolCallHealingTelemetry>();
        var breakerTelemetry = _client.GetService<CircuitBreakerTelemetry>();
        var dispatchTelemetry = _client.GetService<LenientDispatchTelemetry>();

        summary.CompactionStats = compactionTelemetry?.CreateSnapshot();
        summary.HealingStats = healingTelemetry?.CreateSnapshot();
        summary.CircuitBreakerStats = breakerTelemetry?.CreateSnapshot();
        summary.DispatchStats = dispatchTelemetry?.CreateSnapshot();

        var verbatimClient = _client.GetService<VerbatimTranscriptChatClient>();
        if (verbatimClient != null && !string.IsNullOrWhiteSpace(_transcriptPath))
        {
            try
            {
                await verbatimClient.SaveTranscriptAsync(_transcriptPath, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Best effort save
            }
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

        _client.GetService<Pmad.AI.Guardrails.ToolLoopCircuitBreaker>()?.Reset();
        scenario.OnScenarioStart();

        var compaction = _client.GetService<CompactionTelemetry>();
        var prevPrunedChars = compaction?.TotalPrunedChars ?? 0;

        var isGpu = _gpuLayers > 0 || string.Equals(_backend, "Vulkan", StringComparison.OrdinalIgnoreCase);
        var scenarioResult = new BenchmarkScenarioResult
        {
            ScenarioId = scenario.Id,
            ScenarioName = scenario.Name,
            ModelId = _modelId,
            Weight = scenario.Weight,
            TotalTurns = scenario.Turns.Count,
            ExecutionDetails = (scenario as Scenarios.SweBench.SweBenchTraceScenario)?.Executor.ExecutionMode,
            PeakMemoryMb = GetCurrentWorkingSetMb(),
            PeakVramMb = isGpu ? (NvidiaSmiMonitor.GetUsedVramMb() ?? 0) : 0
        };

        var scenarioStopwatch = Stopwatch.StartNew();
        var history = new List<ChatMessage>();

        int expectedToolCallMatches = 0;
        int totalExpectedToolCalls = 0;

        for (int turnIdx = 0; turnIdx < scenario.Turns.Count; turnIdx++)
        {
            var turn = scenario.Turns[turnIdx];
            totalExpectedToolCalls += turn.ExpectedToolNames.Count;

            if (turn.ClearHistoryBeforeTurn)
            {
                history.Clear();
            }

            var chatOptions = new ChatOptions
            {
                ConversationId = scenario.Id,
                Tools = (turn.Tools ?? scenario.Tools).ToList(),
                Temperature = _temperature,
                Seed = _seed
            };

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

                using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                turnCts.CancelAfter(_turnTimeout);

                try
                {
                    response = await _client.GetResponseAsync(history, chatOptions, turnCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    var msg = $"Turn timed out after {_turnTimeout.TotalMinutes:0.#} minutes awaiting model response.";
                    Log($"    [ERROR] {msg}");
                    turnResult.Status = BenchmarkStatus.Failed;
                    turnResult.ValidationMessage = msg;
                    scenarioResult.ErrorMessage = msg;
                    break;
                }
                catch (Exception ex)
                {
                    Log($"    [ERROR] Client exception during GetResponseAsync: {ex.Message}");
                    turnResult.Status = BenchmarkStatus.Failed;
                    turnResult.ValidationMessage = $"Client exception during GetResponseAsync: {ex.Message}";
                    scenarioResult.ErrorMessage = ex.Message;
                    break;
                }

                if (response.Usage != null)
                {
                    var inTokens = (int)(response.Usage.InputTokenCount ?? 0);
                    turnResult.PromptTokens += inTokens;
                    turnResult.ContextTokens = inTokens;
                    turnResult.OutputTokens += (int)(response.Usage.OutputTokenCount ?? 0);

                    if (inTokens > scenarioResult.PeakContextTokens)
                    {
                        scenarioResult.PeakContextTokens = inTokens;
                    }
                }

                var assistantMessage = response.Messages.FirstOrDefault()
                    ?? new ChatMessage(ChatRole.Assistant, [new TextContent(string.Empty)]);

                history.Add(assistantMessage);

                // Collect reasoning
                foreach (var reasoning in assistantMessage.Contents.OfType<TextReasoningContent>())
                {
                    turnResult.AssistantReasoning = reasoning.Text;
                    var estTokens = Pmad.AI.Tokens.HeuristicTokenEstimator.Default.Estimate(reasoning.Text);
                    turnResult.ReasoningTokens += estTokens;
                    scenarioResult.TotalReasoningTokens += estTokens;

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

                        var callId = call.CallId ?? Guid.NewGuid().ToString("N");
                        var argsJson = JsonSerializer.Serialize(call.Arguments);
                        turnResult.ToolExecutions.Add(new ToolCallRecord(callId, call.Name, argsJson, resultJson));

                        toolResultContents.Add(new FunctionResultContent(callId, resultJson));
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

            var currentPrunedChars = compaction?.TotalPrunedChars ?? 0;
            var turnPruned = Math.Max(0, currentPrunedChars - prevPrunedChars);
            prevPrunedChars = currentPrunedChars;
            turnResult.CompactionPrunedChars = turnPruned;
            turnResult.CompactionPrunedTokens = (int)Math.Round(turnPruned / 4.0);

            // Validate Turn Expectations
            // 1. Tool name matching
            if (turnResult.AssistantFinalText.Contains("[Circuit Breaker Tripped]", StringComparison.OrdinalIgnoreCase))
            {
                turnResult.Status = BenchmarkStatus.Passed;
                turnResult.ValidationMessage = "Circuit breaker intercepted repetitive execution.";
            }
            else if (turn.AllowAnyTool)
            {
                // Trajectory replay / open coding exploration
                if (turnResult.ToolCalls.Count > 0)
                {
                    expectedToolCallMatches++;
                }
            }
            else if (turn.ExpectedToolNames.Count > 0)
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

            if (turn.IsRecallProbe && turnResult.Status == BenchmarkStatus.Failed)
            {
                turnResult.SemanticRecallPassed = false;
                scenarioResult.SemanticRecallPassed = false;
            }

            scenarioResult.TurnResults.Add(turnResult);
            scenarioResult.TotalPromptTokens += turnResult.PromptTokens;
            scenarioResult.TotalOutputTokens += turnResult.OutputTokens;

            var transcriptClient = _client.GetService<VerbatimTranscriptChatClient>();
            if (turnResult.Status == BenchmarkStatus.Failed)
            {
                Log($"  ❌ [Turn {turnIdx + 1} FAILED]: {turnResult.ValidationMessage}");
                transcriptClient?.RecordTurnOutcome(false, turnResult.ValidationMessage, scenario.Id);
            }
            else
            {
                Log($"  ✅ [Turn {turnIdx + 1} PASSED] ({turnResult.DurationMs:F0} ms, {turnResult.OutputTokens} tokens)");
                transcriptClient?.RecordTurnOutcome(true, "All turn assertions and tool expectations satisfied.", scenario.Id);
            }

            var currentMem = GetCurrentWorkingSetMb();
            if (currentMem > scenarioResult.PeakMemoryMb)
            {
                scenarioResult.PeakMemoryMb = currentMem;
            }

            if (isGpu)
            {
                var vram = NvidiaSmiMonitor.GetUsedVramMb();
                if (vram.HasValue && vram.Value > scenarioResult.PeakVramMb)
                {
                    scenarioResult.PeakVramMb = vram.Value;
                }
            }
        }

        scenarioStopwatch.Stop();
        scenarioResult.TotalDurationMs = scenarioStopwatch.Elapsed.TotalMilliseconds;

        // Compute scoring metrics: TaskSuccess (Binary Pass@1) & StepAccuracy (turn-by-turn)
        scenarioResult.Suite = scenario.Suite;
        scenarioResult.TurnsPassed = scenarioResult.TurnResults.Count(t => t.Status == BenchmarkStatus.Passed);
        scenarioResult.TotalTurns = scenario.Turns.Count;
        scenarioResult.StepAccuracy = scenarioResult.TotalTurns > 0
            ? Math.Round(scenarioResult.TurnsPassed * 100.0 / scenarioResult.TotalTurns, 1)
            : 0.0;

        scenarioResult.TaskSuccess = scenario.EvaluateTaskSuccess(scenarioResult);
        scenarioResult.Score = scenario.ComputeScore(scenarioResult);
        scenarioResult.FailureTaxonomy = FailureTaxonomyClassifier.Classify(scenarioResult);

        if (scenarioResult.TaskSuccess && scenarioResult.StepAccuracy >= 100.0)
        {
            scenarioResult.Status = BenchmarkStatus.Passed;
        }
        else if (scenarioResult.TaskSuccess || scenarioResult.StepAccuracy > 0.0)
        {
            scenarioResult.Status = BenchmarkStatus.Partial;
        }
        else
        {
            scenarioResult.Status = BenchmarkStatus.Failed;
        }

        var healing = _client.GetService<ToolCallHealingTelemetry>();
        var breaker = _client.GetService<CircuitBreakerTelemetry>();
        var dispatch = _client.GetService<LenientDispatchTelemetry>();

        scenarioResult.CompactionStats = compaction?.CreateSnapshot(scenario.Id);
        scenarioResult.HealingStats = healing?.CreateSnapshot(scenario.Id);
        scenarioResult.CircuitBreakerStats = breaker?.CreateSnapshot(scenario.Id);
        scenarioResult.DispatchStats = dispatch?.CreateSnapshot(scenario.Id);

        var endMem = GetCurrentWorkingSetMb();
        if (endMem > scenarioResult.PeakMemoryMb)
        {
            scenarioResult.PeakMemoryMb = endMem;
        }

        if (isGpu)
        {
            var endVram = NvidiaSmiMonitor.GetUsedVramMb();
            if (endVram.HasValue && endVram.Value > scenarioResult.PeakVramMb)
            {
                scenarioResult.PeakVramMb = endVram.Value;
            }
        }

        if (totalExpectedToolCalls > 0)
        {
            scenarioResult.ToolAccuracyPercentage = Math.Round(expectedToolCallMatches * 100.0 / totalExpectedToolCalls, 1);
        }

        var memStr = $"RAM: {FormatMemory(scenarioResult.PeakMemoryMb)}";
        if (scenarioResult.PeakVramMb > 0)
        {
            memStr += $", VRAM: {FormatMemory(scenarioResult.PeakVramMb)}";
        }

        Log($"--- Scenario [{scenario.Id}] Completed: {scenarioResult.Status} (Score: {scenarioResult.Score:F1}%, {scenarioResult.TurnsPassed}/{scenarioResult.TotalTurns} turns, Weight: {scenarioResult.Weight:F1}) in {scenarioResult.TotalDurationMs:F0} ms (Gen: {scenarioResult.GenerationTokensPerSecond:F1} tok/s, {memStr}) ---");

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
