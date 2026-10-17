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

        if (summaries.Any(s => !string.IsNullOrWhiteSpace(s.TranscriptPath)))
        {
            sb.AppendLine($"> [!NOTE]");
            sb.AppendLine($"> 📄 **Execution Transcripts:** Individual turn-by-turn verbatim transcripts and failure analyses for each configuration are saved in the `transcripts/` directory and linked directly in the tables below.");
            sb.AppendLine();
        }
        else if (!string.IsNullOrWhiteSpace(transcriptFileName))
        {
            sb.AppendLine($"> [!NOTE]");
            sb.AppendLine($"> 📄 **Execution Transcript & Failure Analysis:** Detailed turn-by-turn prompts, reasoning traces, tool inputs/outputs, and failure analysis are recorded in [{transcriptFileName}]({transcriptFileName}).");
            sb.AppendLine();
        }

        sb.AppendLine("## Summary Comparison");
        sb.AppendLine();
        sb.AppendLine("| Model | Backend | Reasoning | Scenarios Passed | Pass@1 Rate | Step Acc | Pass Rate | Tool Calls | Gen Tokens | Avg Speed (tok/s) | Sys RAM | GPU VRAM | Total Time |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");

        foreach (var m in summaries)
        {
            var backendStr = m.Backend ?? (m.GpuLayers > 0 ? "Vulkan" : "CPU");
            var reasStr = m.ReasoningEnabled.HasValue ? (m.ReasoningEnabled.Value ? "Enabled" : "Disabled") : "Default";
            var vramStr = m.PeakVramMb > 0 ? BenchmarkRunner.FormatMemory(m.PeakVramMb) : "-";
            var modelDisplay = !string.IsNullOrWhiteSpace(m.TranscriptPath)
                ? $"[`{m.DisplayName}`]({m.TranscriptPath})"
                : $"`{m.DisplayName}`";
            sb.AppendLine($"| {modelDisplay} | {backendStr} | {reasStr} | {m.ScenariosPassed}/{m.ScenariosRun} | {m.TaskSuccessRatePercentage:F1}% | {m.StepAccuracyPercentage:F1}% | {m.PassRatePercentage:F1}% | {m.TotalToolCalls} | {m.TotalOutputTokens} | {m.AverageGenerationTokensPerSecond:F1} | {BenchmarkRunner.FormatMemory(m.PeakMemoryMb)} | {vramStr} | {m.TotalTimeMs / 1000.0:F2}s |");
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

        // Optimization Matrix Gain Comparison
        var modelsWithBaseline = summaries
            .GroupBy(s => s.ModelName)
            .Where(g => g.Any(s => s.OptimizationConfigId == "baseline" || s.ConfigurationLabel?.Contains("baseline", StringComparison.OrdinalIgnoreCase) == true))
            .ToList();

        if (modelsWithBaseline.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Pmad.AI Optimization Matrix: Empirical Utility Gains");
            sb.AppendLine();
            sb.AppendLine("> Quantifies prompt token reduction, context ceiling capping, execution time savings, and accuracy improvements achieved by Pmad.AI utilities vs. unassisted baseline model execution.");
            sb.AppendLine();
            sb.AppendLine("| Model | Configuration | Pass Rate | Prompt Tokens | Token Savings % | Peak Context | Context Reduction % | Gen Speed | Duration | Time Savings % |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");

            foreach (var group in modelsWithBaseline)
            {
                var baseline = group.First(s => s.OptimizationConfigId == "baseline" || s.ConfigurationLabel?.Contains("baseline", StringComparison.OrdinalIgnoreCase) == true);

                foreach (var opt in group)
                {
                    var tokenSavings = baseline.TotalPromptTokens > 0
                        ? ((baseline.TotalPromptTokens - opt.TotalPromptTokens) * 100.0 / baseline.TotalPromptTokens)
                        : 0.0;
                    opt.PromptTokenSavingsPercentage = tokenSavings;

                    var contextReduction = baseline.PeakContextTokens > 0
                        ? ((baseline.PeakContextTokens - opt.PeakContextTokens) * 100.0 / baseline.PeakContextTokens)
                        : 0.0;
                    opt.PeakContextReductionPercentage = contextReduction;

                    var timeSavings = baseline.TotalTimeMs > 0
                        ? ((baseline.TotalTimeMs - opt.TotalTimeMs) * 100.0 / baseline.TotalTimeMs)
                        : 0.0;
                    opt.TimeSavingsPercentage = timeSavings;

                    var savingsStr = opt == baseline ? "—" : (tokenSavings >= 0 ? $"**-{tokenSavings:F1}%**" : $"+{-tokenSavings:F1}%");
                    var contextRedStr = opt == baseline ? "—" : (contextReduction >= 0 ? $"**-{contextReduction:F1}%**" : $"+{-contextReduction:F1}%");
                    var timeSavingsStr = opt == baseline ? "—" : (timeSavings >= 0 ? $"**-{timeSavings:F1}%**" : $"+{-timeSavings:F1}%");

                    var configName = !string.IsNullOrWhiteSpace(opt.OptimizationConfigLabel)
                        ? opt.OptimizationConfigLabel
                        : (opt.ConfigurationLabel ?? "Default");

                    var configDisplay = !string.IsNullOrWhiteSpace(opt.TranscriptPath)
                        ? $"[**{configName}**]({opt.TranscriptPath})"
                        : $"**{configName}**";

                    sb.AppendLine($"| `{opt.ModelName}` | {configDisplay} | {opt.PassRatePercentage:F1}% | {opt.TotalPromptTokens} | {savingsStr} | {opt.PeakContextTokens} tok | {contextRedStr} | {opt.AverageGenerationTokensPerSecond:F1} tok/s | {opt.TotalTimeMs / 1000.0:F2}s | {timeSavingsStr} |");
                }
            }

            // Semantic Recall Scorecard
            var hasCompaction = summaries.Any(s => s.ScenarioResults.Any(r => r.ScenarioId == "compaction"));
            if (hasCompaction)
            {
                sb.AppendLine();
                sb.AppendLine("### Semantic Probe Recall Scorecard (15-Turn Coding Agent)");
                sb.AppendLine();
                sb.AppendLine("| Model | Configuration | 15-Turn Workflow | Turn 15 Semantic Probe | Peak Context Window | Total Scenario Tokens |");
                sb.AppendLine("|---|---|---|---|---|---|");

                foreach (var group in modelsWithBaseline)
                {
                    foreach (var s in group)
                    {
                        var compSc = s.ScenarioResults.FirstOrDefault(r => r.ScenarioId == "compaction");
                        if (compSc != null)
                        {
                            var recallStatus = compSc.SemanticRecallPassed ? "✅ PASS" : "❌ FAIL";
                            var workflowStatus = compSc.Status == BenchmarkStatus.Passed ? "✅ PASS" : "❌ FAIL";
                            var configName = s.OptimizationConfigLabel ?? s.ConfigurationLabel ?? "Default";
                            sb.AppendLine($"| `{s.ModelName}` | **{configName}** | {workflowStatus} | {recallStatus} | {compSc.PeakContextTokens} tok | {compSc.TotalTokens} |");
                        }
                    }
                }
            }

            // Resilience & Recovery Scorecard
            var hasRecoveryOrLoop = summaries.Any(s => s.ScenarioResults.Any(r => r.ScenarioId is "healing" or "runaway-loop"));
            if (hasRecoveryOrLoop)
            {
                sb.AppendLine();
                sb.AppendLine("### Resilience & Model Error Recovery Scorecard");
                sb.AppendLine();
                sb.AppendLine("| Model | Scenario | Baseline Status | Optimized Status | Healing / Breaker Active |");
                sb.AppendLine("|---|---|---|---|---|");

                foreach (var group in modelsWithBaseline)
                {
                    var baseline = group.First(s => s.OptimizationConfigId == "baseline" || s.ConfigurationLabel?.Contains("baseline", StringComparison.OrdinalIgnoreCase) == true);
                    foreach (var scId in new[] { "healing", "runaway-loop" })
                    {
                        var bSc = baseline.ScenarioResults.FirstOrDefault(r => r.ScenarioId == scId);
                        if (bSc != null)
                        {
                            var bStatus = bSc.Status == BenchmarkStatus.Passed ? "✅ PASS" : "❌ FAIL";
                            foreach (var opt in group.Where(s => s != baseline))
                            {
                                var oSc = opt.ScenarioResults.FirstOrDefault(r => r.ScenarioId == scId);
                                if (oSc != null)
                                {
                                    var oStatus = oSc.Status == BenchmarkStatus.Passed ? "✅ PASS" : "❌ FAIL";
                                    var configName = opt.OptimizationConfigLabel ?? opt.ConfigurationLabel ?? "Default";
                                    sb.AppendLine($"| `{opt.ModelName}` | {bSc.ScenarioName} | {bStatus} | {oStatus} | **{configName}** |");
                                }
                            }
                        }
                    }
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Detailed Scenario Breakdown");
        sb.AppendLine();

        foreach (var m in summaries)
        {
            var transcriptLink = !string.IsNullOrWhiteSpace(m.TranscriptPath)
                ? $" — [📄 View Full Transcript]({m.TranscriptPath})"
                : "";
            sb.AppendLine($"### Model: `{m.DisplayName}`{transcriptLink}");
            sb.AppendLine();
            sb.AppendLine("| Scenario | Status | Pass@1 | Step Acc. | Score | Weight | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");

            foreach (var sc in m.ScenarioResults)
            {
                var statusBadge = sc.Status switch
                {
                    BenchmarkStatus.Passed => "✅ PASS",
                    BenchmarkStatus.Partial => "⚠️ PARTIAL",
                    _ => "❌ FAIL"
                };

                var passAt1Badge = sc.TaskSuccess ? "✅ PASS" : "❌ FAIL";
                var vramStr = sc.PeakVramMb > 0 ? BenchmarkRunner.FormatMemory(sc.PeakVramMb) : "-";
                var scenarioLabel = !string.IsNullOrEmpty(sc.ExecutionDetails)
                    ? $"**{sc.ScenarioName}**<br/><sub>`[{sc.ExecutionDetails}]`</sub>"
                    : $"**{sc.ScenarioName}**";

                sb.AppendLine($"| {scenarioLabel} | {statusBadge} | {passAt1Badge} | {sc.StepAccuracy:F1}% | {sc.Score:F1}% | {sc.Weight:F1} | {sc.TotalToolCalls} | {sc.ToolAccuracyPercentage:F0}% | {sc.TotalPromptTokens} | {sc.TotalOutputTokens} | {sc.GenerationTokensPerSecond:F1} tok/s | {BenchmarkRunner.FormatMemory(sc.PeakMemoryMb)} | {vramStr} | {sc.TotalDurationMs:F0} ms |");
            }

            sb.AppendLine();

            foreach (var sc in m.ScenarioResults)
            {
                if (sc.Status != BenchmarkStatus.Passed || !string.IsNullOrWhiteSpace(sc.ErrorMessage))
                {
                    sb.AppendLine($"> [!WARNING]");
                    var detailHeader = sc.Status == BenchmarkStatus.Partial
                        ? $"**Partial Outcome Details for {sc.ScenarioName} (Score: {sc.Score:F1}%, {sc.TurnsPassed}/{sc.TotalTurns} turns passed):**"
                        : $"**Failure Details for {sc.ScenarioName}:**";
                    sb.AppendLine($"> {detailHeader} {sc.ErrorMessage}");
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
