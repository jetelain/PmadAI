using Microsoft.Extensions.AI;
using Pmad.AI.Compaction;
using Pmad.AI.Guardrails;
using Pmad.AI.Healing;
using Pmad.AI.Tools;

namespace Pmad.AI.Benchmark.Engine;

/// <summary>
/// Execution status for a benchmark turn or scenario.
/// </summary>
public enum BenchmarkStatus
{
    Passed,
    Partial,
    Failed,
    Skipped
}

/// <summary>
/// Detailed record of an invoked tool call and its execution result.
/// </summary>
public sealed record ToolCallRecord(
    string CallId,
    string ToolName,
    string ArgumentsJson,
    string? ResultJson
);

/// <summary>
/// Detailed result metrics for a single turn in a benchmark scenario.
/// </summary>
public sealed class TurnResult
{
    public required int TurnIndex { get; init; }
    public required string UserPrompt { get; init; }
    public required BenchmarkStatus Status { get; set; }
    public double DurationMs { get; set; }
    public int PromptTokens { get; set; }
    public int OutputTokens { get; set; }
    public int ReasoningTokens { get; set; }
    public int ContextTokens { get; set; }
    public double TokensPerSecond => DurationMs > 0 ? (OutputTokens / (DurationMs / 1000.0)) : 0;
    public List<FunctionCallContent> ToolCalls { get; init; } = [];
    public List<ToolCallRecord> ToolExecutions { get; init; } = [];
    public string AssistantFinalText { get; set; } = string.Empty;
    public string? AssistantReasoning { get; set; }
    public string? ValidationMessage { get; set; }
    public bool SemanticRecallPassed { get; set; } = true;
    public int CompactionPrunedChars { get; set; }
    public int CompactionPrunedTokens { get; set; }
}

/// <summary>
/// Aggregate result metrics for a scenario execution against a model.
/// </summary>
public sealed class BenchmarkScenarioResult
{
    public required string ScenarioId { get; init; }
    public required string ScenarioName { get; init; }
    public required string ModelId { get; init; }
    public BenchmarkStatus Status { get; set; } = BenchmarkStatus.Passed;

    /// <summary>
    /// Whether the scenario achieved end-to-end task success (binary Pass@1).
    /// In SWE-bench, only true if the patch is applied and verification tests pass.
    /// In other scenarios, true if all turns pass assertions.
    /// </summary>
    public bool TaskSuccess { get; set; } = true;

    /// <summary>
    /// Optional environment or sandbox execution mode note (e.g. "Mock", "Docker (swebench_...)", "Docker (Fallback: Mock)").
    /// </summary>
    public string? ExecutionDetails { get; set; }

    /// <summary>
    /// Turn-by-turn step accuracy percentage (0.0 to 100.0) measuring tool adherence and partial progress.
    /// Formula: (TurnsPassed / TotalTurns) * 100.0.
    /// </summary>
    public double StepAccuracy { get; set; } = 100.0;

    /// <summary>
    /// Combined scenario evaluation score ranging from 0.0 to 100.0 based on dual scoring metrics:
    /// 70% TaskSuccess (Binary Pass@1) + 30% StepAccuracy (turn-by-turn).
    /// </summary>
    public double Score { get; set; } = 100.0;

    /// <summary>
    /// Importance weight assigned to this scenario based on turn count and complexity.
    /// </summary>
    public double Weight { get; set; } = 1.0;

    /// <summary>
    /// Number of subtest turns that passed validation.
    /// </summary>
    public int TurnsPassed { get; set; }

    /// <summary>
    /// Total number of subtest turns in this scenario.
    /// </summary>
    public int TotalTurns { get; set; }

