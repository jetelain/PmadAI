using System.Text.Json;

namespace Pmad.AI.Benchmark.Scenarios.Compaction;

/// <summary>
/// Loads and manages compaction benchmark trajectory fixtures.
/// Automatically creates deterministic on-disk fixture JSON files if missing.
/// </summary>
public static class CompactionTrajectoryLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string FindFixturesDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "compaction"),
            Path.Combine(Directory.GetCurrentDirectory(), "Fixtures", "compaction"),
            Path.Combine(Directory.GetCurrentDirectory(), "benchmarks", "Pmad.AI.Benchmark", "Fixtures", "compaction"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "compaction"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "benchmarks", "Pmad.AI.Benchmark", "Fixtures", "compaction")
        };

        foreach (var dir in candidates)
        {
            var fullPath = Path.GetFullPath(dir);
            if (Directory.Exists(fullPath))
            {
                return fullPath;
            }
        }

        // Default to creating under current directory or AppContext.BaseDirectory
        var primaryCandidate = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "benchmarks", "Pmad.AI.Benchmark", "Fixtures", "compaction"));
        if (!Directory.Exists(primaryCandidate))
        {
            try
            {
                Directory.CreateDirectory(primaryCandidate);
                return primaryCandidate;
            }
            catch
            {
                // Fall back to base directory
            }
        }

        var fallbackCandidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Fixtures", "compaction"));
        Directory.CreateDirectory(fallbackCandidate);
        return fallbackCandidate;
    }

    public static CompactionTrajectory LoadLargeToolTrajectory()
    {
        var dir = FindFixturesDirectory();
        var filePath = Path.Combine(dir, "compaction_large_tool.json");

        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var traj = JsonSerializer.Deserialize<CompactionTrajectory>(json, JsonOptions);
                if (traj != null && traj.Turns.Count > 0)
                {
                    return traj;
                }
            }
            catch
            {
                // Re-create corrupt file
            }
        }

        var generated = CompactionFixtureGenerator.CreateLargeToolTrajectory();
        try
        {
            Directory.CreateDirectory(dir);
            var serialized = JsonSerializer.Serialize(generated, JsonOptions);
            File.WriteAllText(filePath, serialized);
        }
        catch
        {
            // Ignore write failures in read-only environments
        }

        return generated;
    }

    public static CompactionTrajectory LoadDeepContextTrajectory()
    {
        var dir = FindFixturesDirectory();
        var filePath = Path.Combine(dir, "compaction_deep_context.json");

        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var traj = JsonSerializer.Deserialize<CompactionTrajectory>(json, JsonOptions);
                if (traj != null && traj.Turns.Count > 0)
                {
                    return traj;
                }
            }
            catch
            {
                // Re-create corrupt file
            }
        }

        var generated = CompactionFixtureGenerator.CreateDeepContextTrajectory();
        try
        {
            Directory.CreateDirectory(dir);
            var serialized = JsonSerializer.Serialize(generated, JsonOptions);
            File.WriteAllText(filePath, serialized);
        }
        catch
        {
            // Ignore write failures in read-only environments
        }

        return generated;
    }
}
