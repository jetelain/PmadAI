using System.Text.Json;
using Pmad.AI.Common;

namespace Pmad.AI.Tests;

/// <summary>
/// Unit tests for <see cref="JsonRepairUtilities"/>, covering truncated-JSON repair (missing
/// brackets, unterminated strings, dangling keys, truncated scalars) and stringified-JSON parsing.
/// </summary>
public class JsonRepairUtilitiesTests
{
    [Fact]
    public void TryRepairTruncatedJson_ClosesMissingArrayBracket()
    {
        var input = "[{\"prompt\":\"Q1\",\"options\":[\"A\",\"B\"]},{\"prompt\":\"Q2\",\"options\":[\"C\",\"D\"]}";
        Assert.Equal(
            "[{\"prompt\":\"Q1\",\"options\":[\"A\",\"B\"]},{\"prompt\":\"Q2\",\"options\":[\"C\",\"D\"]}]",
            JsonRepairUtilities.TryRepairTruncatedJson(input));
    }

    [Fact]
    public void TryRepairTruncatedJson_ClosesMissingObjectAndArray()
    {
        var input = "{\"questions\":[{\"prompt\":\"Proceed?\"}";
        Assert.Equal(
            "{\"questions\":[{\"prompt\":\"Proceed?\"}]}",
            JsonRepairUtilities.TryRepairTruncatedJson(input));
    }

    [Fact]
    public void TryRepairTruncatedJson_ClosesUnterminatedValueString()
    {
        var input = "[{\"prompt\":\"Q1\"},{\"prompt\":\"Q2: Enable caching";
        Assert.Equal(
            "[{\"prompt\":\"Q1\"},{\"prompt\":\"Q2: Enable caching\"}]",
            JsonRepairUtilities.TryRepairTruncatedJson(input));
    }

    [Fact]
    public void TryRepairTruncatedJson_DropsDanglingKey()
    {
        var input = "{\"questions\":[{\"prompt\":\"Which DB?\",\"options\":[\"SQLite\",\"Postgres\"],\"ration";
        Assert.Equal(
            "{\"questions\":[{\"prompt\":\"Which DB?\",\"options\":[\"SQLite\",\"Postgres\"]}]}",
            JsonRepairUtilities.TryRepairTruncatedJson(input));
    }

    [Fact]
    public void TryRepairTruncatedJson_DropsDanglingKeyWithoutColon()
    {
        var input = "{\"questions\":[{\"prompt\":\"Which DB?\",\"options\":[\"SQLite\",\"Postgres\"],\"rat";
        Assert.Equal(
            "{\"questions\":[{\"prompt\":\"Which DB?\",\"options\":[\"SQLite\",\"Postgres\"]}]}",
            JsonRepairUtilities.TryRepairTruncatedJson(input));
    }

    [Fact]
    public void TryRepairTruncatedJson_DropsKeyTruncatedMidString()
    {
        var input = "{\"questions\":[{\"prompt\":\"Which DB?\",\"rat";
        Assert.Equal(
            "{\"questions\":[{\"prompt\":\"Which DB?\"}]}",
            JsonRepairUtilities.TryRepairTruncatedJson(input));
    }

    [Fact]
    public void TryRepairTruncatedJson_DropsEmptyNestedObjectWhenTruncatedOnFirstKey()
    {
        // Truncated inside the very first key of a nested array item: should not leave a bogus `{}` element.
        var input = "{\"questions\":[{\"prom";
        Assert.Equal(
            "{\"questions\":[]}",
            JsonRepairUtilities.TryRepairTruncatedJson(input));

        var input2 = "[{\"prompt\":\"Q1\"},{\"prom";
        Assert.Equal(
            "[{\"prompt\":\"Q1\"}]",
            JsonRepairUtilities.TryRepairTruncatedJson(input2));
    }

