using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.DiffGuard.Cli.Execution;

public sealed class DiffGuardReportModel
{
    public required string RepositoryPath { get; init; }
    public required string ComparisonDescription { get; init; }
    public string? BaseRef { get; init; }
    public string? HeadRef { get; init; }
    public string? BaseCommitSha { get; init; }
    public string? HeadCommitSha { get; init; }
    public IReadOnlyList<string> LoadedConfigPaths { get; init; } = [];
    public bool DeterministicScannerEnabled { get; init; }
    public bool LlmJudgeEnabled { get; init; }
    public string? LlmProvider { get; init; }
    public string? LlmModelId { get; init; }
    public int LlmChunksEvaluated { get; init; }
    public long ElapsedMilliseconds { get; init; }
    public required DiffRiskScanResult ScanResult { get; init; }
    public RepositoryBaselineStats? BaselineStats { get; init; }
    public DiffRiskSeverity MinimumRiskSeverity { get; init; } = DiffRiskSeverity.Medium;
    public string? FirstChangedFilePath { get; init; }
}

