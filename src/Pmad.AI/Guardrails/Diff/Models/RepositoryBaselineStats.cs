namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Baseline metrics for the target repository used to compute relative blast radius impact.
/// </summary>
/// <param name="TotalTrackedFiles">Total number of tracked files in the repository.</param>
/// <param name="TotalTrackedLines">Total number of lines across tracked files in the repository.</param>
public sealed record RepositoryBaselineStats(
    int TotalTrackedFiles,
    int TotalTrackedLines);

