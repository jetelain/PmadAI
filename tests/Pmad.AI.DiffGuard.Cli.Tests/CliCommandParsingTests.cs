using System.CommandLine;
using Pmad.AI.DiffGuard.Cli.Cli;
using Pmad.AI.DiffGuard.Cli.Configuration;
using Pmad.AI.DiffGuard.Cli.Execution;
using Pmad.AI.DiffGuard.Cli.Formatting;
using Pmad.AI.DiffGuard.Cli.Git;
using Pmad.AI.DiffGuard.Cli.Llm;
using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.DiffGuard.Cli.Tests;

public class CliCommandParsingTests
{
    private sealed class FakeGitDiffProvider : IGitDiffProvider
    {
        public GitComparisonTarget? LastTarget { get; private set; }
        public string? LastRepoRoot { get; private set; }
        public bool LastSkipBaseline { get; private set; }

        public Task<GitDiffMetadata> GetDiffAsync(
            string repoRoot,
            GitComparisonTarget target,
            bool skipBaseline,
            IEnumerable<string> excludedGlobs,
            CancellationToken cancellationToken = default)
        {
            LastRepoRoot = repoRoot;
            LastTarget = target;
            LastSkipBaseline = skipBaseline;

            var meta = new GitDiffMetadata
            {
                TargetDescription = target.ToString()!,
                UnifiedDiffText = "",
                RepositoryRootPath = repoRoot
            };
            return Task.FromResult(meta);
        }

        public Func<FileDiff, CancellationToken, ValueTask<Stream?>> CreateBlobStreamProvider(
            string repoRoot,
            GitComparisonTarget target,
            string? headCommitSha)
        {
            return (file, ct) => ValueTask.FromResult<Stream?>(null);
        }
    }

    private sealed class FakeConfigLoader(DiffGuardPolicy policy) : IDiffGuardConfigLoader
    {
        public DiffGuardPolicy LoadPolicy(string? repoRoot = null, string? customRepoConfigPath = null) => policy;
    }

    private sealed class FakeLlmJudgeClientFactory : ILlmJudgeClientFactory
    {
        public DiffGuardCliOptions? LastOptions { get; private set; }

        public (Microsoft.Extensions.AI.IChatClient? Client, IDisposable? Disposable) CreateClient(DiffGuardCliOptions options)
        {
            LastOptions = options;
            return (null, null);
        }
    }

    [Fact]
    public async Task Parse_PositionalBaseAndHead_BindsBranchRange()
    {
        var fakeGit = new FakeGitDiffProvider();
        var fakeLoader = new FakeConfigLoader(new DiffGuardPolicy());
        var runner = new DiffGuardRunner(gitProvider: fakeGit);
        var root = CommandLineConfiguration.CreateRootCommand(() => runner, fakeLoader);

        int exitCode = await root.Parse("main feature/new-auth").InvokeAsync();

        Assert.Equal(ExitCodes.Success, exitCode);
        var branchRange = Assert.IsType<GitComparisonTarget.BranchRange>(fakeGit.LastTarget);
        Assert.Equal("main", branchRange.BaseRef);
        Assert.Equal("feature/new-auth", branchRange.HeadRef);
        Assert.False(branchRange.TwoDot);
    }

    [Fact]
    public async Task Parse_TwoDotFlag_SetsTwoDotOnBranchRange()
    {
        var fakeGit = new FakeGitDiffProvider();
        var fakeLoader = new FakeConfigLoader(new DiffGuardPolicy());
        var runner = new DiffGuardRunner(gitProvider: fakeGit);
        var root = CommandLineConfiguration.CreateRootCommand(() => runner, fakeLoader);

        int exitCode = await root.Parse("main feature/new-auth --two-dot").InvokeAsync();

        Assert.Equal(ExitCodes.Success, exitCode);
        var branchRange = Assert.IsType<GitComparisonTarget.BranchRange>(fakeGit.LastTarget);
        Assert.True(branchRange.TwoDot);
    }

