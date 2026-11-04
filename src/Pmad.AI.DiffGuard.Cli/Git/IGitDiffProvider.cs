using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.DiffGuard.Cli.Git;

public interface IGitDiffProvider
{
    Task<GitDiffMetadata> GetDiffAsync(
        string repoRoot,
        GitComparisonTarget target,
        bool skipBaseline,
        IEnumerable<string> excludedGlobs,
        CancellationToken cancellationToken = default);

    Func<FileDiff, CancellationToken, ValueTask<Stream?>> CreateBlobStreamProvider(
        string repoRoot,
        GitComparisonTarget target,
        string? headCommitSha);
}

