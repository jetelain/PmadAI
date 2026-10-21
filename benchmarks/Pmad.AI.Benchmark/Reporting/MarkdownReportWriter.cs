using System.Globalization;
using System.Text;
using System.Text.Json;
using Pmad.AI.Benchmark.Engine;
using Pmad.AI.Compaction;
using Pmad.AI.Guardrails;
using Pmad.AI.Healing;
using Pmad.AI.Tools;

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
        sb.AppendLine("| Model | Backend | Reasoning | Scenarios Passed | Pass@1 (Wtd) | Pass@1 (Unwtd) | Step Acc | Pass Rate | Tool Calls | Gen Tokens | Avg Speed (tok/s) | Sys RAM | GPU VRAM | Total Time |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");

        foreach (var m in summaries)
        {
            var backendStr = m.Backend ?? (m.GpuLayers > 0 ? "Vulkan" : "CPU");
            var reasStr = m.ReasoningEnabled.HasValue ? (m.ReasoningEnabled.Value ? "Enabled" : "Disabled") : "Default";
            var vramStr = m.PeakVramMb > 0 ? BenchmarkRunner.FormatMemory(m.PeakVramMb) : "-";
            var modelDisplay = !string.IsNullOrWhiteSpace(m.TranscriptPath)
                ? $"[`{m.DisplayName}`]({m.TranscriptPath})"
                : $"`{m.DisplayName}`";
            sb.AppendLine($"| {modelDisplay} | {backendStr} | {reasStr} | {m.ScenariosPassed}/{m.ScenariosRun} | {m.TaskSuccessRatePercentage:F1}% | {m.UnweightedTaskSuccessRatePercentage:F1}% | {m.StepAccuracyPercentage:F1}% | {m.PassRatePercentage:F1}% | {m.TotalToolCalls} | {m.TotalOutputTokens} | {m.AverageGenerationTokensPerSecond:F1} | {BenchmarkRunner.FormatMemory(m.PeakMemoryMb)} | {vramStr} | {m.TotalTimeMs / 1000.0:F2}s |");
        }

        // Suite Breakdown (B1)
        var anySuiteBreakdown = summaries.Any(s => s.ScenarioResults.Any(r => !string.IsNullOrEmpty(r.Suite)));
        if (anySuiteBreakdown)
        {
            sb.AppendLine();
            sb.AppendLine("## Benchmark Suite Breakdown (BFCL / SWE-bench / RULER)");
            sb.AppendLine();
            sb.AppendLine("> Evaluates performance separated by benchmark domain: schema adherence and tool call syntax (BFCL), multi-turn agentic software engineering (SWE-bench), and needle-in-a-haystack retrieval (RULER).");
            sb.AppendLine();
            sb.AppendLine("| Model | Suite | Scenarios | Pass@1 (Task Success) | Step Acc | Turns Passed | Turn Acc | Weighted Pass Rate |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");

            foreach (var m in summaries)
            {
                var suites = m.GetSuiteSummaries();
                foreach (var suite in suites)
                {
                    var modelDisplay = !string.IsNullOrWhiteSpace(m.TranscriptPath)
                        ? $"[`{m.DisplayName}`]({m.TranscriptPath})"
                        : $"`{m.DisplayName}`";

                    sb.AppendLine($"| {modelDisplay} | **{suite.Suite}** | {suite.ScenariosPassed}/{suite.ScenariosCount} | {suite.TaskSuccessRatePercentage:F1}% | {suite.StepAccuracyPercentage:F1}% | {suite.TurnsPassed}/{suite.TotalTurns} | {suite.TurnAccuracyPercentage:F1}% | {suite.PassRatePercentage:F1}% |");
                }
            }
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

            // Semantic Recall & Compaction Scorecard
            var hasCompaction = summaries.Any(s => s.ScenarioResults.Any(r => r.Suite == "Compaction" || r.ScenarioId.Contains("compaction")));
            if (hasCompaction)
            {
                sb.AppendLine();
                sb.AppendLine("### Compaction & Semantic Probe Recall Scorecard");
                sb.AppendLine();
                sb.AppendLine("| Model | Configuration | Scenario | Workflow Status | Semantic Recall Probe | Peak Context Window | Total Scenario Tokens |");
                sb.AppendLine("|---|---|---|---|---|---|---|");

                foreach (var group in modelsWithBaseline)
                {
                    foreach (var s in group)
                    {
                        var compScenarios = s.ScenarioResults.Where(r => r.Suite == "Compaction" || r.ScenarioId.Contains("compaction")).ToList();
                        foreach (var compSc in compScenarios)
                        {
                            var recallStatus = compSc.SemanticRecallPassed ? "✅ PASS" : "❌ FAIL";
                            var workflowStatus = compSc.Status == BenchmarkStatus.Passed ? "✅ PASS" : "❌ FAIL";
                            var configName = s.OptimizationConfigLabel ?? s.ConfigurationLabel ?? "Default";
                            sb.AppendLine($"| `{s.ModelName}` | **{configName}** | `{compSc.ScenarioName}` | {workflowStatus} | {recallStatus} | {compSc.PeakContextTokens} tok | {compSc.TotalTokens} |");
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

        // Dedicated Compaction Efficiency Table
        var summariesWithCompaction = summaries
            .Where(s => (s.CompactionStats != null && s.CompactionStats.TotalEvents > 0) ||
                        s.ScenarioResults.Any(r => r.CompactionStats != null && r.CompactionStats.TotalEvents > 0))
            .ToList();

        if (summariesWithCompaction.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Compaction Efficiency & Telemetry Matrix");
            sb.AppendLine();
            sb.AppendLine("> Reports characters and tokens pruned, speculative SLM hit rate, and breakdown of collapsed reasoning traces vs. truncated/summarized tool results.");
            sb.AppendLine();
            sb.AppendLine("| Model | Configuration | Pruned Chars | Pruned Tokens | Speculative Hit Rate | Thinking Traces Collapsed | Tool Results Truncated | Tool Results Summarized | Total Events |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");

            foreach (var s in summariesWithCompaction)
            {
                var stats = s.CompactionStats ?? AggregateScenarioStats(s.ScenarioResults);
                var configName = !string.IsNullOrWhiteSpace(s.OptimizationConfigLabel)
                    ? s.OptimizationConfigLabel
                    : (s.ConfigurationLabel ?? "Default");

                var configDisplay = !string.IsNullOrWhiteSpace(s.TranscriptPath)
                    ? $"[**{configName}**]({s.TranscriptPath})"
                    : $"**{configName}**";

                var hitRateStr = stats.TotalEvents > 0
                    ? $"{stats.SpeculativeHitRatePercentage.ToString("F1", CultureInfo.InvariantCulture)}% ({stats.SpeculativeHits}/{stats.TotalEvents})"
                    : "0.0%";
                var prunedCharsStr = stats.TotalPrunedChars.ToString("N0", CultureInfo.InvariantCulture);
                var prunedTokensStr = stats.EstimatedPrunedTokens.ToString("N0", CultureInfo.InvariantCulture);

                sb.AppendLine($"| `{s.ModelName}` | {configDisplay} | {prunedCharsStr} | {prunedTokensStr} | {hitRateStr} | {stats.ThinkingTracesCollapsed} | {stats.ToolResultsTruncated} | {stats.ToolResultsSummarized} | {stats.TotalEvents} |");
            }
        }

        // Pmad.AI Utility Interventions & Telemetry Matrix (U7)
        var summariesWithTelemetry = summaries
            .Where(s => (s.HealingStats != null && s.HealingStats.TotalInterventions > 0) ||
                        (s.CircuitBreakerStats != null && s.CircuitBreakerStats.TotalTrips > 0) ||
                        (s.DispatchStats != null && s.DispatchStats.TotalHits > 0) ||
                        s.ScenarioResults.Any(r => (r.HealingStats != null && r.HealingStats.TotalInterventions > 0) ||
                                                   (r.CircuitBreakerStats != null && r.CircuitBreakerStats.TotalTrips > 0) ||
                                                   (r.DispatchStats != null && r.DispatchStats.TotalHits > 0)))
            .ToList();

        if (summariesWithTelemetry.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Pmad.AI Utility Interventions & Telemetry Matrix");
            sb.AppendLine();
            sb.AppendLine("> Quantifies runtime interventions by Pmad.AI components: Tool Call Healing (malformed JSON, pseudo-tag extractions, fence strippings), Circuit Breaker (loop and ping-pong interceptions), and Lenient Dispatch (tool/parameter normalization).");
            sb.AppendLine();
            sb.AppendLine("| Model | Configuration | Healing Total | Pseudo-Tags | JSON Repairs | Breaker Total | Breaker Loops | Repetitions | Dispatch Total | Alias Hits | Param Hits | Casing/Norm |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");

            foreach (var s in summariesWithTelemetry)
            {
                var h = s.HealingStats ?? AggregateHealingStats(s.ScenarioResults);
                var b = s.CircuitBreakerStats ?? AggregateBreakerStats(s.ScenarioResults);
                var d = s.DispatchStats ?? AggregateDispatchStats(s.ScenarioResults);

                var configName = !string.IsNullOrWhiteSpace(s.OptimizationConfigLabel)
                    ? s.OptimizationConfigLabel
                    : (s.ConfigurationLabel ?? "Default");

                var configDisplay = !string.IsNullOrWhiteSpace(s.TranscriptPath)
                    ? $"[**{configName}**]({s.TranscriptPath})"
                    : $"**{configName}**";

                sb.AppendLine($"| `{s.ModelName}` | {configDisplay} | {h.TotalInterventions} | {h.PseudoTagsExtracted} | {h.JsonRepairs} | {b.TotalTrips} | {b.ToolPingPongTrips + b.NoProgressTrips} | {b.StreamingRepetitionTrips} | {d.TotalHits} | {d.ToolAliasesResolved} | {d.ParameterAliasesResolved} | {d.CasingNormalized} |");
            }
        }

        // Multi-Run Statistical Evaluation (B7)
        var summariesWithMultiRun = summaries.Where(s => s.MultiRunStats != null).ToList();
        if (summariesWithMultiRun.Count > 0)
        {
            var runsCount = summariesWithMultiRun.First().MultiRunStats!.RunsCount;
            sb.AppendLine();
            sb.AppendLine($"## Statistical Multi-Run Evaluation (N = {runsCount}, Mean ± StdDev)");
            sb.AppendLine();
            sb.AppendLine("> Reports sample mean and standard deviation across repeated runs with identical fixed seeds and temperature to quantify confidence intervals and isolate non-deterministic variance.");
            sb.AppendLine();
            sb.AppendLine("| Model | Configuration | Pass Rate % | Pass@1 (Task Success) % | Step Accuracy % | Prompt Tokens | Tok/Call | Gen Speed (tok/s) | Duration |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");

            foreach (var s in summariesWithMultiRun)
            {
                var stats = s.MultiRunStats!;
                var configName = !string.IsNullOrWhiteSpace(s.OptimizationConfigLabel)
                    ? s.OptimizationConfigLabel
                    : (s.ConfigurationLabel ?? "Default");

                var passRateDelta = stats.DeltaPassRateVsBaseline.HasValue && stats.DeltaPassRateVsBaseline.Value != 0
                    ? $" ({(stats.DeltaPassRateVsBaseline.Value > 0 ? "+" : "")}{stats.DeltaPassRateVsBaseline.Value:F1}%)"
                    : "";
                var taskSuccessDelta = stats.DeltaTaskSuccessVsBaseline.HasValue && stats.DeltaTaskSuccessVsBaseline.Value != 0
                    ? $" ({(stats.DeltaTaskSuccessVsBaseline.Value > 0 ? "+" : "")}{stats.DeltaTaskSuccessVsBaseline.Value:F1}%)"
                    : "";
                var tokenDelta = stats.DeltaPromptTokensPercentageVsBaseline.HasValue && stats.DeltaPromptTokensPercentageVsBaseline.Value != 0
                    ? $" ({(stats.DeltaPromptTokensPercentageVsBaseline.Value >= 0 ? "-" : "+")}{Math.Abs(stats.DeltaPromptTokensPercentageVsBaseline.Value):F1}%)"
                    : "";
                var timeDelta = stats.DeltaTimePercentageVsBaseline.HasValue && stats.DeltaTimePercentageVsBaseline.Value != 0
                    ? $" ({(stats.DeltaTimePercentageVsBaseline.Value >= 0 ? "-" : "+")}{Math.Abs(stats.DeltaTimePercentageVsBaseline.Value):F1}%)"
                    : "";

                sb.AppendLine($"| `{s.ModelName}` | **{configName}** | {stats.PassRate.Mean:F1} ± {stats.PassRate.StdDev:F1}%{passRateDelta} | {stats.TaskSuccessRate.Mean:F1} ± {stats.TaskSuccessRate.StdDev:F1}%{taskSuccessDelta} | {stats.StepAccuracy.Mean:F1} ± {stats.StepAccuracy.StdDev:F1}% | {stats.PromptTokens.Mean:F0} ± {stats.PromptTokens.StdDev:F0}{tokenDelta} | {stats.PromptTokensPerToolCall.Mean:F0} ± {stats.PromptTokensPerToolCall.StdDev:F0} | {stats.GenerationTokensPerSecond.Mean:F1} ± {stats.GenerationTokensPerSecond.StdDev:F1} | {stats.TotalTimeSeconds.Mean:F2} ± {stats.TotalTimeSeconds.StdDev:F2}s{timeDelta} |");
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
            sb.AppendLine("| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");

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
                var taxonomyStr = !string.IsNullOrEmpty(sc.FailureTaxonomy) ? $"`{sc.FailureTaxonomy}`" : "—";
                var tokPerCallStr = sc.TotalToolCalls > 0 ? $"{sc.PromptTokensPerToolCall:F0}" : "—";
                var peakContextStr = sc.PeakContextTokens > 0 ? $"{sc.PeakContextTokens} tok" : "—";

                sb.AppendLine($"| {scenarioLabel} | {sc.Suite} | {statusBadge} | {passAt1Badge} | {sc.StepAccuracy:F1}% | {sc.Score:F1}% | {sc.Weight:F1} | {taxonomyStr} | {sc.TotalToolCalls} | {sc.ToolAccuracyPercentage:F0}% | {sc.TotalPromptTokens} | {tokPerCallStr} | {peakContextStr} | {sc.TotalOutputTokens} | {sc.GenerationTokensPerSecond:F1} tok/s | {BenchmarkRunner.FormatMemory(sc.PeakMemoryMb)} | {vramStr} | {sc.TotalDurationMs:F0} ms |");
            }

            sb.AppendLine();

            foreach (var sc in m.ScenarioResults)
            {
                if (sc.CompactionStats != null && sc.CompactionStats.TotalEvents > 0)
                {
                    var c = sc.CompactionStats;
                    sb.AppendLine($"> ✂️ **Compaction Details for {sc.ScenarioName}:** {c.TotalPrunedChars.ToString("N0", CultureInfo.InvariantCulture)} chars pruned (~{c.EstimatedPrunedTokens.ToString("N0", CultureInfo.InvariantCulture)} tokens), {c.ThinkingTracesCollapsed} thinking traces collapsed, {c.ToolResultsTruncated} tool results truncated, {c.ToolResultsSummarized} summarized (Speculative Hit Rate: {c.SpeculativeHitRatePercentage.ToString("F1", CultureInfo.InvariantCulture)}%).");
                    sb.AppendLine();
                }

                if (sc.HealingStats != null && sc.HealingStats.TotalInterventions > 0)
                {
                    var h = sc.HealingStats;
                    sb.AppendLine($"> 🩹 **Healing Details for {sc.ScenarioName}:** {h.TotalInterventions} intervention(s) ({h.PseudoTagsExtracted} pseudo-tags, {h.JsonRepairs} JSON repairs, {h.FencesStripped} fence strippings, {h.ActionBlocksExtracted} action blocks).");
                    sb.AppendLine();
                }

                if (sc.CircuitBreakerStats != null && sc.CircuitBreakerStats.TotalTrips > 0)
                {
                    var b = sc.CircuitBreakerStats;
                    sb.AppendLine($"> ⚡ **Circuit Breaker Details for {sc.ScenarioName}:** {b.TotalTrips} trip(s), {b.ToolPingPongTrips} ping-pong loop trip(s), {b.StreamingRepetitionTrips} streaming repetition trip(s){(b.NoProgressTrips > 0 ? $", {b.NoProgressTrips} semantic/no-progress loop trip(s)" : "")}.");
                    sb.AppendLine();
                }

                if (sc.DispatchStats != null && sc.DispatchStats.TotalHits > 0)
                {
                    var d = sc.DispatchStats;
                    sb.AppendLine($"> 🔀 **Dispatch Normalization Details for {sc.ScenarioName}:** {d.TotalHits} event(s) ({d.ToolAliasesResolved} tool alias hits, {d.ParameterAliasesResolved} param alias hits, {d.CasingNormalized} casing normalizations{(d.EnumValuesSnapped > 0 ? $", {d.EnumValuesSnapped} enum value(s) snapped" : "")}).");
                    sb.AppendLine();
                }

                if (sc.Status != BenchmarkStatus.Passed || !sc.TaskSuccess || !string.IsNullOrWhiteSpace(sc.ErrorMessage))
                {
                    sb.AppendLine($"> [!WARNING]");
                    var detailHeader = sc.Status == BenchmarkStatus.Partial
                        ? $"**Partial Outcome Details for {sc.ScenarioName} (Score: {sc.Score:F1}%, {sc.TurnsPassed}/{sc.TotalTurns} turns passed):**"
                        : $"**Failure Details for {sc.ScenarioName}:**";
                    sb.AppendLine($"> {detailHeader} {sc.ErrorMessage}");

                    if (!sc.TaskSuccess)
                    {
                        var failureReasonStr = sc.TaskFailureReason ?? "Applied patch did not pass verification or expected task goal was not achieved.";
                        sb.AppendLine($"> - End-to-end task failed: {failureReasonStr}");
                        if (!string.IsNullOrEmpty(sc.FailureTaxonomy))
                        {
                            sb.AppendLine($"> - Failure Taxonomy: `{sc.FailureTaxonomy}`");
                        }
                    }

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

    private static CompactionTelemetrySnapshot AggregateScenarioStats(IEnumerable<BenchmarkScenarioResult> scenarioResults)
    {
        var scStats = scenarioResults
            .Where(sc => sc.CompactionStats != null)
            .Select(sc => sc.CompactionStats!)
            .ToList();

        if (scStats.Count == 0)
        {
            return new CompactionTelemetrySnapshot(0, 0, 0, 0, 0, 0, 0.0, 0, 0, 0, 0);
        }

        var totalEvents = scStats.Sum(s => s.TotalEvents);
        var totalOrig = scStats.Sum(s => s.TotalOriginalChars);
        var totalComp = scStats.Sum(s => s.TotalCompactedChars);
        var totalPruned = scStats.Sum(s => s.TotalPrunedChars);
        var estTokens = scStats.Sum(s => s.EstimatedPrunedTokens);
        var specHits = scStats.Sum(s => s.SpeculativeHits);
        var hitRate = totalEvents > 0 ? Math.Round((double)specHits * 100.0 / totalEvents, 1) : 0.0;
        var thinking = scStats.Sum(s => s.ThinkingTracesCollapsed);
        var toolTrunc = scStats.Sum(s => s.ToolResultsTruncated);
        var toolSumm = scStats.Sum(s => s.ToolResultsSummarized);
        var toolComp = scStats.Sum(s => s.ToolResultsCompacted);

        return new CompactionTelemetrySnapshot(
            TotalEvents: totalEvents,
            TotalOriginalChars: totalOrig,
            TotalCompactedChars: totalComp,
            TotalPrunedChars: totalPruned,
            EstimatedPrunedTokens: estTokens,
            SpeculativeHits: specHits,
            SpeculativeHitRatePercentage: hitRate,
            ThinkingTracesCollapsed: thinking,
            ToolResultsTruncated: toolTrunc,
            ToolResultsSummarized: toolSumm,
            ToolResultsCompacted: toolComp
        );
    }

    private static ToolCallHealingTelemetrySnapshot AggregateHealingStats(IEnumerable<BenchmarkScenarioResult> scenarioResults)
    {
        var list = scenarioResults.Where(s => s.HealingStats != null).Select(s => s.HealingStats!).ToList();
        if (list.Count == 0) return new ToolCallHealingTelemetrySnapshot(0, 0, 0, 0, 0, 0, 0);
        return new ToolCallHealingTelemetrySnapshot(
            TotalInterventions: list.Sum(s => s.TotalInterventions),
            PseudoTagsExtracted: list.Sum(s => s.PseudoTagsExtracted),
            ActionBlocksExtracted: list.Sum(s => s.ActionBlocksExtracted),
            JsonRepairs: list.Sum(s => s.JsonRepairs),
            FencesStripped: list.Sum(s => s.FencesStripped),
            CallIdsGenerated: list.Sum(s => s.CallIdsGenerated),
            ArgumentsUnwrapped: list.Sum(s => s.ArgumentsUnwrapped)
        );
    }

    private static CircuitBreakerTelemetrySnapshot AggregateBreakerStats(IEnumerable<BenchmarkScenarioResult> scenarioResults)
    {
        var list = scenarioResults.Where(s => s.CircuitBreakerStats != null).Select(s => s.CircuitBreakerStats!).ToList();
        if (list.Count == 0) return new CircuitBreakerTelemetrySnapshot(0, 0, 0, 0);
        return new CircuitBreakerTelemetrySnapshot(
            TotalTrips: list.Sum(s => s.TotalTrips),
            StreamingRepetitionTrips: list.Sum(s => s.StreamingRepetitionTrips),
            ToolPingPongTrips: list.Sum(s => s.ToolPingPongTrips),
            NoProgressTrips: list.Sum(s => s.NoProgressTrips)
        );
    }

    private static LenientDispatchTelemetrySnapshot AggregateDispatchStats(IEnumerable<BenchmarkScenarioResult> scenarioResults)
    {
        var list = scenarioResults.Where(s => s.DispatchStats != null).Select(s => s.DispatchStats!).ToList();
        if (list.Count == 0) return new LenientDispatchTelemetrySnapshot(0, 0, 0, 0, 0);
        return new LenientDispatchTelemetrySnapshot(
            TotalHits: list.Sum(s => s.TotalHits),
            ToolAliasesResolved: list.Sum(s => s.ToolAliasesResolved),
            ParameterAliasesResolved: list.Sum(s => s.ParameterAliasesResolved),
            NamespacesStripped: list.Sum(s => s.NamespacesStripped),
            CasingNormalized: list.Sum(s => s.CasingNormalized),
            EnumValuesSnapped: list.Sum(s => s.EnumValuesSnapped),
            EnumValuesUnmatched: list.Sum(s => s.EnumValuesUnmatched)
        );
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
