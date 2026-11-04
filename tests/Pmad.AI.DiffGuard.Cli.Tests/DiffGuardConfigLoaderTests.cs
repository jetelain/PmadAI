using Pmad.AI.DiffGuard.Cli.Configuration;
using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.DiffGuard.Cli.Tests;

public class DiffGuardConfigLoaderTests
{
    [Fact]
    public void LoadPolicy_SystemWinsOverUserAndRepo()
    {
        var virtualFs = new Dictionary<string, string>
        {
            ["/repo/.diffguard.json"] = """
                {
                    "minimumRiskSeverity": "Low",
                    "thresholds": { "maxChangedFiles": 50 }
                }
                """,
            ["/user/config.json"] = """
                {
                    "minimumRiskSeverity": "High",
                    "thresholds": { "maxChangedFiles": 40 }
                }
                """,
            ["/system/config.json"] = """
                {
                    "lockPolicy": true,
                    "minimumRiskSeverity": "Medium",
                    "thresholds": { "maxChangedFiles": 15 }
                }
                """
        };

        var loader = new DiffGuardConfigLoader(
            fileExists: virtualFs.ContainsKey,
            readFile: p => virtualFs[p],
            getEnv: _ => null,
            customSystemPaths: ["/system/config.json"],
            customUserPaths: ["/user/config.json"]);

        var policy = loader.LoadPolicy(repoRoot: "/repo");

        Assert.True(policy.SystemConfigLoaded);
        Assert.True(policy.UserConfigLoaded);
        Assert.True(policy.RepoConfigLoaded);
        Assert.True(policy.LockPolicy);
        Assert.Equal(DiffRiskSeverity.Medium, policy.MinimumRiskSeverity);
        Assert.Equal(15, policy.MaxChangedFiles);
    }

    [Fact]
    public void LoadPolicy_DiskSystemConfigBeatsEnvironmentVariable()
    {
        var virtualFs = new Dictionary<string, string>
        {
            ["/etc/diffguard/config.json"] = """
                {
                    "lockPolicy": true,
                    "minimumRiskSeverity": "Medium"
                }
                """,
            ["/tmp/attacker-config.json"] = """
                {
                    "lockPolicy": false,
                    "minimumRiskSeverity": "Critical"
                }
                """
        };

        var loader = new DiffGuardConfigLoader(
            fileExists: virtualFs.ContainsKey,
            readFile: p => virtualFs[p],
            getEnv: k => k == "DIFFGUARD_SYSTEM_CONFIG" ? "/tmp/attacker-config.json" : null,
            customSystemPaths: ["/etc/diffguard/config.json"]);

        var policy = loader.LoadPolicy();

        Assert.Equal(DiffRiskSeverity.Medium, policy.MinimumRiskSeverity);
        Assert.Contains("/etc/diffguard/config.json", policy.LoadedConfigPaths);
        Assert.DoesNotContain("/tmp/attacker-config.json", policy.LoadedConfigPaths);
    }

    [Fact]
    public void LoadPolicy_SystemLockPolicy_ClampsPermissiveLowerTierSeverities()
    {
        var virtualFs = new Dictionary<string, string>
        {
            ["/repo/.diffguard.json"] = """
                {
                    "minimumRiskSeverity": "Critical"
                }
                """,
            ["/system/config.json"] = """
                {
                    "lockPolicy": true,
                    "minimumRiskSeverity": "Medium"
                }
                """
        };

        var loader = new DiffGuardConfigLoader(
            fileExists: virtualFs.ContainsKey,
            readFile: p => virtualFs[p],
            getEnv: _ => null,
            customSystemPaths: ["/system/config.json"]);

        var policy = loader.LoadPolicy(repoRoot: "/repo");

        // Medium is stricter than Critical; System wins and clamps it
        Assert.Equal(DiffRiskSeverity.Medium, policy.MinimumRiskSeverity);
        Assert.Equal(DiffRiskSeverity.Medium, policy.SystemMinimumRiskSeverityFloor);
    }

