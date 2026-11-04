using System.Diagnostics;
using Pmad.AI.DiffGuard.Cli.Git;
using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.DiffGuard.Cli.Tests;

public class GitDiffProviderTests : IDisposable
{
    private readonly string _tempRepoDir;

    public GitDiffProviderTests()
    {
        _tempRepoDir = Path.Combine(Path.GetTempPath(), "DiffGuardTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRepoDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRepoDir))
            {
                // Remove readonly attributes from .git files before deleting
                foreach (var file in Directory.EnumerateFiles(_tempRepoDir, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(_tempRepoDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures on temp dirs
        }
    }

    private void RunGit(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _tempRepoDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode != 0)
        {
            string err = p.StandardError.ReadToEnd();
            throw new InvalidOperationException($"git {string.Join(" ", args)} failed: {err}");
        }
    }

    private void InitializeGitRepo()
    {
        RunGit("init", "-b", "main");
        RunGit("config", "user.name", "DiffGuard Test");
        RunGit("config", "user.email", "test@pmad.ai");
        RunGit("config", "commit.gpgSign", "false");
    }

    [Fact]
    public async Task GetDiffAsync_BranchComparison_ExtractsMergeBaseDiff()
    {
        InitializeGitRepo();

        File.WriteAllText(Path.Combine(_tempRepoDir, "initial.txt"), "hello world\nline 2\n");
        RunGit("add", ".");
        RunGit("commit", "-m", "Initial commit on main");

        RunGit("checkout", "-b", "feature/auth");
        File.WriteAllText(Path.Combine(_tempRepoDir, "auth.cs"), "public class AuthService { }\n");
        RunGit("add", ".");
        RunGit("commit", "-m", "Add auth service");

        RunGit("checkout", "main");

        var provider = new GitDiffProvider();
        var target = new GitComparisonTarget.BranchRange("main", "feature/auth");

        var meta = await provider.GetDiffAsync(_tempRepoDir, target, skipBaseline: false, excludedGlobs: []);

        Assert.Equal("main...feature/auth", meta.TargetDescription);
        Assert.Equal("main", meta.BaseRef);
        Assert.Equal("feature/auth", meta.HeadRef);
        Assert.False(string.IsNullOrEmpty(meta.BaseCommitSha));
        Assert.False(string.IsNullOrEmpty(meta.HeadCommitSha));
        Assert.Contains("diff --git a/auth.cs b/auth.cs", meta.UnifiedDiffText);
        Assert.Contains("+public class AuthService { }", meta.UnifiedDiffText);
        Assert.NotNull(meta.BaselineStats);
        Assert.Equal(1, meta.BaselineStats.TotalTrackedFiles);
        Assert.Equal(2, meta.BaselineStats.TotalTrackedLines);
    }

    [Fact]
    public async Task CreateBlobStreamProvider_UncheckedOutTargetBranch_StreamsBinaryBlobFromGit()
    {
        InitializeGitRepo();

        File.WriteAllText(Path.Combine(_tempRepoDir, "main.txt"), "main line\n");
        RunGit("add", ".");
        RunGit("commit", "-m", "Base commit");

        RunGit("checkout", "-b", "feature/assets");

        // Write disguised MZ executable with PNG extension on feature branch
        byte[] mzPayload = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]; // "MZ" DOS header
        string imgPath = Path.Combine(_tempRepoDir, "avatar.png");
        File.WriteAllBytes(imgPath, mzPayload);
        RunGit("add", ".");
        RunGit("commit", "-m", "Add disguised avatar.png");

        // Checkout main: working tree does NOT have avatar.png on disk!
        RunGit("checkout", "main");
        Assert.False(File.Exists(imgPath));

        var provider = new GitDiffProvider();
        var target = new GitComparisonTarget.BranchRange("main", "feature/assets");
        var meta = await provider.GetDiffAsync(_tempRepoDir, target, skipBaseline: true, excludedGlobs: []);

        var files = DiffParser.Parse(meta.UnifiedDiffText);
        var fileDiff = Assert.Single(files);
        Assert.Equal("avatar.png", fileDiff.FilePath);
        Assert.NotNull(fileDiff.NewBlobSha);

        // Scan with BinaryAndImageScanner using BlobStreamProvider
        var scannerOptions = new DiffRiskScannerOptions
        {
            RepositoryRootPath = _tempRepoDir,
            TargetGitRef = meta.HeadCommitSha,
            BlobStreamProvider = provider.CreateBlobStreamProvider(_tempRepoDir, target, meta.HeadCommitSha)
        };

        var scanner = new BinaryAndImageScanner(scannerOptions);
        var findings = await scanner.ScanAsync(files);

        // Must flag PAY-BIN-002 Critical because blob stream was read from Git and detected MZ header
        var finding = Assert.Single(findings);
        Assert.Equal("PAY-BIN-002", finding.RuleId);
        Assert.Equal(DiffRiskSeverity.Critical, finding.Severity);
        Assert.Contains("MZ", finding.Description);
    }

    [Fact]
    public async Task GetDiffAsync_CommitTarget_ExtractsCommitDiff()
    {
        InitializeGitRepo();

        File.WriteAllText(Path.Combine(_tempRepoDir, "f1.txt"), "v1\n");
        RunGit("add", ".");
        RunGit("commit", "-m", "Commit 1");

        File.WriteAllText(Path.Combine(_tempRepoDir, "f1.txt"), "v2\n");
        RunGit("add", ".");
        RunGit("commit", "-m", "Commit 2");

        var headSha = File.ReadAllText(Path.Combine(_tempRepoDir, ".git", "refs", "heads", "main")).Trim();

        var provider = new GitDiffProvider();
        var target = new GitComparisonTarget.Commit(headSha);

        var meta = await provider.GetDiffAsync(_tempRepoDir, target, skipBaseline: true, excludedGlobs: []);

        Assert.Contains("f1.txt", meta.UnifiedDiffText);
        Assert.Contains("+v2", meta.UnifiedDiffText);
        Assert.Contains("-v1", meta.UnifiedDiffText);
    }

    [Fact]
    public async Task GetDiffAsync_InitialRootCommit_SucceedsWithoutParentCommit()
    {
        InitializeGitRepo();

        File.WriteAllText(Path.Combine(_tempRepoDir, "root.txt"), "root content\n");
        RunGit("add", ".");
        RunGit("commit", "-m", "Initial root commit");

        var rootSha = File.ReadAllText(Path.Combine(_tempRepoDir, ".git", "refs", "heads", "main")).Trim();

        var provider = new GitDiffProvider();
        var target = new GitComparisonTarget.Commit(rootSha);

        var meta = await provider.GetDiffAsync(_tempRepoDir, target, skipBaseline: false, excludedGlobs: []);

        Assert.Equal(rootSha, meta.HeadCommitSha);
        Assert.Null(meta.BaseCommitSha);
        Assert.Contains("+root content", meta.UnifiedDiffText);
    }

    [Fact]
    public async Task GetDiffAsync_StagedOnUnbornHead_SucceedsBeforeFirstCommit()
    {
        InitializeGitRepo();

        File.WriteAllText(Path.Combine(_tempRepoDir, "staged.txt"), "staged before first commit\n");
        RunGit("add", "staged.txt");

        var provider = new GitDiffProvider();
        var target = new GitComparisonTarget.Staged();

        var meta = await provider.GetDiffAsync(_tempRepoDir, target, skipBaseline: false, excludedGlobs: []);

        Assert.Null(meta.HeadCommitSha);
        Assert.Contains("+staged before first commit", meta.UnifiedDiffText);
    }

    [Fact]
    public async Task GetDiffAsync_RefStartingWithHyphen_ThrowsArgumentException()
    {
        InitializeGitRepo();

        var provider = new GitDiffProvider();
        var target = new GitComparisonTarget.BranchRange("--output=/tmp/pwn", "HEAD");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.GetDiffAsync(_tempRepoDir, target, skipBaseline: true, excludedGlobs: []));
    }
}

