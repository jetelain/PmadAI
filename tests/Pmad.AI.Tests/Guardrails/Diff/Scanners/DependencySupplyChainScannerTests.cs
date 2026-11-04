using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.Tests.Guardrails.Diff.Scanners;

public class DependencySupplyChainScannerTests
{
    private readonly DependencySupplyChainScanner _scanner = new();

    [Theory]
    [InlineData("Microsoft.Extensions.AI")]
    [InlineData("Microsoft.Extensions.Logging")]
    [InlineData("System.Text.Json")]
    [InlineData("Pmad.AI")]
    [InlineData("Pmad.AI.Guardrails")]
    public void Scan_SafePackagesAllowlisted_PermittedWithoutFinding(string safePackage)
    {
        var diff = new FileDiff
        {
            FilePath = "src/App.csproj",
            AddedLines = [new DiffAddedLine("src/App.csproj", 10, $"  <PackageReference Include=\"{safePackage}\" Version=\"1.0.0\" />")]
        };

        var findings = _scanner.Scan([diff]);

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("src/App.csproj", "  <PackageReference Include=\"Newtonsoft.Jsn\" Version=\"1.0.0\" />")]
    [InlineData("package.json", "    \"reqeusts\": \"^2.0.0\",")]
    [InlineData("requirements.txt", "reqeusts==2.28.1")]
    [InlineData("Cargo.toml", "malicious-crate = \"1.0.0\"")]
    [InlineData("go.mod", "require github.com/evil/backdoor v1.0.0")]
    public void Scan_UnverifiedPackages_FlagsDepNew001(string filePath, string addedLine)
    {
        var diff = new FileDiff
        {
            FilePath = filePath,
            AddedLines = [new DiffAddedLine(filePath, 1, addedLine)]
        };

        var findings = _scanner.Scan([diff]);

        Assert.Contains(findings, f => f.RuleId == "DEP-NEW-001" && f.Category == DiffRiskCategory.UnverifiedDependency && f.Severity == DiffRiskSeverity.High);
    }

    [Fact]
    public void Scan_CpmMigration_WithinCommit_DoesNotFlagDepNew001()
    {
        // Moving an existing package from .csproj to Directory.Packages.props
        var csprojDiff = new FileDiff
        {
            FilePath = "src/Project.csproj",
            DeletedLines = [new DiffDeletedLine("src/Project.csproj", 12, "  <PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />")],
            AddedLines = [new DiffAddedLine("src/Project.csproj", 12, "  <PackageReference Include=\"Newtonsoft.Json\" />")]
        };

        var propsDiff = new FileDiff
        {
            FilePath = "Directory.Packages.props",
            AddedLines = [new DiffAddedLine("Directory.Packages.props", 5, "  <PackageVersion Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />")]
        };

        var findings = _scanner.Scan([csprojDiff, propsDiff]);

        Assert.Empty(findings);
    }

    [Fact]
    public void Scan_VersionUpgrade_PermittedWithoutFinding()
    {
        var diff = new FileDiff
        {
            FilePath = "package.json",
            DeletedLines = [new DiffDeletedLine("package.json", 15, "    \"lodash\": \"^1.2.0\",")],
            AddedLines = [new DiffAddedLine("package.json", 15, "    \"lodash\": \"^1.3.0\",")]
        };

        var findings = _scanner.Scan([diff]);

        Assert.DoesNotContain(findings, f => f.RuleId == "DEP-DOWN-001");
    }

    [Theory]
    [InlineData("1.3.0", "1.2.0")]
    [InlineData("2.0.0", "2.0.0-preview.1")]
    [InlineData("1.0.0", "0.9.9")]
    [InlineData("1.2.0.0", "1.1.0.0")]
    [InlineData("1.2.3.4", "1.2.3.2")]
    public void Scan_VersionDowngrade_FlagsDepDown001(string oldVer, string newVer)
    {
        var diff = new FileDiff
        {
            FilePath = "src/App.csproj",
            DeletedLines = [new DiffDeletedLine("src/App.csproj", 8, $"  <PackageReference Include=\"SomePackage\" Version=\"{oldVer}\" />")],
            AddedLines = [new DiffAddedLine("src/App.csproj", 8, $"  <PackageReference Include=\"SomePackage\" Version=\"{newVer}\" />")]
        };

        var findings = _scanner.Scan([diff]);

        Assert.Contains(findings, f => f.RuleId == "DEP-DOWN-001" && f.Category == DiffRiskCategory.DependencyDowngrade && f.Severity == DiffRiskSeverity.High);
    }

