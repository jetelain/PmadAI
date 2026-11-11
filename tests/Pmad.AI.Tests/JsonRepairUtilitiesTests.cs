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
    public void ConvertJsonElementToClr_ValuesOutsideIntRange_MapToLong()
    {
        using var doc = JsonDocument.Parse("{\"big\":3000000000,\"small\":-3000000000,\"pi\":3.14}");
        var obj = Assert.IsAssignableFrom<Dictionary<string, object?>>(JsonRepairUtilities.ConvertJsonElementToClr(doc.RootElement));
        Assert.Equal(3000000000L, obj["big"]);
        Assert.Equal(-3000000000L, obj["small"]);
        Assert.Equal(3.14, Assert.IsType<double>(obj["pi"]));
    }

    [Fact]
    public void ConvertJsonElementToClr_EmptyContainersAndPrimitives()
    {
        using var doc = JsonDocument.Parse("{\"o\":{},\"a\":[],\"x\":[true,false,null]}");
        var obj = Assert.IsAssignableFrom<Dictionary<string, object?>>(JsonRepairUtilities.ConvertJsonElementToClr(doc.RootElement));
        Assert.Empty((Dictionary<string, object?>)obj["o"]!);
        Assert.Empty((List<object?>)obj["a"]!);
        Assert.Equal(new object?[] { true, false, null }, (List<object?>)obj["x"]!);
    }

    [Fact]
    public void TryParseStringifiedJson_TopLevelScalar_ParseDirectly()
    {
        Assert.Equal(42, JsonRepairUtilities.TryParseStringifiedJson("42", out var r1));
        Assert.False(r1);
        Assert.Equal("hi", JsonRepairUtilities.TryParseStringifiedJson("\"hi\"", out var r2));
        Assert.False(r2);
    }

    [Fact]
    public void TryParseStringifiedJson_ValidJsonWithWhitespace_ParseDirectly()
    {
        var parsed = JsonRepairUtilities.TryParseStringifiedJson(" { \"a\" : 1 } ", out var repaired);
        var dict = Assert.IsAssignableFrom<Dictionary<string, object?>>(parsed);
        Assert.Equal(1, dict["a"]);
        Assert.False(repaired);
    }

    [Fact]
    public void TryParseStringifiedJson_UnrepairableMalformedJson_ReturnsNull()
    {
        // Missing comma: not a truncation the prefix repairer can fix, so no repair is applicable.
        Assert.Null(JsonRepairUtilities.TryParseStringifiedJson("[1 2]", out var repaired));
        Assert.False(repaired);
    }

    [Fact]
    public void TryRepairTruncatedJson_NonJsonStart_ReturnsNull()
    {
        Assert.Null(JsonRepairUtilities.TryRepairTruncatedJson("42"));
        Assert.Null(JsonRepairUtilities.TryRepairTruncatedJson("\"str\""));
        Assert.Null(JsonRepairUtilities.TryRepairTruncatedJson("tru"));
    }

    [Fact]
    public void TryRepairTruncatedJson_NestedArrayTruncation()
    {
        Assert.Equal("[1,[2]]", JsonRepairUtilities.TryRepairTruncatedJson("[1,[2"));
        Assert.Equal("[[[]]]", JsonRepairUtilities.TryRepairTruncatedJson("[[["));
    }

    [Fact]
    public void TryRepairTruncatedJson_KeyMissingColon_DropsKey()
    {
        Assert.Equal(
            "{\"a\":1}",
            JsonRepairUtilities.TryRepairTruncatedJson("{\"a\":1,\"b\""));
    }

    [Fact]
    public void TryRepairTruncatedJson_TrailingCommaIsTrimmed()
    {
        Assert.Equal(
            "{\"a\":1}",
            JsonRepairUtilities.TryRepairTruncatedJson("{\"a\":1,"));
    }

    [Theory]
    [InlineData("{\"k\":\"a\\\\\\",    "{\"k\":\"a\\\\\"}")] // three backslashes: last one dangling, stripped, string closed
    [InlineData("{\"k\":\"a\\\\u\"",     "{\"k\":\"a\\\\u\"}")] // escaped backslash then 'u': no \\u escape to strip
    [InlineData("{\"k\":\"a\\\\\\u00", "{\"k\":\"a\\\\\"}")] // escaped backslash + incomplete \\u00: escape stripped
    [InlineData("{\"k\":\"a\\\\\\\\\"",  "{\"k\":\"a\\\\\\\\\"}")] // four backslashes: all complete, all kept
    public void TryRepairTruncatedJson_EdgeCaseEscapesInValueString(string input, string expected)
    {
        var repaired = JsonRepairUtilities.TryRepairTruncatedJson(input);
        Assert.Equal(expected, repaired);
        using var _ = JsonDocument.Parse(repaired!);
    }

    [Fact]
    public void TryRepairTruncatedJson_IncompleteUnicodeEscapeAfterEscapedBackslash()
    {
        var repaired = JsonRepairUtilities.TryRepairTruncatedJson("{\"k\":\"a\\\\u0");
        Assert.Equal("{\"k\":\"a\\\\u0\"}", repaired);
        using var doc = JsonDocument.Parse(repaired!);
        Assert.Equal("a\\u0", doc.RootElement.GetProperty("k").GetString());
    }

    [Fact]
    public void TryRepairTruncatedJson_PartialExponentScalarKeptOrDropped()
    {
        Assert.Equal("{\"a\": 1e1}", JsonRepairUtilities.TryRepairTruncatedJson("{\"a\": 1e1"));
        Assert.Equal("{\"a\": 1}", JsonRepairUtilities.TryRepairTruncatedJson("{\"a\": 1e"));
    }

    [Fact]
    public void TryRepairTruncatedJson_DeeplyNestedTruncation()
    {
        Assert.Equal(
            "{\"a\":{\"b\":[{\"c\":1}]}}",
            JsonRepairUtilities.TryRepairTruncatedJson("{\"a\":{\"b\":[{\"c\":1")); // truncated inside a number
        Assert.Equal(
            "{\"a\":{\"b\":[{\"c\":1}]}}",
            JsonRepairUtilities.TryRepairTruncatedJson("{\"a\":{\"b\":[{\"c\":1}]}"));
    }

    [Fact]
    public void TryRepairTruncatedJson_EmptyNestedObjectInArrayIsDropped()
    {
        // Cut right after the colon of the only property: the inner object has no completed field, so it is dropped.
        Assert.Equal("[]", JsonRepairUtilities.TryRepairTruncatedJson("[{\"a\":"));
    }

    [Fact]
    public void ExtractBalancedJson_NullOrEmptyOrInvalidIndex_ReturnsNull()
    {
        Assert.Null(JsonRepairUtilities.ExtractBalancedJson(null!, 0, out var len1, out var trunc1));
        Assert.Equal(0, len1);
        Assert.False(trunc1);

        Assert.Null(JsonRepairUtilities.ExtractBalancedJson("", 0, out var len2, out var trunc2));
        Assert.Equal(0, len2);
        Assert.False(trunc2);

        Assert.Null(JsonRepairUtilities.ExtractBalancedJson("{}", -1, out var len3, out var trunc3));
        Assert.Equal(0, len3);
        Assert.False(trunc3);

        Assert.Null(JsonRepairUtilities.ExtractBalancedJson("{}", 5, out var len4, out var trunc4));
        Assert.Equal(0, len4);
        Assert.False(trunc4);

        Assert.Null(JsonRepairUtilities.ExtractBalancedJson("No json here at all", 0, out var len5, out var trunc5));
        Assert.Equal(0, len5);
        Assert.False(trunc5);
    }

    [Fact]
    public void ExtractBalancedJson_CompleteObject_ExtractsCorrectly()
    {
        var text = "Prefix text {\"key\": \"value\", \"nested\": {\"inner\": 123}} suffix prose";
        var result = JsonRepairUtilities.ExtractBalancedJson(text, 0, out var consumedLen, out var isTruncated);

        Assert.NotNull(result);
        Assert.Equal("{\"key\": \"value\", \"nested\": {\"inner\": 123}}", result);
        Assert.False(isTruncated);
        int lastCloseBrace = text.IndexOf("}}", StringComparison.Ordinal) + 2;
        Assert.Equal(lastCloseBrace, consumedLen);
    }

    [Fact]
    public void ExtractBalancedJson_CompleteArray_ExtractsCorrectly()
    {
        var text = "Result: [1, 2, [\"nested\", {\"a\": true}]] and done.";
        var result = JsonRepairUtilities.ExtractBalancedJson(text, 0, out var consumedLen, out var isTruncated);

        Assert.NotNull(result);
        Assert.Equal("[1, 2, [\"nested\", {\"a\": true}]]", result);
        Assert.False(isTruncated);
        int expectedEnd = text.IndexOf("]]", StringComparison.Ordinal) + 2;
        Assert.Equal(expectedEnd, consumedLen);
    }

    [Fact]
    public void ExtractBalancedJson_HandlesBracesAndBracketsInsideStringLiteralsAndEscapes()
    {
        var json = """{"query": "SELECT * FROM t WHERE a = '}' AND b = \"}\" [0]", "count": 1}""";
        var text = "Action Input: " + json + " trailing";
        int start = text.IndexOf('{');
        var result = JsonRepairUtilities.ExtractBalancedJson(text, start, out var consumedLen, out var isTruncated);

        Assert.NotNull(result);
        Assert.Equal(json, result);
        Assert.False(isTruncated);
        using var doc = JsonDocument.Parse(result);
        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public void ExtractBalancedJson_PicksEarliestBetweenBraceAndBracket()
    {
        var text1 = "Array first [{\"a\": 1}]";
        var res1 = JsonRepairUtilities.ExtractBalancedJson(text1, 0, out _, out var trunc1);
        Assert.Equal("[{\"a\": 1}]", res1);
        Assert.False(trunc1);

        var text2 = "Object first {\"arr\": [1, 2]}";
        var res2 = JsonRepairUtilities.ExtractBalancedJson(text2, 0, out _, out var trunc2);
        Assert.Equal("{\"arr\": [1, 2]}", res2);
        Assert.False(trunc2);
    }

    [Fact]
    public void ExtractBalancedJson_TruncatedJson_RepairsUsingTryRepairTruncatedJson()
    {
        var text = "Action: ask\nAction Input: [{\"prompt\": \"Q1\", \"options\": [\"A\", \"B\"]}, {\"prompt\": \"Q2\"";
        int start = text.IndexOf('[');
        var result = JsonRepairUtilities.ExtractBalancedJson(text, start, out var consumedLen, out var isTruncated);

        Assert.NotNull(result);
        Assert.True(isTruncated);
        Assert.Equal(text.Length - start, consumedLen);
        Assert.Equal("[{\"prompt\": \"Q1\", \"options\": [\"A\", \"B\"]}, {\"prompt\": \"Q2\"}]", result);
        using var doc = JsonDocument.Parse(result);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public void ExtractBalancedJson_TruncatedJson_DanglingKeyIsDroppedDuringRepair()
    {
        var text = "Input: {\"name\": \"search\", \"params\": {\"query\": \"test\", \"optio";
        int start = text.IndexOf('{');
        var result = JsonRepairUtilities.ExtractBalancedJson(text, start, out var consumedLen, out var isTruncated);

        Assert.NotNull(result);
        Assert.True(isTruncated);
        Assert.Equal("{\"name\": \"search\", \"params\": {\"query\": \"test\"}}", result);
        using var doc = JsonDocument.Parse(result);
        Assert.Equal("test", doc.RootElement.GetProperty("params").GetProperty("query").GetString());
    }

    [Fact]
    public void ExtractBalancedJson_TruncatedJson_DanglingValueDropped()
    {
        var text = "Broken: {\"unrepairable\": ";
        int start = text.IndexOf('{');
        var result = JsonRepairUtilities.ExtractBalancedJson(text, start, out var consumedLen, out var isTruncated);

        Assert.NotNull(result);
        Assert.True(isTruncated);
        Assert.Equal("{}", result);
    }

    [Fact]
    public void ExtractBalancedJson_StartIndexOffsetRespected()
    {
        var text = "ignore first {\"first\": 1} and take second {\"second\": 2}";
        int secondStart = text.IndexOf("{\"second\"", StringComparison.Ordinal);

        var result = JsonRepairUtilities.ExtractBalancedJson(text, secondStart, out var consumedLen, out var isTruncated);
        Assert.Equal("{\"second\": 2}", result);
        Assert.False(isTruncated);
        Assert.Equal("{\"second\": 2}".Length, consumedLen);
    }

    [Fact]
    public void ExtractBalancedJsonElements_CorruptedArray_ExtractsIndividualBalancedElements()
    {
        var input = "[{\"prompt\": \"Q1\", \"options\": [\"A\", \"B\"]}, \"options\": null}, {\"prompt\": \"Q2\"}]";
        var elements = JsonRepairUtilities.ExtractBalancedJsonElements(input);

        Assert.Equal(2, elements.Count);
        Assert.Equal("{\"prompt\": \"Q1\", \"options\": [\"A\", \"B\"]}", elements[0]);
        Assert.Equal("{\"prompt\": \"Q2\"}", elements[1]);
    }

    [Fact]
    public void ExtractBalancedJsonElements_TruncatedTrailingElement_RepairsAndExtracts()
    {
        var input = "[{\"prompt\": \"Q1\"}, \"options\": null}, {\"prompt\": \"Q2\"";
        var elements = JsonRepairUtilities.ExtractBalancedJsonElements(input, '{');

        Assert.Equal(2, elements.Count);
        Assert.Equal("{\"prompt\": \"Q1\"}", elements[0]);
        Assert.Equal("{\"prompt\": \"Q2\"}", elements[1]);
    }

    [Fact]
    public void ExtractBalancedJsonElements_NestedArrays_ExtractsNestedArrayElements()
    {
        var input = "[[1, 2], rogue, [3, 4]]";
        var elements = JsonRepairUtilities.ExtractBalancedJsonElements(input, '[');

        Assert.Equal(2, elements.Count);
        Assert.Equal("[1, 2]", elements[0]);
        Assert.Equal("[3, 4]", elements[1]);
    }

    [Fact]
    public void TryParseStringifiedJsonArrayElements_CorruptedArray_ReturnsClrObjects()
    {
        var input = "[{\"prompt\": \"Q1\", \"options\": [\"A\", \"B\"]}, \"options\": null}, {\"prompt\": \"Q2\"}]";
        var result = JsonRepairUtilities.TryParseStringifiedJsonArrayElements(input);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);

        var first = Assert.IsAssignableFrom<IDictionary<string, object?>>(result[0]);
        Assert.Equal("Q1", first["prompt"]);
        var opts = Assert.IsAssignableFrom<IList<object?>>(first["options"]);
        Assert.Equal(new object?[] { "A", "B" }, opts);

        var second = Assert.IsAssignableFrom<IDictionary<string, object?>>(result[1]);
        Assert.Equal("Q2", second["prompt"]);
    }

    [Fact]
    public void TryParseStringifiedJsonArrayElements_EmptyOrInvalid_ReturnsNull()
    {
        Assert.Null(JsonRepairUtilities.TryParseStringifiedJsonArrayElements(""));
        Assert.Null(JsonRepairUtilities.TryParseStringifiedJsonArrayElements("[]"));
        Assert.Null(JsonRepairUtilities.TryParseStringifiedJsonArrayElements("not json"));
    }

    [Fact]
    public void ExtractBalancedJsonElements_TruncatedOuterArrayWithNestedArrayInFirstElement_ExtractsAllElements()
    {
        // First element contains an inner ']', and the outer array is truncated at the end.
        var input = "[{\"prompt\": \"Q1\", \"options\": [\"A\", \"B\"]}, \"options\": null}, {\"prompt\": \"Q2\"";
        var elements = JsonRepairUtilities.ExtractBalancedJsonElements(input, '{');

        Assert.Equal(2, elements.Count);
        Assert.Equal("{\"prompt\": \"Q1\", \"options\": [\"A\", \"B\"]}", elements[0]);
        Assert.Equal("{\"prompt\": \"Q2\"}", elements[1]);
    }

    [Fact]
    public void ExtractBalancedJsonElements_UnclosedLastObjectBeforeOuterClosingBracket_RepairsAndExtracts()
    {
        var input = "[{\"prompt\": \"Q1\"}, \"options\": null}, {\"prompt\": \"Q2\"]";
        var elements = JsonRepairUtilities.ExtractBalancedJsonElements(input, '{');

        Assert.Equal(2, elements.Count);
        Assert.Equal("{\"prompt\": \"Q1\"}", elements[0]);
        Assert.Equal("{\"prompt\": \"Q2\"}", elements[1]);
    }

    [Fact]
    public void ExtractBalancedJsonElements_RogueStringLiteralsContainingBracesAndUnclosedBraces_SkipsStringDelimiters()
    {
        var input = "[{\"prompt\": \"Q1\"}, \"note\": \"ignore {unclosed brace and {\\\"fake\\\": 1}\", {\"prompt\": \"Q2\"}]";
        var elements = JsonRepairUtilities.ExtractBalancedJsonElements(input, '{');

        Assert.Equal(2, elements.Count);
        Assert.Equal("{\"prompt\": \"Q1\"}", elements[0]);
        Assert.Equal("{\"prompt\": \"Q2\"}", elements[1]);
    }

    [Fact]
    public void ExtractBalancedJsonElements_FirstElementMissingClosingBraceOrMistypedAsBracket_RepairsFirstAndExtractsSecond()
    {
        // Case 1: Missing closing '}' on first element before ', {"prompt": "Q2"}'
        var missingBrace = "[{\"prompt\": \"Q1\", \"options\": [\"A\", \"B\"], {\"prompt\": \"Q2\"}]";
        var el1 = JsonRepairUtilities.ExtractBalancedJsonElements(missingBrace, '{');
        Assert.Equal(2, el1.Count);
        Assert.Equal("{\"prompt\": \"Q1\", \"options\": [\"A\", \"B\"]}", el1[0]);
        Assert.Equal("{\"prompt\": \"Q2\"}", el1[1]);

        // Case 2: Model wrote ']]' instead of ']}' at the end of the first element
        var mistypedBracket = "[{\"prompt\": \"Q1\", \"options\": [\"A\", \"B\"]]], {\"prompt\": \"Q2\"}]";
        var el2 = JsonRepairUtilities.ExtractBalancedJsonElements(mistypedBracket, '{');
        Assert.Equal(2, el2.Count);
        Assert.Equal("{\"prompt\": \"Q1\", \"options\": [\"A\", \"B\"]}", el2[0]);
        Assert.Equal("{\"prompt\": \"Q2\"}", el2[1]);

        // Case 3: Model omitted ']' before '}' on inner array of first element (wrote '["A", "B"}' instead of '["A", "B"]}')
        var missingInnerBracket = "[{\"prompt\": \"Q1\", \"options\": [\"A\", \"B\"}, {\"prompt\": \"Q2\"}]";
        var el3 = JsonRepairUtilities.ExtractBalancedJsonElements(missingInnerBracket, '{');
        Assert.Equal(2, el3.Count);
        Assert.Equal("{\"prompt\": \"Q1\", \"options\": [\"A\", \"B\"]}", el3[0]);
        Assert.Equal("{\"prompt\": \"Q2\"}", el3[1]);
    }

    [Fact]
    public void ExtractBalancedJsonElements_BracketlessCommaSeparatedArrays_ExtractsAllArrays()
    {
        var input = "[1, 2], [3, 4]";
        var elements = JsonRepairUtilities.ExtractBalancedJsonElements(input, '[');

        Assert.Equal(2, elements.Count);
        Assert.Equal("[1, 2]", elements[0]);
        Assert.Equal("[3, 4]", elements[1]);
    }

    [Fact]
    public void TryParseStringifiedJson_TrailingCommaInObjectOrArray_RepairsAndParses()
    {
        var parsedObj = JsonRepairUtilities.TryParseStringifiedJson("{\"a\": 1, }", out bool repairedObj);
        Assert.True(repairedObj);
        var dict = Assert.IsAssignableFrom<IDictionary<string, object?>>(parsedObj);
        Assert.Equal(1, dict["a"]);

        var parsedArr = JsonRepairUtilities.TryParseStringifiedJson("[1, 2, ]", out bool repairedArr);
        Assert.True(repairedArr);
        var list = Assert.IsAssignableFrom<IList<object?>>(parsedArr);
        Assert.Equal(new object?[] { 1, 2 }, list);
    }

    [Fact]
    public void HealthyJson_WithTrickyDelimitersInsideStrings_IsNotModifiedOrMisclassifiedAsTruncated()
    {
        var healthyJson = "[{\"prompt\": \"Why did log show '}, \\\"options\\\": null}, {' and ']]' or '[\\\"A\\\", \\\"B\\\"}' ?\", \"options\": [\"Keep {as-is}\", \"Fix [now]\"]}, {\"prompt\": \"public void Test() { if (x[0] == '}') return; }\"}]";

        // 1. TryRepairTruncatedJson must return null for already-valid JSON
        Assert.Null(JsonRepairUtilities.TryRepairTruncatedJson(healthyJson));

        // 2. TryParseStringifiedJson must parse cleanly with wasRepaired = false
        var parsed = JsonRepairUtilities.TryParseStringifiedJson(healthyJson, out bool wasRepaired);
        Assert.False(wasRepaired);
        var list = Assert.IsAssignableFrom<IList<object?>>(parsed);
        Assert.Equal(2, list.Count);

        // 3. ExtractBalancedJson must extract exact string with isTruncated = false
        var extracted = JsonRepairUtilities.ExtractBalancedJson(healthyJson, 0, out int consumed, out bool isTruncated);
        Assert.False(isTruncated);
        Assert.Equal(healthyJson.Length, consumed);
        Assert.Equal(healthyJson, extracted);

        // 4. ExtractBalancedJsonElements must extract both objects untouched
        var elements = JsonRepairUtilities.ExtractBalancedJsonElements(healthyJson, '{');
        Assert.Equal(2, elements.Count);
        Assert.Equal("{\"prompt\": \"Why did log show '}, \\\"options\\\": null}, {' and ']]' or '[\\\"A\\\", \\\"B\\\"}' ?\", \"options\": [\"Keep {as-is}\", \"Fix [now]\"]}", elements[0]);
        Assert.Equal("{\"prompt\": \"public void Test() { if (x[0] == '}') return; }\"}", elements[1]);
    }
}
