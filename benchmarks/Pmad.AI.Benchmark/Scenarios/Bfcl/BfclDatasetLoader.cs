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

        return new BfclEntry(id, defaultCategory, question, tools, groundTruth);
    }

    private static object? ConvertJsonValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => el.Clone()
    };
}