    public double TotalDurationMs { get; set; }
    public int TotalPromptTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public int TotalReasoningTokens { get; set; }
    public int PeakContextTokens { get; set; }
    public int TotalTokens => TotalPromptTokens + TotalOutputTokens;
    public double GenerationTokensPerSecond => TotalDurationMs > 0 ? (TotalOutputTokens / (TotalDurationMs / 1000.0)) : 0;
    public int TotalToolCalls { get; set; }
    public double PromptTokensPerToolCall => TotalToolCalls > 0 ? Math.Round((double)TotalPromptTokens / TotalToolCalls, 1) : 0.0;
    public double ToolAccuracyPercentage { get; set; } = 100.0;
    public double PeakMemoryMb { get; set; }
    public double PeakVramMb { get; set; }
    public bool SemanticRecallPassed { get; set; } = true;
    public List<TurnResult> TurnResults { get; init; } = [];
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Benchmark suite category (e.g. "BFCL", "SWE-bench", "RULER").
    /// </summary>
    public string Suite { get; set; } = "General";

    /// <summary>
    /// Diagnostic failure reason if binary TaskSuccess was not achieved.
    /// </summary>
    public string? TaskFailureReason { get; set; }

    /// <summary>
    /// Diagnostic failure taxonomy classification (e.g. "Patch Rejected", "Tool Loop", "Missing Tool Call", "Wrong Tool", "Bad Arguments", "Over-Calling").
    /// </summary>
    public string? FailureTaxonomy { get; set; }

    public CompactionTelemetrySnapshot? CompactionStats { get; set; }
    public int CompactionPrunedChars => CompactionStats?.TotalPrunedChars ?? 0;
    public int CompactionPrunedTokens => CompactionStats?.EstimatedPrunedTokens ?? 0;

    public ToolCallHealingTelemetrySnapshot? HealingStats { get; set; }
    public CircuitBreakerTelemetrySnapshot? CircuitBreakerStats { get; set; }
    public LenientDispatchTelemetrySnapshot? DispatchStats { get; set; }
}

/// <summary>
/// Summary metrics for a model across all evaluated scenarios.
/// </summary>
public sealed class ModelBenchmarkSummary
{
    public required string ModelId { get; init; }
    public required string ModelName { get; init; }

    /// <summary>
    /// Hardware backend used for inference (e.g. "CPU", "Vulkan", "Mock").
    /// </summary>
    public string? Backend { get; set; }

    /// <summary>
    /// Number of GPU layers offloaded (0 for pure CPU).
    /// </summary>
    public int GpuLayers { get; set; }

    /// <summary>
    /// Whether chain-of-thought reasoning was enabled for this benchmark run.
    /// </summary>
    public bool? ReasoningEnabled { get; set; }

    /// <summary>
    /// Configured reasoning effort level (e.g. "low", "medium", "high", etc.).
    /// </summary>
    public string? ReasoningEffort { get; set; }

    /// <summary>
    /// Short label describing the execution configuration, e.g. "Vulkan | Reasoning" or "CPU | No Reasoning".
    /// </summary>
    public string? ConfigurationLabel { get; set; }

    /// <summary>
    /// Optimization configuration ID (e.g. "baseline", "opt-healing", "all-optimizations").
    /// </summary>
    public string? OptimizationConfigId { get; set; }

    /// <summary>
    /// Optimization configuration label (e.g. "Baseline (Raw)", "All Optimizations").
    /// </summary>
    public string? OptimizationConfigLabel { get; set; }

    /// <summary>
    /// Combined model name and configuration label for display.
    /// </summary>
    public string DisplayName => string.IsNullOrWhiteSpace(ConfigurationLabel)
        ? ModelName
        : $"{ModelName} [{ConfigurationLabel}]";

    public int ScenariosRun { get; set; }
    public int ScenariosPassed { get; set; }
    public int ScenariosPartial { get; set; }
    public int ScenariosFailed { get; set; }

    /// <summary>
    /// Overall binary Task Success (Pass@1) rate percentage across all evaluated scenarios.
    /// </summary>
    public double TaskSuccessRatePercentage
    {
        get
        {
            if (ScenarioResults.Count == 0) return 0.0;
            var totalWeight = ScenarioResults.Sum(s => s.Weight);
            return totalWeight > 0
                ? Math.Round(ScenarioResults.Sum(s => (s.TaskSuccess ? 100.0 : 0.0) * s.Weight) / totalWeight, 1)
                : Math.Round(ScenarioResults.Count(s => s.TaskSuccess) * 100.0 / ScenarioResults.Count, 1);
        }
    }

