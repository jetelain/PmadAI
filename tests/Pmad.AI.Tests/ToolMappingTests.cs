using Pmad.AI.Common;
using Pmad.AI.Tools;

namespace Pmad.AI.Tests;

/// <summary>
/// Enum whose members carry <see cref="ToolValueAliasAttribute"/> annotations, used to exercise
/// <see cref="ToolMapping.AddEnumAliases{TEnum}"/> discovery.
/// </summary>
public enum TestResourceType
{
    [ToolValueAlias("memory", "ram")]
    MemoryMb,

    [ToolValueAlias("disk", "storage")]
    DiskMb,

    /// <summary>Member without a <see cref="ToolValueAliasAttribute"/>; must be ignored by discovery.</summary>
    Cpu,

    /// <summary>Member with an empty alias list; must be ignored by discovery.</summary>
    [ToolValueAlias()]
    Gpu
}

/// <summary>
/// Enum with no annotated members, used to verify that <see cref="ToolMapping.AddEnumAliases{TEnum}"/>
/// registers nothing for unannotated types.
/// </summary>
public enum TestUnannotatedEnum
{
    A,
    B
}

/// <summary>
/// Unit tests for <see cref="ToolMapping"/>, aiming for full behavioral coverage of the constructor,
/// the fluent builder methods, and the alias-resolution queries (nested parameter aliases and
/// parameter value aliases, including their path, case, and plural/singular fallbacks).
/// </summary>
public class ToolMappingTests
{
    // ---------------------------------------------------------------------
    // Constructors & properties
    // ---------------------------------------------------------------------

    [Fact]
    public void DefaultConstructor_InitializesEmptyCollections()
    {
        var m = new ToolMapping();
        Assert.Empty(m.Aliases);
        Assert.Empty(m.ParameterAliases);
        Assert.Empty(m.ComputedParameters);
        Assert.Empty(m.NestedParameterAliases);
        Assert.Empty(m.ParameterValueAliases);
        Assert.Empty(m.PropagatedParameters);
        Assert.Empty(m.PrimaryCollectionProperties);
    }

