namespace Pmad.AI.DiffGuard.Cli.Git;

public abstract record GitComparisonTarget
{
    private GitComparisonTarget() { }

    public sealed record BranchRange(string BaseRef, string HeadRef, bool TwoDot = false) : GitComparisonTarget
    {
        public override string ToString() => TwoDot ? $"{BaseRef}..{HeadRef}" : $"{BaseRef}...{HeadRef}";
    }

    public sealed record Staged : GitComparisonTarget
    {
        public override string ToString() => "--staged";
    }

    public sealed record WorkingTree : GitComparisonTarget
    {
        public override string ToString() => "--working-tree";
    }

    public sealed record Commit(string CommitSha) : GitComparisonTarget
    {
        public override string ToString() => $"commit {CommitSha}";
    }

    public sealed record PatchFile(string FilePath) : GitComparisonTarget
    {
        public override string ToString() => $"file {FilePath}";
    }
}

