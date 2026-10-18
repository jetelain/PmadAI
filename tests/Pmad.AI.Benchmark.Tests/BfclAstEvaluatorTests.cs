using System.Text.Json;
using Microsoft.Extensions.AI;
using Pmad.AI.Benchmark.Scenarios;
using Pmad.AI.Benchmark.Scenarios.Bfcl;
using Xunit;

namespace Pmad.AI.Benchmark.Tests;

public class BfclAstEvaluatorTests
{
    private static TurnValidationContext CreateContext(IReadOnlyList<FunctionCallContent> toolCalls, string finalText = "")
    {
        return new TurnValidationContext(1, "test prompt", toolCalls, finalText, null, true);
    }

    [Fact]
    public void Evaluate_Turn11_PointArrayAsJsonElement_MatchesListGroundTruth()
    {
        // Turn 11: calculate_euclidean_distance with point_a: [3, 4] and point_b: [1, 2]
        var groundTruth = new List<BfclExpectedCall>
        {
            new("calculate_euclidean_distance", new Dictionary<string, object?>
            {
                ["point_a"] = new List<object?> { 3L, 4L },
                ["point_b"] = new List<object?> { 1L, 2L },
                ["rounding"] = 2L
            })
        };

        var entry = new BfclEntry("simple_11", BfclCategory.Simple, "Calculate distance", [], groundTruth, true, null, null);

        using var doc = JsonDocument.Parse("{\"point_a\": [3, 4], \"point_b\": [1, 2], \"rounding\": 2}");
        var actualArgs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            actualArgs[prop.Name] = prop.Value.Clone();
        }

        var ctx = CreateContext([new FunctionCallContent("call_1", "calculate_euclidean_distance", actualArgs)]);

