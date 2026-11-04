using System.Text.RegularExpressions;

namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Scans git diffs for dependency supply-chain risks: newly added unverified dependencies (DEP-NEW-001),
/// semantic version downgrades (DEP-DOWN-001), and untrusted package feeds or registries (DEP-SRC-001).
/// Features commit-wide manifest comparison for Central Package Management (CPM) awareness.
/// </summary>
public sealed partial class DependencySupplyChainScanner
{
    private sealed record PackageEntry(
        string Ecosystem,
        string PackageId,
        string? Version,
        string FilePath,
        int LineNumber,
        string LineContent);

    // .NET PackageReference / PackageVersion / packages.config
    [GeneratedRegex(@"<(?:PackageReference|PackageVersion)\b([^>]+)>", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex DotNetPackageTagRegex();

    [GeneratedRegex(@"\b(?:Include|Update)\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex DotNetIncludeAttrRegex();

    [GeneratedRegex(@"\b(?:Version|VersionOverride)\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex DotNetVersionAttrRegex();

    [GeneratedRegex(@"<package\b([^>]+)>", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex PackagesConfigTagRegex();

    [GeneratedRegex(@"\bid\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex PackagesConfigIdAttrRegex();

    // Package feeds
    [GeneratedRegex(@"<add\b[^>]*\bvalue\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex NugetConfigAddSourceRegex();

    [GeneratedRegex(@"<Restore(?:AdditionalProject)?Sources>([^<]+)</Restore(?:AdditionalProject)?Sources>", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex DotNetRestoreSourcesRegex();

    [GeneratedRegex(@"(?:^|\n)\s*(?:@[\w\-]+:)?registry\s*=\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex NpmrcRegistryRegex();

    [GeneratedRegex(@"(?:^|\n)\s*(?:extra-)?index-url\s*=\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex PipConfIndexRegex();

    [GeneratedRegex(@"(?:--extra-index-url|--index-url|-i)\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex RequirementsFeedRegex();

    // npm package.json
    [GeneratedRegex(@"""(@?[\w\-\.\/]+)""\s*:\s*""([^""]+)""", RegexOptions.NonBacktracking)]
    private static partial Regex NpmPackageRegex();

    // Python requirements.txt
    [GeneratedRegex(@"^\s*([a-zA-Z0-9][a-zA-Z0-9_\-\.]*)(?:\[[a-zA-Z0-9_,\-\s]+\])?\s*(?:==|>=|<=|~=|!=|<|>|\^)?\s*([0-9a-zA-Z_\-\.\+\*]*)", RegexOptions.NonBacktracking)]
    private static partial Regex PythonRequirementsRegex();

    // Python pyproject.toml (PEP 621 array entry or Poetry/table entry)
    [GeneratedRegex(@"[""']([a-zA-Z][a-zA-Z0-9_\-\.]*)(?:\[[a-zA-Z0-9_,\-\s]+\])?\s*(?:(?:==|>=|<=|~=|!=|<|>|\^)\s*([0-9a-zA-Z_\-\.\+\*]+))?[""']", RegexOptions.NonBacktracking)]
    private static partial Regex PyProjectArrayDepRegex();

    [GeneratedRegex(@"^\s*([a-zA-Z][a-zA-Z0-9_\-\.]*)\s*=\s*(?:[""'](?:\^|~|>=|<=|==|~=)?([0-9][0-9a-zA-Z_\-\.\+\*]*)[""']|\{[^}]*version\s*=\s*[""']([^""']+)[""'])", RegexOptions.NonBacktracking)]
    private static partial Regex PyProjectTableDepRegex();

    // Cargo.toml
    [GeneratedRegex(@"^\s*([a-zA-Z0-9_\-]+)\s*=\s*[""']([0-9\^~=><\*][^""']*)[""']", RegexOptions.NonBacktracking)]
    private static partial Regex CargoSimpleDepRegex();

    [GeneratedRegex(@"^\s*([a-zA-Z0-9_\-]+)\s*=\s*\{[^}]*version\s*=\s*[""']([^""']+)[""']", RegexOptions.NonBacktracking)]
    private static partial Regex CargoTableDepRegex();

    // go.mod
    [GeneratedRegex(@"^\s*(?:require\s+)?([a-zA-Z0-9_\-\.\/]+)\s+v?([0-9][0-9a-zA-Z_\-\.\+]*)", RegexOptions.NonBacktracking)]
    private static partial Regex GoModDepRegex();

    private static readonly HashSet<string> IgnoredNpmKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "version", "description", "main", "types", "typings",
        "scripts", "repository", "keywords", "author", "license", "bugs", "homepage",
        "packageManager", "engines", "type", "module"
    };

    private static readonly HashSet<string> IgnoredTomlKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "version", "description", "edition", "rust-version", "authors",
        "license", "license-file", "readme", "homepage", "repository", "documentation",
        "keywords", "categories", "workspace", "build", "links", "publish",
        "default-run", "autobins", "autoexamples", "autotests", "autobenches",
        "resolver", "build-backend", "requires-python", "python", "path", "git", "branch", "tag", "rev"
    };

    private readonly DiffRiskScannerOptions _options;

    public DependencySupplyChainScanner(DiffRiskScannerOptions? options = null)
    {
        _options = options ?? new DiffRiskScannerOptions();
    }

    public IReadOnlyList<DiffRiskFinding> Scan(IReadOnlyList<FileDiff> files)
    {
        if (!_options.EnableDependencyRules || files.Count == 0)
        {
            return [];
        }

        var findings = new List<DiffRiskFinding>();
        var removedEntries = new List<PackageEntry>();
        var addedEntries = new List<PackageEntry>();

        foreach (var file in files)
        {
            string fileName = Path.GetFileName(file.FilePath);

            // 1. Check Package Feeds / Sources (DEP-SRC-001)
            CheckPackageFeeds(file, findings);

            // 2. Extract removed and added dependencies for CPM-aware commit-wide comparison
            string? ecosystem = DetectEcosystem(fileName);
            if (ecosystem == null)
            {
                continue;
            }

            foreach (var deleted in file.DeletedLines)
            {
                ExtractPackagesFromLine(deleted.Content, deleted.LineNumber, file.FilePath, ecosystem, fileName, removedEntries);
            }

            foreach (var added in file.AddedLines)
            {
                ExtractPackagesFromLine(added.Content, added.LineNumber, file.FilePath, ecosystem, fileName, addedEntries);
            }
        }

        // Group removed entries by (Ecosystem, PackageId)
        var removedDict = removedEntries
            .GroupBy(e => (e.Ecosystem, e.PackageId.ToLowerInvariant()))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Process added entries
        foreach (var added in addedEntries)
        {
            var key = (added.Ecosystem, added.PackageId.ToLowerInvariant());

            if (removedDict.TryGetValue(key, out var matchingRemoved))
            {
                // Dependency was existing in the commit! Check for SemVer downgrades (DEP-DOWN-001)
                if (!string.IsNullOrEmpty(added.Version))
                {
                    // Prioritize removals in the same file first; fall back to commit-wide removals for CPM migrations / file moves
                    var sameFileRemoved = matchingRemoved.FindAll(r => string.Equals(r.FilePath, added.FilePath, StringComparison.OrdinalIgnoreCase));
                    var candidates = sameFileRemoved.Count > 0 ? sameFileRemoved : matchingRemoved;

                    foreach (var old in candidates)
                    {
                        if (!string.IsNullOrEmpty(old.Version) &&
                            SemanticVersion.TryParse(old.Version, out var oldSemVer) &&
                            SemanticVersion.TryParse(added.Version, out var newSemVer))
                        {
                            if (newSemVer < oldSemVer)
                            {
                                findings.Add(new DiffRiskFinding(
                                    RuleId: "DEP-DOWN-001",
                                    Title: "Dependency Version Downgrade",
                                    Category: DiffRiskCategory.DependencyDowngrade,
                                    Severity: DiffRiskSeverity.High,
                                    FilePath: added.FilePath,
                                    LineNumber: added.LineNumber,
                                    Column: null,
                                    Description: $"Dependency '{added.PackageId}' was downgraded from {old.Version} to {added.Version}.",
                                    Snippet: added.LineContent.Trim()));
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                // Newly added external package dependency (DEP-NEW-001)
                if (!IsSafePackage(added.PackageId, _options.WellKnownSafePackages))
                {
                    string verStr = !string.IsNullOrEmpty(added.Version) ? $" ({added.Version})" : "";
                    findings.Add(new DiffRiskFinding(
                        RuleId: "DEP-NEW-001",
                        Title: "Unverified Package Dependency Added",
                        Category: DiffRiskCategory.UnverifiedDependency,
                        Severity: DiffRiskSeverity.High,
                        FilePath: added.FilePath,
                        LineNumber: added.LineNumber,
                        Column: null,
                        Description: $"Newly added external dependency '{added.PackageId}'{verStr} is not in the allowlist of safe packages.",
                        Snippet: added.LineContent.Trim()));
                }
            }
        }

        return findings;
    }

    private void CheckPackageFeeds(FileDiff file, List<DiffRiskFinding> findings)
    {
        string fileName = Path.GetFileName(file.FilePath);

        bool isNugetConfig = fileName.Equals("nuget.config", StringComparison.OrdinalIgnoreCase);
        bool isDotNetProject = fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                               fileName.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                               fileName.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) ||
                               fileName.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
                               fileName.EndsWith(".targets", StringComparison.OrdinalIgnoreCase);
        bool isNpmrc = fileName.Equals(".npmrc", StringComparison.OrdinalIgnoreCase);
        bool isPipConf = fileName.Equals("pip.conf", StringComparison.OrdinalIgnoreCase);
        bool isRequirementsTxt = fileName.StartsWith("requirements", StringComparison.OrdinalIgnoreCase) &&
                                 fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

        if (!isNugetConfig && !isDotNetProject && !isNpmrc && !isPipConf && !isRequirementsTxt)
        {
            return;
        }

        foreach (var line in file.AddedLines)
        {
            if (isNugetConfig)
            {
                var match = NugetConfigAddSourceRegex().Match(line.Content);
                if (match.Success)
                {
                    EvaluateFeedUrl(match.Groups[1].Value, line, findings);
                }
            }
            else if (isDotNetProject)
            {
                var match = DotNetRestoreSourcesRegex().Match(line.Content);
                if (match.Success)
                {
                    string[] sources = match.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var s in sources)
                    {
                        EvaluateFeedUrl(s, line, findings);
                    }
                }
            }
            else if (isNpmrc)
            {
                var match = NpmrcRegistryRegex().Match(line.Content);
                if (match.Success)
                {
                    EvaluateFeedUrl(match.Groups[1].Value, line, findings);
                }
            }
            else if (isPipConf)
            {
                var match = PipConfIndexRegex().Match(line.Content);
                if (match.Success)
                {
                    EvaluateFeedUrl(match.Groups[1].Value, line, findings);
                }
            }
            else if (isRequirementsTxt)
            {
                var match = RequirementsFeedRegex().Match(line.Content);
                if (match.Success)
                {
                    EvaluateFeedUrl(match.Groups[1].Value, line, findings);
                }
            }
        }
    }

    private void EvaluateFeedUrl(string feedUrl, DiffAddedLine line, List<DiffRiskFinding> findings)
    {
        feedUrl = feedUrl.Trim('"', '\'', ' ', '\t', ';');

        if (Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri))
        {
            if (uri.IsUnc || uri.IsFile)
            {
                findings.Add(new DiffRiskFinding(
                    RuleId: "DEP-SRC-001",
                    Title: "Untrusted Package Feed / Source",
                    Category: DiffRiskCategory.UnverifiedDependency,
                    Severity: DiffRiskSeverity.Critical,
                    FilePath: line.FilePath,
                    LineNumber: line.LineNumber,
                    Column: null,
                    Description: $"Untrusted UNC or local file package feed path '{feedUrl}' added.",
                    Snippet: line.Content.Trim()));
                return;
            }

            if (!string.IsNullOrEmpty(uri.Host) && !uri.IsLoopback)
            {
                if (!string.IsNullOrEmpty(uri.UserInfo) || !IsSafeDomain(uri.IdnHost, _options.WellKnownSafeDomains))
                {
                    findings.Add(new DiffRiskFinding(
                        RuleId: "DEP-SRC-001",
                        Title: "Untrusted Package Feed / Source",
                        Category: DiffRiskCategory.UnverifiedDependency,
                        Severity: DiffRiskSeverity.Critical,
                        FilePath: line.FilePath,
                        LineNumber: line.LineNumber,
                        Column: null,
                        Description: $"Untrusted package feed URL '{feedUrl}' targets non-allowlisted domain '{uri.IdnHost}'.",
                        Snippet: line.Content.Trim()));
                }
                return;
            }
        }

        // If it starts with UNC path \\server\share or //server/share
        if (feedUrl.StartsWith(@"\\", StringComparison.Ordinal) || feedUrl.StartsWith("//", StringComparison.Ordinal))
        {
            findings.Add(new DiffRiskFinding(
                RuleId: "DEP-SRC-001",
                Title: "Untrusted Package Feed / Source",
                Category: DiffRiskCategory.UnverifiedDependency,
                Severity: DiffRiskSeverity.Critical,
                FilePath: line.FilePath,
                LineNumber: line.LineNumber,
                Column: null,
                Description: $"Untrusted UNC or network package feed path '{feedUrl}' added.",
                Snippet: line.Content.Trim()));
        }
    }

    private static string? DetectEcosystem(string fileName)
    {
        if (fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".targets", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("packages.config", StringComparison.OrdinalIgnoreCase))
        {
            return ".NET";
        }

        if (fileName.Equals("package.json", StringComparison.OrdinalIgnoreCase))
        {
            return "npm";
        }

        if (fileName.StartsWith("requirements", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase))
        {
            return "Python";
        }

        if (fileName.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase))
        {
            return "Cargo";
        }

        if (fileName.Equals("go.mod", StringComparison.OrdinalIgnoreCase))
        {
            return "Go";
        }

        return null;
    }

    private static void ExtractPackagesFromLine(
        string content,
        int lineNumber,
        string filePath,
        string ecosystem,
        string fileName,
        List<PackageEntry> targetList)
    {
        if (string.IsNullOrWhiteSpace(content)) return;

        switch (ecosystem)
        {
            case ".NET":
                var dotNetMatch = DotNetPackageTagRegex().Match(content);
                if (dotNetMatch.Success)
                {
                    string tagContent = dotNetMatch.Groups[1].Value;
                    var incMatch = DotNetIncludeAttrRegex().Match(tagContent);
                    if (incMatch.Success)
                    {
                        string pkg = incMatch.Groups[1].Value;
                        var verMatch = DotNetVersionAttrRegex().Match(tagContent);
                        string? ver = verMatch.Success ? verMatch.Groups[1].Value : null;
                        targetList.Add(new PackageEntry(ecosystem, pkg, ver, filePath, lineNumber, content));
                    }
                }
                else
                {
                    var pcMatch = PackagesConfigTagRegex().Match(content);
                    if (pcMatch.Success)
                    {
                        string tagContent = pcMatch.Groups[1].Value;
                        var idMatch = PackagesConfigIdAttrRegex().Match(tagContent);
                        var verMatch = DotNetVersionAttrRegex().Match(tagContent);
                        if (idMatch.Success)
                        {
                            string pkg = idMatch.Groups[1].Value;
                            string? ver = verMatch.Success ? verMatch.Groups[1].Value : null;
                            targetList.Add(new PackageEntry(ecosystem, pkg, ver, filePath, lineNumber, content));
                        }
                    }
                }
                break;

            case "npm":
                var npmMatch = NpmPackageRegex().Match(content);
                if (npmMatch.Success)
                {
                    string pkg = npmMatch.Groups[1].Value;
                    string ver = npmMatch.Groups[2].Value;
                    if (!IgnoredNpmKeys.Contains(pkg))
                    {
                        targetList.Add(new PackageEntry(ecosystem, pkg, ver, filePath, lineNumber, content));
                    }
                }
                break;

            case "Python":
                if (fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    string trimmed = content.Trim();
                    if (!trimmed.StartsWith('#') && !trimmed.StartsWith('-'))
                    {
                        var pyMatch = PythonRequirementsRegex().Match(trimmed);
                        if (pyMatch.Success && !string.IsNullOrEmpty(pyMatch.Groups[1].Value))
                        {
                            string pkg = pyMatch.Groups[1].Value;
                            string? ver = pyMatch.Groups[2].Success && !string.IsNullOrEmpty(pyMatch.Groups[2].Value) ? pyMatch.Groups[2].Value : null;
                            targetList.Add(new PackageEntry(ecosystem, pkg, ver, filePath, lineNumber, content));
                        }
                    }
                }
                else if (fileName.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase))
                {
                    var tblMatch = PyProjectTableDepRegex().Match(content);
                    if (tblMatch.Success)
                    {
                        string pkg = tblMatch.Groups[1].Value;
                        string ver = tblMatch.Groups[2].Success ? tblMatch.Groups[2].Value : tblMatch.Groups[3].Value;
                        if (!IgnoredTomlKeys.Contains(pkg))
                        {
                            targetList.Add(new PackageEntry(ecosystem, pkg, ver, filePath, lineNumber, content));
                        }
                    }
                    else
                    {
                        // Check if this is an array item line (either starts with a quote or contains '[' before the quote)
                        string trimmed = content.Trim();
                        int eqIdx = trimmed.IndexOf('=');
                        int quoteIdx = trimmed.IndexOfAny(['"', '\'']);
                        int bracketIdx = trimmed.IndexOf('[');
                        bool isArrayContext = eqIdx < 0 || (quoteIdx >= 0 && quoteIdx < eqIdx) || (bracketIdx >= 0 && bracketIdx > eqIdx);
                        if (isArrayContext)
                        {
                            foreach (Match arrMatch in PyProjectArrayDepRegex().Matches(content))
                            {
                                string pkg = arrMatch.Groups[1].Value;
                                string? ver = arrMatch.Groups[2].Success && !string.IsNullOrEmpty(arrMatch.Groups[2].Value)
                                    ? arrMatch.Groups[2].Value
                                    : null;
                                if (!IgnoredTomlKeys.Contains(pkg))
                                {
                                    targetList.Add(new PackageEntry(ecosystem, pkg, ver, filePath, lineNumber, content));
                                }
                            }
                        }
                    }
                }
                break;

            case "Cargo":
                var cargoTbl = CargoTableDepRegex().Match(content);
                if (cargoTbl.Success)
                {
                    string pkg = cargoTbl.Groups[1].Value;
                    if (!IgnoredTomlKeys.Contains(pkg))
                    {
                        targetList.Add(new PackageEntry(ecosystem, pkg, cargoTbl.Groups[2].Value, filePath, lineNumber, content));
                    }
                }
                else
                {
                    var cargoSimple = CargoSimpleDepRegex().Match(content);
                    if (cargoSimple.Success)
                    {
                        string pkg = cargoSimple.Groups[1].Value;
                        if (!IgnoredTomlKeys.Contains(pkg))
                        {
                            targetList.Add(new PackageEntry(ecosystem, pkg, cargoSimple.Groups[2].Value, filePath, lineNumber, content));
                        }
                    }
                }
                break;

            case "Go":
                var goMatch = GoModDepRegex().Match(content);
                if (goMatch.Success)
                {
                    string pkg = goMatch.Groups[1].Value;
                    if (!pkg.Equals("go", StringComparison.OrdinalIgnoreCase) &&
                        !pkg.Equals("toolchain", StringComparison.OrdinalIgnoreCase) &&
                        !pkg.Equals("module", StringComparison.OrdinalIgnoreCase))
                    {
                        targetList.Add(new PackageEntry(ecosystem, pkg, goMatch.Groups[2].Value, filePath, lineNumber, content));
                    }
                }
                break;
        }
    }

    private static bool IsSafePackage(string packageId, HashSet<string> safePackages)
    {
        if (safePackages.Contains(packageId))
        {
            return true;
        }

        foreach (var pattern in safePackages)
        {
            if (pattern.EndsWith('*'))
            {
                string prefix = pattern[..^1];
                if (packageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsSafeDomain(string host, HashSet<string> safeDomains)
    {
        if (safeDomains.Contains(host))
        {
            return true;
        }

        foreach (var pattern in safeDomains)
        {
            if (pattern.StartsWith("*.", StringComparison.OrdinalIgnoreCase))
            {
                string suffix = pattern[2..];
                if (host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