    /// <summary>
    /// Overall turn-by-turn Step Accuracy percentage across all evaluated scenarios.
    /// </summary>
    public double StepAccuracyPercentage
    {
        get
        {
            if (ScenarioResults.Count == 0) return 0.0;
            var totalWeight = ScenarioResults.Sum(s => s.Weight);
            return totalWeight > 0
                ? Math.Round(ScenarioResults.Sum(s => s.StepAccuracy * s.Weight) / totalWeight, 1)
                : Math.Round(ScenarioResults.Average(s => s.StepAccuracy), 1);
        }
    }

    /// <summary>
    /// Overall benchmark pass rate percentage computed as sum(score * weight) / sum(weight),
    /// reflecting the dual metric (70% Task Success + 30% Step Accuracy).
    /// </summary>
    public double PassRatePercentage
    {
        get
        {
            if (ScenarioResults.Count > 0)
            {
                var totalWeight = ScenarioResults.Sum(s => s.Weight);
                if (totalWeight > 0)
                {
                    return Math.Round(ScenarioResults.Sum(s => s.Score * s.Weight) / totalWeight, 1);
                }
            }

            return ScenariosRun > 0 ? Math.Round(ScenariosPassed * 100.0 / ScenariosRun, 1) : 0.0;
        }
    }
    public double TotalTimeMs { get; set; }
    public int TotalPromptTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public int TotalReasoningTokens { get; set; }
    public int PeakContextTokens { get; set; }
    public double AverageGenerationTokensPerSecond { get; set; }
    public int TotalToolCalls { get; set; }
    public double PeakMemoryMb { get; set; }

    /// <summary>
    /// Peak GPU VRAM in MiB observed via nvidia-smi when running on GPU.
    /// </summary>
    public double PeakVramMb { get; set; }

    /// <summary>
    /// Baseline VRAM in MiB before model execution was started.
    /// </summary>
    public double BaselineVramMb { get; set; }

    /// <summary>
    /// Estimated VRAM in MiB allocated specifically by this model (Peak - Baseline).
    /// </summary>
    public double ModelVramMb => PeakVramMb > BaselineVramMb ? (PeakVramMb - BaselineVramMb) : 0.0;

    // Gain metrics vs baseline
    public double? PromptTokenSavingsPercentage { get; set; }
    public double? PeakContextReductionPercentage { get; set; }
    public double? TimeSavingsPercentage { get; set; }
    public double? AccuracyGainPercentage { get; set; }

    /// <summary>
    /// Relative or absolute path to the dedicated verbatim transcript file for this model and configuration.
    /// </summary>
    public string? TranscriptPath { get; set; }

    /// <summary>
    /// Unweighted binary Task Success (Pass@1) rate percentage across all evaluated scenarios:
    /// (Scenarios with TaskSuccess / ScenariosRun) * 100.0.
    /// </summary>
    public double UnweightedTaskSuccessRatePercentage =>
        ScenarioResults.Count > 0 ? Math.Round(ScenarioResults.Count(s => s.TaskSuccess) * 100.0 / ScenarioResults.Count, 1) : 0.0;

    public CompactionTelemetrySnapshot? CompactionStats { get; set; }
    public int CompactionPrunedChars => CompactionStats?.TotalPrunedChars ?? 0;
    public int CompactionPrunedTokens => CompactionStats?.EstimatedPrunedTokens ?? 0;

    public ToolCallHealingTelemetrySnapshot? HealingStats { get; set; }
    public CircuitBreakerTelemetrySnapshot? CircuitBreakerStats { get; set; }
    public LenientDispatchTelemetrySnapshot? DispatchStats { get; set; }

    public List<BenchmarkScenarioResult> ScenarioResults { get; init; } = [];

