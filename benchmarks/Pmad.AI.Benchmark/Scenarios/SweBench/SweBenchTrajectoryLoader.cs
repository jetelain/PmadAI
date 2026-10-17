using System.Text.Json;

namespace Pmad.AI.Benchmark.Scenarios.SweBench;

/// <summary>
/// Loads and validates SWE-bench trajectory JSON fixtures.
/// </summary>
public static class SweBenchTrajectoryLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Locates the directory containing SWE-bench JSON fixtures.
    /// </summary>
    public static string FindFixturesDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "swebench"),
            Path.Combine(Directory.GetCurrentDirectory(), "Fixtures", "swebench"),
            Path.Combine(Directory.GetCurrentDirectory(), "benchmarks", "Pmad.AI.Benchmark", "Fixtures", "swebench"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "swebench"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "benchmarks", "Pmad.AI.Benchmark", "Fixtures", "swebench")
        };

        foreach (var dir in candidates)
        {
            var fullPath = Path.GetFullPath(dir);
            if (Directory.Exists(fullPath) && Directory.EnumerateFiles(fullPath, "*.json").Any())
            {
                return fullPath;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find SWE-bench fixtures directory. Checked:\n{string.Join("\n", candidates.Select(Path.GetFullPath))}");
    }

    /// <summary>
    /// Loads a single SWE-bench trajectory from the specified file path.
    /// </summary>
    public static SweBenchTrajectory LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"SWE-bench trajectory file not found: {filePath}", filePath);
        }

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<SweBenchTrajectory>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize SWE-bench trajectory from: {filePath}");
    }

    /// <summary>
    /// Loads all available SWE-bench archetype trajectories.
    /// </summary>
    public static IReadOnlyList<SweBenchTrajectory> LoadAllArchetypes()
    {
        var dir = FindFixturesDirectory();
        var files = Directory.GetFiles(dir, "archetype_*.json");
        var list = new List<SweBenchTrajectory>();

        foreach (var file in files.OrderBy(f => f))
        {
            list.Add(LoadFromFile(file));
        }

        return list;
    }

    /// <summary>
    /// Loads a trajectory matching a specific archetype substring (e.g. "diff", "logs", "loop", "deep", "succinct").
    /// </summary>
    public static SweBenchTrajectory? LoadByArchetype(string archetypeFilter)
    {
        var all = LoadAllArchetypes();
        return all.FirstOrDefault(t =>
            t.Archetype.Contains(archetypeFilter, StringComparison.OrdinalIgnoreCase) ||
            t.InstanceId.Contains(archetypeFilter, StringComparison.OrdinalIgnoreCase));
    }
}
