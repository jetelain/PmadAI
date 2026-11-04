using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.Tests.Guardrails.Diff.Models;

public class DiffModelsTests
{
    [Fact]
    public void DiffRiskScanResult_ComputesMetricsCorrectly()
    {
        var findings = new List<DiffRiskFinding>
        {
            new("TEST-001", "Low Risk", DiffRiskCategory.BackdoorPattern, DiffRiskSeverity.Low, "a.cs", 1, 1, "test"),
            new("TEST-002", "High Risk", DiffRiskCategory.HardcodedNetworkEndpoint, DiffRiskSeverity.High, "b.cs", 2, 1, "test")
        };

        var result = new DiffRiskScanResult(
            Findings: findings,
            ChangedFilesCount: 2,
            AddedLinesCount: 15,
            DeletedLinesCount: 5,
            CodebaseImpactPercent: 1.5,
            FileImpactPercent: 2.0,
            MinimumRiskSeverity: DiffRiskSeverity.Medium);

        Assert.Equal(20, result.TotalChangedLines);
        Assert.Equal(DiffRiskSeverity.High, result.HighestSeverity);
        Assert.True(result.HasRisks);
        Assert.True(result.HasFindingsAtOrAbove(DiffRiskSeverity.Medium));
        Assert.True(result.HasFindingsAtOrAbove(DiffRiskSeverity.High));
        Assert.False(result.HasFindingsAtOrAbove(DiffRiskSeverity.Critical));
    }

    [Fact]
    public void DiffRiskScanResult_Empty_HasNoRisks()
    {
        var empty = DiffRiskScanResult.Empty;
        Assert.Empty(empty.Findings);
        Assert.Equal(0, empty.TotalChangedLines);
        Assert.Null(empty.HighestSeverity);
        Assert.False(empty.HasRisks);
    }

    [Fact]
    public void DiffRiskScannerOptions_HashSetProperties_CoerceOrdinalIgnoreCase()
    {
        var options = new DiffRiskScannerOptions
        {
            WellKnownSafeDomains = ["MY-DOMAIN.COM"],
            AllowedIpAddresses = ["192.168.1.1"],
            WellKnownSafePackages = ["MYPACKAGE.*"],
            AllowedImageExtensions = [".PNG"]
        };

        Assert.Contains("my-domain.com", options.WellKnownSafeDomains);
        Assert.Contains("192.168.1.1", options.AllowedIpAddresses);
        Assert.Contains("mypackage.*", options.WellKnownSafePackages);
        Assert.Contains(".png", options.AllowedImageExtensions);
    }

    [Fact]
    public void FileDiff_ToTextAndGetByteCount_Match()
    {
        var file = new FileDiff
        {
            FilePath = "test.txt",
            HeaderLines = ["diff --git a/test.txt b/test.txt", "--- a/test.txt", "+++ b/test.txt"],
            Hunks =
            [
                new DiffHunk
                {
                    Header = "@@ -1,1 +1,2 @@",
                    Lines = [" context", "+added"]
                }
            ]
        };

        var text = file.ToText();
        var bytes = System.Text.Encoding.UTF8.GetByteCount(text);
        Assert.Equal(bytes, file.GetByteCount());
    }

    [Fact]
    public void DiffRiskScannerOptions_NullProperties_ThrowsArgumentNullException()
    {
        var options = new DiffRiskScannerOptions();

        Assert.Throws<ArgumentNullException>(() => options.WellKnownSafeDomains = null!);
        Assert.Throws<ArgumentNullException>(() => options.AllowedIpAddresses = null!);
        Assert.Throws<ArgumentNullException>(() => options.WellKnownSafePackages = null!);
        Assert.Throws<ArgumentNullException>(() => options.AllowedImageExtensions = null!);
        Assert.Throws<ArgumentNullException>(() => options.ExcludedFileGlobs = null!);
        Assert.Throws<ArgumentNullException>(() => options.TestFileGlobs = null!);
        Assert.Throws<ArgumentNullException>(() => options.SafeBinaryValidators = null!);
    }

    [Fact]
    public void BinaryValidationContext_NullStreamOpener_OpenReadReturnsNull()
    {
        var context = new BinaryValidationContext
        {
            FilePath = "image.png",
            Extension = ".png",
            StreamOpener = null!
        };

        Assert.Null(context.OpenRead());
    }

