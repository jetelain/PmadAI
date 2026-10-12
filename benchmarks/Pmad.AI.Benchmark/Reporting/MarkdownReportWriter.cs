using System.Text;
using System.Text.Json;
using Pmad.AI.Benchmark.Engine;

namespace Pmad.AI.Benchmark.Reporting;

/// <summary>
/// Writes benchmark results to a formatted Markdown report.
/// </summary>
public static class MarkdownReportWriter
{
    public static string GenerateMarkdown(IReadOnlyList<ModelBenchmarkSummary> summaries)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Pmad.AI Tool Calling Benchmark Report");
        sb.AppendLine();
        sb.AppendLine($"Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine("## Summary Comparison");
        sb.AppendLine();
        sb.AppendLine("| Model | Scenarios Passed | Pass Rate | Tool Calls | Gen Tokens | Avg Speed (tok/s) | Peak RAM | Total Time |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");

        foreach (var m in summaries)
        {
            sb.AppendLine($"| `{m.ModelName}` | {m.ScenariosPassed}/{m.ScenariosRun} | {m.PassRatePercentage:F0}% | {m.TotalToolCalls} | {m.TotalOutputTokens} | {m.AverageGenerationTokensPerSecond:F1} | {BenchmarkRunner.FormatMemory(m.PeakMemoryMb)} | {m.TotalTimeMs / 1000.0:F2}s |");
        }

        sb.AppendLine();
        sb.AppendLine("## Detailed Scenario Breakdown");
        sb.AppendLine();

        foreach (var m in summaries)
        {
            sb.AppendLine($"### Model: `{m.ModelName}`");
            sb.AppendLine();
            sb.AppendLine("| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | Peak RAM | Duration |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");

            foreach (var sc in m.ScenarioResults)
            {
                var statusBadge = sc.Status switch
                {
                    BenchmarkStatus.Passed => "✅ PASS",
                    BenchmarkStatus.Failed => "❌ FAIL",
                    _ => "⚠️ PARTIAL"
                };

                sb.AppendLine($"| **{sc.ScenarioName}** | {statusBadge} | {sc.TotalToolCalls} | {sc.ToolAccuracyPercentage:F0}% | {sc.TotalPromptTokens} | {sc.TotalOutputTokens} | {sc.GenerationTokensPerSecond:F1} tok/s | {BenchmarkRunner.FormatMemory(sc.PeakMemoryMb)} | {sc.TotalDurationMs:F0} ms |");
            }

            sb.AppendLine();

            foreach (var sc in m.ScenarioResults)
            {
                if (sc.Status == BenchmarkStatus.Failed || !string.IsNullOrWhiteSpace(sc.ErrorMessage))
                {
                    sb.AppendLine($"> [!WARNING]");
                    sb.AppendLine($"> **Failure Details for {sc.ScenarioName}:** {sc.ErrorMessage}");
                    foreach (var turn in sc.TurnResults.Where(t => t.Status == BenchmarkStatus.Failed))
                    {
                        sb.AppendLine($"> - Turn {turn.TurnIndex}: {turn.ValidationMessage}");
                    }
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString();
    }

    public static async Task WriteToFileAsync(string filePath, IReadOnlyList<ModelBenchmarkSummary> summaries)
    {
        var md = GenerateMarkdown(summaries);
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        await File.WriteAllTextAsync(filePath, md);
    }
}

/// <summary>
/// Writes benchmark metrics as JSON.
/// </summary>
public static class JsonReportWriter
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public static async Task WriteToFileAsync(string filePath, IReadOnlyList<ModelBenchmarkSummary> summaries)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        var json = JsonSerializer.Serialize(summaries, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }
}
