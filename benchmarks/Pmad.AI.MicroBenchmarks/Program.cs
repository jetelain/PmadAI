using System;
using BenchmarkDotNet.Running;

namespace Pmad.AI.MicroBenchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("======================================================================");
        Console.WriteLine("               PMAD.AI MICROBENCHMARK SUITE                          ");
        Console.WriteLine("======================================================================");
        Console.WriteLine("Available Benchmark Categories:");
        Console.WriteLine("  1. TokenEstimatorBenchmark           - Zero-allocation HeuristicTokenEstimator");
        Console.WriteLine("  2. ToolCallHealingBenchmark          - Protocol sanitizer, markdown fence & JSON repair");
        Console.WriteLine("  3. CompactionBenchmark               - Thinking trace & tool result pruning");
        Console.WriteLine("  4. ToolLoopCircuitBreakerBenchmark   - Streaming repetition & tool ping-pong detection");
        Console.WriteLine("  5. ReadOnlyCommandValidatorBenchmark - Shell dialect AST & command security guardrails");
        Console.WriteLine("  6. LocalModelChatFormatterBenchmark  - Template prompt formatting & response parsing");
        Console.WriteLine("  7. LenientToolDispatcherBenchmark    - Alias resolution, parameter coercion & translation");
        Console.WriteLine("  8. SafeTokenizeBenchmark             - llama.cpp SafeInteractiveExecutor chunked tokenization");
        Console.WriteLine("======================================================================");

        var switcher = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly);

        if (args.Length == 0 && Console.IsInputRedirected)
        {
            // When run non-interactively without arguments, default to running TokenEstimatorBenchmark as a smoke test
            Console.WriteLine("Input redirected: Running TokenEstimatorBenchmark by default...");
            switcher.Run(["--filter", "*TokenEstimator*"]);
            return;
        }

        switcher.Run(args);
    }
}