    /// <summary>
    /// Average prompt tokens per executed tool call across all scenarios for this model.
    /// </summary>
    public double PromptTokensPerToolCall => TotalToolCalls > 0 ? Math.Round((double)TotalPromptTokens / TotalToolCalls, 1) : 0.0;

    /// <summary>
    /// Average prompt tokens expended per successfully completed scenario (Task Success).
    /// </summary>
    public double PromptTokensPerSuccessfulScenario => ScenariosPassed > 0 ? Math.Round((double)TotalPromptTokens / ScenariosPassed, 1) : 0.0;

    /// <summary>
    /// Statistical summary if this model/configuration was evaluated across multiple repeated runs (N >= 2).
    /// </summary>
    public MultiRunStatisticalSummary? MultiRunStats { get; set; }

    /// <summary>
    /// Aggregates scenario results into per-suite summaries (e.g. BFCL, SWE-bench, RULER, Compaction).
    /// </summary>
    public IReadOnlyList<SuiteBenchmarkSummary> GetSuiteSummaries()
    {
        return ScenarioResults
            .GroupBy(s => s.Suite)
            .OrderBy(g => g.Key switch
            {
                "BFCL" => 1,
                "SWE-bench" => 2,
                "RULER" => 3,
                "Compaction" => 4,
                _ => 5
            })
            .Select(g =>
            {
                var totalWeight = g.Sum(s => s.Weight);
                var passRate = totalWeight > 0
                    ? Math.Round(g.Sum(s => s.Score * s.Weight) / totalWeight, 1)
                    : (g.Any() ? Math.Round(g.Average(s => s.Score), 1) : 0.0);
                var taskSuccess = totalWeight > 0
                    ? Math.Round(g.Sum(s => (s.TaskSuccess ? 100.0 : 0.0) * s.Weight) / totalWeight, 1)
                    : 0.0;
                var stepAcc = totalWeight > 0
                    ? Math.Round(g.Sum(s => s.StepAccuracy * s.Weight) / totalWeight, 1)
                    : 0.0;
                var totalTurns = g.Sum(s => s.TotalTurns);
                var turnsPassed = g.Sum(s => s.TurnsPassed);

                return new SuiteBenchmarkSummary
                {
                    Suite = g.Key,
                    ScenariosCount = g.Count(),
                    ScenariosPassed = g.Count(s => s.TaskSuccess),
                    PassRatePercentage = passRate,
                    TaskSuccessRatePercentage = taskSuccess,
                    StepAccuracyPercentage = stepAcc,
                    TotalTurns = totalTurns,
                    TurnsPassed = turnsPassed
                };
            })
            .ToList();
    }
}

/// <summary>
/// Aggregated performance summary for a specific benchmark suite category (e.g. BFCL, SWE-bench, RULER, Compaction).
/// </summary>
public sealed class SuiteBenchmarkSummary
{
    public required string Suite { get; init; }
    public int ScenariosCount { get; init; }
    public int ScenariosPassed { get; init; }
    public double PassRatePercentage { get; init; }
    public double TaskSuccessRatePercentage { get; init; }
    public double StepAccuracyPercentage { get; init; }
    public int TotalTurns { get; init; }
    public int TurnsPassed { get; init; }
    public double TurnAccuracyPercentage => TotalTurns > 0 ? Math.Round(TurnsPassed * 100.0 / TotalTurns, 1) : 0.0;
}

/// <summary>
/// Statistical summary across multiple repeated benchmark runs (N >= 2) for a given configuration.
/// Quantifies Mean ± StdDev for key metrics and computes paired deltas against the baseline configuration.
/// </summary>
public sealed class MultiRunStatisticalSummary
{
    public required string ModelName { get; init; }
    public required string ConfigurationLabel { get; init; }
    public string? OptimizationConfigId { get; init; }
    public int RunsCount { get; init; }

