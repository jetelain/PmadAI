using System.Text.Json.Serialization;

namespace Pmad.AI.DiffGuard.Cli.Configuration;

public sealed class DiffGuardConfigDto
{
    public bool? LockPolicy { get; set; }
    public bool? AllowLowerTierAllowlistAdditions { get; set; }
    public string? MinimumRiskSeverity { get; set; }
    public DiffGuardScannersConfigDto? Scanners { get; set; }
    public DiffGuardThresholdsConfigDto? Thresholds { get; set; }
    public DiffGuardAllowlistsConfigDto? Allowlists { get; set; }
    public DiffGuardLlmJudgeConfigDto? LlmJudge { get; set; }
}

public sealed class DiffGuardScannersConfigDto
{
    public bool? EnableDeterministic { get; set; }
    public bool? ForceDeterministic { get; set; }
    public bool? EnableNetworkEndpointRules { get; set; }
    public bool? EnableBackdoorSignatureRules { get; set; }
    public bool? EnableSecretAndEntropyRules { get; set; }
    public bool? EnableEncodedPayloadRules { get; set; }
    public bool? EnableUnicodeObfuscationRules { get; set; }
    public bool? EnableBinaryFileRules { get; set; }
    public bool? EnableDependencyRules { get; set; }
    public bool? EnableBlastRadiusRules { get; set; }
    public bool? EnablePromptInjectionRules { get; set; }
    public bool? ForcePromptInjectionRules { get; set; }
    public bool? FlagPrivateNetworkIps { get; set; }
    public bool? EnableTestTolerance { get; set; }
    public List<string>? TestFileGlobs { get; set; }
    public bool? AllowUnverifiedImageExtensionsWhenStreamUnavailable { get; set; }
}

public sealed class DiffGuardThresholdsConfigDto
{
    public int? MaxChangedFiles { get; set; }
    public int? MaxChangedLines { get; set; }
    public double? MaxCodebaseImpactPercent { get; set; }
    public double? MaxFileImpactPercent { get; set; }
    public double? ShannonEntropyThreshold { get; set; }
    public int? EntropyMinStringLength { get; set; }
    public int? MinSuspiciousBase64Length { get; set; }
    public int? MinSuspiciousHexLength { get; set; }
    public int? MaxLineLength { get; set; }
}

public sealed class DiffGuardAllowlistsConfigDto
{
    public bool? ReplaceDefaults { get; set; }
    public List<string>? WellKnownSafeDomains { get; set; }
    public List<string>? AllowedIpAddresses { get; set; }
    public List<string>? WellKnownSafePackages { get; set; }
    public List<string>? WellKnownSafeTestPackages { get; set; }
    public List<string>? AllowedImageExtensions { get; set; }
    public List<string>? MaxLineLengthExcludedExtensions { get; set; }
    public List<string>? ExcludedFileGlobs { get; set; }
    public List<string>? TestFileGlobs { get; set; }
}

public sealed class DiffGuardLlmJudgeConfigDto
{
    public bool? Enabled { get; set; }
    public bool? ForceEnabled { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public int? MaxChunkBytes { get; set; }
    public bool? FilterDeletedLines { get; set; }
    public int? MaxConsecutiveDeletedLines { get; set; }
    public bool? FailClosedOnError { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(DiffGuardConfigDto))]
public sealed partial class DiffGuardConfigJsonContext : JsonSerializerContext
{
}
