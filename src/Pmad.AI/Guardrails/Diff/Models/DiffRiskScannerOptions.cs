namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Configuration options, threshold parameters, and allowlists for git diff security scanning.
/// </summary>
public sealed class DiffRiskScannerOptions
{
    private HashSet<string> _wellKnownSafeDomains = new(DiffRiskDefaults.WellKnownSafeDomains, StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _allowedIpAddresses = new(DiffRiskDefaults.AllowedIpAddresses, StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _wellKnownSafePackages = new(DiffRiskDefaults.WellKnownSafePackages, StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _allowedImageExtensions = new(DiffRiskDefaults.AllowedImageExtensions, StringComparer.OrdinalIgnoreCase);
    private List<string> _excludedFileGlobs = [.. DiffRiskDefaults.ExcludedFileGlobs];
    private List<ISafeBinaryValidator> _safeBinaryValidators;

    /// <summary>
    /// Initializes a new instance of <see cref="DiffRiskScannerOptions"/> with default settings and built-in validators.
    /// </summary>
    public DiffRiskScannerOptions()
    {
        _safeBinaryValidators = [new MagicByteImageValidator(this)];
    }

    /// <summary>
    /// Whether to flag hardcoded public IP addresses and non-allowlisted URLs. Default: true.
    /// </summary>
    public bool EnableNetworkEndpointRules { get; set; } = true;

    /// <summary>
    /// Whether to scan for hardcoded authentication bypasses, dynamic code execution, and MSBuild/CI tampering. Default: true.
    /// </summary>
    public bool EnableBackdoorSignatureRules { get; set; } = true;

    /// <summary>
    /// Whether to scan for API tokens and high Shannon entropy literals. Default: true.
    /// </summary>
    public bool EnableSecretAndEntropyRules { get; set; } = true;

    /// <summary>
    /// Whether to scan for Base64 and Hex decoded payloads. Default: true.
    /// </summary>
    public bool EnableEncodedPayloadRules { get; set; } = true;

    /// <summary>
    /// Whether to scan for Unicode obfuscation (Trojan Source BiDi, zero-width chars, Cyrillic homoglyphs) and excessive line lengths. Default: true.
    /// </summary>
    public bool EnableUnicodeObfuscationRules { get; set; } = true;

    /// <summary>
    /// Whether to scan for binary files and validate image formats. Default: true.
    /// </summary>
    public bool EnableBinaryFileRules { get; set; } = true;

    /// <summary>
    /// Whether to scan for new unverified dependencies, downgrades, and untrusted feeds. Default: true.
    /// </summary>
    public bool EnableDependencyRules { get; set; } = true;

    /// <summary>
    /// Whether to scan for excessive blast radius (total files, changed lines, relative codebase percent). Default: true.
    /// </summary>
    public bool EnableBlastRadiusRules { get; set; } = true;

    /// <summary>
    /// Whether to scan for prompt injection directives. Off by default for model-risk guardrails. Default: false.
    /// </summary>
    public bool EnablePromptInjectionRules { get; set; } = false;

    /// <summary>
    /// Whether private network IP addresses (RFC 1918 / RFC 4193) should be flagged. Default: false.
    /// </summary>
    public bool FlagPrivateNetworkIps { get; set; } = false;

    /// <summary>
    /// When true, allows image files whose extension matches AllowedImageExtensions when their byte stream cannot be opened.
    /// When false (default fail-closed), raises a finding if image magic bytes cannot be verified. Default: false.
    /// </summary>
    public bool AllowUnverifiedImageExtensionsWhenStreamUnavailable { get; set; } = false;

    /// <summary>
    /// Minimum severity required for DiffRiskScanResult.HasRisks to evaluate to true. Default: Medium.
    /// </summary>
    public DiffRiskSeverity MinimumRiskSeverity { get; set; } = DiffRiskSeverity.Medium;

    /// <summary>
    /// Maximum allowable percentage of repository lines changed before triggering SCOPE-PCT-001. Default: 10.0%.
    /// </summary>
    public double MaxCodebaseImpactPercent { get; set; } = 10.0;

    /// <summary>
    /// Maximum allowable percentage of repository files changed before triggering SCOPE-FILEPCT-001. Default: 15.0%.
    /// </summary>
    public double MaxFileImpactPercent { get; set; } = 15.0;

    /// <summary>
    /// Maximum absolute number of changed files permitted before triggering SCOPE-FILES-001. Default: 25.
    /// </summary>
    public int MaxChangedFiles { get; set; } = 25;

    /// <summary>
    /// Maximum absolute number of changed lines permitted before triggering SCOPE-LINES-001. Default: 1500.
    /// </summary>
    public int MaxChangedLines { get; set; } = 1_500;

    /// <summary>
    /// Shannon entropy threshold for flagging suspicious string literals. Default: 4.50.
    /// </summary>
    public double ShannonEntropyThreshold { get; set; } = 4.50;

    /// <summary>
    /// Minimum string length required to evaluate Shannon entropy. Default: 20.
    /// </summary>
    public int EntropyMinStringLength { get; set; } = 20;

    /// <summary>
    /// Minimum length of a Base64 string literal to be flagged as suspicious. Default: 64.
    /// </summary>
    public int MinSuspiciousBase64Length { get; set; } = 64;

    /// <summary>
    /// Minimum length of a Hex string literal to be flagged as suspicious. Default: 64.
    /// </summary>
    public int MinSuspiciousHexLength { get; set; } = 64;

    /// <summary>
    /// Maximum single line character length before triggering PAY-UNI-004. Default: 500.
    /// </summary>
    public int MaxLineLength { get; set; } = 500;

    /// <summary>
    /// Trusted domains allowed in URLs and package feeds. Supports exact hostnames and *.domain.tld wildcards.
    /// Setters coerce comparer to StringComparer.OrdinalIgnoreCase.
    /// </summary>
    public HashSet<string> WellKnownSafeDomains
    {
        get => _wellKnownSafeDomains;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _wellKnownSafeDomains = value.Comparer == StringComparer.OrdinalIgnoreCase
                ? value
                : new HashSet<string>(value, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Explicitly permitted IP addresses or CIDR ranges.
    /// Setters coerce comparer to StringComparer.OrdinalIgnoreCase.
    /// </summary>
    public HashSet<string> AllowedIpAddresses
    {
        get => _allowedIpAddresses;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _allowedIpAddresses = value.Comparer == StringComparer.OrdinalIgnoreCase
                ? value
                : new HashSet<string>(value, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Trusted package names or prefix wildcards (e.g. Microsoft.*, System.*, Pmad.*).
    /// Setters coerce comparer to StringComparer.OrdinalIgnoreCase.
    /// </summary>
    public HashSet<string> WellKnownSafePackages
    {
        get => _wellKnownSafePackages;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _wellKnownSafePackages = value.Comparer == StringComparer.OrdinalIgnoreCase
                ? value
                : new HashSet<string>(value, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Permitted file extensions for image diffs.
    /// Setters coerce comparer to StringComparer.OrdinalIgnoreCase.
    /// </summary>
    public HashSet<string> AllowedImageExtensions
    {
        get => _allowedImageExtensions;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _allowedImageExtensions = value.Comparer == StringComparer.OrdinalIgnoreCase
                ? value
                : new HashSet<string>(value, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Glob patterns for files to exclude from diff parsing and scanning.
    /// </summary>
    public List<string> ExcludedFileGlobs
    {
        get => _excludedFileGlobs;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _excludedFileGlobs = value;
        }
    }

    /// <summary>
    /// Ordered chain of safe binary validators evaluated against added or modified binary files.
    /// </summary>
    public List<ISafeBinaryValidator> SafeBinaryValidators
    {
        get => _safeBinaryValidators;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _safeBinaryValidators = value;
        }
    }

    /// <summary>
    /// Optional delegate for resolving a read-only stream to a target repository file during binary scanning.
    /// </summary>
    public Func<string, Stream?>? FileStreamProvider { get; set; }

    /// <summary>
    /// Root path of the repository on disk used for resolving file streams when FileStreamProvider is not set.
    /// </summary>
    public string? RepositoryRootPath { get; set; }
}
