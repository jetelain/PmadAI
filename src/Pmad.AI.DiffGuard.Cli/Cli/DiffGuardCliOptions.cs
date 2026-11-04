using Pmad.AI.DiffGuard.Cli.Formatting;
using Pmad.AI.DiffGuard.Cli.Git;
using Pmad.AI.DiffGuard.Cli.Llm;
using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.DiffGuard.Cli.Cli;

public sealed class DiffGuardCliOptions
{
    // Target
    public GitComparisonTarget Target { get; set; } = new GitComparisonTarget.Staged();
    public string RepoPath { get; set; } = ".";
    public string? CustomConfigPath { get; set; }
    public bool SkipBaseline { get; set; } = false;

    // Output & Reporting
    public OutputFormat PrimaryFormat { get; set; } = OutputFormat.Console;
    public string? OutputPath { get; set; }
    public string? MarkdownPath { get; set; }
    public string? SarifPath { get; set; }
    public DiffRiskSeverity MinimumRiskSeverity { get; set; } = DiffRiskSeverity.Medium;
    public bool Quiet { get; set; } = false;

    // LLM Judge
    public bool EnableLlm { get; set; } = false;
    public LlmProviderKind LlmProvider { get; set; } = LlmProviderKind.None;
    public string? LlmModel { get; set; }
    public string? LlmEndpoint { get; set; }
    public string? LlmApiKey { get; set; }
    public int MaxChunkBytes { get; set; } = 120_000;
    public bool FilterDeletedLines { get; set; } = false;
    public int MaxConsecutiveDeletedLines { get; set; } = 3;
    public bool FailClosedOnError { get; set; } = true;

    // Scanners & Rules
    public bool EnableDeterministic { get; set; } = true;
    public bool EnableNetworkEndpointRules { get; set; } = true;
    public bool EnableBackdoorSignatureRules { get; set; } = true;
    public bool EnableSecretAndEntropyRules { get; set; } = true;
    public bool EnableEncodedPayloadRules { get; set; } = true;
    public bool EnableUnicodeObfuscationRules { get; set; } = true;
    public bool EnableBinaryFileRules { get; set; } = true;
    public bool EnableDependencyRules { get; set; } = true;
    public bool EnableBlastRadiusRules { get; set; } = true;
    public bool EnablePromptInjection { get; set; } = false;
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

    // Allowlists
    public List<string> WellKnownSafeDomains { get; set; } = [.. DiffRiskDefaults.WellKnownSafeDomains];
    public List<string> AllowedIpAddresses { get; set; } = [.. DiffRiskDefaults.AllowedIpAddresses];
    public List<string> WellKnownSafePackages { get; set; } = [.. DiffRiskDefaults.WellKnownSafePackages];
    public List<string> WellKnownSafeTestPackages { get; set; } = [.. DiffRiskDefaults.WellKnownSafeTestPackages];
    public List<string> AllowedImageExtensions { get; set; } = [.. DiffRiskDefaults.AllowedImageExtensions];
    public List<string> MaxLineLengthExcludedExtensions { get; set; } = [.. DiffRiskDefaults.DefaultMaxLineLengthExcludedExtensions];
    public List<string> ExcludedFileGlobs { get; set; } = [.. DiffRiskDefaults.ExcludedFileGlobs];

    // Tracking metadata
    public IReadOnlyList<string> LoadedConfigPaths { get; set; } = [];
}