        var result = BfclAstEvaluator.Evaluate(entry, ctx);
        Assert.True(result.IsSuccess, $"Validation failed: {result.Message}");
    }

    [Fact]
    public void Evaluate_Turn13_CoordinatesAsJsonElement_MatchesDoubleList()
    {
        // Turn 13: get_weather_by_coordinates_date with coordinates: [46.603354, 1.888334]
        var groundTruth = new List<BfclExpectedCall>
        {
            new("get_weather_by_coordinates_date", new Dictionary<string, object?>
            {
                ["coordinates"] = new List<object?> { 46.603354, 1.888334 },
                ["date"] = "2019-12-13"
            })
        };

        var entry = new BfclEntry("simple_13", BfclCategory.Simple, "Weather", [], groundTruth, true, null, null);

        using var doc = JsonDocument.Parse("{\"coordinates\": [46.603354, 1.888334], \"date\": \"2019-12-13\"}");
        var actualArgs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            actualArgs[prop.Name] = prop.Value.Clone();
        }

        var ctx = CreateContext([new FunctionCallContent("call_1", "get_weather_by_coordinates_date", actualArgs)]);

        var result = BfclAstEvaluator.Evaluate(entry, ctx);
        Assert.True(result.IsSuccess, $"Validation failed: {result.Message}");
    }

    [Fact]
    public void Evaluate_Turn16_GradesDictionaryAsJsonElement_MatchesDictionaryGroundTruth()
    {
        // Turn 16: calculate_grade_average with grades dictionary
        var groundTruth = new List<BfclExpectedCall>
        {
            new("calculate_grade_average", new Dictionary<string, object?>
            {
                ["grades"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["math"] = 90L,
                    ["science"] = 75L,
                    ["history"] = 82L,
                    ["music"] = 89L
                }
            })
        };

        var entry = new BfclEntry("simple_16", BfclCategory.Simple, "Grade average", [], groundTruth, true, null, null);

        using var doc = JsonDocument.Parse("{\"grades\": {\"math\": 90, \"science\": 75, \"history\": 82, \"music\": 89}}");
        var actualArgs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            actualArgs[prop.Name] = prop.Value.Clone();
        }

        var ctx = CreateContext([new FunctionCallContent("call_1", "calculate_grade_average", actualArgs)]);

        var result = BfclAstEvaluator.Evaluate(entry, ctx);
        Assert.True(result.IsSuccess, $"Validation failed: {result.Message}");
    }

    [Fact]
    public void Evaluate_Turn17_ColumnsArrayAsJsonElement_MatchesStringList()
    {
        // Turn 17: modify_database_columns with columns: ["email", "ssn"]
        var groundTruth = new List<BfclExpectedCall>
        {
            new("modify_database_columns", new Dictionary<string, object?>
            {
                ["db_name"] = "employees",
                ["table"] = "personal_data",
                ["operation"] = "delete",
                ["columns"] = new List<object?> { "email", "ssn" }
            })
        };

        var entry = new BfclEntry("simple_17", BfclCategory.Simple, "Modify DB", [], groundTruth, true, null, null);

        using var doc = JsonDocument.Parse("{\"db_name\": \"employees\", \"table\": \"personal_data\", \"operation\": \"delete\", \"columns\": [\"email\", \"ssn\"]}");
        var actualArgs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            actualArgs[prop.Name] = prop.Value.Clone();
        }

        var ctx = CreateContext([new FunctionCallContent("call_1", "modify_database_columns", actualArgs)]);

        var result = BfclAstEvaluator.Evaluate(entry, ctx);
        Assert.True(result.IsSuccess, $"Validation failed: {result.Message}");
    }

    [Fact]
    public void Evaluate_MathPowerEquivalent_RecognizesCaretAndDoubleAsterisk()
    {
        var groundTruth = new List<BfclExpectedCall>
        {
            new("calculate_integral", new Dictionary<string, object?>
            {
                ["function"] = "x^2",
                ["lower_limit"] = 1L,
                ["upper_limit"] = 5L
            })
        };

        var entry = new BfclEntry("par_9", BfclCategory.Parallel, "Integral", [], groundTruth, true, null, null);

        var actualArgs = new Dictionary<string, object?>
        {
            ["function"] = "x**2",
            ["lower_limit"] = 1,
            ["upper_limit"] = 5
        };

        var ctx = CreateContext([new FunctionCallContent("call_1", "calculate_integral", actualArgs)]);

        var result = BfclAstEvaluator.Evaluate(entry, ctx);
        Assert.True(result.IsSuccess, $"Validation failed: {result.Message}");
    }

    [Fact]
    public void Evaluate_MismatchedValues_DoesNotPrintInternalTypeName()
    {
        var groundTruth = new List<BfclExpectedCall>
        {
            new("test_func", new Dictionary<string, object?>
            {
                ["items"] = new List<object?> { 1L, 2L }
            })
        };

        var entry = new BfclEntry("mismatch_test", BfclCategory.Simple, "Test", [], groundTruth, true, null, null);

        using var doc = JsonDocument.Parse("{\"items\": [3, 4]}");
        var actualArgs = new Dictionary<string, object?>
        {
            ["items"] = doc.RootElement.GetProperty("items").Clone()
        };

        var ctx = CreateContext([new FunctionCallContent("call_1", "test_func", actualArgs)]);

        var result = BfclAstEvaluator.Evaluate(entry, ctx);
        Assert.False(result.IsSuccess);
        Assert.DoesNotContain("System.Collections.Generic.List", result.Message);
        Assert.Contains("[1,2]", result.Message);
        Assert.Contains("[3, 4]", result.Message);
    }

    [Fact]
    public void EvaluateStrict_NestedObjectAndArray_Succeeds()
    {
        var expectedArgs = new Dictionary<string, object?>
        {
            ["spec"] = new Dictionary<string, object?>
            {
                ["name"] = "api-gateway",
                ["replicas"] = 3L,
                ["resources"] = new Dictionary<string, object?>
                {
                    ["cpu_cores"] = 2.5,
                    ["memory_gib"] = 4.0
                },
                ["tags"] = new List<object?> { "prod", "critical" }
            }
        };

        using var doc = JsonDocument.Parse("{\"spec\": {\"name\": \"api-gateway\", \"replicas\": 3, \"resources\": {\"cpu_cores\": 2.5, \"memory_gib\": 4.0}, \"tags\": [\"prod\", \"critical\"]}}");
        var actualArgs = new Dictionary<string, object?>
        {
            ["spec"] = doc.RootElement.GetProperty("spec").Clone()
        };

        var ctx = CreateContext([new FunctionCallContent("call_1", "deploy_workload", actualArgs)]);

        var result = BfclAstEvaluator.EvaluateStrict("deploy_workload", expectedArgs, null, ctx);
        Assert.True(result.IsSuccess, $"Strict validation failed: {result.Message}");
    }

    [Theory]
    [InlineData("Battle of Stalingrad|Stalingrad", "Stalingrad")]
    [InlineData("Battle of Stalingrad", "The Battle of Stalingrad")]
    [InlineData("wild tiger", "wild tigers")]
    public void Evaluate_RelaxedStringVariants_AreRejected(string expected, string actual)
    {
        var groundTruth = new List<BfclExpectedCall>
        {
            new("inspect_target", new Dictionary<string, object?> { ["target"] = expected })
        };

        var entry = new BfclEntry("test_strict", BfclCategory.Simple, "query", [], groundTruth);
        var ctx = CreateContext([new FunctionCallContent("c1", "inspect_target", new Dictionary<string, object?> { ["target"] = actual })]);

        Assert.False(BfclAstEvaluator.Evaluate(entry, ctx).IsSuccess);
    }

    [Fact]
    public void Evaluate_ParallelCategory_DuplicateCalls_Fail()
    {
        var groundTruth = new List<BfclExpectedCall>
        {
            new("get_weather", new Dictionary<string, object?> { ["location"] = "Boston" }),
            new("get_weather", new Dictionary<string, object?> { ["location"] = "San Francisco" })
        };

        var entry = new BfclEntry("parallel_01", BfclCategory.Parallel, "weather query", [], groundTruth);
        var calls = new List<FunctionCallContent>
        {
            new("c1", "get_weather", new Dictionary<string, object?> { ["location"] = "Boston" }),
            new("c2", "get_weather", new Dictionary<string, object?> { ["location"] = "San Francisco" }),
            new("c3", "get_weather", new Dictionary<string, object?> { ["location"] = "Boston" })
        };

        Assert.False(BfclAstEvaluator.Evaluate(entry, CreateContext(calls)).IsSuccess);
    }
}
