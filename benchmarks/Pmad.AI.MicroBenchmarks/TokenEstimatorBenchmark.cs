using BenchmarkDotNet.Attributes;
using Pmad.AI.Tokens;

namespace Pmad.AI.MicroBenchmarks;

[Config(typeof(FastInProcessConfig))]
public class TokenEstimatorBenchmark
{
    private static readonly string ShortProse = "Hello, can you help me write a function to calculate Fibonacci numbers in C#?";

    private static readonly string MediumCode = """
        public static class MathUtils
        {
            public static long Fibonacci(int n)
            {
                if (n <= 1) return n;
                long a = 0, b = 1;
                for (int i = 2; i <= n; i++)
                {
                    long temp = a + b;
                    a = b;
                    b = temp;
                }
                return b;
            }

            public static bool IsPrime(int number)
            {
                if (number <= 1) return false;
                if (number <= 3) return true;
                if (number % 2 == 0 || number % 3 == 0) return false;
                for (int i = 5; i * i <= number; i += 6)
                {
                    if (number % i == 0 || number % (i + 2) == 0) return false;
                }
                return true;
            }
        }
        """;

    private static readonly string LargeTrace = string.Join("\n", Enumerable.Range(1, 400).Select(i =>
        $"[2026-10-19T06:50:{i % 60:D2}.{i:D3}Z] TRACE {i} Pmad.AI.Engine.StepRunner: Executed tool 'read_file' with path='src/Core/Service_{i}.cs' status=Success latency={(i * 3.7) % 50:F1}ms"));

    private static readonly string UnicodeMultilingual = """
        English: The quick brown fox jumps over the lazy dog.
        Français: Voix ambiguë d'un cœur qui au zéphyr préfère les jattes de kiwis.
        Español: El veloz murciélago hindú comía feliz cardillo y kiwi.
        Deutsch: Victor jagt zwölf Boxkämpfer quer über den großen Sylter Deich.
        日本語: いろはにほへと ちりぬるを わかよたれそ つねならむ うゐのおくやま けふこえて あさきゆめみし ゑひもせす
        中文: 快速的棕色狐狸跳过了懒惰的狗。人工智能系统正在进行微基准测试。
        Русский: Съешь же ещё этих мягких французских булок, да выпей чаю.
        """;

    private static readonly IReadOnlyList<string> BatchMessages = Enumerable.Range(1, 20).Select(i =>
        $"Turn {i}: System processed user request regarding module {i}. Result: 0xC000000{i % 9} handled cleanly with retry count 0.").ToList();

    [Benchmark(Baseline = true)]
    public TokenMetrics Analyze_ShortProse_Span() => HeuristicTokenEstimator.Analyze(ShortProse.AsSpan());

    [Benchmark]
    public TokenMetrics Analyze_MediumCode_Span() => HeuristicTokenEstimator.Analyze(MediumCode.AsSpan());

    [Benchmark]
    public TokenMetrics Analyze_LargeTrace_Span() => HeuristicTokenEstimator.Analyze(LargeTrace.AsSpan());

    [Benchmark]
    public TokenMetrics Analyze_Unicode_Span() => HeuristicTokenEstimator.Analyze(UnicodeMultilingual.AsSpan());

    [Benchmark]
    public int Estimate_ShortProse_Span() => HeuristicTokenEstimator.Default.Estimate(ShortProse.AsSpan());

    [Benchmark]
    public int Estimate_MediumCode_Span() => HeuristicTokenEstimator.Default.Estimate(MediumCode.AsSpan());

    [Benchmark]
    public int Estimate_LargeTrace_Span() => HeuristicTokenEstimator.Default.Estimate(LargeTrace.AsSpan());

    [Benchmark]
    public int Estimate_Unicode_Span() => HeuristicTokenEstimator.Default.Estimate(UnicodeMultilingual.AsSpan());

    [Benchmark]
    public int Estimate_MediumCode_String() => HeuristicTokenEstimator.Default.Estimate(MediumCode);

    [Benchmark]
    public int Estimate_BatchMessages() => HeuristicTokenEstimator.Default.Estimate(BatchMessages);

    [Benchmark]
    public int Estimate_MediumCode_QwenPreset() => HeuristicTokenEstimator.Qwen.Estimate(MediumCode.AsSpan());

    [Benchmark]
    public int Estimate_MediumCode_GemmaPreset() => HeuristicTokenEstimator.Gemma.Estimate(MediumCode.AsSpan());
}
