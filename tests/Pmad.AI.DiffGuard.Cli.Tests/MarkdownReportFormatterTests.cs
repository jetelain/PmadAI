using Pmad.AI.DiffGuard.Cli.Execution;
using Pmad.AI.DiffGuard.Cli.Formatting;
using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.DiffGuard.Cli.Tests;

public class MarkdownReportFormatterTests
{
    [Fact]
    public async Task FormatAsync_WithRisks_GeneratesCompleteMarkdownDocument()
    {
        var findings = new List<DiffRiskFinding>
        {
            new(
                RuleId: "BD-AUTH-001",
                Title: "Hardcoded Auth Bypass",
                Category: DiffRiskCategory.BackdoorPattern,
                Severity: DiffRiskSeverity.Critical,
                FilePath: "src/Auth/LoginService.cs",
                LineNumber: 42,
                Column: 13,
                Description: "Hardcoded credential comparison bypassing auth.",
                Snippet: "if (password == \"secret_backdoor\") return true;"),
            new(
                RuleId: "PAY-SEC-001",
                Title: "Hardcoded API Key",
                Category: DiffRiskCategory.SuspiciousPayload,
                Severity: DiffRiskSeverity.High,
                FilePath: "src/Config.cs",
                LineNumber: 10,
                Column: 5,
                Description: "Detected GitHub token literal in source code.",
                Snippet: "var token = \"ghp_123456789012345678901234567890123456\";")
        };

        var scanResult = new DiffRiskScanResult(findings, 2, 50, 10, 0.4, 1.2, DiffRiskSeverity.Medium);
        var report = new DiffGuardReportModel
        {
            RepositoryPath = "C:/repos/MyProject",
            ComparisonDescription = "main...feature/auth",
            BaseRef = "main",
            HeadRef = "feature/auth",
            BaseCommitSha = "a1b2c3d4e5",
            HeadCommitSha = "f6g7h8i9j0",
            DeterministicScannerEnabled = true,
            LlmJudgeEnabled = true,
            LlmProvider = "ollama",
            LlmModelId = "qwen3:8b",
            LlmChunksEvaluated = 1,
            ElapsedMilliseconds = 1234,
            ScanResult = scanResult,
            BaselineStats = new RepositoryBaselineStats(100, 5000),
            MinimumRiskSeverity = DiffRiskSeverity.Medium
        };

        var writer = new StringWriter();
        await MarkdownReportFormatter.Instance.FormatAsync(report, writer);

        string md = writer.ToString();

        Assert.Contains("# 🛡️ DiffGuard Security Report — ❌ RISKS DETECTED", md);
        Assert.Contains("`main...feature/auth`", md);
        Assert.Contains("`a1b2c3d4` ... `f6g7h8i9`", md);
        Assert.Contains("**Changed Files**", md);
        Assert.Contains("**Changed Lines** | +50 / -10 (60 total", md);
        Assert.Contains("Tracked Files (Baseline)", md);
        Assert.Contains("🔴 **Critical** | 1", md);
        Assert.Contains("🟠 **High** | 1", md);
        Assert.Contains("BD-AUTH-001", md);
        Assert.Contains("`src/Auth/LoginService.cs:42:13`", md);
        Assert.Contains("secret_backdoor", md);
    }

    [Fact]
    public async Task FormatAsync_CleanDiff_DisplaysCleanStatus()
    {
        var scanResult = new DiffRiskScanResult([], 1, 5, 0, 0.1, 1.0, DiffRiskSeverity.Medium);
        var report = new DiffGuardReportModel
        {
            RepositoryPath = ".",
            ComparisonDescription = "--staged",
            DeterministicScannerEnabled = true,
            LlmJudgeEnabled = false,
            ElapsedMilliseconds = 50,
            ScanResult = scanResult,
            MinimumRiskSeverity = DiffRiskSeverity.Medium
        };

        var writer = new StringWriter();
        await MarkdownReportFormatter.Instance.FormatAsync(report, writer);

        string md = writer.ToString();

        Assert.Contains("# 🛡️ DiffGuard Security Report — ✅ CLEAN", md);
        Assert.Contains("🔴 **Critical** | 0", md);
        Assert.DoesNotContain("Detailed Findings", md);
    }
}

