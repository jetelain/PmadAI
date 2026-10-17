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
        Console.WriteLine(new string('=', 134));
        Console.WriteLine($"   BENCHMARK SCENARIO BREAKDOWN: {summary.DisplayName.ToUpperInvariant()}");
        Console.WriteLine(new string('=', 134));
        Console.ResetColor();

        Console.WriteLine(string.Format("{0,-23} | {1,-6} | {2,-6} | {3,-8} | {4,-7} | {5,-6} | {6,-6} | {7,-8} | {8,-8} | {9,-9} | {10,-8} | {11,-8} | {12,-8}",
            "Scenario", "Status", "Pass@1", "Step Acc", "Score", "Weight", "Tools", "Prompt", "Gen Tok", "Gen Tok/s", "RAM", "GPU VRAM", "Time(ms)"));
        Console.WriteLine(new string('-', 134));

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

            Console.WriteLine(string.Format("{0,-23} | {1,-6} | {2,-6} | {3,-8} | {4,-7} | {5,-6} | {6,-6} | {7,-8} | {8,-8} | {9,-9:F1} | {10,-8} | {11,-8} | {12,-8:F0}",
                sc.ScenarioName.Length > 23 ? sc.ScenarioName[..20] + "..." : sc.ScenarioName,
                statusStr,
                passAt1Str,
                stepAccStr,
                scoreStr,
                weightStr,
                sc.TotalToolCalls,
                sc.TotalPromptTokens,
                sc.TotalOutputTokens,
                sc.GenerationTokensPerSecond,
                BenchmarkRunner.FormatMemory(sc.PeakMemoryMb),
                vramStr,
                sc.TotalDurationMs));

            Console.ResetColor();
        }

        Console.WriteLine(new string('-', 134));
    }

    public static void PrintModelsComparisonTable(IReadOnlyList<ModelBenchmarkSummary> allSummaries)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(new string('=', 134));
        Console.WriteLine("   OVERALL MODELS TOOL CALLING & PERFORMANCE METRICS COMPARISON");
        Console.WriteLine(new string('=', 134));
        Console.ResetColor();

        Console.WriteLine(string.Format("{0,-20} | {1,-7} | {2,-9} | {3,-6} | {4,-7} | {5,-8} | {6,-9} | {7,-6} | {8,-8} | {9,-8} | {10,-8} | {11,-8} | {12,-9}",
            "Model", "Backend", "Reasoning", "Passed", "Pass@1", "Step Acc", "Pass Rate", "Tools", "Gen Toks", "Avg Tok/s", "RAM", "GPU VRAM", "Total Time"));
        Console.WriteLine(new string('-', 134));

        foreach (var m in allSummaries)
        {
            var passedRatio = $"{m.ScenariosPassed}/{m.ScenariosRun}";
            var passAt1RateStr = $"{m.TaskSuccessRatePercentage:F1}%";
            var stepAccRateStr = $"{m.StepAccuracyPercentage:F1}%";
            var passRateStr = $"{m.PassRatePercentage:F1}%";
            var totalTimeSec = $"{m.TotalTimeMs / 1000.0:F2}s";
            var backendStr = m.Backend ?? (m.GpuLayers > 0 ? "Vulkan" : "CPU");
            var reasStr = m.ReasoningEnabled.HasValue ? (m.ReasoningEnabled.Value ? "Enabled" : "Disabled") : "Default";
            var vramStr = m.PeakVramMb > 0 ? BenchmarkRunner.FormatMemory(m.PeakVramMb) : "-";

            if (m.PassRatePercentage >= 100) Console.ForegroundColor = ConsoleColor.Green;
            else if (m.PassRatePercentage >= 50) Console.ForegroundColor = ConsoleColor.Yellow;
            else Console.ForegroundColor = ConsoleColor.Red;

            Console.WriteLine(string.Format("{0,-20} | {1,-7} | {2,-9} | {3,-6} | {4,-7} | {5,-8} | {6,-9} | {7,-6} | {8,-8} | {9,-8:F1} | {10,-8} | {11,-8} | {12,-9}",
                m.ModelName.Length > 20 ? m.ModelName[..17] + "..." : m.ModelName,
                backendStr,
                reasStr,
                passedRatio,
                passAt1RateStr,
                stepAccRateStr,
                passRateStr,
                m.TotalToolCalls,
                m.TotalOutputTokens,
                m.AverageGenerationTokensPerSecond,
                BenchmarkRunner.FormatMemory(m.PeakMemoryMb),
                vramStr,
                totalTimeSec));

            Console.ResetColor();
        }

        Console.WriteLine(new string('-', 134));
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
}
