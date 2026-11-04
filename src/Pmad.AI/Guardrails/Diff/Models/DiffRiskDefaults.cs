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
    /// Determines whether a file path belongs to a test project, test directory, or test source file.
    /// </summary>
    public static bool IsTestFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return false;
        }

        string norm = filePath.Replace('\\', '/');
        var segments = norm.Split('/');
        for (int i = 0; i < segments.Length - 1; i++)
        {
            string seg = segments[i];
            if (seg.Equals("test", StringComparison.OrdinalIgnoreCase) ||
                seg.Equals("tests", StringComparison.OrdinalIgnoreCase) ||
                seg.EndsWith(".test", StringComparison.OrdinalIgnoreCase) ||
                seg.EndsWith(".tests", StringComparison.OrdinalIgnoreCase) ||
                seg.EndsWith("test", StringComparison.OrdinalIgnoreCase) ||
                seg.EndsWith("tests", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        string fileName = segments[^1];
        string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        return nameWithoutExt.EndsWith("tests", StringComparison.OrdinalIgnoreCase) ||
               nameWithoutExt.EndsWith("test", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".tests.csproj", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".test.csproj", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".tests.props", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".test.props", StringComparison.OrdinalIgnoreCase);
    }
}