    [Fact]
    public void Ctor_AllInputs_PopulatesCollections()
    {
        ParameterComputer computer = args => args["a"];
        var m = new ToolMapping(
            aliases: new[] { "run", "execute" },
            parameterAliases: new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["limit"] = new[] { "max", "cap" }
            },
            computedParameters: new Dictionary<string, ParameterComputer>(StringComparer.OrdinalIgnoreCase)
            {
                ["count"] = computer
            },
            nestedParameterAliases: new Dictionary<string, IDictionary<string, IEnumerable<string>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["items[]"] = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["oldText"] = new[] { "old_text", "search" }
                }
            },
            parameterValueAliases: new Dictionary<string, IDictionary<string, IEnumerable<string>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["type"] = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["fast"] = new[] { "quick", "turbo" }
                }
            },
            propagatedParameters: new[] { "tenantId" },
            primaryCollectionProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["items"] = "name"
            });

        // Aliases remain a plain set of alternative names; the alias -> canonical flat map is an
        // internal convention owned by the dispatcher (canonical -> canonical + each alias -> canonical).
        Assert.Equal(2, m.Aliases.Count);
        Assert.Contains("run", m.Aliases, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("execute", m.Aliases, StringComparer.OrdinalIgnoreCase);

        // ParameterAliases: canonical self-maps, each alias maps to the canonical name.
        Assert.Equal("limit", m.ParameterAliases["limit"]);
        Assert.Equal("limit", m.ParameterAliases["max"]);
        Assert.Equal("limit", m.ParameterAliases["cap"]);

        Assert.Same(computer, m.ComputedParameters["count"]);

        // Nested aliases: flat map with canonical self-entry plus alias -> canonical entries.
        Assert.Equal("oldText", m.NestedParameterAliases["items[]"]["oldText"]);
        Assert.Equal("oldText", m.NestedParameterAliases["items[]"]["old_text"]);
        Assert.Equal("oldText", m.NestedParameterAliases["items[]"]["search"]);

        // Value aliases: flat map with canonical self-entry plus alias -> canonical entries.
        Assert.Equal("fast", m.ParameterValueAliases["type"]["fast"]);
        Assert.Equal("fast", m.ParameterValueAliases["type"]["turbo"]);
        Assert.Contains("tenantId", m.PropagatedParameters);
        Assert.Equal("name", m.PrimaryCollectionProperties["items"]);
    }

    [Fact]
    public void Ctor_NullInputs_CreatesEmptyInstance()
    {
        var m = new ToolMapping(null, null, null, null, null, null, null);
        Assert.Empty(m.Aliases);
        Assert.Empty(m.ParameterAliases);
        Assert.Empty(m.ComputedParameters);
        Assert.Empty(m.NestedParameterAliases);
        Assert.Empty(m.ParameterValueAliases);
        Assert.Empty(m.PropagatedParameters);
        Assert.Empty(m.PrimaryCollectionProperties);
    }

    [Fact]
    public void Ctor_PropagatedAndPrimaryEntries_TrimsAndSkipsWhitespace()
    {
        var m = new ToolMapping(
            propagatedParameters: new[] { "  tenantId  ", "  ", "", "region" },
            primaryCollectionProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["  items  "] = "  name  ",
                ["  "] = "prop",
                ["missing"] = "  "
            });

        Assert.Equal(2, m.PropagatedParameters.Count);
        Assert.Contains("tenantId", m.PropagatedParameters);
        Assert.Contains("region", m.PropagatedParameters);

        Assert.Single(m.PrimaryCollectionProperties);
        Assert.Equal("name", m.PrimaryCollectionProperties["items"]);
    }

    [Fact]
    public void Ctor_ParameterAliases_BuiltCaseInsensitive()
    {
        var m = new ToolMapping(parameterAliases: new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["limit"] = new[] { "Max" }
        });

        // The map is ordinal case-insensitive: "MAX" retrieves the "limit" entry.
        Assert.Equal("limit", m.ParameterAliases["MAX"]);
    }

    // ---------------------------------------------------------------------
    // Fluent builder methods
    // ---------------------------------------------------------------------

    [Fact]
    public void AddPropagatedParameter_Trimmed_ReturnsSelf()
    {
        var m = new ToolMapping();
        var returned = m.AddPropagatedParameter("  tenantId  ");
        Assert.Same(m, returned);
        Assert.Contains("tenantId", m.PropagatedParameters);
    }

    [Fact]
    public void AddPropagatedParameter_Blank_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ToolMapping().AddPropagatedParameter(null!));
        Assert.Throws<ArgumentException>(() => new ToolMapping().AddPropagatedParameter("   "));

    }

    [Fact]
    public void SetPrimaryCollectionProperty_Trimmed_ReturnsSelf()
    {
        var m = new ToolMapping();
        var returned = m.SetPrimaryCollectionProperty("  items[]  ", "  prompt  ");
        Assert.Same(m, returned);
        Assert.Equal("prompt", m.PrimaryCollectionProperties["items[]"]);
    }

    [Theory]
    [InlineData("", "x")]
    [InlineData("x", " ")]
    public void SetPrimaryCollectionProperty_Blank_Throws(string path, string prop)
    {
        Assert.Throws<ArgumentException>(() => new ToolMapping().SetPrimaryCollectionProperty(path, prop));
    }

    [Fact]
    public void AddNestedParameterAlias_RegistersCasingVariants()
    {
        var m = new ToolMapping().AddNestedParameterAlias("questions[]", "oldText", "old_text", "search");

        var map = m.NestedParameterAliases["questions[]"];

        // Canonical (and its casing variants) map to itself.
        Assert.Equal("oldText", map["oldText"]);
        Assert.Equal("oldText", map["old_text"]); // snake variant of canonical
        Assert.Equal("oldText", map["OldText"]);  // pascal variant of canonical

        // Every alias (and its casing variants) maps to the canonical name.
        Assert.Equal("oldText", map["search"]);
        Assert.Equal("oldText", map["SEARCH"]);
    }

    [Theory]
    [InlineData("  ", "x")]
    [InlineData("x", " ")]
    public void AddNestedParameterAlias_BlankPathOrKey_Throws(string path, string key)
    {
        Assert.Throws<ArgumentException>(() => new ToolMapping().AddNestedParameterAlias(path, key, "alias"));
    }

    [Fact]
    public void AddNestedParameterAlias_SkipsWhitespaceAliases()
    {
        var m = new ToolMapping().AddNestedParameterAlias("p[]", "k", "  ", "a");
        var map = m.NestedParameterAliases["p[]"];
        Assert.Equal("k", map["a"]);
        Assert.False(map.ContainsKey(" "));
    }

    [Fact]
    public void AddNestedParameterAlias_SecondCall_MergesIntoExistingMap()
    {
        var m = new ToolMapping()
            .AddNestedParameterAlias("p[]", "k", "a")
            .AddNestedParameterAlias("p[]", "k", "b");

        var map = m.NestedParameterAliases["p[]"];
        Assert.Equal("k", map["a"]);
        Assert.Equal("k", map["b"]);
    }

    [Fact]
    public void AddNestedParameterAlias_TrimsPathAndCanonical()
    {
        var m = new ToolMapping().AddNestedParameterAlias("  edits[]  ", "  oldText  ", "old_text");

        var map = m.NestedParameterAliases["edits[]"];
        Assert.Equal("oldText", map["oldText"]);
        Assert.Equal("oldText", map["old_text"]);
    }

    [Fact]
    public void AddParameterValueAlias_RegistersAliases()
    {
        var m = new ToolMapping().AddParameterValueAlias("species", "wild tiger", "tiger", "tigers");
        var map = m.ParameterValueAliases["species"];
        Assert.Equal("wild tiger", map["wild tiger"]);
        Assert.Equal("wild tiger", map["tiger"]);
        Assert.Equal("wild tiger", map["tigers"]);
    }

    [Fact]
    public void AddParameterValueAlias_MergesAndTrims()
    {
        var m = new ToolMapping()
            .AddParameterValueAlias("species", "wild tiger", "tiger")
            .AddParameterValueAlias("species", "wild tiger", "  tigers  ");

        var map = m.ParameterValueAliases["species"];
        Assert.Equal("wild tiger", map["tiger"]);
        Assert.Equal("wild tiger", map["tigers"]);
        Assert.False(map.ContainsKey("  tigers  "));
    }

    [Theory]
    [InlineData("  ", "v", new string[] { "a" })]
    [InlineData("p", " ", new string[] { "a" })]
    public void AddParameterValueAlias_BlankPathOrValue_Throws(string path, string value, string[] aliases)
    {
        Assert.Throws<ArgumentException>(() => new ToolMapping().AddParameterValueAlias(path, value, aliases));
    }

    [Fact]
    public void AddEnumAliases_UsesEnumNameAsDefaultPath()
    {
        var m = new ToolMapping().AddEnumAliases<TestResourceType>();
        Assert.True(m.TryResolveValueAlias("TestResourceType", "ram", out var canonical));
        Assert.Equal("MemoryMb", canonical);
    }

    [Fact]
    public void AddEnumAliases_UsesExplicitPath()
    {
        var m = new ToolMapping().AddEnumAliases<TestResourceType>("resource_type");
        Assert.True(m.TryResolveValueAlias("resource_type", "storage", out var canonical));
        Assert.Equal("DiskMb", canonical);
    }

    [Fact]
    public void AddEnumAliases_SkipsUnannotatedAndEmptyMembers()
    {
        var m = new ToolMapping().AddEnumAliases<TestResourceType>("p");
        // "Cpu" has no attribute, "Gpu" has an empty alias list -> neither resolvable.
        Assert.False(m.TryResolveValueAlias("p", "Cpu", out var c1));
        Assert.Equal("Cpu", c1);
        Assert.False(m.TryResolveValueAlias("p", "Gpu", out var c2));
        Assert.Equal("Gpu", c2);
    }

    [Fact]
    public void AddEnumAliases_UnannotatedEnum_RegistersNothing()
    {
        var m = new ToolMapping().AddEnumAliases<TestUnannotatedEnum>("p");
        Assert.False(m.TryResolveValueAlias("p", "A", out var c));
        Assert.Equal("A", c);
    }

    // ---------------------------------------------------------------------
    // TryGetNestedAlias
    // ---------------------------------------------------------------------

    [Fact]
    public void TryGetNestedAlias_AliasToCanonical_CaseInsensitive()
    {
        var m = new ToolMapping().AddNestedParameterAlias("questions[]", "options", "optionsJson", "choices");

        // Alias -> canonical (both alias spellings, any case).
        Assert.True(m.TryGetNestedAlias("questions[]", "optionsJson", out var c1));
        Assert.Equal("options", c1, StringComparer.OrdinalIgnoreCase);
        Assert.True(m.TryGetNestedAlias("questions[]", "choices", out var c2));
        Assert.Equal("options", c2, StringComparer.OrdinalIgnoreCase);

        // Case-variant of an alias also resolves (via the casing-variant fallback).
        Assert.True(m.TryGetNestedAlias("questions[]", "OPTIONSJSON", out var c5));
        Assert.Equal("options", c5, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryGetNestedAlias_CanonicalResolvesToItself()
    {
        var m = new ToolMapping().AddNestedParameterAlias("questions[]", "options", "optionsJson");

        Assert.True(m.TryGetNestedAlias("questions[]", "options", out var c3));
        Assert.Equal("options", c3, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryGetNestedAlias_SnakeCaseKeyFallsBackToSchemaDeclaredCasing()
    {
        // The model emits snake_case "old_text" but only pascal "OldText" was registered as canonical.
        var m = new ToolMapping().AddNestedParameterAlias("edits[]", "OldText", "targetContent");

        Assert.True(m.TryGetNestedAlias("edits[]", "old_text", out var canonical));
        Assert.Equal("OldText", canonical);
    }

    [Fact]
    public void TryGetNestedAlias_UnrelatedName_ReturnsFalse()
    {
        var m = new ToolMapping().AddNestedParameterAlias("questions[]", "options", "optionsJson");
        Assert.False(m.TryGetNestedAlias("questions[]", "nope", out var c4));
        Assert.Equal("nope", c4);
    }

    [Fact]
    public void TryGetNestedAlias_FallsBackToBarePath()
    {
        var m = new ToolMapping().AddNestedParameterAlias("questions", "options", "optionsJson");
        Assert.True(m.TryGetNestedAlias("questions", "optionsJson", out var canonical));
        Assert.Equal("options", canonical, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryGetNestedAlias_WildcardPath()
    {
        var m = new ToolMapping().AddNestedParameterAlias("*", "options", "optionsJson");
        Assert.True(m.TryGetNestedAlias("anything[]", "optionsJson", out var canonical));
        Assert.Equal("options", canonical, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryGetNestedAlias_NoMapping_ReturnsFalse()
    {
        var m = new ToolMapping();
        Assert.False(m.TryGetNestedAlias("p", "k", out var c));
        Assert.Equal("k", c);
    }

    // ---------------------------------------------------------------------
    // TryResolveValueAlias
    // ---------------------------------------------------------------------

    [Fact]
    public void TryResolveValueAlias_DirectAliasMatch()
    {
        var m = new ToolMapping().AddParameterValueAlias("type", "fast", "quick", "turbo");
        Assert.True(m.TryResolveValueAlias("type", "turbo", out var canonical));
        Assert.Equal("fast", canonical);
    }

    [Fact]
    public void TryResolveValueAlias_DirectCanonicalMatch_CaseInsensitive()
    {
        var m = new ToolMapping().AddParameterValueAlias("type", "MemoryMb", "ram");
        Assert.True(m.TryResolveValueAlias("type", "memorymb", out var canonical));
        Assert.Equal("MemoryMb", canonical);
    }

    [Fact]
    public void TryResolveValueAlias_SnakeCasePathFallback()
    {
        // Registered under a Pascal path; queryable via its snake_case form.
        var m = new ToolMapping().AddParameterValueAlias("ResourceType", "MemoryMb", "ram");
        Assert.True(m.TryResolveValueAlias("resource_type", "ram", out var canonical));
        Assert.Equal("MemoryMb", canonical);
    }

    [Fact]
    public void TryResolveValueAlias_SnakeCaseValueMatch()
    {
        // Canonical "MemoryMb" is registered with only a non-snake alias; "memory_mb" resolves via snake normalization.
        var m = new ToolMapping().AddParameterValueAlias("type", "MemoryMb", "ram");
        Assert.True(m.TryResolveValueAlias("type", "memory_mb", out var canonical));
        Assert.Equal("MemoryMb", canonical);
    }

    [Fact]
    public void TryResolveValueAlias_PluralToSingular()
    {
        var m = new ToolMapping().AddParameterValueAlias("species", "tiger", "cat");
        Assert.True(m.TryResolveValueAlias("species", "tigers", out var canonical));
        Assert.Equal("tiger", canonical);
    }

    [Fact]
    public void TryResolveValueAlias_SingularToPlural()
    {
        var m = new ToolMapping().AddParameterValueAlias("species", "tigers", "cats");
        Assert.True(m.TryResolveValueAlias("species", "tiger", out var canonical));
        Assert.Equal("tigers", canonical);
    }

    [Fact]
    public void TryResolveValueAlias_AliasPluralMatch()
    {
        // Plural heuristic applied to a registered alias: "ram" -> "rams" matches alias "ram".
        var m = new ToolMapping().AddParameterValueAlias("type", "MemoryMb", "ram");
        Assert.True(m.TryResolveValueAlias("type", "rams", out var canonical));
        Assert.Equal("MemoryMb", canonical);
    }

    [Fact]
    public void TryResolveValueAlias_SnakePluralValueMatch()
    {
        // "wild_tigers" resolves to canonical "wild tiger" via snake_case plural normalization.
        var m = new ToolMapping().AddParameterValueAlias("species", "wild tiger", "tiger");
        Assert.True(m.TryResolveValueAlias("species", "wild_tigers", out var canonical));
        Assert.Equal("wild tiger", canonical);
    }

    [Fact]
    public void TryResolveValueAlias_WildcardPath()
    {
        var m = new ToolMapping().AddParameterValueAlias("*", "MemoryMb", "ram");
        Assert.True(m.TryResolveValueAlias("some_param", "ram", out var canonical));
        Assert.Equal("MemoryMb", canonical);
    }

    [Fact]
    public void TryResolveValueAlias_DottedLeafPathFallback()
    {
        // Registered under the bare leaf name; resolvable through a dotted path via the leaf fallback.
        var m = new ToolMapping().AddParameterValueAlias("type", "fast", "quick");
        Assert.True(m.TryResolveValueAlias("cfg.type", "quick", out var canonical));
        Assert.Equal("fast", canonical);
    }

    [Fact]
    public void TryResolveValueAlias_DottedArrayLeafFallback()
    {
        // Leaf with brackets is stripped before lookup: "items[].type" -> "type".
        var m = new ToolMapping().AddParameterValueAlias("type", "fast", "quick");
        Assert.True(m.TryResolveValueAlias("items[].type", "quick", out var canonical));
        Assert.Equal("fast", canonical);
    }

    [Fact]
    public void TryResolveValueAlias_Unresolvable_ReturnsFalse_WithValueUnchanged()
    {
        var m = new ToolMapping().AddParameterValueAlias("type", "fast", "quick");
        Assert.False(m.TryResolveValueAlias("type", "unknown", out var canonical));
        Assert.Equal("unknown", canonical);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolveValueAlias_BlankValue_ReturnsFalse(string value)
    {
        var m = new ToolMapping().AddParameterValueAlias("type", "fast", "quick");
        Assert.False(m.TryResolveValueAlias("type", value, out var canonical));
        Assert.Equal(value, canonical);
    }

    [Fact]
    public void TryResolveValueAlias_NoMappings_ReturnsFalse()
    {
        var m = new ToolMapping();
        Assert.False(m.TryResolveValueAlias("type", "fast", out var canonical));
        Assert.Equal("fast", canonical);
    }

    [Fact]
    public void TryResolveValueAlias_PluralDoesNotCorruptShortValues()
    {
        // Single-character values must not produce degenerate plural/singular probes
        // (e.g. "a" -> singular "" must not resolve to canonical "b").
        var m = new ToolMapping().AddParameterValueAlias("type", "b");
        Assert.False(m.TryResolveValueAlias("type", "a", out var canonical));
        Assert.Equal("a", canonical);
    }

    // ---------------------------------------------------------------------
    // Clone
    // ---------------------------------------------------------------------

    private static ToolMapping CreatePopulatedMapping()
    {
        ParameterComputer computer = args => args["a"];
        return new ToolMapping(
            aliases: new[] { "run", "execute" },
            parameterAliases: new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["limit"] = new[] { "max", "cap" }
            },
            computedParameters: new Dictionary<string, ParameterComputer>(StringComparer.OrdinalIgnoreCase)
            {
                ["count"] = computer
            },
            nestedParameterAliases: new Dictionary<string, IDictionary<string, IEnumerable<string>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["items[]"] = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["oldText"] = new[] { "old_text", "search" }
                }
            },
            parameterValueAliases: new Dictionary<string, IDictionary<string, IEnumerable<string>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["type"] = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["fast"] = new[] { "quick" }
                }
            },
            propagatedParameters: new[] { "owner" },
            primaryCollectionProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["questions[]"] = "text"
            });
    }

    [Fact]
    public void Clone_PopulatedMapping_PreservesAllData()
    {
        var original = CreatePopulatedMapping();
        var clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.Equal(original.Aliases, clone.Aliases);
        Assert.Equal(original.ParameterAliases, clone.ParameterAliases);
        Assert.Equal(original.PropagatedParameters, clone.PropagatedParameters);
        Assert.Equal(original.PrimaryCollectionProperties, clone.PrimaryCollectionProperties);
        Assert.Equal(original.ComputedParameters.Keys, clone.ComputedParameters.Keys);
        Assert.Same(original.ComputedParameters["count"], clone.ComputedParameters["count"]); // delegates are shared by reference
        Assert.Equal(original.NestedParameterAliases.Keys, clone.NestedParameterAliases.Keys);
        Assert.Equal(original.ParameterValueAliases.Keys, clone.ParameterValueAliases.Keys);
        Assert.Equal(original.NestedParameterAliases["items[]"], clone.NestedParameterAliases["items[]"]);
        Assert.Equal(original.ParameterValueAliases["type"], clone.ParameterValueAliases["type"]);
    }

    [Fact]
    public void Clone_EmptyMapping_ProducesEmptyMapping()
    {
        var clone = new ToolMapping().Clone();

        Assert.NotSame(new ToolMapping(), clone);
        Assert.Empty(clone.Aliases);
        Assert.Empty(clone.ParameterAliases);
        Assert.Empty(clone.ComputedParameters);
        Assert.Empty(clone.NestedParameterAliases);
        Assert.Empty(clone.ParameterValueAliases);
        Assert.Empty(clone.PropagatedParameters);
        Assert.Empty(clone.PrimaryCollectionProperties);
    }

    [Fact]
    public void Clone_MutatingClone_DoesNotAffectOriginal()
    {
        var original = CreatePopulatedMapping();
        var clone = original.Clone();

        clone.Aliases.Add("mutated");
        clone.ParameterAliases["newParam"] = "canon";
        clone.NestedParameterAliases["items[]"]["mutated"] = "x";
        clone.ParameterValueAliases["type"]["mutated"] = "fast";
        clone.PropagatedParameters.Add("mutated");
        clone.PrimaryCollectionProperties["mutated[]"] = "prop";

        Assert.DoesNotContain("mutated", original.Aliases);
        Assert.False(original.ParameterAliases.ContainsKey("newParam"));
        Assert.False(original.NestedParameterAliases["items[]"].ContainsKey("mutated"));
        Assert.False(original.ParameterValueAliases["type"].ContainsKey("mutated"));
        Assert.DoesNotContain("mutated", original.PropagatedParameters);
        Assert.False(original.PrimaryCollectionProperties.ContainsKey("mutated[]"));
    }

    [Fact]
    public void Clone_MutatingOriginal_DoesNotAffectClone()
    {
        var original = CreatePopulatedMapping();
        var clone = original.Clone();

        original.Aliases.Add("mutated");
        original.NestedParameterAliases["items[]"]["mutated"] = "x";

        Assert.DoesNotContain("mutated", clone.Aliases);
        Assert.False(clone.NestedParameterAliases["items[]"].ContainsKey("mutated"));
    }
}
