using System.Text;
using Pmad.AI.Benchmark.Engine;

namespace Pmad.AI.Benchmark.Reporting;

/// <summary>
/// Formats benchmark execution metrics into clear ASCII / Unicode console tables.
/// </summary>
public static class ConsoleReportFormatter
{
    public static void PrintScenarioResultsTable(ModelBenchmarkSummary summary)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(new string('=', 140));
        Console.WriteLine($"   BENCHMARK SCENARIO BREAKDOWN: {summary.DisplayName.ToUpperInvariant()}");
        Console.WriteLine(new string('=', 140));
        Console.ResetColor();

        Console.WriteLine(string.Format("{0,-20} | {1,-9} | {2,-6} | {3,-6} | {4,-8} | {5,-7} | {6,-5} | {7,-14} | {8,-5} | {9,-9} | {10,-8} | {11,-8} | {12,-8}",
            "Scenario", "Suite", "Status", "Pass@1", "Step Acc", "Score", "Wgt", "Taxonomy", "Tools", "Gen Tok/s", "RAM", "GPU VRAM", "Time(ms)"));
        Console.WriteLine(new string('-', 140));

        foreach (var sc in summary.ScenarioResults)
        {
            var statusStr = sc.Status switch
            {
                BenchmarkStatus.Passed => "[PASS]",
                BenchmarkStatus.Failed => "[FAIL]",
                BenchmarkStatus.Partial => "[PART]",
                _ => "[SKIP]"
            };

            if (sc.Status == BenchmarkStatus.Passed) Console.ForegroundColor = ConsoleColor.Green;
            else if (sc.Status == BenchmarkStatus.Failed) Console.ForegroundColor = ConsoleColor.Red;
            else Console.ForegroundColor = ConsoleColor.Yellow;

            var vramStr = sc.PeakVramMb > 0 ? BenchmarkRunner.FormatMemory(sc.PeakVramMb) : "-";
            var passAt1Str = sc.TaskSuccess ? "PASS" : "FAIL";
            var stepAccStr = $"{sc.StepAccuracy:F1}%";
            var scoreStr = $"{sc.Score:F1}%";
            var weightStr = $"{sc.Weight:F1}";
            var taxonomyStr = !string.IsNullOrEmpty(sc.FailureTaxonomy)
                ? (sc.FailureTaxonomy.Length > 14 ? sc.FailureTaxonomy[..11] + "..." : sc.FailureTaxonomy)
                : "-";

            Console.WriteLine(string.Format("{0,-20} | {1,-9} | {2,-6} | {3,-6} | {4,-8} | {5,-7} | {6,-5} | {7,-14} | {8,-5} | {9,-9:F1} | {10,-8} | {11,-8} | {12,-8:F0}",
                sc.ScenarioName.Length > 20 ? sc.ScenarioName[..17] + "..." : sc.ScenarioName,
                sc.Suite.Length > 9 ? sc.Suite[..9] : sc.Suite,
                statusStr,
                passAt1Str,
                stepAccStr,
                scoreStr,
                weightStr,
                taxonomyStr,
                sc.TotalToolCalls,
                sc.GenerationTokensPerSecond,
                BenchmarkRunner.FormatMemory(sc.PeakMemoryMb),
                vramStr,
                sc.TotalDurationMs));

            if (!sc.TaskSuccess)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                var reason = sc.TaskFailureReason ?? sc.ErrorMessage ?? "Task verification failed";
                var tax = !string.IsNullOrEmpty(sc.FailureTaxonomy) ? $" [{sc.FailureTaxonomy}]" : "";
                Console.WriteLine($"      ↳ Task Failure{tax}: {reason}");
            }

