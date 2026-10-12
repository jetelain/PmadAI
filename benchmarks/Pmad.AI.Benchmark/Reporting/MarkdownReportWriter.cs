using System.Text;
using System.Text.Json;
using Pmad.AI.Benchmark.Engine;

namespace Pmad.AI.Benchmark.Reporting;

/// <summary>
/// Writes benchmark results to a formatted Markdown report.
/// </summary>
public static class MarkdownReportWriter
{
    public static string GenerateMarkdown(
        IReadOnlyList<ModelBenchmarkSummary> summaries,
        SystemHardwareInfo? hardware = null,
        string? transcriptFileName = null)
    {
        var hw = hardware ?? SystemHardwareInfo.Current;
        var sb = new StringBuilder();

        sb.AppendLine("# Pmad.AI Tool Calling Benchmark Report");
        sb.AppendLine();
        sb.AppendLine($"Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine("## Environment");
        sb.AppendLine();
        sb.AppendLine($"- **CPU:** {hw.CpuSummary}");
        sb.AppendLine($"- **GPU:** {hw.GpuSummary}");
        sb.AppendLine($"- **OS:** {hw.OsDescription}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(transcriptFileName))
        {
            sb.AppendLine($"> [!NOTE]");
            sb.AppendLine($"> 📄 **Execution Transcript & Failure Analysis:** Detailed turn-by-turn prompts, reasoning traces, tool inputs/outputs, and failure analysis are recorded in [{transcriptFileName}]({transcriptFileName}).");
            sb.AppendLine();
        }

        sb.AppendLine("## Summary Comparison");
        sb.AppendLine();
        sb.AppendLine("| Model | Backend | Reasoning | Scenarios Passed | Pass Rate | Tool Calls | Gen Tokens | Avg Speed (tok/s) | Sys RAM | GPU VRAM | Total Time |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");

        foreach (var m in summaries)
        {
            var backendStr = m.Backend ?? (m.GpuLayers > 0 ? "Vulkan" : "CPU");
            var reasStr = m.ReasoningEnabled.HasValue ? (m.ReasoningEnabled.Value ? "Enabled" : "Disabled") : "Default";
            var vramStr = m.PeakVramMb > 0 ? BenchmarkRunner.FormatMemory(m.PeakVramMb) : "-";
            sb.AppendLine($"| `{m.ModelName}` | {backendStr} | {reasStr} | {m.ScenariosPassed}/{m.ScenariosRun} | {m.PassRatePercentage:F0}% | {m.TotalToolCalls} | {m.TotalOutputTokens} | {m.AverageGenerationTokensPerSecond:F1} | {BenchmarkRunner.FormatMemory(m.PeakMemoryMb)} | {vramStr} | {m.TotalTimeMs / 1000.0:F2}s |");
        }

        // Hardware acceleration comparison (CPU vs Vulkan)
        var cpuSummaries = summaries.Where(s => string.Equals(s.Backend, "CPU", StringComparison.OrdinalIgnoreCase)).ToList();
        var vulkanSummaries = summaries.Where(s => string.Equals(s.Backend, "Vulkan", StringComparison.OrdinalIgnoreCase)).ToList();

        if (cpuSummaries.Count > 0 && vulkanSummaries.Count > 0)
        {
            var pairs = new List<(ModelBenchmarkSummary Cpu, ModelBenchmarkSummary Vulkan)>();
            foreach (var cpu in cpuSummaries)
            {
                var match = vulkanSummaries.FirstOrDefault(v =>
                    v.ModelId == cpu.ModelId &&
                    v.ReasoningEnabled == cpu.ReasoningEnabled);

                if (match != null)
                {
                    pairs.Add((cpu, match));
                }
            }

            if (pairs.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Hardware Acceleration Comparison (CPU vs Vulkan GPU)");
                sb.AppendLine();
                sb.AppendLine("| Model | Reasoning | CPU Speed (tok/s) | Vulkan Speed (tok/s) | Vulkan Speedup | CPU RAM | Vulkan VRAM | Duration (CPU -> Vulkan) |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|");

                foreach (var (cpu, vulkan) in pairs)
                {
                    var speedup = cpu.AverageGenerationTokensPerSecond > 0
                        ? (vulkan.AverageGenerationTokensPerSecond / cpu.AverageGenerationTokensPerSecond)
                        : 0.0;
                    var speedupStr = speedup > 0 ? $"**{speedup:F1}x**" : "N/A";
                    var reasStr = cpu.ReasoningEnabled.HasValue ? (cpu.ReasoningEnabled.Value ? "Enabled" : "Disabled") : "Default";
                    var timeCompStr = $"{cpu.TotalTimeMs / 1000.0:F1}s -> {vulkan.TotalTimeMs / 1000.0:F1}s";
                    var vulkanMemStr = vulkan.PeakVramMb > 0
                        ? BenchmarkRunner.FormatMemory(vulkan.PeakVramMb)
                        : BenchmarkRunner.FormatMemory(vulkan.PeakMemoryMb);

                    sb.AppendLine($"| `{cpu.ModelName}` | {reasStr} | {cpu.AverageGenerationTokensPerSecond:F1} | {vulkan.AverageGenerationTokensPerSecond:F1} | {speedupStr} | {BenchmarkRunner.FormatMemory(cpu.PeakMemoryMb)} | {vulkanMemStr} | {timeCompStr} |");
                }
            }
        }

        // Reasoning impact comparison (Without vs With reasoning)
        var withoutReasoning = summaries.Where(s => s.ReasoningEnabled == false).ToList();
        var withReasoning = summaries.Where(s => s.ReasoningEnabled == true).ToList();

        if (withoutReasoning.Count > 0 && withReasoning.Count > 0)
        {
            var pairs = new List<(ModelBenchmarkSummary Without, ModelBenchmarkSummary With)>();
            foreach (var noReas in withoutReasoning)
            {
                var match = withReasoning.FirstOrDefault(w =>
                    w.ModelId == noReas.ModelId &&
                    string.Equals(w.Backend, noReas.Backend, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    pairs.Add((noReas, match));
                }
            }

            if (pairs.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Reasoning Impact Comparison (With vs Without Reasoning)");
                sb.AppendLine();
                sb.AppendLine("| Model | Backend | No-Reasoning Speed | Reasoning Speed | Speed Ratio | Generated Tokens (Off -> On) | Duration (Off -> On) |");
                sb.AppendLine("|---|---|---|---|---|---|---|");

                foreach (var (noReas, reas) in pairs)
                {
                    var speedRatio = noReas.AverageGenerationTokensPerSecond > 0
                        ? (reas.AverageGenerationTokensPerSecond / noReas.AverageGenerationTokensPerSecond)
                        : 0.0;
                    var speedRatioStr = speedRatio > 0 ? $"{speedRatio:F2}x" : "N/A";
                    var backendStr = noReas.Backend ?? "Default";
                    var tokDiff = reas.TotalOutputTokens - noReas.TotalOutputTokens;
                    var tokDiffPct = noReas.TotalOutputTokens > 0 ? (tokDiff * 100.0 / noReas.TotalOutputTokens) : 0.0;
                    var tokStr = $"{noReas.TotalOutputTokens} -> {reas.TotalOutputTokens} (+{tokDiffPct:F0}%)";
                    var timeDiffSec = (reas.TotalTimeMs - noReas.TotalTimeMs) / 1000.0;
                    var timeStr = $"{noReas.TotalTimeMs / 1000.0:F1}s -> {reas.TotalTimeMs / 1000.0:F1}s (+{timeDiffSec:F1}s)";

                    sb.AppendLine($"| `{noReas.ModelName}` | {backendStr} | {noReas.AverageGenerationTokensPerSecond:F1} tok/s | {reas.AverageGenerationTokensPerSecond:F1} tok/s | {speedRatioStr} | {tokStr} | {timeStr} |");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Detailed Scenario Breakdown");
        sb.AppendLine();

        foreach (var m in summaries)
        {
            sb.AppendLine($"### Model: `{m.DisplayName}`");
            sb.AppendLine();
            sb.AppendLine("| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");

            foreach (var sc in m.ScenarioResults)
            {
                var statusBadge = sc.Status switch
                {
                    BenchmarkStatus.Passed => "✅ PASS",
                    BenchmarkStatus.Failed => "❌ FAIL",
                    _ => "⚠️ PARTIAL"
                };

                var vramStr = sc.PeakVramMb > 0 ? BenchmarkRunner.FormatMemory(sc.PeakVramMb) : "-";

                sb.AppendLine($"| **{sc.ScenarioName}** | {statusBadge} | {sc.TotalToolCalls} | {sc.ToolAccuracyPercentage:F0}% | {sc.TotalPromptTokens} | {sc.TotalOutputTokens} | {sc.GenerationTokensPerSecond:F1} tok/s | {BenchmarkRunner.FormatMemory(sc.PeakMemoryMb)} | {vramStr} | {sc.TotalDurationMs:F0} ms |");
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

    public static async Task WriteToFileAsync(
        string filePath,
        IReadOnlyList<ModelBenchmarkSummary> summaries,
        SystemHardwareInfo? hardware = null,
        string? transcriptFileName = null)
    {
        var md = GenerateMarkdown(summaries, hardware, transcriptFileName);
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
