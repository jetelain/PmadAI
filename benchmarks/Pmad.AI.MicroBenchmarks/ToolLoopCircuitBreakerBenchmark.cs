using BenchmarkDotNet.Attributes;
using Pmad.AI.Guardrails;

namespace Pmad.AI.MicroBenchmarks;

[Config(typeof(FastInProcessConfig))]
public class ToolLoopCircuitBreakerBenchmark
{
    private ToolLoopCircuitBreaker _canonicalBreaker = null!;
    private ToolLoopCircuitBreaker _rawBreaker = null!;

    private static readonly string CleanStreamingText = """
        I will now review the implementation of the method. First, let's examine lines 10 to 45.
        The algorithm utilizes an in-memory dictionary to cache previously computed token estimates.
        Next, we need to verify whether the cache handles thread concurrency and eviction properly.
        Overall, the logic is sound and conforms to all project guidelines and architectural patterns.
        """;

    private static readonly string LoopingStreamingText = """
        Checking status of build.
        RepeatedPatternSeq123!
        RepeatedPatternSeq123!
        RepeatedPatternSeq123!
        RepeatedPatternSeq123!
        """;

    private const string JsonArguments = """{"filter":"*.cs","path":"src/Pmad.AI/Core","recursive":true,"maxResults":50}""";
    private const string JsonArgumentsPermuted = """{"maxResults":50,"path":"src/Pmad.AI/Core","filter":"*.cs","recursive":true}""";
    private const string BulkyObservation = "Build succeeded with 0 errors and 0 warnings. 95 tests passed, 0 failed. Execution time: 1.42s.";

    [GlobalSetup]
    public void Setup()
    {
        _canonicalBreaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            CanonicalizeJsonArguments = true,
            NgramMinLength = 16,
            NgramMinOccurrences = 3,
            MaxToolHistory = 50,
            PingPongThreshold = 2
        });

        _rawBreaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            CanonicalizeJsonArguments = false,
            NgramMinLength = 16,
            NgramMinOccurrences = 3,
            MaxToolHistory = 50,
            PingPongThreshold = 2
        });
    }

    [Benchmark(Baseline = true)]
    public LoopCheckResult DetectStreaming_Clean()
    {
        return _canonicalBreaker.DetectStreamingRepetition(CleanStreamingText.AsSpan());
    }

    [Benchmark]
    public LoopCheckResult DetectStreaming_Looping()
    {
        return _canonicalBreaker.DetectStreamingRepetition(LoopingStreamingText.AsSpan());
    }

    [Benchmark]
    public LoopCheckResult EvaluateTool_CanonicalJson()
    {
        _canonicalBreaker.ResetPingPong();
        return _canonicalBreaker.EvaluateToolInvocation("search_files", JsonArguments, "state-1");
    }

    [Benchmark]
    public LoopCheckResult EvaluateTool_RawHash()
    {
        _rawBreaker.ResetPingPong();
        return _rawBreaker.EvaluateToolInvocation("search_files", JsonArguments, "state-1");
    }

    [Benchmark]
    public void RecordObservation()
    {
        _canonicalBreaker.RecordObservation(BulkyObservation);
    }
}
