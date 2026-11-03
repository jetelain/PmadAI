using System.Text.Json;
using Microsoft.Extensions.AI;
using Pmad.AI.Tools;

namespace Pmad.AI.Tests;

public class RegistredToolsMetadataTests
{
    private static AIFunction CreateWeatherTool()
        => AIFunctionFactory.Create((string location) => $"sunny in {location}", "get_weather");

    [Fact]
    public void Constructor_WithNull_ResultsInEmptyMetadata()
    {
        var metadata = new RegistredToolsMetadata(null);

        Assert.Equal(0, metadata.Count);
        Assert.False(metadata.ContainsKey("anything"));
        Assert.False(metadata.TryGetValue("anything", out var tool));
        Assert.Null(tool);
        Assert.False(metadata.TryGetSchema("anything", out _, out var rootSchema));
    }

    [Fact]
    public void Constructor_WithTools_RegistersEachTool()
    {
        var a = CreateWeatherTool();
        var b = AIFunctionFactory.Create((string name) => name, "create_user");

        var metadata = new RegistredToolsMetadata(new AIFunction[] { a, b });

        Assert.Equal(2, metadata.Count);
        Assert.True(metadata.TryGetValue("get_weather", out var toolA));
        Assert.Same(a, toolA);
        Assert.True(metadata.TryGetValue("create_user", out var toolB));
        Assert.Same(b, toolB);
    }

    [Fact]
    public void TryGetValue_ResolvesToolNames_CaseInsensitively()
    {
        var tool = CreateWeatherTool();
        var metadata = new RegistredToolsMetadata([tool]);

        Assert.True(metadata.TryGetValue("get_weather", out var exact));
        Assert.Same(tool, exact);
        Assert.True(metadata.TryGetValue("GET_WEATHER", out var upper));
        Assert.Same(tool, upper);
        Assert.True(metadata.TryGetValue("Get_Weather", out var mixed));
        Assert.Same(tool, mixed);
        Assert.False(metadata.TryGetValue("get_temperature", out _));
    }

    [Fact]
    public void ContainsKey_ResolvesToolNames_CaseInsensitively()
    {
        var metadata = new RegistredToolsMetadata([CreateWeatherTool()]);

        Assert.True(metadata.ContainsKey("get_weather"));
        Assert.True(metadata.ContainsKey("GET_WEATHER"));
        Assert.False(metadata.ContainsKey("unknown_tool"));
    }

    [Fact]
    public void WithTool_WithSameReference_ReturnsSameInstance()
    {
        var tool = CreateWeatherTool();
        var metadata = new RegistredToolsMetadata([tool]);

        var result = metadata.WithTool(tool);

        Assert.Same(metadata, result);
    }

    [Fact]
    public void WithTool_WithDifferentReference_SameName_SameSchema_UpdatesFunction()
    {
        var original = CreateWeatherTool();
        var replacement = AIFunctionFactory.Create((string location) => $"cloudy in {location}", "get_weather");
        var metadata = new RegistredToolsMetadata([original]);

        var result = metadata.WithTool(replacement);

        Assert.NotSame(metadata, result);
        Assert.True(result.TryGetValue("get_weather", out var updated));
        Assert.Same(replacement, updated);
        // The baseline instance is immutable and must keep the original function.
        Assert.True(metadata.TryGetValue("get_weather", out var originalStill));
        Assert.Same(original, originalStill);
    }

    [Fact]
    public void WithTool_WithDifferentSchema_ReturnsNewInstance()
    {
        var original = AIFunctionFactory.Create((string location) => location, "get_weather");
        var different = AIFunctionFactory.Create((int id) => id, "get_weather");
        var metadata = new RegistredToolsMetadata([original]);

        var result = metadata.WithTool(different);

        Assert.NotSame(metadata, result);
        Assert.True(result.TryGetValue("get_weather", out var updated));
        Assert.Same(different, updated);
        Assert.NotSame(original, updated);
    }

