using System.Text.Json;
using Pmad.AI.Tools;

namespace Pmad.AI.Tests;

/// <summary>
/// Unit tests for <see cref="SchemaHelpers"/>: JSON-schema property extraction, schema
/// comparison, type matching, <c>$ref</c>/<c>anyOf</c>/<c>oneOf</c> resolution, enum/const
/// member extraction, token/affix containment, and deterministic enum-value snapping.
/// </summary>
public class SchemaHelpersTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    #region ExtractSchemaProperties

    [Fact]
    public void ExtractSchemaProperties_ReturnsTopLevelPropertyNames()
    {
        var schema = Parse("{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"depth\":{\"type\":\"integer\"}}}");

        var props = SchemaHelpers.ExtractSchemaProperties(schema);

        Assert.NotNull(props);
        Assert.Contains("path", props!);
        Assert.Contains("depth", props);
        Assert.Equal(2, props.Count);
    }

    [Fact]
    public void ExtractSchemaProperties_ReturnsNull_WhenNoPropertiesBag()
    {
        Assert.Null(SchemaHelpers.ExtractSchemaProperties(Parse("{\"type\":\"string\"}")));
        Assert.Null(SchemaHelpers.ExtractSchemaProperties(Parse("{\"type\":\"object\"}")));
        Assert.Null(SchemaHelpers.ExtractSchemaProperties(Parse("null")));
    }

    [Fact]
    public void ExtractSchemaProperties_IsCaseInsensitive()
    {
        var schema = Parse("{\"properties\":{\"MyProp\":{\"type\":\"string\"}}}");

        var props = SchemaHelpers.ExtractSchemaProperties(schema);

        Assert.Contains("myprop", props!);
    }

    #endregion

    #region AreSchemasEqual

    [Fact]
    public void AreSchemasEqual_OrdinaryEqual_SameRawText()
    {
        var a = Parse("{\"type\":\"string\",\"enum\":[\"a\",\"b\"]}");
        var b = Parse("{\"type\":\"string\",\"enum\":[\"a\",\"b\"]}");

        Assert.True(SchemaHelpers.AreSchemasEqual(a, b));
    }

    [Fact]
    public void AreSchemasEqual_DifferentContent_ReturnsFalse()
    {
        var a = Parse("{\"type\":\"string\"}");
        var b = Parse("{\"type\":\"integer\"}");

        Assert.False(SchemaHelpers.AreSchemasEqual(a, b));
    }

    [Fact]
    public void AreSchemasEqual_SameKindNullOrUndefined_AreEqual()
    {
        var nullEl = Parse("null");

        // Same ValueKind (both Undefined, or both Null) short-circuits to true.
        Assert.True(SchemaHelpers.AreSchemasEqual(default, default));
        Assert.True(SchemaHelpers.AreSchemasEqual(nullEl, nullEl));
    }

    [Fact]
    public void AreSchemasEqual_UndefinedVsNull_DifferInKind()
    {
        var nullEl = Parse("null");

        // Undefined and Null are distinct ValueKinds, so the equality check is false.
        Assert.False(SchemaHelpers.AreSchemasEqual(default, nullEl));
    }

    [Fact]
    public void AreSchemasEqual_DifferentValueKind_ReturnsFalse()
    {
        Assert.False(SchemaHelpers.AreSchemasEqual(Parse("1"), Parse("\"1\"")));
    }

    #endregion

    #region SchemaTypeMatches

    [Fact]
    public void SchemaTypeMatches_SingleStringType()
    {
        Assert.True(SchemaHelpers.SchemaTypeMatches(Parse("{\"type\":\"boolean\"}"), "boolean"));
        Assert.False(SchemaHelpers.SchemaTypeMatches(Parse("{\"type\":\"boolean\"}"), "string"));
    }

    [Fact]
    public void SchemaTypeMatches_TypeArray()
    {
        var schema = Parse("{\"type\":[\"string\",\"null\"]}");

        Assert.True(SchemaHelpers.SchemaTypeMatches(schema, "string"));
        Assert.True(SchemaHelpers.SchemaTypeMatches(schema, "null"));
        Assert.False(SchemaHelpers.SchemaTypeMatches(schema, "array"));
    }

    [Fact]
    public void SchemaTypeMatches_NoTypeProperty_ReturnsFalse()
    {
        Assert.False(SchemaHelpers.SchemaTypeMatches(Parse("{\"properties\":{}}"), "object"));
        Assert.False(SchemaHelpers.SchemaTypeMatches(Parse("null"), "object"));
    }

    #endregion

    #region ResolveSchema

    [Fact]
    public void ResolveSchema_FollowsRefPointer()
    {
        var root = Parse("{\"definitions\":{\"address\":{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"}}}},\"type\":\"object\",\"properties\":{\"loc\":{\"$ref\":\"#/definitions/address\"}}}");
        var refEl = Parse("{\"$ref\":\"#/definitions/address\"}");

        var resolved = SchemaHelpers.ResolveSchema(refEl, root);

        Assert.True(resolved.TryGetProperty("properties", out _));
        Assert.True(resolved.GetProperty("properties").TryGetProperty("city", out _));
    }

    [Fact]
    public void ResolveSchema_UnresolvableRef_ReturnsOriginal()
    {
        var root = Parse("{\"type\":\"object\"}");
        var refEl = Parse("{\"$ref\":\"#/missing/thing\"}");

        var resolved = SchemaHelpers.ResolveSchema(refEl, root);

        Assert.True(resolved.TryGetProperty("$ref", out _));
    }

    [Fact]
    public void ResolveSchema_SkipsNullInAnyOf()
    {
        var root = Parse("{\"type\":\"object\"}");
        var schema = Parse("{\"anyOf\":[{\"type\":\"null\"},{\"type\":\"string\"}]}");

        var resolved = SchemaHelpers.ResolveSchema(schema, root);

        Assert.True(SchemaHelpers.SchemaTypeMatches(resolved, "string"));
    }

    [Fact]
    public void ResolveSchema_SkipsNullInOneOf()
    {
        var root = Parse("{\"type\":\"object\"}");
        var schema = Parse("{\"oneOf\":[{\"type\":\"string\"},{\"type\":\"null\"}]}");

        var resolved = SchemaHelpers.ResolveSchema(schema, root);

        Assert.True(SchemaHelpers.SchemaTypeMatches(resolved, "string"));
    }

    [Fact]
    public void ResolveSchema_NonObjectOrDepthExceeded_ReturnsUnchanged()
    {
        var root = Parse("{\"type\":\"object\"}");
        var str = Parse("\"literal\"");

        Assert.Equal(JsonValueKind.String, SchemaHelpers.ResolveSchema(str, root).ValueKind);
    }

    #endregion

    #region TryGetEnumOrConstMembers

    [Fact]
    public void TryGetEnumOrConstMembers_FromEnum()
    {
        var schema = Parse("{\"enum\":[\"Active\",\"Inactive\",\"Pending\"]}");

        var ok = SchemaHelpers.TryGetEnumOrConstMembers(schema, out var candidates);

        Assert.True(ok);
        Assert.Equal(new[] { "Active", "Inactive", "Pending" }, candidates.ToArray());
    }

    [Fact]
    public void TryGetEnumOrConstMembers_FromConst()
    {
        var schema = Parse("{\"const\":\"prod\"}");

        Assert.True(SchemaHelpers.TryGetEnumOrConstMembers(schema, out var candidates));
        Assert.Equal(new[] { "prod" }, candidates.ToArray());
    }

    [Fact]
    public void TryGetEnumOrConstMembers_FromOneOfConst()
    {
        var schema = Parse("{\"oneOf\":[{\"const\":\"A\"},{\"const\":\"B\"},{\"type\":\"null\"}]}");

        Assert.True(SchemaHelpers.TryGetEnumOrConstMembers(schema, out var candidates));
        Assert.Equal(new[] { "A", "B" }, candidates.ToArray());
    }

    [Fact]
    public void TryGetEnumOrConstMembers_FromAnyOfEnum()
    {
        var schema = Parse("{\"anyOf\":[{\"enum\":[\"x\",\"y\"]},{\"type\":\"null\"}]}");

        Assert.True(SchemaHelpers.TryGetEnumOrConstMembers(schema, out var candidates));
        Assert.Equal(new[] { "x", "y" }, candidates.ToArray());
    }

    [Fact]
    public void TryGetEnumOrConstMembers_NoMembers_ReturnsFalse()
    {
        Assert.False(SchemaHelpers.TryGetEnumOrConstMembers(Parse("{\"type\":\"string\"}"), out var none));
        Assert.Empty(none);
        Assert.False(SchemaHelpers.TryGetEnumOrConstMembers(Parse("null"), out _));
    }

    #endregion

    #region ContainsTokenOrAffix

    [Theory]
    [InlineData("Battle of Stalingrad", "Stalingrad", true)]   // substring bounded by spaces
    [InlineData("wild_tiger", "tiger", true)]                  // suffix bounded by separator
    [InlineData("turn-on", "on", true)]                        // suffix bounded by separator
    [InlineData("connection", "on", false)]                    // raw substring, not bounded
    [InlineData("admin", "in", false)]                         // substring inside a word
    [InlineData("streetcar", "car", false)]                    // substring not bounded
    public void ContainsTokenOrAffix_BoundedTokenMatching(string container, string part, bool expected)
    {
        Assert.Equal(expected, SchemaHelpers.ContainsTokenOrAffix(container, part));
    }

    [Fact]
    public void ContainsTokenOrAffix_ShortOrEmpty_ReturnsFalse()
    {
        Assert.False(SchemaHelpers.ContainsTokenOrAffix("abc", ""));
        Assert.False(SchemaHelpers.ContainsTokenOrAffix("", "abc"));
        Assert.False(SchemaHelpers.ContainsTokenOrAffix("abc", "a")); // part shorter than 2
    }

    #endregion

    #region SnapEnumValueCore

    [Fact]
    public void SnapEnumValue_ExactMatch_ReturnsUnchanged_NoTelemetry()
    {
        var telemetry = new LenientDispatchTelemetry();

        var result = SchemaHelpers.SnapEnumValueCore("Production", ["Development", "Staging", "Production"], telemetry);

        Assert.Equal("Production", result);
        Assert.Empty(telemetry.Events);
    }

    [Fact]
    public void SnapEnumValue_CaseInsensitive_SnapsToUniqueCandidate()
    {
        var telemetry = new LenientDispatchTelemetry();

        var result = SchemaHelpers.SnapEnumValueCore("production", ["Development", "Staging", "Production"], telemetry);

        Assert.Equal("Production", result);
        Assert.Contains(telemetry.Events, e => e.Type == LenientDispatchEventType.EnumValueSnapped);
    }

    [Fact]
    public void SnapEnumValue_CaseInsensitiveTie_LeavesUntouched()
    {
        var telemetry = new LenientDispatchTelemetry();

        var result = SchemaHelpers.SnapEnumValueCore("Foo", ["foo", "FOO", "Bar"], telemetry);

        Assert.Equal("Foo", result);
        Assert.Contains(telemetry.Events, e => e.Type == LenientDispatchEventType.EnumValueUnmatched);
    }

    [Fact]
    public void SnapEnumValue_Normalization_CollapsesSeparatorsAndWhitespace()
    {
        IReadOnlyList<string> candidates = ["in_progress"];

        Assert.Equal("in_progress", SchemaHelpers.SnapEnumValueCore("  in-progress  ", candidates));
        Assert.Equal("in_progress", SchemaHelpers.SnapEnumValueCore("in progress", candidates));
        Assert.Equal("in_progress", SchemaHelpers.SnapEnumValueCore("inProgress", candidates));
        Assert.Equal("in_progress", SchemaHelpers.SnapEnumValueCore("in___progress", candidates));
    }

    [Fact]
    public void SnapEnumValue_NaivePluralStripping()
    {
        Assert.Equal("wild_tiger", SchemaHelpers.SnapEnumValueCore("wild tigers", ["wild_tiger"]));
        Assert.Equal("wild_tigers", SchemaHelpers.SnapEnumValueCore("wild tiger", ["wild_tigers"]));
    }

    [Fact]
    public void SnapEnumValue_UniqueSubstringContainment()
    {
        IReadOnlyList<string> candidates = ["Battle of Stalingrad", "Battle of Kursk"];

        Assert.Equal("Battle of Stalingrad", SchemaHelpers.SnapEnumValueCore("Stalingrad", candidates));
    }

    [Fact]
    public void SnapEnumValue_BoundedEditDistance_SnapsWithinBound()
    {
        IReadOnlyList<string> candidates = ["pending", "approved"];

        Assert.Equal("pending", SchemaHelpers.SnapEnumValueCore("pedning", candidates));
        Assert.Equal("pending", SchemaHelpers.SnapEnumValueCore("pendng", candidates));
    }

    [Fact]
    public void SnapEnumValue_BoundedEditDistance_RejectsWhenTooFar()
    {
        var result = SchemaHelpers.SnapEnumValueCore("xyzw", new[] { "abc" });

        Assert.Equal("xyzw", result);
    }

    [Fact]
    public void SnapEnumValue_PreventsFalseSubstringSnappingOnShortWords()
    {
        Assert.Equal("connection", SchemaHelpers.SnapEnumValueCore("connection", ["on", "off"]));
        Assert.Equal("admin", SchemaHelpers.SnapEnumValueCore("admin", ["in", "out"]));
        Assert.Equal("streetcar", SchemaHelpers.SnapEnumValueCore("streetcar", ["car", "bus"]));
    }

    [Fact]
    public void SnapEnumValue_EmptyCandidates_ReturnsUnchanged()
    {
        Assert.Equal("anything", SchemaHelpers.SnapEnumValueCore("anything", []));
    }

    #endregion
}
