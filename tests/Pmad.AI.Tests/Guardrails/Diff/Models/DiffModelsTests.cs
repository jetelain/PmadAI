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
}
