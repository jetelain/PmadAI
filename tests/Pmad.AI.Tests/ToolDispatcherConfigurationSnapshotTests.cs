using Pmad.AI.Tools;

namespace Pmad.AI.Tests;

/// <summary>
/// Unit tests for <see cref="ToolDispatcherConfigurationSnapshot"/>, verifying that the snapshot is a
/// safe (decoupled) copy of the live configuration, that alias and translation lookups resolve
/// correctly, and that the internal "canonical maps to itself" convention is enforced.
/// </summary>
public class ToolDispatcherConfigurationSnapshotTests
{
    // ---------------------------------------------------------------------
    // Constructor: safe snapshot (decoupling)
    // ---------------------------------------------------------------------

    [Fact]
    public void Constructor_CopiesToolMappingsByValue()
    {
        var mapping = new ToolMapping().AddPropagatedParameter("owner");
        var config = new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["bash"] = mapping,
            }
        };

        var snapshot = config.Snapshot();

        // The snapshot must hold its own ToolMapping instance, not the live one.
        Assert.NotSame(mapping, snapshot.Tools["bash"]);
        // But it must preserve the data.
        Assert.Contains("owner", snapshot.Tools["bash"].PropagatedParameters);
    }

    [Fact]
    public void Constructor_MutatingLiveConfig_DoesNotAffectSnapshot()
    {
        var mapping = new ToolMapping();
        var config = new ToolDispatcherConfiguration
        {
            Tools = { ["bash"] = mapping },
        };

        var snapshot = config.Snapshot();

        // Mutate the live configuration after the snapshot was taken.
        mapping.Aliases.Add("shell");
        mapping.PropagatedParameters.Add("mutated");
        config.Tools["bash"].ParameterAliases["newParam"] = "canon";

        Assert.DoesNotContain("shell", snapshot.Tools["bash"].Aliases);
        Assert.DoesNotContain("mutated", snapshot.Tools["bash"].PropagatedParameters);
        Assert.False(snapshot.Tools["bash"].ParameterAliases.ContainsKey("newParam"));
    }

    [Fact]
    public void Constructor_MutatingSnapshot_DoesNotAffectLiveConfig()
    {
        var mapping = new ToolMapping();
        var config = new ToolDispatcherConfiguration
        {
            Tools = { ["bash"] = mapping },
        };

        var snapshot = config.Snapshot();

        // Mutate the snapshot's mapping.
        snapshot.Tools["bash"].Aliases.Add("shell");
        snapshot.Tools["bash"].PropagatedParameters.Add("mutated");

        Assert.DoesNotContain("shell", mapping.Aliases);
        Assert.DoesNotContain("mutated", mapping.PropagatedParameters);
        Assert.Empty(config.Tools["bash"].Aliases);
    }

    [Fact]
    public void Constructor_CopiesTranslationsList()
    {
        var translations = new List<ToolTranslation>
        {
            new() { SourceTool = "grep_search", TargetTool = "bash", CommandTemplate = "grep {pattern}" },
        };
        var config = new ToolDispatcherConfiguration
        {
            Translations = translations,
        };

        var snapshot = config.Snapshot();

        // The snapshot holds its own list instance (ToList), decoupled from the source list.
        Assert.NotSame(translations, snapshot.Translations);
        Assert.Single(snapshot.Translations);
    }

    [Fact]
    public void Constructor_PreservesCaseInsensitiveToolComparer()
    {
        var config = new ToolDispatcherConfiguration
        {
            Tools = { ["BASH"] = new ToolMapping() },
        };

        var snapshot = config.Snapshot();

        // Case-insensitive comparer must be preserved on the snapshot's dictionary.
        Assert.True(snapshot.Tools.ContainsKey("bash"));
        Assert.True(snapshot.Tools.ContainsKey("BASH"));
    }

    // ---------------------------------------------------------------------
    // TryGetAlias
    // ---------------------------------------------------------------------

    [Fact]
    public void TryGetAlias_CanonicalName_ResolvesToItself()
    {
        var config = new ToolDispatcherConfiguration
        {
            Tools = { ["bash"] = new ToolMapping() },
        };
        var snapshot = config.Snapshot();

        Assert.True(snapshot.TryGetAlias("bash", out var alias));
        Assert.Equal("bash", alias);
    }

    [Fact]
    public void TryGetAlias_ConfiguredAlias_ResolvesToCanonical()
    {
        var config = new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["bash"] = new ToolMapping(aliases: new[] { "shell", "terminal" }),
            }
        };
        var snapshot = config.Snapshot();

        Assert.True(snapshot.TryGetAlias("shell", out var fromShell));
        Assert.Equal("bash", fromShell);

        Assert.True(snapshot.TryGetAlias("terminal", out var fromTerminal));
        Assert.Equal("bash", fromTerminal);
    }

    [Fact]
    public void TryGetAlias_MixedCase_ResolvesCaseInsensitively()
    {
        var config = new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["bash"] = new ToolMapping(aliases: new[] { "shell" }),
            }
        };
        var snapshot = config.Snapshot();

        Assert.True(snapshot.TryGetAlias("SHELL", out var alias));
        Assert.Equal("bash", alias);

        Assert.True(snapshot.TryGetAlias("Bash", out var canonical));
        Assert.Equal("bash", canonical);
    }

    [Fact]
    public void TryGetAlias_UnknownTool_ReturnsFalse()
    {
        var config = new ToolDispatcherConfiguration
        {
            Tools = { ["bash"] = new ToolMapping() },
        };
        var snapshot = config.Snapshot();

        Assert.False(snapshot.TryGetAlias("python", out var alias));
        Assert.Null(alias);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryGetAlias_BlankConfiguredAlias_IsIgnored(string alias)
    {
        var config = new ToolDispatcherConfiguration
        {
            Tools = { ["bash"] = new ToolMapping() },
        };
        var mapping = config.Tools["bash"];
        mapping.Aliases.Add(alias);
        var snapshot = config.Snapshot();

        // Blank aliases must not poison the lookup table (e.g. "" would otherwise map to "bash").
        Assert.False(snapshot.TryGetAlias(alias.Trim(), out _));
    }

    // ---------------------------------------------------------------------
    // TryGetTranslation
    // ---------------------------------------------------------------------

    [Fact]
    public void TryGetTranslation_SourceTool_ReturnsTranslation()
    {
        var config = new ToolDispatcherConfiguration
        {
            Translations =
            [
                new ToolTranslation { SourceTool = "grep_search", TargetTool = "bash", CommandTemplate = "grep {pattern}" },
            ]
        };
        var snapshot = config.Snapshot();

        Assert.True(snapshot.TryGetTranslation("grep_search", out var translation));
        Assert.NotNull(translation);
        Assert.Equal("bash", translation!.TargetTool);
    }

    [Fact]
    public void TryGetTranslation_MixedCase_ResolvesCaseInsensitively()
    {
        var config = new ToolDispatcherConfiguration
        {
            Translations =
            [
                new ToolTranslation { SourceTool = "grep_search", TargetTool = "bash", CommandTemplate = "grep {pattern}" },
            ]
        };
        var snapshot = config.Snapshot();

        Assert.True(snapshot.TryGetTranslation("GREP_SEARCH", out var translation));
        Assert.Equal("bash", translation!.TargetTool);
    }

    [Fact]
    public void TryGetTranslation_UnknownTool_ReturnsFalse()
    {
        var config = new ToolDispatcherConfiguration
        {
            Translations =
            [
                new ToolTranslation { SourceTool = "grep_search", TargetTool = "bash", CommandTemplate = "grep {pattern}" },
            ]
        };
        var snapshot = config.Snapshot();

        Assert.False(snapshot.TryGetTranslation("sed_replace", out var translation));
        Assert.Null(translation);
    }

    [Fact]
    public void TryGetTranslation_NoTranslations_ReturnsFalse()
    {
        var config = new ToolDispatcherConfiguration { Translations = [] };
        var snapshot = config.Snapshot();

        Assert.False(snapshot.TryGetTranslation("grep_search", out var translation));
        Assert.Null(translation);
    }

    // ---------------------------------------------------------------------
    // BuildLookupTables: canonical->canonical normalization
    // ---------------------------------------------------------------------

    [Fact]
    public void Constructor_NormalizesParameterAliasTargetsToSelfMap()
    {
        // The flat map is alias -> canonical. When a canonical target is present only as a value
        // (not as a key), the snapshot must still add the "canonical -> canonical" self-entry.
        var config = new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["tool"] = new ToolMapping
                {
                    ParameterAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["max"] = "limit",
                        ["cap"] = "limit",
                    },
                },
            }
        };
        var snapshot = config.Snapshot();

        var paramAliases = snapshot.Tools["tool"].ParameterAliases;
        // Aliases still point at the canonical name.
        Assert.Equal("limit", paramAliases["max"]);
        Assert.Equal("limit", paramAliases["cap"]);
        // The canonical target, previously only a value, must now self-map.
        Assert.Equal("limit", paramAliases["limit"]);
    }

    [Fact]
    public void Constructor_NormalizesNestedAndValueAliasTargetsToSelfMap()
    {
        var config = new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["tool"] = new ToolMapping
                {
                    NestedParameterAliases = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["items[]"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["search"] = "oldText",
                        },
                    },
                    ParameterValueAliases = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["type"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["quick"] = "fast",
                        },
                    },
                },
            }
        };
        var snapshot = config.Snapshot();

        var nested = snapshot.Tools["tool"].NestedParameterAliases["items[]"];
        Assert.Equal("oldText", nested["search"]);
        // Canonical target must self-map even though it was only a value.
        Assert.Equal("oldText", nested["oldText"]);

        var values = snapshot.Tools["tool"].ParameterValueAliases["type"];
        Assert.Equal("fast", values["quick"]);
        Assert.Equal("fast", values["fast"]);
    }
}
