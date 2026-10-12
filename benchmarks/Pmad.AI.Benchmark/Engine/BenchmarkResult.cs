using Microsoft.Extensions.AI;

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
    public double TokensPerSecond => DurationMs > 0 ? (OutputTokens / (DurationMs / 1000.0)) : 0;
    public List<FunctionCallContent> ToolCalls { get; init; } = [];
    public string AssistantFinalText { get; set; } = string.Empty;
    public string? AssistantReasoning { get; set; }
    public string? ValidationMessage { get; set; }
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
    public double TotalDurationMs { get; set; }
    public int TotalPromptTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public int TotalTokens => TotalPromptTokens + TotalOutputTokens;
    public double GenerationTokensPerSecond => TotalDurationMs > 0 ? (TotalOutputTokens / (TotalDurationMs / 1000.0)) : 0;
    public int TotalToolCalls { get; set; }
    public double ToolAccuracyPercentage { get; set; } = 100.0;
    public double PeakMemoryMb { get; set; }
    public List<TurnResult> TurnResults { get; init; } = [];
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Summary metrics for a model across all evaluated scenarios.
/// </summary>
public sealed class ModelBenchmarkSummary
{
    public required string ModelId { get; init; }
    public required string ModelName { get; init; }
    public int ScenariosRun { get; set; }
    public int ScenariosPassed { get; set; }
    public int ScenariosFailed { get; set; }
    public double PassRatePercentage => ScenariosRun > 0 ? (ScenariosPassed * 100.0 / ScenariosRun) : 0.0;
    public double TotalTimeMs { get; set; }
    public int TotalPromptTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public double AverageGenerationTokensPerSecond { get; set; }
    public int TotalToolCalls { get; set; }
    public double PeakMemoryMb { get; set; }
    public List<BenchmarkScenarioResult> ScenarioResults { get; init; } = [];
}
