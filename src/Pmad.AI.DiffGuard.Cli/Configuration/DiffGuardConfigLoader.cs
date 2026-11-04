using System.Text.Json;
using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.DiffGuard.Cli.Configuration;

public sealed class DiffGuardConfigLoader : IDiffGuardConfigLoader
{
    public static readonly DiffGuardConfigLoader Instance = new();

    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, string> _readFile;
    private readonly Func<string, string?> _getEnv;
    private readonly IEnumerable<string>? _customSystemPaths;
    private readonly IEnumerable<string>? _customUserPaths;

    public DiffGuardConfigLoader(
        Func<string, bool>? fileExists = null,
        Func<string, string>? readFile = null,
        Func<string, string?>? getEnv = null,
        IEnumerable<string>? customSystemPaths = null,
        IEnumerable<string>? customUserPaths = null)
    {
        _fileExists = fileExists ?? File.Exists;
        _readFile = readFile ?? File.ReadAllText;
        _getEnv = getEnv ?? Environment.GetEnvironmentVariable;
        _customSystemPaths = customSystemPaths;
        _customUserPaths = customUserPaths;
    }

    public DiffGuardPolicy LoadPolicy(string? repoRoot = null, string? customRepoConfigPath = null)
    {
        var policy = new DiffGuardPolicy();

        string? repoConfigPath = FindRepoConfigPath(repoRoot, customRepoConfigPath);
        string? userConfigPath = FindUserConfigPath();
        string? systemConfigPath = FindSystemConfigPath();

        DiffGuardConfigDto? repoDto = null;
        if (repoConfigPath != null)
        {
            repoDto = ParseConfigFile(repoConfigPath);
            ApplyConfig(policy, repoDto, isSystemTier: false);
            policy.LoadedConfigPaths.Add(repoConfigPath);
            policy.RepoConfigLoaded = true;
        }

        DiffGuardConfigDto? userDto = null;
        if (userConfigPath != null)
        {
            userDto = ParseConfigFile(userConfigPath);
            ApplyConfig(policy, userDto, isSystemTier: false);
            policy.LoadedConfigPaths.Add(userConfigPath);
            policy.UserConfigLoaded = true;
        }

        if (systemConfigPath != null)
        {
            var systemDto = ParseConfigFile(systemConfigPath);
            ApplySystemConfig(policy, systemDto);
            policy.LoadedConfigPaths.Add(systemConfigPath);
            policy.SystemConfigLoaded = true;
        }

        return policy;
    }

    private string? FindSystemConfigPath()
    {
        if (_customSystemPaths != null)
        {
            foreach (var path in _customSystemPaths)
            {
                if (!string.IsNullOrWhiteSpace(path) && _fileExists(path))
                {
                    return path;
                }
            }
            return null;
        }

        string? CheckWindowsProgramData()
        {
            try
            {
                var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                if (!string.IsNullOrWhiteSpace(programData))
                {
                    var p1 = Path.Combine(programData, "DiffGuard", "config.json");
                    if (_fileExists(p1)) return p1;

                    var p2 = Path.Combine(programData, "diffguard", "config.json");
                    if (_fileExists(p2)) return p2;
                }
            }
            catch
            {
            }
            return null;
        }

        string? CheckLinuxPaths()
        {
            var linuxPaths = new[]
            {
                "/etc/diffguard/config.json",
                "/etc/pmad-diffguard/config.json",
                "/etc/diffguard.json"
            };

            foreach (var path in linuxPaths)
            {
                if (_fileExists(path))
                {
                    return path;
                }
            }
            return null;
        }

        // 1. Physical system paths: prioritize native OS path
        var physicalPath = OperatingSystem.IsWindows()
            ? (CheckWindowsProgramData() ?? CheckLinuxPaths())
            : (CheckLinuxPaths() ?? CheckWindowsProgramData());

        if (physicalPath != null)
        {
            return physicalPath;
        }

        // 2. Environment variable fallback ONLY when no physical system file exists on disk
        var envVar = _getEnv("DIFFGUARD_SYSTEM_CONFIG");
        if (!string.IsNullOrWhiteSpace(envVar) && _fileExists(envVar))
        {
            return envVar;
        }

        return null;
    }