    [Fact]
    public void DiffRiskDefaults_IsTestFile_MatchesDefaultGlobs()
    {
        Assert.True(DiffRiskDefaults.IsTestFile("tests/ClientTests.cs"));
        Assert.True(DiffRiskDefaults.IsTestFile("tests/subdir/DeepTests.cs"));
        Assert.True(DiffRiskDefaults.IsTestFile("test/UnitTest.cs"));
        Assert.True(DiffRiskDefaults.IsTestFile("tests/App.Tests.csproj"));
        Assert.True(DiffRiskDefaults.IsTestFile("tests/App.test.props"));

        Assert.False(DiffRiskDefaults.IsTestFile(null));
        Assert.False(DiffRiskDefaults.IsTestFile(""));
        Assert.False(DiffRiskDefaults.IsTestFile("src/Client.cs"));
        Assert.False(DiffRiskDefaults.IsTestFile("src/contest/contest.cs"));
        Assert.False(DiffRiskDefaults.IsTestFile("src/MyProject.Tests/UnitTest1.cs"));
        Assert.False(DiffRiskDefaults.IsTestFile("MyProject.Tests/UnitTest1.cs"));
    }

    [Fact]
    public void DiffRiskDefaults_IsTestFile_CustomGlobsAndDirectories()
    {
        var customGlobs = new[] { "spec/**", "custom_tests", "trailing_tests/", "src/exact-test.cs" };

        Assert.True(DiffRiskDefaults.IsTestFile("spec/unit/suite.rb", customGlobs));
        Assert.True(DiffRiskDefaults.IsTestFile("custom_tests/foo.py", customGlobs));
        Assert.True(DiffRiskDefaults.IsTestFile("trailing_tests/bar.py", customGlobs));
        Assert.True(DiffRiskDefaults.IsTestFile("src/exact-test.cs", customGlobs));

        // Root-anchored directory patterns ("custom_tests" and "trailing_tests/") must not match nested subdirectories
        Assert.False(DiffRiskDefaults.IsTestFile("src/custom_tests/foo.py", customGlobs));
        Assert.False(DiffRiskDefaults.IsTestFile("src/trailing_tests/bar.py", customGlobs));
        Assert.False(DiffRiskDefaults.IsTestFile("tests/ClientTests.cs", customGlobs));
        Assert.False(DiffRiskDefaults.IsTestFile("src/Client.cs", customGlobs));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("/", false)]
    [InlineData("*", false)]
    [InlineData("**", false)]
    [InlineData("**/*", false)]
    [InlineData("*.*", false)]
    [InlineData("*.cs", false)]
    [InlineData("**/*.cs", false)]
    [InlineData("test/**", true)]
    [InlineData("tests/**", true)]
    [InlineData("custom_tests", true)]
    [InlineData("src/*.Tests/**", true)]
    [InlineData("**/*.Tests.csproj", true)]
    [InlineData("**/*.spec.ts", true)]
    [InlineData("**/*Tests.cs", true)]
    public void DiffRiskDefaults_IsValidTestFileGlob_ValidatesExpectedPatterns(string? pattern, bool expected)
    {
        Assert.Equal(expected, DiffRiskDefaults.IsValidTestFileGlob(pattern));
    }

    [Fact]
    public void DiffRiskDefaults_IsTestFile_IgnoresInvalidBroadWildcardGlobs()
    {
        var broadGlobs = new[] { "*", "**", "**/*", "*.*", "*.cs", "**/*.cs" };

        Assert.False(DiffRiskDefaults.IsTestFile("src/Client.cs", broadGlobs));
        Assert.False(DiffRiskDefaults.IsTestFile("Program.cs", broadGlobs));
    }

    [Fact]
    public void DiffRiskScannerOptions_TestFileGlobs_DefaultsAndProperties()
    {
        var options = new DiffRiskScannerOptions();
        Assert.Equal(DiffRiskDefaults.DefaultTestFileGlobs, options.TestFileGlobs);

        options.TestFileGlobs = ["my-test/**"];
        Assert.True(options.IsTestFile("my-test/foo.cs"));
        Assert.False(options.IsTestFile("tests/ClientTests.cs"));
    }
}
