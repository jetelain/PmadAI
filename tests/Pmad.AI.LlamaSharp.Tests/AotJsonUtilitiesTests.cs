using System.Text.Json;
using Microsoft.Extensions.AI;
using Pmad.AI.LlamaSharp;

namespace Pmad.AI.LlamaSharp.Tests;

public class AotJsonUtilitiesTests
{
    [Fact]
    public void SerializeArguments_WhenNullOrEmpty_ReturnsEmptyJsonObject()
    {
        Assert.Equal("{}", AotJsonUtilities.SerializeArguments(null));
        Assert.Equal("{}", AotJsonUtilities.SerializeArguments(new Dictionary<string, object?>()));
    }

    [Fact]
    public void SerializeArguments_WithPrimitivesAndNestedObjects_ReturnsValidJson()
    {
        var args = new Dictionary<string, object?>
        {
            ["name"] = "Alice",
            ["age"] = 30,
            ["pi"] = 3.14,
            ["active"] = true,
            ["data"] = null,
            ["nested"] = new Dictionary<string, object?>
            {
                ["inner"] = "val"
            },
            ["list"] = new object?[] { 1, "two", false }
        };

        var json = AotJsonUtilities.SerializeArguments(args);
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("Alice", root.GetProperty("name").GetString());
        Assert.Equal(30, root.GetProperty("age").GetInt32());
        Assert.True(root.GetProperty("active").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
        Assert.Equal("val", root.GetProperty("nested").GetProperty("inner").GetString());
        Assert.Equal(3, root.GetProperty("list").GetArrayLength());
    }

    [Fact]
    public void SerializeFunctionDefinition_GeneratesExpectedJson()
    {
        var fn = AIFunctionFactory.Create(
            (string location, string unit) => $"Weather for {location} in {unit}",
            "get_weather",
            "Fetches weather for a location");

        var json = AotJsonUtilities.SerializeFunctionDefinition(fn);
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("function", root.GetProperty("type").GetString());
        var fnObj = root.GetProperty("function");
        Assert.Equal("get_weather", fnObj.GetProperty("name").GetString());
        Assert.Equal("Fetches weather for a location", fnObj.GetProperty("description").GetString());
        Assert.True(fnObj.TryGetProperty("parameters", out var paramsElem));
        Assert.Equal(JsonValueKind.Object, paramsElem.ValueKind);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("simple string", "simple string")]
    [InlineData(42, "42")]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void FormatFunctionResult_HandlesPrimitives(object? input, string expected)
    {
        var formatted = AotJsonUtilities.FormatFunctionResult(input);
        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void FormatFunctionResult_HandlesJsonElement()
    {
        using var doc = JsonDocument.Parse("{\"key\":\"value\"}");
        var formatted = AotJsonUtilities.FormatFunctionResult(doc.RootElement);
        Assert.Equal("{\"key\":\"value\"}", formatted);
    }

    [Fact]
    public void FormatFunctionResult_HandlesDictionary()
    {
        var dict = new Dictionary<string, object?> { ["status"] = "ok" };
        var formatted = AotJsonUtilities.FormatFunctionResult(dict);
        Assert.Equal("{\"status\":\"ok\"}", formatted);
    }

    [Fact]
    public void FormatFunctionResult_HandlesList()
    {
        var list = new List<object?> { "item1", 123 };
        var formatted = AotJsonUtilities.FormatFunctionResult(list);
        Assert.Equal("[\"item1\",123]", formatted);
    }
}