    [Fact]
    public void WithTools_WithUnknownTool_ReturnsNewInstance()
    {
        var metadata = new RegistredToolsMetadata([CreateWeatherTool()]);

        var result = metadata.WithTools([AIFunctionFactory.Create((string name) => name, "create_user")]);

        Assert.NotSame(metadata, result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void WithTools_WithAllSameReferences_ReturnsSameInstance()
    {
        var a = CreateWeatherTool();
        var b = AIFunctionFactory.Create((string name) => name, "create_user");
        var metadata = new RegistredToolsMetadata(new AIFunction[] { a, b });

        var result = metadata.WithTools(new AIFunction[] { a, b });

        Assert.Same(metadata, result);
    }

    // Regression test: the copy constructor must preserve the case-insensitive comparer of the
    // internal dictionaries. A copy that fell back to the default (ordinal) comparer would
    // silently break case-insensitive resolution (e.g. "GET_WEATHER" would no longer match
    // the registered "get_weather").
    [Fact]
    public void WithTool_CopiedMetadata_PreservesCaseInsensitiveLookups()
    {
        var tool = CreateWeatherTool();
        var metadata = new RegistredToolsMetadata(null);
        var copied = metadata.WithTool(tool);

        Assert.True(copied.TryGetValue("GET_WEATHER", out var resolved));
        Assert.Same(tool, resolved);
        Assert.True(copied.ContainsKey("Get_Weather"));

        // Schema lookups must also survive the copy, including case drift.
        Assert.True(copied.TryGetSchema("GET_WEATHER", out var propDict, out var rootSchema));
        Assert.NotNull(propDict);
        Assert.True(rootSchema.ValueKind == JsonValueKind.Object);
        Assert.True(rootSchema.TryGetProperty("properties", out _));
    }

    [Fact]
    public void ChainedWithTool_Copies_PreserveCaseInsensitiveLookups()
    {
        var metadata = new RegistredToolsMetadata(null);
        var weather = CreateWeatherTool();
        var second = AIFunctionFactory.Create((string name) => name, "create_user");

        var result = metadata.WithTool(weather).WithTool(second).WithTool(AIFunctionFactory.Create((string q) => q, "search_documents"));

        Assert.Equal(3, result.Count);
        Assert.True(result.TryGetValue("GET_WEATHER", out var resolved));
        Assert.Same(weather, resolved);
        Assert.True(result.ContainsKey("CREATE_USER"));
        Assert.True(result.ContainsKey("Search_Documents"));
    }

    [Fact]
    public void TryGetSchema_CachesProperties_WithCaseVariantKeys()
    {
        var tool = AIFunctionFactory.Create((int id) => id, "fetch_item");
        var metadata = new RegistredToolsMetadata([tool]);

        Assert.True(metadata.TryGetSchema("fetch_item", out var propDict, out _));
        Assert.NotNull(propDict);
        Assert.True(propDict.ContainsKey("id"));
        Assert.Equal("id", propDict["id"].CanonicalName);
    }

    [Fact]
    public void TryGetSchema_ForToolWithoutParameters_ReturnsEmptyPropertyMap()
    {
        // A function with no parameters produces an empty "properties" bag.
        var tool = AIFunctionFactory.Create(() => 42, "noop");
        var metadata = new RegistredToolsMetadata([tool]);

        Assert.True(metadata.TryGetSchema("noop", out var propDict, out var rootSchema));
        Assert.NotNull(propDict);
        Assert.Empty(propDict);
        Assert.NotEqual(rootSchema, default);
        Assert.False(metadata.TryGetCachedSchemaProperties("unknown_tool", out _));
    }

    [Fact]
    public void TryGetSchema_ForUnknownTool_ReturnsFalse()
    {
        var metadata = new RegistredToolsMetadata(null);

        Assert.False(metadata.TryGetSchema("missing", out var propDict, out var rootSchema));
        Assert.Null(propDict);
        Assert.Equal(JsonValueKind.Undefined, rootSchema.ValueKind);
    }

    [Fact]
    public void TryGetCachedSchemaProperties_ResolvesToolNames_CaseInsensitively()
    {
        var metadata = new RegistredToolsMetadata([CreateWeatherTool()]);

        Assert.True(metadata.TryGetCachedSchemaProperties("get_weather", out var lower));
        Assert.True(metadata.TryGetCachedSchemaProperties("GET_WEATHER", out var upper));
        Assert.Same(lower, upper);
        Assert.False(metadata.TryGetCachedSchemaProperties("unknown_tool", out _));
    }

    [Fact]
    public void TryGetSchema_AfterWithTool_ReturnsUpdatedSchema()
    {
        var original = CreateWeatherTool();
        var replacement = AIFunctionFactory.Create((string location) => location, "get_weather");
        var metadata = new RegistredToolsMetadata([original]);
        var result = metadata.WithTool(replacement);

        Assert.True(result.TryGetSchema("get_weather", out _, out var rootSchema));
        var expected = replacement.JsonSchema;
        Assert.True(JsonSerializer.Serialize(rootSchema) == JsonSerializer.Serialize(expected));
    }
}
