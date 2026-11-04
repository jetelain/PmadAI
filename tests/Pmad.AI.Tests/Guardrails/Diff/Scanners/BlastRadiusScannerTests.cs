using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.Tests.Guardrails.Diff.Scanners;

public class BlastRadiusScannerTests
{
    [Fact]
    public void Scan_ExceedingMaxChangedFiles_FlagsScopeFiles001()
    {
        var options = new DiffRiskScannerOptions { MaxChangedFiles = 5 };
        var scanner = new BlastRadiusScanner(options);

        var files = Enumerable.Range(1, 6)
            .Select(i => new FileDiff { FilePath = $"file{i}.cs", AddedLines = [new DiffAddedLine($"file{i}.cs", 1, "test")] })
            .ToList();

        var findings = scanner.Scan(files);

        Assert.Contains(findings, f => f.RuleId == "SCOPE-FILES-001" && f.Category == DiffRiskCategory.ExcessiveBlastRadius);
    }

    [Fact]
    public void Scan_ExceedingMaxChangedLines_FlagsScopeLines001()
    {
        var options = new DiffRiskScannerOptions { MaxChangedLines = 50 };
        var scanner = new BlastRadiusScanner(options);

        var lines = Enumerable.Range(1, 60)
            .Select(i => new DiffAddedLine("big.cs", i, "line"))
            .ToList();

        var file = new FileDiff { FilePath = "big.cs", AddedLines = lines };

        var findings = scanner.Scan([file]);

        Assert.Contains(findings, f => f.RuleId == "SCOPE-LINES-001" && f.Category == DiffRiskCategory.ExcessiveBlastRadius);
    }

    [Fact]
    public void Scan_ExceedingCodebaseImpactPercent_FlagsScopePct001()
    {
        var options = new DiffRiskScannerOptions { MaxCodebaseImpactPercent = 10.0 };
        var scanner = new BlastRadiusScanner(options);

        // 15 changed lines on a baseline of 100 lines = 15% > 10%
        var lines = Enumerable.Range(1, 15)
            .Select(i => new DiffAddedLine("file.cs", i, "line"))
            .ToList();

        var file = new FileDiff { FilePath = "file.cs", AddedLines = lines };
        var baseline = new RepositoryBaselineStats(TotalTrackedFiles: 10, TotalTrackedLines: 100);

        var findings = scanner.Scan([file], baseline);

        Assert.Contains(findings, f => f.RuleId == "SCOPE-PCT-001" && f.Category == DiffRiskCategory.ExcessiveBlastRadius);
    }

    [Fact]
    public void Scan_ExceedingFileImpactPercent_FlagsScopeFilePct001()
    {
        var options = new DiffRiskScannerOptions { MaxFileImpactPercent = 15.0 };
        var scanner = new BlastRadiusScanner(options);

        // 3 changed files on a baseline of 10 files = 30% > 15%
        var files = Enumerable.Range(1, 3)
            .Select(i => new FileDiff { FilePath = $"f{i}.cs", AddedLines = [new DiffAddedLine($"f{i}.cs", 1, "test")] })
            .ToList();

        var baseline = new RepositoryBaselineStats(TotalTrackedFiles: 10, TotalTrackedLines: 1000);

        var findings = scanner.Scan(files, baseline);

        Assert.Contains(findings, f => f.RuleId == "SCOPE-FILEPCT-001" && f.Category == DiffRiskCategory.ExcessiveBlastRadius);
    }

    [Fact]
    public void Scan_WithinThresholds_ReturnsNoFindings()
    {
        var scanner = new BlastRadiusScanner();

        var file = new FileDiff
        {
            FilePath = "clean.cs",
            AddedLines = [new DiffAddedLine("clean.cs", 1, "int x = 1;")]
        };
        var baseline = new RepositoryBaselineStats(TotalTrackedFiles: 100, TotalTrackedLines: 10000);

        var findings = scanner.Scan([file], baseline);

        Assert.Empty(findings);
    }

    [Fact]
    public void Scan_ExceedingThresholds_EmitsLowSeverity()
    {
        var options = new DiffRiskScannerOptions { MaxChangedFiles = 1 };
        var scanner = new BlastRadiusScanner(options);

        var files = new List<FileDiff>
        {
            new() { FilePath = "f1.cs", AddedLines = [new DiffAddedLine("f1.cs", 1, "a")] },
            new() { FilePath = "f2.cs", AddedLines = [new DiffAddedLine("f2.cs", 1, "b")] }
        };

        var findings = scanner.Scan(files);
        var f = Assert.Single(findings);
        Assert.Equal(DiffRiskSeverity.Low, f.Severity);
    }
}