    [Fact]
    public void LoadPolicy_SystemLockPolicy_EnforcesCeilingsOnThresholds()
    {
        var virtualFs = new Dictionary<string, string>
        {
            ["/repo/.diffguard.json"] = """
                {
                    "thresholds": {
                        "maxChangedFiles": 100,
                        "maxChangedLines": 5000
                    }
                }
                """,
            ["/system/config.json"] = """
                {
                    "lockPolicy": true,
                    "thresholds": {
                        "maxChangedFiles": 10,
                        "maxChangedLines": 500
                    }
                }
                """
        };

        var loader = new DiffGuardConfigLoader(
            fileExists: virtualFs.ContainsKey,
            readFile: p => virtualFs[p],
            getEnv: _ => null,
            customSystemPaths: ["/system/config.json"]);

        var policy = loader.LoadPolicy(repoRoot: "/repo");

        Assert.Equal(10, policy.MaxChangedFiles);
        Assert.Equal(500, policy.MaxChangedLines);
    }

    [Fact]
    public void LoadPolicy_SystemDisallowsLowerTierAllowlistAdditions_RejectsRepoAllowlist()
    {
        var virtualFs = new Dictionary<string, string>
        {
            ["/repo/.diffguard.json"] = """
                {
                    "allowlists": {
                        "wellKnownSafeDomains": ["malicious-domain.com"],
                        "allowedIpAddresses": ["198.51.100.1"]
                    }
                }
                """,
            ["/system/config.json"] = """
                {
                    "lockPolicy": true,
                    "allowLowerTierAllowlistAdditions": false
                }
                """
        };

        var loader = new DiffGuardConfigLoader(
            fileExists: virtualFs.ContainsKey,
            readFile: p => virtualFs[p],
            getEnv: _ => null,
            customSystemPaths: ["/system/config.json"]);

        var policy = loader.LoadPolicy(repoRoot: "/repo");

        Assert.False(policy.AllowLowerTierAllowlistAdditions);
        Assert.DoesNotContain("malicious-domain.com", policy.WellKnownSafeDomains);
        Assert.DoesNotContain("198.51.100.1", policy.AllowedIpAddresses);
    }

    [Fact]
    public void LoadPolicy_RepoConfigAlone_AppliesCorrectly()
    {
        var virtualFs = new Dictionary<string, string>
        {
            ["/repo/.diffguard.json"] = """
                {
                    "scanners": { "enablePromptInjectionRules": true },
                    "thresholds": { "maxChangedFiles": 42 },
                    "allowlists": { "wellKnownSafeDomains": ["internal.corp"] }
                }
                """
        };

        var loader = new DiffGuardConfigLoader(
            fileExists: virtualFs.ContainsKey,
            readFile: p => virtualFs[p],
            getEnv: _ => null,
            customSystemPaths: [],
            customUserPaths: []);

        var policy = loader.LoadPolicy(repoRoot: "/repo");

        Assert.True(policy.EnablePromptInjectionRules);
        Assert.Equal(42, policy.MaxChangedFiles);
        Assert.Contains("internal.corp", policy.WellKnownSafeDomains);
    }

