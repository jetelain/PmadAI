using Microsoft.Extensions.AI;
using Pmad.AI.Compaction;

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
    public double ToolAccuracyPercentage { get; set; } = 100.0;
    public double PeakMemoryMb { get; set; }
    public double PeakVramMb { get; set; }
    public bool SemanticRecallPassed { get; set; } = true;
    public List<TurnResult> TurnResults { get; init; } = [];
    public string? ErrorMessage { get; set; }
    public CompactionTelemetrySnapshot? CompactionStats { get; set; }
    public int CompactionPrunedChars => CompactionStats?.TotalPrunedChars ?? 0;
    public int CompactionPrunedTokens => CompactionStats?.EstimatedPrunedTokens ?? 0;
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

    public CompactionTelemetrySnapshot? CompactionStats { get; set; }
    public int CompactionPrunedChars => CompactionStats?.TotalPrunedChars ?? 0;
    public int CompactionPrunedTokens => CompactionStats?.EstimatedPrunedTokens ?? 0;

    public List<BenchmarkScenarioResult> ScenarioResults { get; init; } = [];
}
