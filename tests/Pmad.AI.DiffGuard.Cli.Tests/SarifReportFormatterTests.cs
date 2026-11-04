using System.Text.Json;
using Pmad.AI.DiffGuard.Cli.Execution;
using Pmad.AI.DiffGuard.Cli.Formatting.Sarif;
using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.DiffGuard.Cli.Tests;

public class SarifReportFormatterTests
{
    [Fact]
    public void BuildSarifLog_MapsSeverityAndProducesDeterministicGuids()
    {
        var findings = new List<DiffRiskFinding>
        {
            new(
                RuleId: "BD-AUTH-001",
                Title: "Auth Bypass",
                Category: DiffRiskCategory.BackdoorPattern,
                Severity: DiffRiskSeverity.Critical,
                FilePath: "src\\Auth\\Login.cs",
                LineNumber: 42,
                Column: 5,
                Description: "Hardcoded master password comparison.",
                Snippet: "if (p == \"master\") return true;"),
            new(
                RuleId: "PAY-ENT-001",
                Title: "High Entropy",
                Category: DiffRiskCategory.SuspiciousPayload,
                Severity: DiffRiskSeverity.Medium,
                FilePath: "data/secret.txt",
                LineNumber: 1,
                Column: 1,
                Description: "Shannon entropy exceeds threshold.",
                Snippet: "9k2m4n5b6v7c8x9z0a1s2d3f4g5h6j7k8l9")
        };

        var scanResult = new DiffRiskScanResult(findings, 2, 10, 0);
        var report = new DiffGuardReportModel
        {
            RepositoryPath = "repo",
            ComparisonDescription = "main...head",
            DeterministicScannerEnabled = true,
            ScanResult = scanResult,
            MinimumRiskSeverity = DiffRiskSeverity.Medium
        };

        var log = SarifReportFormatter.BuildSarifLog(report);

        Assert.Equal("2.1.0", log.Version);
        var run = Assert.Single(log.Runs);
        Assert.Equal("Pmad.AI.DiffGuard", run.Tool.Driver.Name);
        Assert.NotEmpty(run.Tool.Driver.Rules);

        Assert.Equal(2, run.Results.Count);

        // Result 1: Critical -> "error"
        var r1 = run.Results[0];
        Assert.Equal("BD-AUTH-001", r1.RuleId);
        Assert.Equal("error", r1.Level);
        Assert.NotNull(r1.CorrelationGuid);
        Assert.NotNull(r1.PartialFingerprints);
        Assert.Equal("BD-AUTH-001", r1.PartialFingerprints["ruleId/v1"]);
        Assert.Equal("src/Auth/Login.cs:42:5", r1.PartialFingerprints["locationKey/v1"]);
        Assert.Equal(16, r1.PartialFingerprints["contentHash/v1"].Length);

        var loc1 = Assert.Single(r1.Locations);
        Assert.Equal("src/Auth/Login.cs", loc1.PhysicalLocation.ArtifactLocation.Uri);
        Assert.Equal(42, loc1.PhysicalLocation.Region.StartLine);
        Assert.Equal(5, loc1.PhysicalLocation.Region.StartColumn);

        // Result 2: Medium -> "warning"
        var r2 = run.Results[1];
        Assert.Equal("PAY-ENT-001", r2.RuleId);
        Assert.Equal("warning", r2.Level);

        // Test deterministic correlation GUID stability across multiple calls
        var log2 = SarifReportFormatter.BuildSarifLog(report);
        Assert.Equal(r1.CorrelationGuid, log2.Runs[0].Results[0].CorrelationGuid);
    }

    [Fact]
    public void BuildSarifLog_RepoLevelFindingWithoutFilePath_UsesFallbackLocation()
    {
        var findings = new List<DiffRiskFinding>
        {
            new(
                RuleId: "SCOPE-FILES-001",
                Title: "Excessive Changed Files",
                Category: DiffRiskCategory.ExcessiveBlastRadius,
                Severity: DiffRiskSeverity.High,
                FilePath: null,
                LineNumber: null,
                Column: null,
                Description: "Changed files count exceeds threshold.")
        };

        var scanResult = new DiffRiskScanResult(findings, 30, 100, 0);
        var report = new DiffGuardReportModel
        {
            RepositoryPath = "repo",
            ComparisonDescription = "--staged",
            DeterministicScannerEnabled = true,
            ScanResult = scanResult,
            MinimumRiskSeverity = DiffRiskSeverity.Medium
        };

        var log = SarifReportFormatter.BuildSarifLog(report);

        var r = Assert.Single(log.Runs[0].Results);
        var loc = Assert.Single(r.Locations);
        Assert.Equal("README.md", loc.PhysicalLocation.ArtifactLocation.Uri);
        Assert.Equal(1, loc.PhysicalLocation.Region.StartLine);
    }

    [Fact]
    public void BuildSarifLog_RepoLevelFindingWithFirstChangedFilePath_UsesFirstChangedFile()
    {
        var findings = new List<DiffRiskFinding>
        {
            new(
                RuleId: "SCOPE-FILES-001",
                Title: "Excessive Changed Files",
                Category: DiffRiskCategory.ExcessiveBlastRadius,
                Severity: DiffRiskSeverity.Medium,
                FilePath: null,
                LineNumber: null,
                Column: null,
                Description: "Changed files count exceeds threshold.")
        };

        var scanResult = new DiffRiskScanResult(findings, 30, 100, 0);
        var report = new DiffGuardReportModel
        {
            RepositoryPath = "repo",
            ComparisonDescription = "--staged",
            DeterministicScannerEnabled = true,
            ScanResult = scanResult,
            MinimumRiskSeverity = DiffRiskSeverity.Medium,
            FirstChangedFilePath = "src\\FirstChanged.cs"
        };

        var log = SarifReportFormatter.BuildSarifLog(report);

        var r = Assert.Single(log.Runs[0].Results);
        var loc = Assert.Single(r.Locations);
        Assert.Equal("src/FirstChanged.cs", loc.PhysicalLocation.ArtifactLocation.Uri);
        Assert.Equal(1, loc.PhysicalLocation.Region.StartLine);
    }

    [Fact]
    public async Task FormatAsync_SerializesValidSarifJson()
    {
        var findings = new List<DiffRiskFinding>
        {
            new(
                RuleId: "BD-EXEC-001",
                Title: "Dynamic Execution",
                Category: DiffRiskCategory.BackdoorPattern,
                Severity: DiffRiskSeverity.High,
                FilePath: "eval.py",
                LineNumber: 5,
                Column: 1,
                Description: "subprocess call detected.")
        };

        var scanResult = new DiffRiskScanResult(findings, 1, 10, 0);
        var report = new DiffGuardReportModel
        {
            RepositoryPath = "repo",
            ComparisonDescription = "head",
            DeterministicScannerEnabled = true,
            ScanResult = scanResult,
            MinimumRiskSeverity = DiffRiskSeverity.Medium
        };

        var writer = new StringWriter();
        await SarifReportFormatter.Instance.FormatAsync(report, writer);

        string json = writer.ToString();
        Assert.Contains("\"version\": \"2.1.0\"", json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("2.1.0", root.GetProperty("version").GetString());
        Assert.True(root.GetProperty("runs").GetArrayLength() > 0);
    }
}

