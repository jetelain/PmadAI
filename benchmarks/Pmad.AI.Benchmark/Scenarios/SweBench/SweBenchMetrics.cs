namespace Pmad.AI.Benchmark.Scenarios.SweBench;

/// <summary>
/// Metric details for a single turn in a SWE-bench trajectory replay.
/// </summary>
public sealed record SweBenchTurnMetric(
    int TurnIndex,
    int RawChars,
    int CompactedChars,
    double ReductionRatioPercentage
);

/// <summary>
/// Aggregated metrics for a SWE-bench trajectory evaluation.
/// </summary>
public sealed class SweBenchMetricsResult
{
    public string InstanceId { get; init; } = string.Empty;
    public string Archetype { get; init; } = string.Empty;
    public int TotalTurns { get; init; }
    public long TotalRawChars { get; init; }
    public long TotalCompactedChars { get; init; }
    public double OverallTokenReductionRatio => TotalRawChars > 0
        ? Math.Max(0.0, (1.0 - ((double)TotalCompactedChars / TotalRawChars)) * 100.0)
        : 0.0;

    public bool CircuitBreakerTriggered { get; init; }
    public List<SweBenchTurnMetric> TurnMetrics { get; init; } = [];
}
