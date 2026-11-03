namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Aggregated scan result evaluating findings, blast radius, and risk thresholds across a git diff.
/// </summary>
/// <param name="Findings">List of risk findings identified in the diff.</param>
/// <param name="ChangedFilesCount">Number of non-excluded files modified, added, or deleted in the diff.</param>
/// <param name="AddedLinesCount">Total lines added across all hunks.</param>
/// <param name="DeletedLinesCount">Total lines removed across all hunks.</param>
/// <param name="CodebaseImpactPercent">Percentage of repository lines modified, if baseline stats are provided.</param>
/// <param name="FileImpactPercent">Percentage of repository files modified, if baseline stats are provided.</param>
/// <param name="MinimumRiskSeverity">Threshold severity at which <see cref="HasRisks"/> evaluates to true. Default: Medium.</param>
public sealed record DiffRiskScanResult(
    IReadOnlyList<DiffRiskFinding> Findings,
    int ChangedFilesCount,
    int AddedLinesCount,
    int DeletedLinesCount,
    double? CodebaseImpactPercent = null,
    double? FileImpactPercent = null,
    DiffRiskSeverity MinimumRiskSeverity = DiffRiskSeverity.Medium)
{
    /// <summary>
    /// Empty scan result with no findings and zero changes.
    /// </summary>
    public static DiffRiskScanResult Empty { get; } = new([], 0, 0, 0);

    /// <summary>
    /// Total count of changed lines (additions plus deletions).
    /// </summary>
    public int TotalChangedLines => AddedLinesCount + DeletedLinesCount;

    /// <summary>
    /// The highest severity among all recorded findings, or null if there are no findings.
    /// </summary>
    public DiffRiskSeverity? HighestSeverity =>
        Findings.Count == 0 ? null : Findings.Max(f => f.Severity);

    /// <summary>
    /// True when at least one finding meets or exceeds <see cref="MinimumRiskSeverity"/>.
    /// </summary>
    public bool HasRisks => HasFindingsAtOrAbove(MinimumRiskSeverity);

    /// <summary>
    /// Checks whether any finding meets or exceeds the specified minimum severity.
    /// </summary>
    /// <param name="minimumSeverity">The minimum severity threshold to check.</param>
    /// <returns>True if at least one finding has a severity greater than or equal to <paramref name="minimumSeverity"/>.</returns>
    public bool HasFindingsAtOrAbove(DiffRiskSeverity minimumSeverity) =>
        Findings.Any(f => f.Severity >= minimumSeverity);
}

