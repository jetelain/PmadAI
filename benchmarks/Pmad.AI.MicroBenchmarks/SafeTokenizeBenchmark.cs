using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using LLama;
using LLama.Common;
using Pmad.AI.LlamaSharp;

using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;

namespace Pmad.AI.MicroBenchmarks;


public enum BenchmarkPayload
{
    /// <summary>~500 chars: Standard single user turn.</summary>
    Short_500Chars,

    /// <summary>~3,500 chars: Multi-turn tool calling conversation.</summary>
    Medium_3500Chars,

    /// <summary>~6,500 chars: Just under the 7k threshold (un-chunked fast path).</summary>
    Threshold_Sub7k,

    /// <summary>~8,000 chars: Just over the 7k threshold (triggers chunking).</summary>
    Threshold_Over7k,

    /// <summary>~25,000 chars: Realistic SWE-bench / test log explosion with newlines.</summary>
    LargeTrace_25k,

    /// <summary>~25,000 chars: Dense text with few newlines (tests whitespace splitting).</summary>
    LargeDense_25k
}

[Config(typeof(FastInProcessConfig))]
public class SafeTokenizeBenchmark
{
    private LLamaWeights? _weights;
    private LLamaContext? _context;
    private SafeInteractiveExecutor? _executor;

    [Params(
        BenchmarkPayload.Short_500Chars,
        BenchmarkPayload.Medium_3500Chars,
        BenchmarkPayload.Threshold_Sub7k,
        BenchmarkPayload.Threshold_Over7k,
        BenchmarkPayload.LargeTrace_25k,
        BenchmarkPayload.LargeDense_25k
    )]
    public BenchmarkPayload Payload { get; set; }

    public string Text { get; private set; } = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        var modelsDir = LocalModelPathResolver.ResolveModelsDirectory(null);
        var candidates = new[]
        {
            Path.Combine(modelsDir, "qwen3.5-2b-ud-q4_k_xl.gguf"),
            Path.Combine(modelsDir, "qwen3.5-4b-ud-q4_k_xl.gguf"),
            Path.Combine(modelsDir, "gemma-4-e2b-it-ud-q4_k_xl.gguf")
        };

        var modelPath = candidates.FirstOrDefault(File.Exists)
            ?? Directory.EnumerateFiles(modelsDir, "*.gguf").FirstOrDefault()
            ?? throw new FileNotFoundException($"No GGUF models found in '{modelsDir}'.");

        var parameters = new ModelParams(modelPath)
        {
            ContextSize = 8192,
            GpuLayerCount = 0 // CPU execution for consistent, deterministic micro-benchmarking
        };

        _weights = LLamaWeights.LoadFromFile(parameters);
        _context = _weights.CreateContext(parameters);
        _executor = new SafeInteractiveExecutor(_context);