    private string? FindUserConfigPath()
    {
        if (_customUserPaths != null)
        {
            foreach (var path in _customUserPaths)
            {
                if (!string.IsNullOrWhiteSpace(path) && _fileExists(path))
                {
                    return path;
                }
            }
            return null;
        }

        string? CheckWindowsUserData()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrWhiteSpace(appData))
                {
                    var p1 = Path.Combine(appData, "DiffGuard", "config.json");
                    if (_fileExists(p1)) return p1;
                }

                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrWhiteSpace(userProfile))
                {
                    var p2 = Path.Combine(userProfile, ".config", "diffguard", "config.json");
                    if (_fileExists(p2)) return p2;

                    var p3 = Path.Combine(userProfile, ".diffguard.json");
                    if (_fileExists(p3)) return p3;
                }
            }
            catch
            {
            }
            return null;
        }

        string? CheckLinuxUserPaths()
        {
            try
            {
                var xdgConfig = _getEnv("XDG_CONFIG_HOME");
                if (!string.IsNullOrWhiteSpace(xdgConfig))
                {
                    var p1 = Path.Combine(xdgConfig, "diffguard", "config.json");
                    if (_fileExists(p1)) return p1;
                }

                var home = _getEnv("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrWhiteSpace(home))
                {
                    var p2 = Path.Combine(home, ".config", "diffguard", "config.json");
                    if (_fileExists(p2)) return p2;

                    var p3 = Path.Combine(home, ".diffguard.json");
                    if (_fileExists(p3)) return p3;
                }
            }
            catch
            {
            }
            return null;
        }

        var userPath = OperatingSystem.IsWindows()
            ? (CheckWindowsUserData() ?? CheckLinuxUserPaths())
            : (CheckLinuxUserPaths() ?? CheckWindowsUserData());

        if (userPath != null)
        {
            return userPath;
        }

        var envVar = _getEnv("DIFFGUARD_USER_CONFIG");
        if (!string.IsNullOrWhiteSpace(envVar) && _fileExists(envVar))
        {
            return envVar;
        }

        return null;
    }

    private string? FindRepoConfigPath(string? repoRoot, string? customRepoConfigPath)
    {
        if (!string.IsNullOrWhiteSpace(customRepoConfigPath))
        {
            if (_fileExists(customRepoConfigPath))
            {
                return customRepoConfigPath;
            }
            throw new FileNotFoundException($"Configuration file not found: '{customRepoConfigPath}'", customRepoConfigPath);
        }

        var root = string.IsNullOrWhiteSpace(repoRoot) ? Directory.GetCurrentDirectory() : repoRoot;

        string? CheckDirectoryCandidates(string dir)
        {
            string[] candidates =
            [
                Path.Combine(dir, ".diffguard.json"),
                Path.Combine(dir, "diffguard.json"),
                Path.Combine(dir, ".config", "diffguard.json"),
                Path.Combine(dir, ".github", "diffguard.json")
            ];

            foreach (var candidate in candidates)
            {
                if (_fileExists(candidate))
                {
                    return candidate;
                }
                string forward = candidate.Replace('\\', '/');
                if (_fileExists(forward))
                {
                    return forward;
                }
            }

            return null;
        }

        var directMatch = CheckDirectoryCandidates(root);
        if (directMatch != null)
        {
            return directMatch;
        }

        // If repoRoot is a subdirectory of a Git repository, also check parent directories up to the .git root
        try
        {
            var current = new DirectoryInfo(Path.GetFullPath(root));
            while (current?.Parent != null)
            {
                current = current.Parent;
                var parentMatch = CheckDirectoryCandidates(current.FullName);
                if (parentMatch != null)
                {
                    return parentMatch;
                }

                if (_fileExists(Path.Combine(current.FullName, ".git")) ||
                    Directory.Exists(Path.Combine(current.FullName, ".git")))
                {
                    break;
                }
            }
        }
        catch
        {
        }

        var envVar = _getEnv("DIFFGUARD_CONFIG");
        if (!string.IsNullOrWhiteSpace(envVar) && _fileExists(envVar))
        {
            return envVar;
        }

        return null;
    }

    private DiffGuardConfigDto ParseConfigFile(string path)
    {
        try
        {
            string json = _readFile(path);
            var dto = JsonSerializer.Deserialize(json, DiffGuardConfigJsonContext.Default.DiffGuardConfigDto);
            return dto ?? throw new InvalidOperationException($"Configuration file at '{path}' is empty or invalid.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON in configuration file '{path}': {ex.Message}", ex);
        }
    }

    private static DiffRiskSeverity ParseRiskSeverity(string raw)
    {
        if (!int.TryParse(raw.Trim(), out _) &&
            Enum.TryParse<DiffRiskSeverity>(raw.Trim(), ignoreCase: true, out var sev) &&
            Enum.IsDefined(sev))
        {
            return sev;
        }

        throw new InvalidOperationException($"Invalid minimumRiskSeverity '{raw}'. Valid values are: Low, Medium, High, Critical.");
    }

    private static void ApplyConfig(DiffGuardPolicy policy, DiffGuardConfigDto dto, bool isSystemTier)
    {
        if (isSystemTier)
        {
            if (dto.LockPolicy.HasValue) policy.LockPolicy = dto.LockPolicy.Value;
            if (dto.AllowLowerTierAllowlistAdditions.HasValue) policy.AllowLowerTierAllowlistAdditions = dto.AllowLowerTierAllowlistAdditions.Value;
        }

        if (!string.IsNullOrWhiteSpace(dto.MinimumRiskSeverity))
        {
            policy.MinimumRiskSeverity = ParseRiskSeverity(dto.MinimumRiskSeverity);
        }

        if (dto.Scanners != null)
        {
            var s = dto.Scanners;
            if (s.EnableDeterministic.HasValue) policy.EnableDeterministic = s.EnableDeterministic.Value;
            if (isSystemTier && s.ForceDeterministic.HasValue) policy.ForceDeterministic = s.ForceDeterministic.Value;
            if (s.EnableNetworkEndpointRules.HasValue) policy.EnableNetworkEndpointRules = s.EnableNetworkEndpointRules.Value;
            if (s.EnableBackdoorSignatureRules.HasValue) policy.EnableBackdoorSignatureRules = s.EnableBackdoorSignatureRules.Value;
            if (s.EnableSecretAndEntropyRules.HasValue) policy.EnableSecretAndEntropyRules = s.EnableSecretAndEntropyRules.Value;
            if (s.EnableEncodedPayloadRules.HasValue) policy.EnableEncodedPayloadRules = s.EnableEncodedPayloadRules.Value;
            if (s.EnableUnicodeObfuscationRules.HasValue) policy.EnableUnicodeObfuscationRules = s.EnableUnicodeObfuscationRules.Value;
            if (s.EnableBinaryFileRules.HasValue) policy.EnableBinaryFileRules = s.EnableBinaryFileRules.Value;
            if (s.EnableDependencyRules.HasValue) policy.EnableDependencyRules = s.EnableDependencyRules.Value;
            if (s.EnableBlastRadiusRules.HasValue) policy.EnableBlastRadiusRules = s.EnableBlastRadiusRules.Value;
            if (s.EnablePromptInjectionRules.HasValue) policy.EnablePromptInjectionRules = s.EnablePromptInjectionRules.Value;
            if (isSystemTier && s.ForcePromptInjectionRules.HasValue) policy.ForcePromptInjectionRules = s.ForcePromptInjectionRules.Value;
            if (s.FlagPrivateNetworkIps.HasValue) policy.FlagPrivateNetworkIps = s.FlagPrivateNetworkIps.Value;
            if (s.EnableTestTolerance.HasValue) policy.EnableTestTolerance = s.EnableTestTolerance.Value;
            if (s.AllowUnverifiedImageExtensionsWhenStreamUnavailable.HasValue) policy.AllowUnverifiedImageExtensionsWhenStreamUnavailable = s.AllowUnverifiedImageExtensionsWhenStreamUnavailable.Value;
        }

        if (dto.Thresholds != null)
        {
            var t = dto.Thresholds;
            if (t.MaxChangedFiles.HasValue) policy.MaxChangedFiles = t.MaxChangedFiles.Value;
            if (t.MaxChangedLines.HasValue) policy.MaxChangedLines = t.MaxChangedLines.Value;
            if (t.MaxCodebaseImpactPercent.HasValue) policy.MaxCodebaseImpactPercent = t.MaxCodebaseImpactPercent.Value;
            if (t.MaxFileImpactPercent.HasValue) policy.MaxFileImpactPercent = t.MaxFileImpactPercent.Value;
            if (t.ShannonEntropyThreshold.HasValue) policy.ShannonEntropyThreshold = t.ShannonEntropyThreshold.Value;
            if (t.EntropyMinStringLength.HasValue) policy.EntropyMinStringLength = t.EntropyMinStringLength.Value;
            if (t.MinSuspiciousBase64Length.HasValue) policy.MinSuspiciousBase64Length = t.MinSuspiciousBase64Length.Value;
            if (t.MinSuspiciousHexLength.HasValue) policy.MinSuspiciousHexLength = t.MinSuspiciousHexLength.Value;
            if (t.MaxLineLength.HasValue) policy.MaxLineLength = t.MaxLineLength.Value;
        }

        if (dto.Allowlists != null)
        {
            var a = dto.Allowlists;
            bool replace = a.ReplaceDefaults ?? false;

            if (a.WellKnownSafeDomains != null)
            {
                if (replace) policy.WellKnownSafeDomains.Clear();
                foreach (var item in a.WellKnownSafeDomains)
                {
                    if (!string.IsNullOrWhiteSpace(item) && !policy.WellKnownSafeDomains.Contains(item, StringComparer.OrdinalIgnoreCase))
                    {
                        policy.WellKnownSafeDomains.Add(item);
                    }
                }
            }

            if (a.AllowedIpAddresses != null)
            {
                if (replace) policy.AllowedIpAddresses.Clear();
                foreach (var item in a.AllowedIpAddresses)
                {
                    if (!string.IsNullOrWhiteSpace(item) && !policy.AllowedIpAddresses.Contains(item, StringComparer.OrdinalIgnoreCase))
                    {
                        policy.AllowedIpAddresses.Add(item);
                    }
                }
            }

            if (a.WellKnownSafePackages != null)
            {
                if (replace) policy.WellKnownSafePackages.Clear();
                foreach (var item in a.WellKnownSafePackages)
                {
                    if (!string.IsNullOrWhiteSpace(item) && !policy.WellKnownSafePackages.Contains(item, StringComparer.OrdinalIgnoreCase))
                    {
                        policy.WellKnownSafePackages.Add(item);
                    }
                }
            }

            if (a.WellKnownSafeTestPackages != null)
            {
                if (replace) policy.WellKnownSafeTestPackages.Clear();
                foreach (var item in a.WellKnownSafeTestPackages)
                {
                    if (!string.IsNullOrWhiteSpace(item) && !policy.WellKnownSafeTestPackages.Contains(item, StringComparer.OrdinalIgnoreCase))
                    {
                        policy.WellKnownSafeTestPackages.Add(item);
                    }
                }
            }

            if (a.MaxLineLengthExcludedExtensions != null)
            {
                if (replace) policy.MaxLineLengthExcludedExtensions.Clear();
                foreach (var item in a.MaxLineLengthExcludedExtensions)
                {
                    if (!string.IsNullOrWhiteSpace(item) && !policy.MaxLineLengthExcludedExtensions.Contains(item, StringComparer.OrdinalIgnoreCase))
                    {
                        policy.MaxLineLengthExcludedExtensions.Add(item);
                    }
                }
            }

            if (a.AllowedImageExtensions != null)
            {
                if (replace) policy.AllowedImageExtensions.Clear();
                foreach (var item in a.AllowedImageExtensions)
                {
                    if (!string.IsNullOrWhiteSpace(item) && !policy.AllowedImageExtensions.Contains(item, StringComparer.OrdinalIgnoreCase))
                    {
                        policy.AllowedImageExtensions.Add(item);
                    }
                }
            }

            if (a.ExcludedFileGlobs != null)
            {
                if (replace) policy.ExcludedFileGlobs.Clear();
                foreach (var item in a.ExcludedFileGlobs)
                {
                    if (!string.IsNullOrWhiteSpace(item) && !policy.ExcludedFileGlobs.Contains(item, StringComparer.OrdinalIgnoreCase))
                    {
                        policy.ExcludedFileGlobs.Add(item);
                    }
                }
            }
        }

        if (dto.LlmJudge != null)
        {
            var l = dto.LlmJudge;
            if (l.Enabled.HasValue) policy.LlmEnabled = l.Enabled.Value;
            if (isSystemTier && l.ForceEnabled.HasValue) policy.ForceLlmEnabled = l.ForceEnabled.Value;
            if (!string.IsNullOrWhiteSpace(l.Provider)) policy.LlmProvider = l.Provider;
            if (!string.IsNullOrWhiteSpace(l.Model)) policy.LlmModel = l.Model;
            if (!string.IsNullOrWhiteSpace(l.Endpoint)) policy.LlmEndpoint = l.Endpoint;
            if (!string.IsNullOrWhiteSpace(l.ApiKey)) policy.LlmApiKey = l.ApiKey;
            if (l.MaxChunkBytes.HasValue) policy.MaxChunkBytes = l.MaxChunkBytes.Value;
            if (l.FilterDeletedLines.HasValue) policy.FilterDeletedLines = l.FilterDeletedLines.Value;
            if (l.MaxConsecutiveDeletedLines.HasValue) policy.MaxConsecutiveDeletedLines = l.MaxConsecutiveDeletedLines.Value;
            if (l.FailClosedOnError.HasValue) policy.FailClosedOnError = l.FailClosedOnError.Value;
        }
    }

    private static void ApplySystemConfig(DiffGuardPolicy policy, DiffGuardConfigDto dto)
    {
        // System tier defaults lockPolicy to true and allowLowerTierAllowlistAdditions to false
        bool lockPolicy = dto.LockPolicy ?? true;
        policy.LockPolicy = lockPolicy;

        bool allowLowerTierAdditions = dto.AllowLowerTierAllowlistAdditions ?? false;
        policy.AllowLowerTierAllowlistAdditions = allowLowerTierAdditions;

        // If allowLowerTierAdditions is false, purge any additions previously made by lower tiers (reset to defaults or system allowlist)
        if (!allowLowerTierAdditions)
        {
            policy.WellKnownSafeDomains = [.. DiffRiskDefaults.WellKnownSafeDomains];
            policy.AllowedIpAddresses = [.. DiffRiskDefaults.AllowedIpAddresses];
            policy.WellKnownSafePackages = [.. DiffRiskDefaults.WellKnownSafePackages];
            policy.WellKnownSafeTestPackages = [.. DiffRiskDefaults.WellKnownSafeTestPackages];
            policy.AllowedImageExtensions = [.. DiffRiskDefaults.AllowedImageExtensions];
            policy.MaxLineLengthExcludedExtensions = [.. DiffRiskDefaults.DefaultMaxLineLengthExcludedExtensions];
            policy.ExcludedFileGlobs = [.. DiffRiskDefaults.ExcludedFileGlobs];
        }

        // If lockPolicy is true, purge lower-tier LLM endpoint/apiKey/provider/model so an untrusted .diffguard.json cannot redirect LLM calls
        if (lockPolicy)
        {
            policy.LlmEndpoint = null;
            policy.LlmApiKey = null;
            policy.LlmProvider = null;
            policy.LlmModel = null;
        }

        // Apply System config values
        ApplyConfig(policy, dto, isSystemTier: true);

        // Explicit force* flags in System config always apply even when lockPolicy is false
        if (dto.Scanners?.ForceDeterministic == true)
        {
            policy.EnableDeterministic = true;
            policy.ForceDeterministic = true;
        }
        if (dto.Scanners?.ForcePromptInjectionRules == true)
        {
            policy.EnablePromptInjectionRules = true;
            policy.ForcePromptInjectionRules = true;
        }
        if (dto.LlmJudge?.ForceEnabled == true)
        {
            policy.LlmEnabled = true;
            policy.ForceLlmEnabled = true;
        }

        // When locked, establish floors, ceilings and locks
        if (lockPolicy)
        {
            DiffRiskSeverity sysSevFloor = !string.IsNullOrWhiteSpace(dto.MinimumRiskSeverity)
                ? ParseRiskSeverity(dto.MinimumRiskSeverity)
                : DiffRiskSeverity.Medium;

            policy.SystemMinimumRiskSeverityFloor = sysSevFloor;
            // Minimum severity floor: cannot be set higher (more permissive) than System floor
            if (policy.MinimumRiskSeverity > sysSevFloor)
            {
                policy.MinimumRiskSeverity = sysSevFloor;
            }

            var s = dto.Scanners;
            if (s?.EnableDeterministic != false)
            {
                policy.EnableDeterministic = true;
                policy.ForceDeterministic = true;
            }

            policy.EnableNetworkEndpointRules = s?.EnableNetworkEndpointRules ?? true;
            policy.EnableBackdoorSignatureRules = s?.EnableBackdoorSignatureRules ?? true;
            policy.EnableSecretAndEntropyRules = s?.EnableSecretAndEntropyRules ?? true;
            policy.EnableEncodedPayloadRules = s?.EnableEncodedPayloadRules ?? true;
            policy.EnableUnicodeObfuscationRules = s?.EnableUnicodeObfuscationRules ?? true;
            policy.EnableBinaryFileRules = s?.EnableBinaryFileRules ?? true;
            policy.EnableDependencyRules = s?.EnableDependencyRules ?? true;
            policy.EnableBlastRadiusRules = s?.EnableBlastRadiusRules ?? true;
            policy.EnableTestTolerance = s?.EnableTestTolerance ?? true;
            policy.AllowUnverifiedImageExtensionsWhenStreamUnavailable = s?.AllowUnverifiedImageExtensionsWhenStreamUnavailable ?? false;

            if (s?.ForcePromptInjectionRules == true || s?.EnablePromptInjectionRules == true)
            {
                policy.EnablePromptInjectionRules = true;
                policy.ForcePromptInjectionRules = true;
            }

            var l = dto.LlmJudge;
            if (l?.ForceEnabled == true || l?.Enabled == true)
            {
                policy.LlmEnabled = true;
                policy.ForceLlmEnabled = true;
            }

            if (l?.FailClosedOnError != false)
            {
                policy.FailClosedOnError = true;
                policy.ForceFailClosedOnError = true;
            }

            var t = dto.Thresholds;
            policy.SystemMaxChangedFilesCeiling = t?.MaxChangedFiles ?? 50;
            policy.SystemMaxChangedLinesCeiling = t?.MaxChangedLines ?? 3000;
            policy.SystemMaxCodebaseImpactPercentCeiling = t?.MaxCodebaseImpactPercent ?? 10.0;
            policy.SystemMaxFileImpactPercentCeiling = t?.MaxFileImpactPercent ?? 15.0;
            policy.SystemShannonEntropyThresholdCeiling = t?.ShannonEntropyThreshold ?? 4.95;
            policy.SystemMaxLineLengthCeiling = t?.MaxLineLength ?? 500;

            int maxEntropyMinLen = t?.EntropyMinStringLength ?? 20;
            int maxBase64MinLen = t?.MinSuspiciousBase64Length ?? 64;
            int maxHexMinLen = t?.MinSuspiciousHexLength ?? 64;

            // Clamp policy thresholds to system ceilings
            policy.MaxChangedFiles = Math.Min(policy.MaxChangedFiles, policy.SystemMaxChangedFilesCeiling.Value);
            policy.MaxChangedLines = Math.Min(policy.MaxChangedLines, policy.SystemMaxChangedLinesCeiling.Value);
            policy.MaxCodebaseImpactPercent = Math.Min(policy.MaxCodebaseImpactPercent, policy.SystemMaxCodebaseImpactPercentCeiling.Value);
            policy.MaxFileImpactPercent = Math.Min(policy.MaxFileImpactPercent, policy.SystemMaxFileImpactPercentCeiling.Value);
            policy.ShannonEntropyThreshold = Math.Min(policy.ShannonEntropyThreshold, policy.SystemShannonEntropyThresholdCeiling.Value);
            policy.MaxLineLength = Math.Min(policy.MaxLineLength, policy.SystemMaxLineLengthCeiling.Value);
            policy.EntropyMinStringLength = Math.Min(policy.EntropyMinStringLength, maxEntropyMinLen);
            policy.MinSuspiciousBase64Length = Math.Min(policy.MinSuspiciousBase64Length, maxBase64MinLen);
            policy.MinSuspiciousHexLength = Math.Min(policy.MinSuspiciousHexLength, maxHexMinLen);
        }
    }
}
