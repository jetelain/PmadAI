using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.Tests.Guardrails.Diff.Scanners;

public class DeterministicDiffRiskScannerTests
{
    private readonly DeterministicDiffRiskScanner _scanner = new();

    [Fact]
    public async Task ScanAsync_EmptyOrWhitespaceDiff_ReturnsEmptyResult()
    {
        var result1 = await _scanner.ScanAsync("");
        var result2 = await _scanner.ScanAsync("   \r\n  ");
        var result3 = await _scanner.ScanAsync([]);

        Assert.False(result1.HasRisks);
        Assert.Empty(result1.Findings);
        Assert.False(result2.HasRisks);
        Assert.False(result3.HasRisks);
    }

    [Fact]
    public async Task ScanAsync_CleanDiff_ReturnsZeroFindingsAndNoRisks()
    {
        string cleanDiff = """
            diff --git a/src/Calculator.cs b/src/Calculator.cs
            index 83db48f..bf269f4 100644
            --- a/src/Calculator.cs
            +++ b/src/Calculator.cs
            @@ -10,2 +10,4 @@
             public int Add(int a, int b)
             {
            +    // Clean business logic
            +    return a + b;
             }
            """;

        var result = await _scanner.ScanAsync(cleanDiff);

        Assert.False(result.HasRisks);
        Assert.Empty(result.Findings);
        Assert.Equal(1, result.ChangedFilesCount);
        Assert.Equal(2, result.AddedLinesCount);
        Assert.Equal(0, result.DeletedLinesCount);
    }

    [Fact]
    public async Task ScanAsync_BackdoorDiff_DetectsSecurityFindings()
    {
        string backdoorDiff = """
            diff --git a/src/AuthService.cs b/src/AuthService.cs
            index 1234567..89abcde 100644
            --- a/src/AuthService.cs
            +++ b/src/AuthService.cs
            @@ -20,3 +20,6 @@
             public bool Verify(string user, string pwd)
             {
            +    if (password == "SUPER_SECRET_BACKDOOR_2026") return true;
            +    var asm = Assembly.Load(payloadBytes);
            +    var url = "http://93.184.216.34/exfil";
                 return DbCheck(user, pwd);
             }
            """;

        var result = await _scanner.ScanAsync(backdoorDiff);

        Assert.True(result.HasRisks);
        Assert.Equal(DiffRiskSeverity.High, result.HighestSeverity);
        Assert.Contains(result.Findings, f => f.RuleId == "BD-AUTH-001");
        Assert.Contains(result.Findings, f => f.RuleId == "BD-EXEC-001");
        Assert.Contains(result.Findings, f => f.RuleId == "NET-IP-001");
    }

    [Fact]
    public async Task ScanAsync_PreParsedFiles_YieldsIdenticalResults()
    {
        string diffText = """
            diff --git a/src/Service.cs b/src/Service.cs
            index 111..222 100644
            --- a/src/Service.cs
            +++ b/src/Service.cs
            @@ -1,2 +1,3 @@
            +string meta = "http://169.254.169.254/meta";
             void Run() {}
            """;

        var parsedFiles = DiffParser.Parse(diffText);
        var resultFromText = await _scanner.ScanAsync(diffText);
        var resultFromFiles = await _scanner.ScanAsync(parsedFiles);

        Assert.Equal(resultFromText.Findings.Count, resultFromFiles.Findings.Count);
        Assert.Equal(resultFromText.HighestSeverity, resultFromFiles.HighestSeverity);
        Assert.Equal(resultFromText.TotalChangedLines, resultFromFiles.TotalChangedLines);
        Assert.Equal(DiffRiskSeverity.Critical, resultFromFiles.HighestSeverity);
    }

    [Fact]
    public async Task ScanAsync_PreParsedFiles_RespectsExcludedFileGlobs()
    {
        var customScanner = new DeterministicDiffRiskScanner(new DiffRiskScannerOptions
        {
            ExcludedFileGlobs = ["*.generated.cs", "vendor/**"]
        });

        var excludedFile = new FileDiff
        {
            FilePath = "src/Model.generated.cs",
            AddedLines = [new DiffAddedLine("src/Model.generated.cs", 1, "var x = \"http://93.184.216.34/api\";")]
        };
        var normalFile = new FileDiff
        {
            FilePath = "src/Clean.cs",
            AddedLines = [new DiffAddedLine("src/Clean.cs", 1, "int a = 1;")]
        };

        var result = await customScanner.ScanAsync([excludedFile, normalFile]);

        Assert.False(result.HasRisks);
        Assert.Empty(result.Findings);
        Assert.Equal(1, result.ChangedFilesCount);
    }

    [Fact]
    public async Task ScanAsync_SamplesFromWorkspace_IfAvailable_ExecutesCleanly()
    {
        string samplePath = @"C:\Workspace\POC\samples\clean.patch";
        if (File.Exists(samplePath))
        {
            string diff = await File.ReadAllTextAsync(samplePath);
            var result = await _scanner.ScanAsync(diff);
            Assert.False(result.HasRisks);
        }

        string backdoorPath = @"C:\Workspace\POC\samples\backdoor.patch";
        if (File.Exists(backdoorPath))
        {
            string diff = await File.ReadAllTextAsync(backdoorPath);
            var result = await _scanner.ScanAsync(diff);
            Assert.True(result.HasRisks);
        }
    }
}