    [Fact]
    public async Task Parse_StagedFlag_BindsStagedTarget()
    {
        var fakeGit = new FakeGitDiffProvider();
        var fakeLoader = new FakeConfigLoader(new DiffGuardPolicy());
        var runner = new DiffGuardRunner(gitProvider: fakeGit);
        var root = CommandLineConfiguration.CreateRootCommand(() => runner, fakeLoader);

        int exitCode = await root.Parse("--staged").InvokeAsync();

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.IsType<GitComparisonTarget.Staged>(fakeGit.LastTarget);
    }

    [Fact]
    public async Task Parse_CommitFlag_BindsCommitTarget()
    {
        var fakeGit = new FakeGitDiffProvider();
        var fakeLoader = new FakeConfigLoader(new DiffGuardPolicy());
        var runner = new DiffGuardRunner(gitProvider: fakeGit);
        var root = CommandLineConfiguration.CreateRootCommand(() => runner, fakeLoader);

        int exitCode = await root.Parse("--commit a1b2c3d4e5f6").InvokeAsync();

        Assert.Equal(ExitCodes.Success, exitCode);
        var commit = Assert.IsType<GitComparisonTarget.Commit>(fakeGit.LastTarget);
        Assert.Equal("a1b2c3d4e5f6", commit.CommitSha);
    }

    [Fact]
    public async Task Parse_DiffFileFlag_BindsPatchFileTarget()
    {
        var fakeGit = new FakeGitDiffProvider();
        var fakeLoader = new FakeConfigLoader(new DiffGuardPolicy());
        var runner = new DiffGuardRunner(gitProvider: fakeGit);
        var root = CommandLineConfiguration.CreateRootCommand(() => runner, fakeLoader);

        int exitCode = await root.Parse("--diff-file changes.patch").InvokeAsync();

        Assert.Equal(ExitCodes.Success, exitCode);
        var patch = Assert.IsType<GitComparisonTarget.PatchFile>(fakeGit.LastTarget);
        Assert.Equal("changes.patch", patch.FilePath);
    }

    [Fact]
    public async Task Parse_FormatSarif_WritesSarifToStdout()
    {
        var fakeGit = new FakeGitDiffProvider();
        var fakeLoader = new FakeConfigLoader(new DiffGuardPolicy());
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var runner = new DiffGuardRunner(gitProvider: fakeGit, stdout: stdout, stderr: stderr);
        var root = CommandLineConfiguration.CreateRootCommand(() => runner, fakeLoader);

        int exitCode = await root.Parse("--staged --format sarif").InvokeAsync();

        Assert.Equal(ExitCodes.Success, exitCode);
        var output = stdout.ToString();
        Assert.Contains("\"version\": \"2.1.0\"", output);
        Assert.Contains("\"name\": \"Pmad.AI.DiffGuard\"", output);
    }

    [Fact]
    public async Task Parse_SystemPolicyEnforcesDeterministic_EmitsWarningWhenDisabled()
    {
        var fakeGit = new FakeGitDiffProvider();
        var policy = new DiffGuardPolicy
        {
            ForceDeterministic = true,
            EnableDeterministic = true
        };
        var fakeLoader = new FakeConfigLoader(policy);
        var stderr = new StringWriter();
        var runner = new DiffGuardRunner(gitProvider: fakeGit, stderr: stderr);
        var root = CommandLineConfiguration.CreateRootCommand(() => runner, fakeLoader, stderr);

        int exitCode = await root.Parse("--staged --no-deterministic").InvokeAsync();

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("[WARNING] Deterministic diff scanning is enforced by system configuration", stderr.ToString());
    }

    [Fact]
    public async Task Parse_SystemPolicyEnforcesSeverityFloor_ClampsPermissiveMinSeverity()
    {
        var fakeGit = new FakeGitDiffProvider();
        var policy = new DiffGuardPolicy
        {
            MinimumRiskSeverity = DiffRiskSeverity.Medium,
            SystemMinimumRiskSeverityFloor = DiffRiskSeverity.Medium
        };
        var fakeLoader = new FakeConfigLoader(policy);
        var stderr = new StringWriter();
        var runner = new DiffGuardRunner(gitProvider: fakeGit, stderr: stderr);
        var root = CommandLineConfiguration.CreateRootCommand(() => runner, fakeLoader, stderr);

        int exitCode = await root.Parse("--staged --min-severity High").InvokeAsync();

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("[WARNING] Minimum risk severity 'High' is more permissive than system configuration floor 'Medium'", stderr.ToString());
    }