    public (double Mean, double StdDev) PassRate { get; init; }
    public (double Mean, double StdDev) TaskSuccessRate { get; init; }
    public (double Mean, double StdDev) StepAccuracy { get; init; }
    public (double Mean, double StdDev) PromptTokens { get; init; }
    public (double Mean, double StdDev) PromptTokensPerToolCall { get; init; }
    public (double Mean, double StdDev) GenerationTokensPerSecond { get; init; }
    public (double Mean, double StdDev) TotalTimeSeconds { get; init; }

    // Paired difference vs baseline (if baseline is present)
    public double? DeltaPassRateVsBaseline { get; set; }
    public double? DeltaTaskSuccessVsBaseline { get; set; }
    public double? DeltaPromptTokensPercentageVsBaseline { get; set; }
    public double? DeltaTimePercentageVsBaseline { get; set; }

    public static MultiRunStatisticalSummary Compute(IReadOnlyList<ModelBenchmarkSummary> runs, MultiRunStatisticalSummary? baselineSummary = null)
    {
        if (runs == null || runs.Count == 0) throw new ArgumentException("Runs cannot be empty.", nameof(runs));

        var n = runs.Count;
        var first = runs[0];

        var passRates = runs.Select(r => r.PassRatePercentage).ToList();
        var taskSuccessRates = runs.Select(r => r.TaskSuccessRatePercentage).ToList();
        var stepAccuracies = runs.Select(r => r.StepAccuracyPercentage).ToList();
        var promptTokens = runs.Select(r => (double)r.TotalPromptTokens).ToList();
        var tokensPerCall = runs.Select(r => r.PromptTokensPerToolCall).ToList();
        var genSpeeds = runs.Select(r => r.AverageGenerationTokensPerSecond).ToList();
        var durations = runs.Select(r => r.TotalTimeMs / 1000.0).ToList();

        var summary = new MultiRunStatisticalSummary
        {
            ModelName = first.ModelName,
            ConfigurationLabel = first.OptimizationConfigLabel ?? first.ConfigurationLabel ?? "Default",
            OptimizationConfigId = first.OptimizationConfigId,
            RunsCount = n,
            PassRate = CalculateMeanAndStdDev(passRates),
            TaskSuccessRate = CalculateMeanAndStdDev(taskSuccessRates),
            StepAccuracy = CalculateMeanAndStdDev(stepAccuracies),
            PromptTokens = CalculateMeanAndStdDev(promptTokens),
            PromptTokensPerToolCall = CalculateMeanAndStdDev(tokensPerCall),
            GenerationTokensPerSecond = CalculateMeanAndStdDev(genSpeeds),
            TotalTimeSeconds = CalculateMeanAndStdDev(durations)
        };

        if (baselineSummary != null)
        {
            summary.DeltaPassRateVsBaseline = Math.Round(summary.PassRate.Mean - baselineSummary.PassRate.Mean, 1);
            summary.DeltaTaskSuccessVsBaseline = Math.Round(summary.TaskSuccessRate.Mean - baselineSummary.TaskSuccessRate.Mean, 1);
            if (baselineSummary.PromptTokens.Mean > 0)
            {
                summary.DeltaPromptTokensPercentageVsBaseline = Math.Round(
                    (baselineSummary.PromptTokens.Mean - summary.PromptTokens.Mean) * 100.0 / baselineSummary.PromptTokens.Mean, 1);
            }
            if (baselineSummary.TotalTimeSeconds.Mean > 0)
            {
                summary.DeltaTimePercentageVsBaseline = Math.Round(
                    (baselineSummary.TotalTimeSeconds.Mean - summary.TotalTimeSeconds.Mean) * 100.0 / baselineSummary.TotalTimeSeconds.Mean, 1);
            }
        }

        return summary;
    }

    public static (double Mean, double StdDev) CalculateMeanAndStdDev(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return (0.0, 0.0);
        if (values.Count == 1) return (Math.Round(values[0], 2), 0.0);
        var mean = values.Average();
        var sumSquares = values.Sum(v => Math.Pow(v - mean, 2));
        var stdDev = Math.Sqrt(sumSquares / (values.Count - 1));
        return (Math.Round(mean, 2), Math.Round(stdDev, 2));
    }
}
