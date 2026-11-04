namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Pre-seeded defaults for allowlisted domains, safe package prefixes, allowed image extensions, and excluded file globs.
/// </summary>
public static class DiffRiskDefaults
{
    /// <summary>
    /// Default set of well-known trusted hostnames and wildcard domain patterns.
    /// </summary>
    public static readonly IReadOnlySet<string> WellKnownSafeDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "localhost",
        "example.com",
        "*.example.com",
        "example.org",
        "*.example.org",
        "example.net",
        "*.example.net",
        "*.local",
        "*.test",
        "*.invalid",
        "pmad.net",
        "*.pmad.net",
        "nuget.org",
        "*.nuget.org",
        "microsoft.com",
        "*.microsoft.com",
        "github.com",
        "*.github.com",
        "githubusercontent.com",
        "*.githubusercontent.com",
        "w3.org",
        "*.w3.org",
        "npmjs.org",
        "*.npmjs.org",
        "npmjs.com",
        "*.npmjs.com",
        "pypi.org",
        "*.pypi.org",
        "python.org",
        "*.python.org",
        "crates.io",
        "*.crates.io",
        "golang.org",
        "*.golang.org",
        "pkg.go.dev",
        "openapis.org",
        "*.openapis.org",
        "json-schema.org",
        "*.json-schema.org"
    };

    /// <summary>
    /// Default set of explicitly allowed IP addresses or CIDR blocks. Default is empty.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedIpAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Default set of well-known trusted package identifiers and prefix patterns.
    /// </summary>
    public static readonly IReadOnlySet<string> WellKnownSafePackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.*",
        "System.*",
        "Pmad.*"
    };

    /// <summary>
    /// Default set of well-known trusted test package identifiers and prefix patterns permitted in test projects.
    /// </summary>
    public static readonly IReadOnlySet<string> WellKnownSafeTestPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.*",
        "System.*",
        "xunit",
        "xunit.*",
        "NUnit",
        "NUnit.*",
        "AwesomeAssertions",
        "AwesomeAssertions.*",
        "FluentAssertions",
        "FluentAssertions.*",
        "Moq",
        "Moq.*",
        "NSubstitute",
        "NSubstitute.*",
        "MSTest.*",
        "coverlet.*",
        "Verify.*",
        "Bogus"
    };

    /// <summary>
    /// Default set of file extensions permitted for image diffs subject to magic-byte verification.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".webp",
        ".ico"
    };

    /// <summary>
    /// Default set of file extensions excluded from line length anomaly checking (PAY-UNI-004).
    /// </summary>
    public static readonly IReadOnlySet<string> DefaultMaxLineLengthExcludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".md",
        ".markdown",
        ".txt",
        ".svg"
    };

    /// <summary>
    /// Default list of file globs excluded from diff security scanning.
    /// </summary>
    public static readonly IReadOnlyList<string> ExcludedFileGlobs = [];

    /// <summary>
    /// Default list of file globs and path patterns identifying test files and test projects.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultTestFileGlobs =
    [
        "test/**",
        "tests/**"
    ];

    /// <summary>
    /// Determines whether a pattern is a valid, non-trivial test file glob or path.
    /// Rejects empty strings and overly broad wildcard patterns (such as <c>*</c>, <c>**</c>, <c>**/*</c>, <c>*.*</c>, or <c>**/*.cs</c>)
    /// that lack a literal directory name or test-identifying filename stem.
    /// </summary>
    public static bool IsValidTestFileGlob(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        string norm = pattern.Trim().Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(norm))
        {
            return false;
        }

        var segments = norm.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        // If any directory segment contains a literal letter or digit (e.g. "test/**", "src/*.Tests/**"), it is valid.
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].Any(char.IsLetterOrDigit))
            {
                return true;
            }
        }

        string lastSeg = segments[^1];
        if (!lastSeg.Contains('*') && !lastSeg.Contains('?'))
        {
            return lastSeg.Any(char.IsLetterOrDigit);
        }

        // When the filename segment contains wildcards and no directory segment had literal characters
        // (e.g. "*.cs", "**/*.cs", "*.*", "**/*Tests.cs", "*.Tests.csproj"), require literal characters
        // in the stem before the final extension.
        int lastDot = lastSeg.LastIndexOf('.');
        string stem = lastDot >= 0 ? lastSeg[..lastDot] : lastSeg;
        return stem.Any(char.IsLetterOrDigit);
    }

    /// <summary>
    /// Determines whether a file path matches any of the specified test file globs or path patterns.
    /// </summary>
    public static bool IsTestFile(string? filePath, IEnumerable<string>? testFileGlobs)
    {
        if (string.IsNullOrEmpty(filePath) || testFileGlobs == null)
        {
            return false;
        }

        string norm = filePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(norm))
        {
            return false;
        }

        foreach (var pattern in testFileGlobs)
        {
            if (!IsValidTestFileGlob(pattern))
            {
                continue;
            }

            string cleanPattern = pattern.Trim().Replace('\\', '/').TrimStart('/');

            // Plain file or directory path without wildcards (e.g. "tests", "custom_tests/", "src/exact-test.cs"):
            // anchor to repository root for consistent matching between "dir", "dir/", and "dir/**".
            if (!cleanPattern.Contains('*') && !cleanPattern.Contains('?'))
            {
                string trimmedDir = cleanPattern.TrimEnd('/');
                if (norm.Equals(trimmedDir, StringComparison.OrdinalIgnoreCase) ||
                    norm.StartsWith(trimmedDir + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                continue;
            }

            if (cleanPattern.EndsWith('/'))
            {
                cleanPattern += "**";
            }

            if (GlobMatcher.IsMatch(norm, cleanPattern))
            {
                return true;
            }
        }

        return false;
    }
}