        Text = GeneratePayload(Payload);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _context?.Dispose();
        _weights?.Dispose();
    }

    [Benchmark(Baseline = true, Description = "SafeTokenize (7000 chars - Current Default)")]
    public int SafeTokenize_7000()
    {
        return _executor!.SafeTokenize(Text, addBos: true, maxChunkChars: 7000).Count;
    }

    [Benchmark(Description = "SafeTokenize (4096 chars)")]
    public int SafeTokenize_4096()
    {
        return _executor!.SafeTokenize(Text, addBos: true, maxChunkChars: 4096).Count;
    }

    [Benchmark(Description = "SafeTokenize (2048 chars)")]
    public int SafeTokenize_2048()
    {
        return _executor!.SafeTokenize(Text, addBos: true, maxChunkChars: 2048).Count;
    }

    [Benchmark(Description = "SafeTokenize (1024 chars)")]
    public int SafeTokenize_1024()
    {
        return _executor!.SafeTokenize(Text, addBos: true, maxChunkChars: 1024).Count;
    }

    [Benchmark(Description = "Raw Context.Tokenize (Unchunked)")]
    public int RawTokenize_Unchunked()
    {
        // Calling raw llama_tokenize on buffers > 8k on Windows MSVC causes stack overflow (0xC0000005).
        // Only run raw tokenize for payloads <= 7000 chars to protect the benchmark runner process.
        if (Text.Length > 7000)
        {
            return -1;
        }

        return _context!.Tokenize(Text, addBos: true, special: true).Length;
    }

    private static string GeneratePayload(BenchmarkPayload payload)
    {
        return payload switch
        {
            BenchmarkPayload.Short_500Chars =>
                "Calculate the monthly mortgage payment for a loan amount of $350,000 with an annual interest rate of 4.5% over a 30-year term. " +
                "Make sure to include property taxes of $4,200 annually, home insurance of $1,200 annually, and private mortgage insurance of 0.5% " +
                "until 20% equity is reached. Provide a breakdown by principal, interest, taxes, and insurance.",

            BenchmarkPayload.Medium_3500Chars =>
                string.Join("\n", Enumerable.Repeat(
                    "[USER]: Refactor the repository search service to support caching and cancellation tokens.\n" +
                    "[ASSISTANT]: I will inspect the current implementation in `src/Services/SearchService.cs` using `read_file`.\n" +
                    "[TOOL CALL]: read_file(path: \"src/Services/SearchService.cs\", start_line: 1, end_line: 80)\n" +
                    "[OBSERVATION]: public async Task<SearchResult> SearchAsync(string query) { var client = new HttpClient(); ... return results; }\n",
                    7)),

            BenchmarkPayload.Threshold_Sub7k =>
                string.Join("\n", Enumerable.Repeat(
                    "pytest testing/test_suite.py::test_case_runner PASSED [100%]\n" +
                    "  Detailed trace log: checking assertion on line 42 with expected output match...\n" +
                    "  Subprocess status: completed cleanly with return code 0 and stdout capture enabled.\n",
                    30)), // ~6,500 chars

            BenchmarkPayload.Threshold_Over7k =>
                string.Join("\n", Enumerable.Repeat(
                    "pytest testing/test_suite.py::test_case_runner PASSED [100%]\n" +
                    "  Detailed trace log: checking assertion on line 42 with expected output match...\n" +
                    "  Subprocess status: completed cleanly with return code 0 and stdout capture enabled.\n",
                    38)), // ~8,200 chars

            BenchmarkPayload.LargeTrace_25k =>
                string.Join("\n", Enumerable.Repeat(
                    "============================= test session starts =============================\n" +
                    "platform linux -- Python 3.9.18, pytest-5.2.1, py-1.8.0, pluggy-0.13.0\n" +
                    "rootdir: /workspace/pytest, inifile: tox.ini\n" +
                    "collected 128 items / 35 deselected / 93 selected\n" +
                    "FAILURES: test_fixture_formatting_scope_verbose FAILED [ 98%]\n" +
                    "E   Failed: nomatch: '*tmp_path [function] -- *'\n" +
                    "E   actual: cache [session] -- /root/venv/lib/python3.9/site-packages/_pytest/cacheprovider.py:488\n" +
                    "----------------------------- Captured stdout call -----------------------------\n" +
                    "running pytest --fixtures -v with full trace dumps and environment variables...",
                    60)), // ~25,000 chars with realistic newlines

            BenchmarkPayload.LargeDense_25k =>
                // Dense continuous JSON-like string with very few newlines (separated by spaces)
                string.Join(" ", Enumerable.Repeat(
                    "{\"event\":\"telemetry_event\",\"id\":\"trace_99482\",\"payload\":{\"data\":\"continuous_dense_string_chunk_without_newlines_testing_whitespace_fallback\",\"count\":12345,\"valid\":true}}",
                    150)), // ~25,000 chars

            _ => "Simple fallback test prompt."
        };
    }
}
