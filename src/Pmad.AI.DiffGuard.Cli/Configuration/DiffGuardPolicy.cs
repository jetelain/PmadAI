using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.DiffGuard.Cli.Configuration;

/// <summary>
/// Effective security policy and scanner configuration resolved from the three configuration tiers
/// (System > User > Repository) and used to enforce guardrails against CLI overrides.
/// </summary>
public sealed class DiffGuardPolicy
{
    public bool LockPolicy { get; set; } = false;
    public bool AllowLowerTierAllowlistAdditions { get; set; } = true;
    public DiffRiskSeverity MinimumRiskSeverity { get; set; } = DiffRiskSeverity.Medium;
    public DiffRiskSeverity? SystemMinimumRiskSeverityFloor { get; set; }

    // Scanners
    public bool EnableDeterministic { get; set; } = true;
    public bool ForceDeterministic { get; set; } = false;
    public bool EnableNetworkEndpointRules { get; set; } = true;
    public bool EnableBackdoorSignatureRules { get; set; } = true;
    public bool EnableSecretAndEntropyRules { get; set; } = true;
    public bool EnableEncodedPayloadRules { get; set; } = true;
    public bool EnableUnicodeObfuscationRules { get; set; } = true;
    public bool EnableBinaryFileRules { get; set; } = true;
    public bool EnableDependencyRules { get; set; } = true;
    public bool EnableBlastRadiusRules { get; set; } = true;
    public bool EnablePromptInjectionRules { get; set; } = false;
    public bool ForcePromptInjectionRules { get; set; } = false;
    public bool FlagPrivateNetworkIps { get; set; } = false;
    public bool EnableTestTolerance { get; set; } = true;
    public bool AllowUnverifiedImageExtensionsWhenStreamUnavailable { get; set; } = false;

    // Thresholds
    public int MaxChangedFiles { get; set; } = 50;
    public int MaxChangedLines { get; set; } = 3000;
    public double MaxCodebaseImpactPercent { get; set; } = 10.0;
    public double MaxFileImpactPercent { get; set; } = 15.0;
    public double ShannonEntropyThreshold { get; set; } = 4.95;
    public int EntropyMinStringLength { get; set; } = 20;
    public int MinSuspiciousBase64Length { get; set; } = 64;
    public int MinSuspiciousHexLength { get; set; } = 64;
    public int MaxLineLength { get; set; } = 500;

    // System ceilings (when System config locks policy)
    public int? SystemMaxChangedFilesCeiling { get; set; }
    public int? SystemMaxChangedLinesCeiling { get; set; }
    public double? SystemMaxCodebaseImpactPercentCeiling { get; set; }
    public double? SystemMaxFileImpactPercentCeiling { get; set; }
    public double? SystemShannonEntropyThresholdCeiling { get; set; }
    public int? SystemMaxLineLengthCeiling { get; set; }

    // Allowlists
    public List<string> WellKnownSafeDomains { get; set; } = [.. DiffRiskDefaults.WellKnownSafeDomains];
    public List<string> AllowedIpAddresses { get; set; } = [.. DiffRiskDefaults.AllowedIpAddresses];
    public List<string> WellKnownSafePackages { get; set; } = [.. DiffRiskDefaults.WellKnownSafePackages];
    public List<string> WellKnownSafeTestPackages { get; set; } = [.. DiffRiskDefaults.WellKnownSafeTestPackages];
    public List<string> AllowedImageExtensions { get; set; } = [.. DiffRiskDefaults.AllowedImageExtensions];
    public List<string> MaxLineLengthExcludedExtensions { get; set; } = [.. DiffRiskDefaults.DefaultMaxLineLengthExcludedExtensions];
    public List<string> ExcludedFileGlobs { get; set; } = [.. DiffRiskDefaults.ExcludedFileGlobs];
    public List<string> TestFileGlobs { get; set; } = [.. DiffRiskDefaults.DefaultTestFileGlobs];

    // LLM Judge
    public bool LlmEnabled { get; set; } = false;
    public bool ForceLlmEnabled { get; set; } = false;
    public string? LlmProvider { get; set; }
    public string? LlmModel { get; set; }
    public string? LlmEndpoint { get; set; }
    public string? LlmApiKey { get; set; }
    public int MaxChunkBytes { get; set; } = 120_000;
    public bool FilterDeletedLines { get; set; } = false;
    public int MaxConsecutiveDeletedLines { get; set; } = 3;
    public bool FailClosedOnError { get; set; } = true;
    public bool ForceFailClosedOnError { get; set; } = false;

    // Metadata tracking
    public List<string> LoadedConfigPaths { get; set; } = [];
    public bool SystemConfigLoaded { get; set; } = false;
    public bool UserConfigLoaded { get; set; } = false;
    public bool RepoConfigLoaded { get; set; } = false;
}