    [Fact]
    public void Scan_MultiProjectDifferentVersions_DoesNotTriggerFalsePositiveDowngrade()
    {
        // Project A upgrades SomePackage from 2.0.0 to 2.1.0
        var diffA = new FileDiff
        {
            FilePath = "src/ProjectA/ProjectA.csproj",
            DeletedLines = [new DiffDeletedLine("src/ProjectA/ProjectA.csproj", 10, "  <PackageReference Include=\"SomePackage\" Version=\"2.0.0\" />")],
            AddedLines = [new DiffAddedLine("src/ProjectA/ProjectA.csproj", 10, "  <PackageReference Include=\"SomePackage\" Version=\"2.1.0\" />")]
        };

        // Project B upgrades SomePackage from 1.0.0 to 1.1.0
        var diffB = new FileDiff
        {
            FilePath = "src/ProjectB/ProjectB.csproj",
            DeletedLines = [new DiffDeletedLine("src/ProjectB/ProjectB.csproj", 10, "  <PackageReference Include=\"SomePackage\" Version=\"1.0.0\" />")],
            AddedLines = [new DiffAddedLine("src/ProjectB/ProjectB.csproj", 10, "  <PackageReference Include=\"SomePackage\" Version=\"1.1.0\" />")]
        };

        var findings = _scanner.Scan([diffA, diffB]);

        // Neither project should be flagged as a downgrade, even though Project B's 1.1.0 < Project A's removed 2.0.0
        Assert.DoesNotContain(findings, f => f.RuleId == "DEP-DOWN-001");
    }

    [Fact]
    public void Scan_CpmMigration_DowngradeAcrossFiles_FlagsDepDown001()
    {
        // SomePackage 2.0.0 removed from project, consolidated into Directory.Packages.props with lower version 1.5.0
        var diffProj = new FileDiff
        {
            FilePath = "src/ProjectA.csproj",
            DeletedLines = [new DiffDeletedLine("src/ProjectA.csproj", 10, "  <PackageReference Include=\"SomePackage\" Version=\"2.0.0\" />")],
            AddedLines = [new DiffAddedLine("src/ProjectA.csproj", 10, "  <PackageReference Include=\"SomePackage\" />")]
        };

        var diffProps = new FileDiff
        {
            FilePath = "Directory.Packages.props",
            AddedLines = [new DiffAddedLine("Directory.Packages.props", 5, "  <PackageVersion Include=\"SomePackage\" Version=\"1.5.0\" />")]
        };

        var findings = _scanner.Scan([diffProj, diffProps]);

        Assert.Contains(findings, f => f.RuleId == "DEP-DOWN-001" && f.Description.Contains("2.0.0") && f.Description.Contains("1.5.0"));
    }

    [Fact]
    public void Scan_PackagesConfig_AttributeOrderVariations_ParsesCorrectly()
    {
        // packages.config with version before id: downgrade check
        var diffDowngrade = new FileDiff
        {
            FilePath = "packages.config",
            DeletedLines = [new DiffDeletedLine("packages.config", 5, "  <package version=\"2.0.0\" id=\"SomePackage\" targetFramework=\"net48\" />")],
            AddedLines = [new DiffAddedLine("packages.config", 5, "  <package targetFramework=\"net48\" version=\"1.0.0\" id=\"SomePackage\" />")]
        };

        var findingsDowngrade = _scanner.Scan([diffDowngrade]);
        Assert.Contains(findingsDowngrade, f => f.RuleId == "DEP-DOWN-001");

        // packages.config with version before id: unverified new dependency check
        var diffNew = new FileDiff
        {
            FilePath = "packages.config",
            AddedLines = [new DiffAddedLine("packages.config", 6, "  <package version=\"1.0.0\" id=\"Untrusted.LegacyPkg\" targetFramework=\"net48\" />")]
        };

        var findingsNew = _scanner.Scan([diffNew]);
        Assert.Contains(findingsNew, f => f.RuleId == "DEP-NEW-001" && f.Description.Contains("Untrusted.LegacyPkg"));
    }

