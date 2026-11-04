using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.DiffGuard.Cli.Git;

public sealed class GitDiffMetadata
{
    public required string TargetDescription { get; init; }
    public string? BaseRef { get; init; }
    public string? HeadRef { get; init; }
    public string? BaseCommitSha { get; init; }
    public string? HeadCommitSha { get; init; }
    public required string UnifiedDiffText { get; init; }
    public RepositoryBaselineStats? BaselineStats { get; init; }
    public string? RepositoryRootPath { get; init; }
}

