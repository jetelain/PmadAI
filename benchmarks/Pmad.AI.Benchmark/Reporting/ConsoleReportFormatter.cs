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
        Console.WriteLine($"==========================================================================================================");
        Console.WriteLine($"   BENCHMARK SCENARIO BREAKDOWN: {summary.ModelName.ToUpperInvariant()}");
        Console.WriteLine($"==========================================================================================================");
        Console.ResetColor();

        Console.WriteLine(string.Format("{0,-25} | {1,-8} | {2,-10} | {3,-10} | {4,-8} | {5,-10} | {6,-9} | {7,-9}",
            "Scenario", "Status", "Tool Calls", "Prompt Tok", "Gen Tok", "Gen Tok/s", "Peak RAM", "Time (ms)"));
        Console.WriteLine(new string('-', 106));

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

            Console.WriteLine(string.Format("{0,-25} | {1,-8} | {2,-10} | {3,-10} | {4,-8} | {5,-10:F1} | {6,-9} | {7,-9:F0}",
                sc.ScenarioName.Length > 25 ? sc.ScenarioName[..22] + "..." : sc.ScenarioName,
                statusStr,
                sc.TotalToolCalls,
                sc.TotalPromptTokens,
                sc.TotalOutputTokens,
                sc.GenerationTokensPerSecond,
                BenchmarkRunner.FormatMemory(sc.PeakMemoryMb),
                sc.TotalDurationMs));

            Console.ResetColor();
        }

        Console.WriteLine(new string('-', 106));
    }

    public static void PrintModelsComparisonTable(IReadOnlyList<ModelBenchmarkSummary> allSummaries)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"==========================================================================================================");
        Console.WriteLine($"   OVERALL MODELS TOOL CALLING & PERFORMANCE METRICS COMPARISON");
        Console.WriteLine($"==========================================================================================================");
        Console.ResetColor();

        Console.WriteLine(string.Format("{0,-22} | {1,-8} | {2,-10} | {3,-10} | {4,-10} | {5,-10} | {6,-9} | {7,-11}",
            "Model", "Passed", "Pass Rate", "Tool Calls", "Gen Tokens", "Avg Tok/s", "Peak RAM", "Total Time"));
        Console.WriteLine(new string('-', 107));

        foreach (var m in allSummaries)
        {
            var passedRatio = $"{m.ScenariosPassed}/{m.ScenariosRun}";
            var passRateStr = $"{m.PassRatePercentage:F0}%";
            var totalTimeSec = $"{m.TotalTimeMs / 1000.0:F2}s";

            if (m.PassRatePercentage >= 100) Console.ForegroundColor = ConsoleColor.Green;
            else if (m.PassRatePercentage >= 50) Console.ForegroundColor = ConsoleColor.Yellow;
            else Console.ForegroundColor = ConsoleColor.Red;

            Console.WriteLine(string.Format("{0,-22} | {1,-8} | {2,-10} | {3,-10} | {4,-10} | {5,-10:F1} | {6,-9} | {7,-11}",
                m.ModelName.Length > 22 ? m.ModelName[..19] + "..." : m.ModelName,
                passedRatio,
                passRateStr,
                m.TotalToolCalls,
                m.TotalOutputTokens,
                m.AverageGenerationTokensPerSecond,
                BenchmarkRunner.FormatMemory(m.PeakMemoryMb),
                totalTimeSec));

            Console.ResetColor();
        }

        Console.WriteLine(new string('-', 107));
        Console.WriteLine();
    }
}
