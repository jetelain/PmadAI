using Microsoft.Extensions.AI;
using Moq;
using Pmad.AI.DiffGuard.Cli.Cli;
using Pmad.AI.DiffGuard.Cli.Execution;
using Pmad.AI.DiffGuard.Cli.Formatting;
using Pmad.AI.DiffGuard.Cli.Git;
using Pmad.AI.DiffGuard.Cli.Llm;
using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.DiffGuard.Cli.Tests;

public class DiffGuardRunnerTests
{
    private sealed class StubGitDiffProvider(string diffText) : IGitDiffProvider
    {
        public Task<GitDiffMetadata> GetDiffAsync(
            string repoRoot,
            GitComparisonTarget target,
            bool skipBaseline,
            IEnumerable<string> excludedGlobs,
            CancellationToken cancellationToken = default)
        {
            var meta = new GitDiffMetadata
            {
                TargetDescription = "test-target",
                UnifiedDiffText = diffText,
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

    private sealed class StubLlmClientFactory(IChatClient chatClient) : ILlmJudgeClientFactory
    {
        public (IChatClient? Client, IDisposable? Disposable) CreateClient(DiffGuardCliOptions options)
        {
            return (chatClient, null);
        }
    }

    [Fact]
    public async Task RunAsync_CleanDiff_ReturnsExitCodeSuccess()
    {
        string diff = """
            diff --git a/src/Utils.cs b/src/Utils.cs
            --- a/src/Utils.cs
            +++ b/src/Utils.cs
            @@ -1,1 +1,2 @@
             public class Utils {
            +    public static int Add(int a, int b) => a + b;
            """;

        var gitProvider = new StubGitDiffProvider(diff);
        var runner = new DiffGuardRunner(gitProvider: gitProvider);

        var options = new DiffGuardCliOptions
        {
            EnableDeterministic = true,
            EnableLlm = false,
            MinimumRiskSeverity = DiffRiskSeverity.Medium
        };

        int exitCode = await runner.RunAsync(options);

        Assert.Equal(ExitCodes.Success, exitCode);
    }

    [Fact]
    public async Task RunAsync_AuthBypassFinding_ReturnsExitCodeRisksDetected()
    {
        string diff = """
            diff --git a/src/Auth.cs b/src/Auth.cs
            --- a/src/Auth.cs
            +++ b/src/Auth.cs
            @@ -1,1 +1,2 @@
             public class Auth {
            +    if (password == "backdoor_admin_pass") return true;
            """;

        var gitProvider = new StubGitDiffProvider(diff);
        var runner = new DiffGuardRunner(gitProvider: gitProvider);

        var options = new DiffGuardCliOptions
        {
            EnableDeterministic = true,
            EnableLlm = false,
            MinimumRiskSeverity = DiffRiskSeverity.Medium
        };

        int exitCode = await runner.RunAsync(options);

        Assert.Equal(ExitCodes.RisksDetected, exitCode);
    }

    [Fact]
    public async Task RunAsync_LlmJudgeFlagsBackdoor_CombinesWithDeterministicResults()
    {
        string diff = """
            diff --git a/src/Service.cs b/src/Service.cs
            --- a/src/Service.cs
            +++ b/src/Service.cs
            @@ -1,1 +1,2 @@
             public class Service {
            +    private string token = "ghp_111111111111111111111111111111111111";
            """;

        var gitProvider = new StubGitDiffProvider(diff);

        var mockChatClient = new Mock<IChatClient>();
        string llmJsonResponse = """
            {
              "status": "SECURITY_ISSUE",
              "summary": "Flagged covert logic bomb",
              "findings": [
                {
                  "severity": "High",
                  "category": "BackdoorPattern",
                  "file": "src/Service.cs",
                  "line": 2,
                  "title": "Covert logic bomb detected",
                  "description": "Exploit trigger detected in service handler."
                }
              ]
            }
            """;

        mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, llmJsonResponse)));

        var llmFactory = new StubLlmClientFactory(mockChatClient.Object);
        var runner = new DiffGuardRunner(gitProvider: gitProvider, llmFactory: llmFactory);