            Console.ResetColor();
        }

        Console.WriteLine(new string('-', 140));
    }

    public static void PrintModelsComparisonTable(IReadOnlyList<ModelBenchmarkSummary> allSummaries)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(new string('=', 140));
        Console.WriteLine("   OVERALL MODELS TOOL CALLING & PERFORMANCE METRICS COMPARISON");
        Console.WriteLine(new string('=', 140));
        Console.ResetColor();

        Console.WriteLine(string.Format("{0,-18} | {1,-7} | {2,-9} | {3,-6} | {4,-8} | {5,-8} | {6,-8} | {7,-8} | {8,-5} | {9,-8} | {10,-8} | {11,-8} | {12,-9}",
            "Model", "Backend", "Reasoning", "Passed", "Pass@1(W)", "Pass@1(U)", "Step Acc", "PassRate", "Tools", "Gen Toks", "AvgTok/s", "RAM", "Total Time"));
        Console.WriteLine(new string('-', 140));

        foreach (var m in allSummaries)
        {
            var passedRatio = $"{m.ScenariosPassed}/{m.ScenariosRun}";
            var passAt1WeightedStr = $"{m.TaskSuccessRatePercentage:F1}%";
            var passAt1UnweightedStr = $"{m.UnweightedTaskSuccessRatePercentage:F1}%";
            var stepAccRateStr = $"{m.StepAccuracyPercentage:F1}%";
            var passRateStr = $"{m.PassRatePercentage:F1}%";
            var totalTimeSec = $"{m.TotalTimeMs / 1000.0:F2}s";
            var backendStr = m.Backend ?? (m.GpuLayers > 0 ? "Vulkan" : "CPU");
            var reasStr = m.ReasoningEnabled.HasValue ? (m.ReasoningEnabled.Value ? "Enabled" : "Disabled") : "Default";

            if (m.PassRatePercentage >= 100) Console.ForegroundColor = ConsoleColor.Green;
            else if (m.PassRatePercentage >= 50) Console.ForegroundColor = ConsoleColor.Yellow;
            else Console.ForegroundColor = ConsoleColor.Red;

            Console.WriteLine(string.Format("{0,-18} | {1,-7} | {2,-9} | {3,-6} | {4,-8} | {5,-8} | {6,-8} | {7,-8} | {8,-5} | {9,-8} | {10,-8:F1} | {11,-8} | {12,-9}",
                m.ModelName.Length > 18 ? m.ModelName[..15] + "..." : m.ModelName,
                backendStr,
                reasStr,
                passedRatio,
                passAt1WeightedStr,
                passAt1UnweightedStr,
                stepAccRateStr,
                passRateStr,
                m.TotalToolCalls,
                m.TotalOutputTokens,
                m.AverageGenerationTokensPerSecond,
                BenchmarkRunner.FormatMemory(m.PeakMemoryMb),
                totalTimeSec));

            Console.ResetColor();
        }

        Console.WriteLine(new string('-', 140));
    }

    /// <summary>
    /// Prints a benchmark suite breakdown (BFCL / SWE-bench / RULER).
    /// </summary>
    public static void PrintSuiteComparisonTable(IReadOnlyList<ModelBenchmarkSummary> allSummaries)
    {
        var anySuites = allSummaries.Any(s => s.ScenarioResults.Any(r => !string.IsNullOrEmpty(r.Suite)));
        if (!anySuites) return;

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(new string('=', 140));
        Console.WriteLine("   BENCHMARK SUITE BREAKDOWN (BFCL / SWE-BENCH / RULER)");
        Console.WriteLine(new string('=', 140));
        Console.ResetColor();

        Console.WriteLine(string.Format("{0,-24} | {1,-12} | {2,-10} | {3,-10} | {4,-10} | {5,-14} | {6,-10} | {7,-10}",
            "Model", "Suite", "Scenarios", "Pass@1", "Step Acc", "Turns Passed", "Turn Acc", "Pass Rate"));
        Console.WriteLine(new string('-', 140));

        foreach (var m in allSummaries)
        {
            foreach (var suite in m.GetSuiteSummaries())
            {
                var scenariosRatio = $"{suite.ScenariosPassed}/{suite.ScenariosCount}";
                var turnsRatio = $"{suite.TurnsPassed}/{suite.TotalTurns}";

                Console.WriteLine(string.Format("{0,-24} | {1,-12} | {2,-10} | {3,-10:F1}% | {4,-10:F1}% | {5,-14} | {6,-10:F1}% | {7,-10:F1}%",
                    m.ModelName.Length > 24 ? m.ModelName[..21] + "..." : m.ModelName,
                    suite.Suite,
                    scenariosRatio,
                    suite.TaskSuccessRatePercentage,
                    suite.StepAccuracyPercentage,
                    turnsRatio,
                    suite.TurnAccuracyPercentage,
                    suite.PassRatePercentage));
            }
        }

        Console.WriteLine(new string('-', 140));
    }

    /// <summary>
    /// Prints a side-by-side performance comparison of CPU vs Vulkan GPU acceleration.
    /// </summary>
    public static void PrintBackendComparisonTable(IReadOnlyList<ModelBenchmarkSummary> allSummaries)
    {
        var cpuSummaries = allSummaries.Where(s => string.Equals(s.Backend, "CPU", StringComparison.OrdinalIgnoreCase)).ToList();
        var vulkanSummaries = allSummaries.Where(s => string.Equals(s.Backend, "Vulkan", StringComparison.OrdinalIgnoreCase)).ToList();

        if (cpuSummaries.Count == 0 || vulkanSummaries.Count == 0)
        {
            return;
        }

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

        if (pairs.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(new string('=', 134));
        Console.WriteLine("   HARDWARE ACCELERATION COMPARISON: CPU vs VULKAN GPU");
        Console.WriteLine(new string('=', 134));
        Console.ResetColor();

        Console.WriteLine(string.Format("{0,-24} | {1,-11} | {2,-10} | {3,-12} | {4,-10} | {5,-10} | {6,-12} | {7,-18}",
            "Model", "Reasoning", "CPU Tok/s", "Vulkan Tok/s", "Speedup", "CPU RAM", "Vulkan VRAM", "Total Time (CPU -> Vulkan)"));
        Console.WriteLine(new string('-', 134));

        foreach (var (cpu, vulkan) in pairs)
        {
            var speedup = cpu.AverageGenerationTokensPerSecond > 0
                ? (vulkan.AverageGenerationTokensPerSecond / cpu.AverageGenerationTokensPerSecond)
                : 0.0;

            var speedupStr = speedup > 0 ? $"{speedup:F1}x" : "N/A";
            var reasStr = cpu.ReasoningEnabled.HasValue ? (cpu.ReasoningEnabled.Value ? "Enabled" : "Disabled") : "Default";
            var timeCompStr = $"{cpu.TotalTimeMs / 1000.0:F1}s -> {vulkan.TotalTimeMs / 1000.0:F1}s";
            var vulkanMemStr = vulkan.PeakVramMb > 0
                ? BenchmarkRunner.FormatMemory(vulkan.PeakVramMb)
                : BenchmarkRunner.FormatMemory(vulkan.PeakMemoryMb);

            if (speedup >= 2.0) Console.ForegroundColor = ConsoleColor.Green;
            else if (speedup >= 1.0) Console.ForegroundColor = ConsoleColor.Yellow;
            else Console.ForegroundColor = ConsoleColor.White;

            Console.WriteLine(string.Format("{0,-24} | {1,-11} | {2,-10:F1} | {3,-12:F1} | {4,-10} | {5,-10} | {6,-12} | {7,-18}",
                cpu.ModelName.Length > 24 ? cpu.ModelName[..21] + "..." : cpu.ModelName,
                reasStr,
                cpu.AverageGenerationTokensPerSecond,
                vulkan.AverageGenerationTokensPerSecond,
                speedupStr,
                BenchmarkRunner.FormatMemory(cpu.PeakMemoryMb),
                vulkanMemStr,
                timeCompStr));

            Console.ResetColor();
        }

        Console.WriteLine(new string('-', 134));
    }

    /// <summary>
    /// Prints a side-by-side comparison of benchmarks with vs without internal reasoning enabled.
    /// </summary>
    public static void PrintReasoningComparisonTable(IReadOnlyList<ModelBenchmarkSummary> allSummaries)
    {
        var withoutReasoning = allSummaries.Where(s => s.ReasoningEnabled == false).ToList();
        var withReasoning = allSummaries.Where(s => s.ReasoningEnabled == true).ToList();

        if (withoutReasoning.Count == 0 || withReasoning.Count == 0)
        {
            return;
        }

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

        if (pairs.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(new string('=', 134));
        Console.WriteLine("   REASONING IMPACT COMPARISON: WITHOUT vs WITH REASONING");
        Console.WriteLine(new string('=', 134));
        Console.ResetColor();

        Console.WriteLine(string.Format("{0,-24} | {1,-8} | {2,-12} | {3,-11} | {4,-12} | {5,-24} | {6,-18}",
            "Model", "Backend", "No-Reas Tok/s", "Reas Tok/s", "Speed Ratio", "Output Tokens (Off -> On)", "Duration Delta"));
        Console.WriteLine(new string('-', 134));

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

            Console.WriteLine(string.Format("{0,-24} | {1,-8} | {2,-12:F1} | {3,-11:F1} | {4,-12} | {5,-24} | {6,-18}",
                noReas.ModelName.Length > 24 ? noReas.ModelName[..21] + "..." : noReas.ModelName,
                backendStr,
                noReas.AverageGenerationTokensPerSecond,
                reas.AverageGenerationTokensPerSecond,
                speedRatioStr,
                tokStr,
                timeStr));
        }

        Console.WriteLine(new string('-', 134));
        Console.WriteLine();
    }

    public static void PrintMultiRunStatisticalTable(IReadOnlyList<ModelBenchmarkSummary> allSummaries)
    {
        var summariesWithMultiRun = allSummaries.Where(s => s.MultiRunStats != null).ToList();
        if (summariesWithMultiRun.Count == 0) return;

        var runsCount = summariesWithMultiRun.First().MultiRunStats!.RunsCount;

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(new string('=', 154));
        Console.WriteLine($"   STATISTICAL MULTI-RUN EVALUATION (N = {runsCount}, Mean ± StdDev)");
        Console.WriteLine(new string('=', 154));
        Console.ResetColor();

        Console.WriteLine(string.Format("{0,-24} | {1,-28} | {2,-16} | {3,-16} | {4,-14} | {5,-16} | {6,-10} | {7,-12}",
            "Model", "Configuration", "Pass Rate %", "Pass@1 (Task) %", "Step Acc %", "Prompt Tokens", "Tok/Call", "Duration"));
        Console.WriteLine(new string('-', 154));

        foreach (var s in summariesWithMultiRun)
        {
            var stats = s.MultiRunStats!;
            var configName = !string.IsNullOrWhiteSpace(s.OptimizationConfigLabel)
                ? s.OptimizationConfigLabel
                : (s.ConfigurationLabel ?? "Default");

            var passRateStr = $"{stats.PassRate.Mean:F1} ± {stats.PassRate.StdDev:F1}%";
            var taskSuccessStr = $"{stats.TaskSuccessRate.Mean:F1} ± {stats.TaskSuccessRate.StdDev:F1}%";
            var stepAccStr = $"{stats.StepAccuracy.Mean:F1} ± {stats.StepAccuracy.StdDev:F1}%";
            var promptTokensStr = $"{stats.PromptTokens.Mean:F0} ± {stats.PromptTokens.StdDev:F0}";
            var tokPerCallStr = $"{stats.PromptTokensPerToolCall.Mean:F0} ± {stats.PromptTokensPerToolCall.StdDev:F0}";
            var durationStr = $"{stats.TotalTimeSeconds.Mean:F1} ± {stats.TotalTimeSeconds.StdDev:F1}s";

            Console.WriteLine(string.Format("{0,-24} | {1,-28} | {2,-16} | {3,-16} | {4,-14} | {5,-16} | {6,-10} | {7,-12}",
                s.ModelName.Length > 24 ? s.ModelName[..21] + "..." : s.ModelName,
                configName.Length > 28 ? configName[..25] + "..." : configName,
                passRateStr,
                taskSuccessStr,
                stepAccStr,
                promptTokensStr,
                tokPerCallStr,
                durationStr));
        }

        Console.WriteLine(new string('-', 154));
        Console.WriteLine();
    }
}