    [Fact]
    public void TryRepairTruncatedJson_TrimsDanglingBackslashAndIncompleteUnicodeEscapeInValueString()
    {
        // Trailing backslash at EOF inside a value string must not escape the closing quote.
        var input = "{\"path\":\"C:\\\\foo\\";
        var repaired = JsonRepairUtilities.TryRepairTruncatedJson(input);
        Assert.Equal("{\"path\":\"C:\\\\foo\"}", repaired);
        using var doc1 = JsonDocument.Parse(repaired!);
        Assert.Equal("C:\\foo", doc1.RootElement.GetProperty("path").GetString());

        // Incomplete \uXXXX escape at EOF inside a value string is stripped before closing quote.
        var inputUnicode = "{\"text\":\"hello\\u00";
        var repairedUnicode = JsonRepairUtilities.TryRepairTruncatedJson(inputUnicode);
        Assert.Equal("{\"text\":\"hello\"}", repairedUnicode);
        using var doc2 = JsonDocument.Parse(repairedUnicode!);
        Assert.Equal("hello", doc2.RootElement.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("{\"flag\": tru", "{\"flag\": true}")]
    [InlineData("{\"flag\": fal", "{\"flag\": false}")]
    [InlineData("{\"val\": nul", "{\"val\": null}")]
    [InlineData("{\"num\": 12.", "{\"num\": 12}")]
    [InlineData("{\"num\": 12.5e-", "{\"num\": 12.5}")]
    [InlineData("{\"a\": 1, \"num\": -", "{\"a\": 1}")]
    public void TryRepairTruncatedJson_RepairsOrDropsPartialScalars(string input, string expected)
    {
        var repaired = JsonRepairUtilities.TryRepairTruncatedJson(input);
        Assert.Equal(expected, repaired);
        using var _ = JsonDocument.Parse(repaired!);
    }

    [Fact]
    public void TryRepairTruncatedJson_KeepsTruncatedScalarValue()
    {
        var input = "{\"count\": 12";
        Assert.Equal(
            "{\"count\": 12}",
            JsonRepairUtilities.TryRepairTruncatedJson(input));
    }

    [Theory]
    [InlineData("{\"a\":[1,2,3]}")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"x\":1}")]
    public void TryRepairTruncatedJson_BalancedDocument_ReturnsNull(string input)
    {
        Assert.Null(JsonRepairUtilities.TryRepairTruncatedJson(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("hello world")]
    public void TryRepairTruncatedJson_NonJsonInput_ReturnsNull(string input)
    {
        Assert.Null(JsonRepairUtilities.TryRepairTruncatedJson(input));
    }

    [Fact]
    public void TryRepairTruncatedJson_RepairsAndReparses()
    {
        var input = "{\"questions\":[{\"prompt\":\"Q1\",\"options\":[\"A\",\"B\"]},{\"prompt\":\"Q2\",\"options\":[\"C\",\"D\"]}";
        var repaired = JsonRepairUtilities.TryRepairTruncatedJson(input);
        Assert.NotNull(repaired);
        using var doc = JsonDocument.Parse(repaired!);
        Assert.Equal(2, doc.RootElement.GetProperty("questions").GetArrayLength());
    }

    [Fact]
    public void TryParseStringifiedJson_ValidArray_ReturnsList()
    {
        var parsed = JsonRepairUtilities.TryParseStringifiedJson("[{\"prompt\":\"Q1\"},{\"prompt\":\"Q2\"}]", out var repaired);
        var list = Assert.IsAssignableFrom<List<object?>>(parsed);
        Assert.Equal(2, list.Count);
        Assert.False(repaired);
    }

    [Fact]
    public void TryParseStringifiedJson_ValidObject_ReturnsDictionary()
    {
        var parsed = JsonRepairUtilities.TryParseStringifiedJson("{\"prompt\":\"Q1\",\"options\":[\"A\",\"B\"]}", out var repaired);
        var dict = Assert.IsAssignableFrom<Dictionary<string, object?>>(parsed);
        Assert.Equal("Q1", dict["prompt"]);
        Assert.False(repaired);
    }

    [Fact]
    public void TryParseStringifiedJson_TruncatedArray_RepairsAndParses()
    {
        var parsed = JsonRepairUtilities.TryParseStringifiedJson(
            "[{\"prompt\":\"Q1\",\"options\":[\"A\",\"B\"]},{\"prompt\":\"Q2\",\"options\":[\"C\",\"D\"]}", out var repaired);
        var list = Assert.IsAssignableFrom<List<object?>>(parsed);
        Assert.Equal(2, list.Count);
        Assert.True(repaired);
    }

    [Fact]
    public void TryParseStringifiedJson_TruncatedObject_RepairsAndParses()
    {
        var parsed = JsonRepairUtilities.TryParseStringifiedJson(
            "{\"prompt\":\"Q1\",\"rationale\":\"storage\",", out var repaired);
        var dict = Assert.IsAssignableFrom<Dictionary<string, object?>>(parsed);
        Assert.Equal("Q1", dict["prompt"]);
        Assert.Equal("storage", dict["rationale"]);
        Assert.True(repaired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("just a raw string")]
    [InlineData("   ")]
    public void TryParseStringifiedJson_NonJson_ReturnsNull(string input)
    {
        Assert.Null(JsonRepairUtilities.TryParseStringifiedJson(input, out var repaired));
        Assert.False(repaired);
    }

    [Fact]
    public void ConvertJsonElementToClr_ConvertsNestedDocument()
    {
        using var doc = JsonDocument.Parse("{\"n\":42,\"l\":[1,2.5,\"x\"],\"b\":true,\"z\":null}");
        var obj = Assert.IsAssignableFrom<Dictionary<string, object?>>(JsonRepairUtilities.ConvertJsonElementToClr(doc.RootElement));
        Assert.Equal(42, obj["n"]);
        Assert.Equal(true, obj["b"]);
        Assert.Null(obj["z"]);
        var list = Assert.IsAssignableFrom<List<object?>>(obj["l"]);
        Assert.Equal(new object?[] { 1, 2.5, "x" }, list);
    }

    [Fact]
    public void AddNestedParameterAlias_SupportsBidirectionalAndCasingLookups()
    {
        var mapping = new Pmad.AI.Tools.ToolMapping()
            .AddNestedParameterAlias("questions[]", "options", "optionsJson", "choices");

        Assert.True(mapping.TryGetNestedAlias("questions[]", "optionsJson", out var c1));
        Assert.Equal("options", c1, ignoreCase: true);

        Assert.True(mapping.TryGetNestedAlias("questions[]", "options_json", out var c2));
        Assert.Equal("options", c2, ignoreCase: true);

        Assert.True(mapping.TryGetNestedAlias("questions[]", "options", out var rev));
        Assert.Contains(rev, new[] { "optionsJson", "choices" }, StringComparer.OrdinalIgnoreCase);
    }
}
