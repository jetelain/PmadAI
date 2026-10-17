using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pmad.AI.Benchmark.Engine;

/// <summary>
/// Manages incremental persistence and recovery of benchmark execution progress.
/// Ensures multi-hour benchmark runs can resume gracefully if interrupted.
/// </summary>
public static class BenchmarkCheckpointManager
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Loads completed summaries from the checkpoint file if it exists.
    /// </summary>
    public static List<ModelBenchmarkSummary> LoadCheckpoint(string checkpointPath)
    {
        if (!File.Exists(checkpointPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(checkpointPath);
            var list = JsonSerializer.Deserialize<List<ModelBenchmarkSummary>>(json, _jsonOptions);
            return list ?? [];
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[Checkpoint] Warning: could not load checkpoint from '{checkpointPath}': {ex.Message}");
            Console.ResetColor();
            return [];
        }
    }

    /// <summary>
    /// Appends or updates the completed summaries in the checkpoint file.
    /// </summary>
    public static async Task SaveCheckpointAsync(string checkpointPath, IReadOnlyList<ModelBenchmarkSummary> summaries)
    {
        try
        {
            var dir = Path.GetDirectoryName(checkpointPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(summaries, _jsonOptions);
            await File.WriteAllTextAsync(checkpointPath, json);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[Checkpoint] Warning: could not save checkpoint to '{checkpointPath}': {ex.Message}");
            Console.ResetColor();
        }
    }

    /// <summary>
    /// Generates a unique checkpoint lookup key for a plan.
    /// </summary>
    public static string GetPlanKey(string targetName, string backend, int gpuLayers, bool? reasoning, string configLabel)
    {
        return $"{targetName}_{backend}_{gpuLayers}_{reasoning?.ToString() ?? "default"}_{configLabel}".ToLowerInvariant();
    }
}