    [Fact]
    public void Scan_VersionDowngrade_WithPackageUpdateAndVersionOverride_FlagsDepDown001()
    {
        var diff = new FileDiff
        {
            FilePath = "Directory.Packages.props",
            DeletedLines = [new DiffDeletedLine("Directory.Packages.props", 10, "  <PackageVersion Update=\"SomePackage\" Version=\"2.0.0\" />")],
            AddedLines = [new DiffAddedLine("Directory.Packages.props", 10, "  <PackageVersion Update=\"SomePackage\" Version=\"1.0.0\" />")]
        };

        var findings = _scanner.Scan([diff]);

        Assert.Contains(findings, f => f.RuleId == "DEP-DOWN-001" && f.Category == DiffRiskCategory.DependencyDowngrade && f.Severity == DiffRiskSeverity.High);
    }

    [Fact]
    public void Scan_UnverifiedPackage_InTargetsOrProps_FlagsDepNew001()
    {
        var diffProps = new FileDiff
        {
            FilePath = "build/Custom.props",
            AddedLines = [new DiffAddedLine("build/Custom.props", 5, "  <PackageReference Include=\"Untrusted.Package\" Version=\"1.0.0\" />")]
        };
        var diffTargets = new FileDiff
        {
            FilePath = "Directory.Build.targets",
            AddedLines = [new DiffAddedLine("Directory.Build.targets", 8, "  <PackageVersion Include=\"Another.Untrusted\" Version=\"2.0.0\" />")]
        };

        var findings = _scanner.Scan([diffProps, diffTargets]);

        Assert.Equal(2, findings.Count(f => f.RuleId == "DEP-NEW-001"));
    }

    [Theory]
    [InlineData("nuget.config", "<add key=\"EvilFeed\" value=\"https://evil-packages.org/v3/index.json\" />")]
    [InlineData(".npmrc", "registry=https://evil-registry.com/")]
    [InlineData(".npmrc", "registry=\"https://evil-registry.com/\"")]
    [InlineData("pip.conf", "index-url = https://evil-pypi.org/simple")]
    [InlineData("pip.conf", "index-url = \"https://evil-pypi.org/simple\"")]
    [InlineData("requirements.txt", "--index-url https://evil-pypi.org/simple")]
    [InlineData("requirements.txt", "--extra-index-url https://evil-pypi.org/simple")]
    [InlineData("requirements-dev.txt", "-i https://evil-pypi.org/simple")]
    [InlineData("nuget.config", "<add key=\"UncFeed\" value=\"\\\\evil-server\\share\\nuget\" />")]
    [InlineData("nuget.config", "<add key=\"UncFeedUri\" value=\"file://evil-server/share/nuget\" />")]
    public void Scan_UntrustedPackageFeed_FlagsDepSrc001Critical(string filePath, string addedLine)
    {
        var diff = new FileDiff
        {
            FilePath = filePath,
            AddedLines = [new DiffAddedLine(filePath, 3, addedLine)]
        };

        var findings = _scanner.Scan([diff]);

        Assert.Contains(findings, f => f.RuleId == "DEP-SRC-001" && f.Category == DiffRiskCategory.UnverifiedDependency && f.Severity == DiffRiskSeverity.Critical);
    }