    [Theory]
    [InlineData("main...feature/auth", "main", "feature/auth", false)]
    [InlineData("main..feature/auth", "main", "feature/auth", true)]
    public async Task Parse_SingleArgDotRangeShorthand_BindsBranchRange(string arg, string expectedBase, string expectedHead, bool expectedTwoDot)
    {
        var fakeGit = new FakeGitDiffProvider();
        var fakeLoader = new FakeConfigLoader(new DiffGuardPolicy());
        var runner = new DiffGuardRunner(gitProvider: fakeGit);
        var root = CommandLineConfiguration.CreateRootCommand(() => runner, fakeLoader);

        int exitCode = await root.Parse(arg).InvokeAsync();

        Assert.Equal(ExitCodes.Success, exitCode);
        var branchRange = Assert.IsType<GitComparisonTarget.BranchRange>(fakeGit.LastTarget);
        Assert.Equal(expectedBase, branchRange.BaseRef);
        Assert.Equal(expectedHead, branchRange.HeadRef);
        Assert.Equal(expectedTwoDot, branchRange.TwoDot);
    }

    [Fact]
    public async Task Parse_SystemPolicyForcesLlm_CannotBeBypassedByLlmProviderNone()
    {
        var fakeGit = new FakeGitDiffProvider();
        var fakeLlm = new FakeLlmJudgeClientFactory();
        var policy = new DiffGuardPolicy
        {
            LockPolicy = true,
            ForceLlmEnabled = true,
            LlmEnabled = true,
            LlmProvider = "ollama",
            LlmModel = "qwen3:8b"
        };
        var fakeLoader = new FakeConfigLoader(policy);
        var stderr = new StringWriter();
        var runner = new DiffGuardRunner(gitProvider: fakeGit, llmFactory: fakeLlm, stdout: new StringWriter(), stderr: stderr);
        var root = CommandLineConfiguration.CreateRootCommand(() => runner, fakeLoader, stderr);

        int exitCode = await root.Parse("--staged --llm-provider none").InvokeAsync();

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("[WARNING] LLM judge is enforced by system configuration", stderr.ToString());
        Assert.NotNull(fakeLlm.LastOptions);
        Assert.True(fakeLlm.LastOptions!.EnableLlm);
        Assert.Equal(LlmProviderKind.Ollama, fakeLlm.LastOptions.LlmProvider);
        Assert.Equal("qwen3:8b", fakeLlm.LastOptions.LlmModel);
    }

    [Theory]
    [InlineData("0.0.0.0:11434", "http://localhost:11434")]
    [InlineData("127.0.0.1:11434", "http://127.0.0.1:11434")]
    [InlineData("localhost", "http://localhost:11434")]
    [InlineData("http://localhost:11434/", "http://localhost:11434")]
    public void NormalizeEndpointUri_HandlesHostOnlyAndZeroBindAddresses(string raw, string expected)
    {
        Assert.Equal(expected, LlmJudgeClientFactory.NormalizeEndpointUri(raw, 11434));
    }

    [Theory]
    [InlineData("--staged --format xml")]
    [InlineData("--staged --min-severity 999")]
    [InlineData("--staged --min-severity Unknown")]
    [InlineData("--staged --llm-provider anthropic")]
    [InlineData("--staged --max-chunk-bytes 0")]
    public async Task Parse_InvalidOptionValues_ReturnsExitCodeError(string commandLine)
    {
        var fakeGit = new FakeGitDiffProvider();
        var fakeLoader = new FakeConfigLoader(new DiffGuardPolicy());
        var stderr = new StringWriter();
        var runner = new DiffGuardRunner(gitProvider: fakeGit, stderr: stderr);
        var root = CommandLineConfiguration.CreateRootCommand(() => runner, fakeLoader, stderr);

        int exitCode = await root.Parse(commandLine).InvokeAsync();

        Assert.Equal(ExitCodes.Error, exitCode);
        Assert.Contains("[ERROR]", stderr.ToString());
    }
}
