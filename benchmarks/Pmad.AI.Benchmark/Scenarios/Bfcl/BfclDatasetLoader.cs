using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Benchmark.Scenarios.Bfcl;

/// <summary>
/// Loads and parses BFCL dataset files from the fixtures directory.
/// </summary>
public static class BfclDatasetLoader
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Locates the base fixtures/bfcl directory across runtime execution and development source paths.
    /// </summary>
    public static string ResolveFixturesDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "bfcl"),
            Path.Combine(Directory.GetCurrentDirectory(), "benchmarks", "Pmad.AI.Benchmark", "Fixtures", "bfcl"),
            Path.Combine(Directory.GetCurrentDirectory(), "Fixtures", "bfcl")
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        // Fallback to base directory
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", "bfcl");
    }

    /// <summary>
    /// Loads BFCL entries for a given category (or all categories).
    /// </summary>
    public static List<BfclEntry> LoadEntries(string? categoryFilter = null)
    {
        var fixturesDir = ResolveFixturesDirectory();
        var filesToLoad = new List<(string FilePath, BfclCategory Category)>();

        var catLower = categoryFilter?.ToLowerInvariant() ?? "all";

        if (catLower is "all" or "bfcl" or "simple" or "bfcl-simple")
        {
            filesToLoad.Add((Path.Combine(fixturesDir, "simple.jsonl"), BfclCategory.Simple));
        }

        if (catLower is "all" or "bfcl" or "multiple" or "bfcl-multiple")
        {
            filesToLoad.Add((Path.Combine(fixturesDir, "multiple.jsonl"), BfclCategory.Multiple));
        }

        if (catLower is "all" or "bfcl" or "parallel" or "bfcl-parallel")
        {
            filesToLoad.Add((Path.Combine(fixturesDir, "parallel.jsonl"), BfclCategory.Parallel));
        }

        if (catLower is "all" or "bfcl" or "irrelevance" or "bfcl-irrelevance")
        {
            filesToLoad.Add((Path.Combine(fixturesDir, "irrelevance.jsonl"), BfclCategory.Irrelevance));
        }

        if (catLower is "all" or "bfcl" or "multiturn" or "bfcl-multiturn")
        {
            filesToLoad.Add((Path.Combine(fixturesDir, "multiturn.jsonl"), BfclCategory.MultiTurn));
        }

        if (catLower is "all" or "bfcl" or "strict" or "bfcl-strict")
        {
            filesToLoad.Add((Path.Combine(fixturesDir, "strict.jsonl"), BfclCategory.Strict));
        }

        var results = new List<BfclEntry>();

        foreach (var (filePath, category) in filesToLoad)
        {
            if (!File.Exists(filePath))
            {
                continue;
            }

            foreach (var line in File.ReadLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var entry = ParseLine(line, category);
                if (entry != null)
                {
                    results.Add(entry);
                }
            }
        }

        return results;
    }

    private static BfclEntry? ParseLine(string line, BfclCategory defaultCategory)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        var id = root.GetProperty("id").GetString() ?? Guid.NewGuid().ToString();
        var question = root.GetProperty("question").GetString() ?? string.Empty;

        var category = defaultCategory;
        if (root.TryGetProperty("category", out var catProp) && catProp.ValueKind == JsonValueKind.String)
        {
            var catStr = catProp.GetString();
            if (Enum.TryParse<BfclCategory>(catStr, ignoreCase: true, out var parsedCategory))
            {
                category = parsedCategory;
            }
        }

        // Parse tools
        var tools = new List<AITool>();
        if (root.TryGetProperty("tools", out var toolsArray) && toolsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var toolElem in toolsArray.EnumerateArray())
            {
                var name = toolElem.GetProperty("name").GetString() ?? "unknown";
                var desc = toolElem.TryGetProperty("description", out var d) ? (d.GetString() ?? "") : "";

                JsonElement parametersSchema;
                if (toolElem.TryGetProperty("parameters", out var p) && p.ValueKind == JsonValueKind.Object)
                {
                    parametersSchema = p.Clone();
                }
                else
                {
                    // Empty object schema default
                    using var emptyDoc = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
                    parametersSchema = emptyDoc.RootElement.Clone();
                }

                tools.Add(new DynamicJsonAIFunction(name, desc, parametersSchema));
            }
        }

        // Parse ground truth
        var groundTruth = new List<BfclExpectedCall>();
        if (root.TryGetProperty("ground_truth", out var gtArray) && gtArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var gtElem in gtArray.EnumerateArray())
            {
                var name = gtElem.GetProperty("name").GetString() ?? "";
                var argsDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                if (gtElem.TryGetProperty("arguments", out var argsElem) && argsElem.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in argsElem.EnumerateObject())
                    {
                        argsDict[prop.Name] = ConvertJsonValue(prop.Value);
                    }
                }

                groundTruth.Add(new BfclExpectedCall(name, argsDict));
            }
        }

        bool clearHistory = true;
        if (root.TryGetProperty("clear_history", out var chElem) &&
            (chElem.ValueKind == JsonValueKind.True || chElem.ValueKind == JsonValueKind.False))
        {
            clearHistory = chElem.GetBoolean();
        }

        string? toolResult = null;
        if (root.TryGetProperty("tool_result", out var trElem))
        {
            toolResult = trElem.ValueKind == JsonValueKind.String
                ? trElem.GetString()
                : trElem.GetRawText();
        }

        List<string>? expectedKeywords = null;
        if (root.TryGetProperty("expected_text", out var etElem) && etElem.ValueKind == JsonValueKind.Array)
        {
            expectedKeywords = etElem.EnumerateArray()
                .Select(x => x.GetString())
                .Where(x => !string.IsNullOrEmpty(x))
                .Select(x => x!)
                .ToList();
        }

        return new BfclEntry(id, category, question, tools, groundTruth, clearHistory, toolResult, expectedKeywords);
    }

    public static object? ConvertJsonValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? (object)l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => ConvertJsonObject(el),
        JsonValueKind.Array => el.EnumerateArray().Select(ConvertJsonValue).ToList(),
        _ => el.Clone()
    };

    private static Dictionary<string, object?> ConvertJsonObject(JsonElement el)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in el.EnumerateObject())
        {
            dict[prop.Name] = ConvertJsonValue(prop.Value);
        }
        return dict;
    }
}