        var options = new DiffGuardCliOptions
        {
            EnableDeterministic = true,
            EnableLlm = true,
            LlmProvider = LlmProviderKind.Ollama,
            LlmModel = "qwen3:8b",
            MinimumRiskSeverity = DiffRiskSeverity.Medium
        };

        int exitCode = await runner.RunAsync(options);

        Assert.Equal(ExitCodes.RisksDetected, exitCode);
    }

    [Fact]
    public async Task RunAsync_SimultaneousMarkdownAndSarifOutputs_CreatesBothFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "DiffGuardReportTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string mdPath = Path.Combine(tempDir, "report.md");
            string sarifPath = Path.Combine(tempDir, "report.sarif");

            string diff = """
                diff --git a/src/Service.cs b/src/Service.cs
                --- a/src/Service.cs
                +++ b/src/Service.cs
                @@ -1,1 +1,2 @@
                 public class Service {
                +    private string token = "ghp_111111111111111111111111111111111111";
                """;

            var gitProvider = new StubGitDiffProvider(diff);
            var runner = new DiffGuardRunner(gitProvider: gitProvider);

            var options = new DiffGuardCliOptions
            {
                EnableDeterministic = true,
                EnableLlm = false,
                PrimaryFormat = OutputFormat.Console,
                MarkdownPath = mdPath,
                SarifPath = sarifPath,
                MinimumRiskSeverity = DiffRiskSeverity.Medium
            };

            int exitCode = await runner.RunAsync(options);

            Assert.Equal(ExitCodes.RisksDetected, exitCode);
            Assert.True(File.Exists(mdPath));
            Assert.True(File.Exists(sarifPath));

            string mdContent = await File.ReadAllTextAsync(mdPath);
            Assert.Contains("# 🛡️ DiffGuard Security Report — ❌ RISKS DETECTED", mdContent);

            string sarifContent = await File.ReadAllTextAsync(sarifPath);
            Assert.Contains("\"version\": \"2.1.0\"", sarifContent);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_CanceledToken_ReturnsExitCodeCanceled()
    {
        var gitProvider = new StubGitDiffProvider("");
        var stderr = new StringWriter();
        var runner = new DiffGuardRunner(gitProvider: gitProvider, stderr: stderr);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        int exitCode = await runner.RunAsync(new DiffGuardCliOptions(), cts.Token);

        Assert.Equal(ExitCodes.Canceled, exitCode);
        Assert.Contains("[CANCELED]", stderr.ToString());
    }

    [Fact]
    public async Task RunAsync_IndividualRuleToggleDisabled_RespectsGranularRuleToggle()
    {
        string diff = """
            diff --git a/src/Client.cs b/src/Client.cs
            --- a/src/Client.cs
            +++ b/src/Client.cs
            @@ -1,1 +1,2 @@
             public class Client {
            +    private string url = "https://unlisted-custom-domain.example/api";
            """;

        var gitProvider = new StubGitDiffProvider(diff);
        var runner = new DiffGuardRunner(gitProvider: gitProvider, stdout: new StringWriter());

        var options = new DiffGuardCliOptions
        {
            EnableDeterministic = true,
            EnableNetworkEndpointRules = false,
            MinimumRiskSeverity = DiffRiskSeverity.Medium
        };

        int exitCode = await runner.RunAsync(options);

        Assert.Equal(ExitCodes.Success, exitCode);
    }

    [Fact]
    public async Task RunAsync_QuietModeWithConsoleFormat_SuppressesStdoutOutput()
    {
        string diff = """
            diff --git a/src/Utils.cs b/src/Utils.cs
            --- a/src/Utils.cs
            +++ b/src/Utils.cs
            @@ -1,1 +1,2 @@
             public class Utils {
            +    public static int Add(int a, int b) => a + b;
            """;

        var gitProvider = new StubGitDiffProvider(diff);
        var stdout = new StringWriter();
        var runner = new DiffGuardRunner(gitProvider: gitProvider, stdout: stdout);

        var options = new DiffGuardCliOptions
        {
            EnableDeterministic = true,
            PrimaryFormat = OutputFormat.Console,
            Quiet = true
        };

        int exitCode = await runner.RunAsync(options);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(stdout.ToString());
    }
}

