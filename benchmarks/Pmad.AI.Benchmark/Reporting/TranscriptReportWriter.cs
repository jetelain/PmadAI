using System.Text;
using Pmad.AI.Benchmark.Engine;

namespace Pmad.AI.Benchmark.Reporting;

/// <summary>
/// Generates full turn-by-turn conversational transcripts and deep failure analysis reports.
/// </summary>
public static class TranscriptReportWriter
{
    public static string GenerateMarkdown(
        IReadOnlyList<ModelBenchmarkSummary> summaries,
        SystemHardwareInfo? hardware = null)
    {
        var hw = hardware ?? SystemHardwareInfo.Current;
        var sb = new StringBuilder();

        sb.AppendLine("# Pmad.AI Benchmark Execution Transcript & Failure Analysis");
        sb.AppendLine();
        sb.AppendLine($"Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        sb.AppendLine("## System Environment");
        sb.AppendLine();
        sb.AppendLine($"- **CPU:** {hw.CpuSummary}");
        sb.AppendLine($"- **GPU:** {hw.GpuSummary}");
        sb.AppendLine($"- **OS:** {hw.OsDescription}");
        sb.AppendLine();

        // 1. Failure Analysis Index
        sb.AppendLine("## Failure Analysis Index");
        sb.AppendLine();

        var failures = new List<(ModelBenchmarkSummary Model, BenchmarkScenarioResult Scenario, TurnResult Turn)>();
        foreach (var m in summaries)
        {
            foreach (var sc in m.ScenarioResults)
            {
                foreach (var turn in sc.TurnResults)
                {
                    if (turn.Status == BenchmarkStatus.Failed)
                    {
                        failures.Add((m, sc, turn));
                    }
                }
            }
        }

        if (failures.Count == 0)
        {
            sb.AppendLine("✅ **All scenarios and turns passed successfully with zero failures!**");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine($"Found **{failures.Count} failed turn(s)** across benchmark execution:");
            sb.AppendLine();
            sb.AppendLine("| Model / Config | Scenario | Turn | Failure Reason |");
            sb.AppendLine("|---|---|---|---|");

            foreach (var (m, sc, turn) in failures)
            {
                var cleanReason = (turn.ValidationMessage ?? sc.ErrorMessage ?? "Unknown failure")
                    .Replace("\r", " ")
                    .Replace("\n", " ");

                if (cleanReason.Length > 120)
                {
                    cleanReason = cleanReason[..117] + "...";
                }

                sb.AppendLine($"| `{m.DisplayName}` | **{sc.ScenarioName}** | Turn {turn.TurnIndex} | ❌ {cleanReason} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();

        // 2. Full Chronological Transcripts
        sb.AppendLine("## Full Chronological Transcripts");
        sb.AppendLine();

        foreach (var m in summaries)
        {
            var backendStr = m.Backend ?? (m.GpuLayers > 0 ? "Vulkan" : "CPU");
            var reasStr = m.ReasoningEnabled.HasValue ? (m.ReasoningEnabled.Value ? "Enabled" : "Disabled") : "Default";
            var vramStr = m.PeakVramMb > 0 ? BenchmarkRunner.FormatMemory(m.PeakVramMb) : "N/A";

            sb.AppendLine($"### Model: `{m.DisplayName}`");
            sb.AppendLine();
            sb.AppendLine($"- **Backend:** {backendStr} (GPU layers: {m.GpuLayers})");
            sb.AppendLine($"- **Reasoning:** {reasStr}");
            sb.AppendLine($"- **Scenarios:** {m.ScenariosPassed}/{m.ScenariosRun} passed (Weighted Pass Rate: {m.PassRatePercentage:F1}%)");
            sb.AppendLine($"- **Metrics:** {m.TotalOutputTokens} tokens in {m.TotalTimeMs / 1000.0:F2}s ({m.AverageGenerationTokensPerSecond:F1} tok/s) | RAM: {BenchmarkRunner.FormatMemory(m.PeakMemoryMb)} | GPU VRAM: {vramStr}");
            sb.AppendLine();

            foreach (var sc in m.ScenarioResults)
            {
                var statusBadge = sc.Status switch
                {
                    BenchmarkStatus.Passed => "✅ PASS",
                    BenchmarkStatus.Partial => "⚠️ PARTIAL",
                    _ => "❌ FAIL"
                };

                sb.AppendLine($"#### Scenario: `{sc.ScenarioId}` - \"{sc.ScenarioName}\" ({statusBadge} - Score: {sc.Score:F1}%, Weight: {sc.Weight:F1})");
                sb.AppendLine();
                sb.AppendLine($"- **Total Time:** {sc.TotalDurationMs:F0} ms | **Speed:** {sc.GenerationTokensPerSecond:F1} tok/s | **Tool Calls:** {sc.TotalToolCalls} (Accuracy: {sc.ToolAccuracyPercentage:F0}%)");

                if (!string.IsNullOrWhiteSpace(sc.ErrorMessage))
                {
                    sb.AppendLine($"> [!WARNING]");
                    sb.AppendLine($"> **Scenario Error:** {sc.ErrorMessage}");
                }
                sb.AppendLine();

                foreach (var turn in sc.TurnResults)
                {
                    var turnBadge = turn.Status switch
                    {
                        BenchmarkStatus.Passed => "✅ PASS",
                        BenchmarkStatus.Failed => "❌ FAIL",
                        _ => "⚠️ PARTIAL"
                    };

                    sb.AppendLine($"##### Turn {turn.TurnIndex} ({turnBadge}) - {turn.DurationMs:F0} ms, {turn.OutputTokens} output tokens ({turn.TokensPerSecond:F1} tok/s)");
                    sb.AppendLine();
                    sb.AppendLine("**User Prompt:**");
                    sb.AppendLine($"> {turn.UserPrompt}");
                    sb.AppendLine();

                    if (!string.IsNullOrWhiteSpace(turn.AssistantReasoning))
                    {
                        sb.AppendLine("**Internal Reasoning / Thought Trace:**");
                        sb.AppendLine("```text");
                        sb.AppendLine(turn.AssistantReasoning.Trim());
                        sb.AppendLine("```");
                        sb.AppendLine();
                    }

                    if (turn.ToolExecutions.Count > 0)
                    {
                        sb.AppendLine("**Tool Invocations & Results:**");
                        for (int tIdx = 0; tIdx < turn.ToolExecutions.Count; tIdx++)
                        {
                            var tool = turn.ToolExecutions[tIdx];
                            sb.AppendLine($"{tIdx + 1}. **Tool Call:** `{tool.ToolName}({tool.ArgumentsJson})`");
                            sb.AppendLine("   ```json");
                            sb.AppendLine($"   {tool.ResultJson ?? "{}"}");
                            sb.AppendLine("   ```");
                        }
                        sb.AppendLine();
                    }
                    else if (turn.ToolCalls.Count > 0)
                    {
                        sb.AppendLine("**Tool Calls:**");
                        foreach (var call in turn.ToolCalls)
                        {
                            sb.AppendLine($"- `{call.Name}`");
                        }
                        sb.AppendLine();
                    }

                    sb.AppendLine("**Assistant Final Response:**");
                    if (string.IsNullOrWhiteSpace(turn.AssistantFinalText))
                    {
                        sb.AppendLine("*(No final text generated)*");
                    }
                    else
                    {
                        sb.AppendLine(turn.AssistantFinalText.Trim());
                    }
                    sb.AppendLine();

                    sb.AppendLine("**Validation Outcome:**");
                    if (turn.Status == BenchmarkStatus.Passed)
                    {
                        sb.AppendLine("✅ **PASSED** - All turn assertions and tool expectations satisfied.");
                    }
                    else
                    {
                        sb.AppendLine($"> [!CAUTION]");
                        sb.AppendLine($"> **FAILED:** {turn.ValidationMessage ?? "Turn assertions failed."}");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static async Task WriteToFileAsync(
        string filePath,
        IReadOnlyList<ModelBenchmarkSummary> summaries,
        SystemHardwareInfo? hardware = null)
    {
        var md = GenerateMarkdown(summaries, hardware);
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        await File.WriteAllTextAsync(filePath, md);
    }
}