    [Fact]
    public void SemanticVersion_Comparison_HandlesPreReleaseAndRanges()
    {
        Assert.True(SemanticVersion.TryParse("^1.2.3", out var v1));
        Assert.True(SemanticVersion.TryParse("1.3.0", out var v2));
        Assert.True(v1 < v2);

        Assert.True(SemanticVersion.TryParse("2.0.0", out var release));
        Assert.True(SemanticVersion.TryParse("2.0.0-preview.1", out var preview));
        Assert.True(preview < release);

        Assert.True(SemanticVersion.TryParse("v1.0.0-alpha.1", out var a1));
        Assert.True(SemanticVersion.TryParse("1.0.0-alpha.2", out var a2));
        Assert.True(a1 < a2);
    }

    [Fact]
    public void SemanticVersion_FourPartVersions_ParsedAndCompared()
    {
        Assert.True(SemanticVersion.TryParse("1.2.3.4", out var v4a));
        Assert.True(SemanticVersion.TryParse("1.2.3.2", out var v4b));
        Assert.True(v4b < v4a);
        Assert.Equal("1.2.3.4", v4a.ToString());
        Assert.Equal(4, v4a.Revision);

        Assert.True(SemanticVersion.TryParse("1.2.0.0", out var vZero));
        Assert.True(SemanticVersion.TryParse("1.1.0.0", out var vLower));
        Assert.True(vLower < vZero);

        Assert.True(SemanticVersion.TryParse("1.2.3a1", out var pythonAlpha));
        Assert.True(SemanticVersion.TryParse("1.2.3", out var pythonRelease));
        Assert.True(pythonAlpha < pythonRelease);

        Assert.True(SemanticVersion.TryParse("[1.2.3]", out var nugetExact));
        Assert.Equal(new SemanticVersion(1, 2, 3), nugetExact);

        Assert.False(SemanticVersion.TryParse("1.2.3.abc", out _));
    }

    [Fact]
    public void Scan_PyProjectAndCargo_IgnoresProjectMetadataAndDetectsRealDependencies()
    {
        var pyDiff = new FileDiff
        {
            FilePath = "pyproject.toml",
            DeletedLines =
            [
                new DiffDeletedLine("pyproject.toml", 5, "requests = \"2.28.0\"")
            ],
            AddedLines =
            [
                new DiffAddedLine("pyproject.toml", 2, "name = \"my-app\""),
                new DiffAddedLine("pyproject.toml", 3, "version = \"1.0.0\""),
                new DiffAddedLine("pyproject.toml", 4, "build-backend = \"setuptools.build_meta\""),
                new DiffAddedLine("pyproject.toml", 5, "requests = \"2.27.0\""),
                new DiffAddedLine("pyproject.toml", 6, "  \"malicious-pkg>=1.0.0\",")
            ]
        };

        var cargoDiff = new FileDiff
        {
            FilePath = "Cargo.toml",
            AddedLines =
            [
                new DiffAddedLine("Cargo.toml", 2, "name = \"my-crate\""),
                new DiffAddedLine("Cargo.toml", 3, "edition = \"2021\""),
                new DiffAddedLine("Cargo.toml", 4, "license = \"MIT\"")
            ]
        };

        var findings = _scanner.Scan([pyDiff, cargoDiff]);

        // requests 2.28.0 -> 2.27.0 is a downgrade
        Assert.Contains(findings, f => f.RuleId == "DEP-DOWN-001" && f.Description.Contains("requests"));
        // malicious-pkg is a new dependency
        Assert.Single(findings, f => f.RuleId == "DEP-NEW-001");
        Assert.Contains(findings, f => f.RuleId == "DEP-NEW-001" && f.Description.Contains("malicious-pkg"));
    }

    [Fact]
    public void Scan_LocalFilePackageFeed_FlagsDepSrc001Critical()
    {
        var diff = new FileDiff
        {
            FilePath = "nuget.config",
            AddedLines = [new DiffAddedLine("nuget.config", 4, "<add key=\"LocalEvil\" value=\"file:///tmp/evil-packages\" />")]
        };

        var findings = _scanner.Scan([diff]);

        Assert.Contains(findings, f => f.RuleId == "DEP-SRC-001" && f.Severity == DiffRiskSeverity.Critical);
    }
}