    [Fact]
    public void LoadPolicy_MinimalLockedSystemConfig_PreventsRepoFromDisablingRulesOrHijackingLlmEndpoint()
    {
        var virtualFs = new Dictionary<string, string>
        {
            ["/repo/.diffguard.json"] = """
                {
                    "lockPolicy": false,
                    "allowLowerTierAllowlistAdditions": true,
                    "scanners": {
                        "enableDeterministic": false,
                        "enableNetworkEndpointRules": false,
                        "enableBackdoorSignatureRules": false,
                        "allowUnverifiedImageExtensionsWhenStreamUnavailable": true
                    },
                    "thresholds": {
                        "maxChangedFiles": 9999,
                        "entropyMinStringLength": 9999
                    },
                    "llmJudge": {
                        "endpoint": "https://attacker.evil.example/v1",
                        "failClosedOnError": false
                    }
                }
                """,
            ["/system/config.json"] = """
                {
                    "lockPolicy": true
                }
                """
        };

        var loader = new DiffGuardConfigLoader(
            fileExists: virtualFs.ContainsKey,
            readFile: p => virtualFs[p],
            getEnv: _ => null,
            customSystemPaths: ["/system/config.json"],
            customUserPaths: []);

        var policy = loader.LoadPolicy(repoRoot: "/repo");

        Assert.True(policy.LockPolicy);
        Assert.False(policy.AllowLowerTierAllowlistAdditions);
        Assert.True(policy.EnableDeterministic);
        Assert.True(policy.ForceDeterministic);
        Assert.True(policy.EnableNetworkEndpointRules);
        Assert.True(policy.EnableBackdoorSignatureRules);
        Assert.False(policy.AllowUnverifiedImageExtensionsWhenStreamUnavailable);
        Assert.Equal(50, policy.MaxChangedFiles);
        Assert.Equal(20, policy.EntropyMinStringLength);
        Assert.Null(policy.LlmEndpoint);
        Assert.True(policy.FailClosedOnError);
        Assert.True(policy.ForceFailClosedOnError);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("InvalidSeverity")]
    public void LoadPolicy_InvalidOrNumericMinimumRiskSeverity_ThrowsInvalidOperationException(string invalidSeverity)
    {
        var virtualFs = new Dictionary<string, string>
        {
            ["/repo/.diffguard.json"] = $$"""
                {
                    "minimumRiskSeverity": "{{invalidSeverity}}"
                }
                """
        };

        var loader = new DiffGuardConfigLoader(
            fileExists: virtualFs.ContainsKey,
            readFile: p => virtualFs[p],
            getEnv: _ => null,
            customSystemPaths: [],
            customUserPaths: []);

        Assert.Throws<InvalidOperationException>(() => loader.LoadPolicy(repoRoot: "/repo"));
    }

    [Fact]
    public void LoadPolicy_RepoConfigCannotSetSystemLocksWhenNoSystemConfigPresent()
    {
        var virtualFs = new Dictionary<string, string>
        {
            ["/repo/.diffguard.json"] = """
                {
                    "lockPolicy": true,
                    "allowLowerTierAllowlistAdditions": false,
                    "scanners": { "forceDeterministic": true }
                }
                """
        };

        var loader = new DiffGuardConfigLoader(
            fileExists: virtualFs.ContainsKey,
            readFile: p => virtualFs[p],
            getEnv: _ => null,
            customSystemPaths: [],
            customUserPaths: []);

        var policy = loader.LoadPolicy(repoRoot: "/repo");

        Assert.False(policy.LockPolicy);
        Assert.True(policy.AllowLowerTierAllowlistAdditions);
        Assert.False(policy.ForceDeterministic);
    }

    [Theory]
    [InlineData("/repo/diffguard.json")]
    [InlineData("/repo/.github/diffguard.json")]
    public void LoadPolicy_AlternativeRepoConfigPaths_DiscoversCorrectly(string configPath)
    {
        var virtualFs = new Dictionary<string, string>
        {
            [configPath] = """
                {
                    "thresholds": { "maxChangedFiles": 33 }
                }
                """
        };

        var loader = new DiffGuardConfigLoader(
            fileExists: virtualFs.ContainsKey,
            readFile: p => virtualFs[p],
            getEnv: _ => null,
            customSystemPaths: [],
            customUserPaths: []);

        var policy = loader.LoadPolicy(repoRoot: "/repo");

        Assert.True(policy.RepoConfigLoaded);
        Assert.Equal(33, policy.MaxChangedFiles);
    }
}

