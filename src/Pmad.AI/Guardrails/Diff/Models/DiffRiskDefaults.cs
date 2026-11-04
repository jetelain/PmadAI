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
    /// Default list of file globs excluded from diff security scanning.
    /// </summary>
    public static readonly IReadOnlyList<string> ExcludedFileGlobs = [];
}

