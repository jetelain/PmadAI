namespace Pmad.AI.Benchmark.Configuration;

/// <summary>
/// Command-line configuration options for the tool calling benchmark runner.
/// </summary>
public sealed class BenchmarkOptions
{
    /// <summary>
    /// Target model identifier (preset id e.g. "qwen3.5-2b", "gemma-4-e2b") or direct path to a .gguf file.
    /// If null and not using Mock, defaults to all locally available presets.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// When true, attempts to benchmark all curated presets in <see cref="Pmad.AI.LlamaSharp.LlamaModelPreset.Presets"/>.
    /// </summary>
    public bool AllPresets { get; set; }

    /// <summary>
    /// When true, automatically downloads model files if missing from the local cache.
    /// </summary>
    public bool DownloadMissing { get; set; }

    /// <summary>
    /// When true, executes with a deterministic mock chat client to test and showcase tool calling flows,
    /// assertions, and metrics without requiring multi-gigabyte GGUF model downloads.
    /// </summary>
    public bool MockMode { get; set; }

    /// <summary>
    /// Name or ID of specific scenario to run (e.g. "weather", "calculator", "support", "direct", or "all").
    /// </summary>
    public string ScenarioFilter { get; set; } = "all";

    /// <summary>
    /// Number of GPU layers to offload to native GPU acceleration (default 99; set 0 for CPU only).
    /// </summary>
    public int GpuLayerCount { get; set; } = 99;

    /// <summary>
    /// Context size tokens for model inference (default 8192).
    /// </summary>
    public int ContextSize { get; set; } = 8192;

    /// <summary>
    /// Optional file path to export benchmark results as Markdown.
    /// </summary>
    public string? OutputMarkdownPath { get; set; }

    /// <summary>
    /// Optional file path to export benchmark results as JSON.
    /// </summary>
    public string? OutputJsonPath { get; set; }

    /// <summary>
    /// When true, outputs verbose logging including formatted prompts, raw generation chunks, and full turns.
    /// </summary>
    public bool Verbose { get; set; }

    /// <summary>
    /// Whether help was requested.
    /// </summary>
    public bool ShowHelp { get; set; }

    /// <summary>
    /// Parses command-line arguments into a <see cref="BenchmarkOptions"/> instance.
    /// </summary>
    public static BenchmarkOptions Parse(string[] args)
    {
        var options = new BenchmarkOptions();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "-h" or "--help" or "/?")
            {
                options.ShowHelp = true;
                return options;
            }

            if (arg is "-m" or "--model" && i + 1 < args.Length)
            {
                options.Model = args[++i];
            }
            else if (arg is "--all" or "--all-presets")
            {
                options.AllPresets = true;
            }
            else if (arg is "-d" or "--download")
            {
                options.DownloadMissing = true;
            }
            else if (arg is "--mock" or "--dry-run")
            {
                options.MockMode = true;
            }
            else if (arg is "-s" or "--scenario" && i + 1 < args.Length)
            {
                options.ScenarioFilter = args[++i];
            }
            else if (arg is "-g" or "--gpu" && i + 1 < args.Length && int.TryParse(args[++i], out var gpu))
            {
                options.GpuLayerCount = gpu;
            }
            else if (arg is "-c" or "--context" && i + 1 < args.Length && int.TryParse(args[++i], out var ctx))
            {
                options.ContextSize = ctx;
            }
            else if (arg is "-o" or "--output" or "--markdown" && i + 1 < args.Length)
            {
                options.OutputMarkdownPath = args[++i];
            }
            else if (arg is "-j" or "--json" && i + 1 < args.Length)
            {
                options.OutputJsonPath = args[++i];
            }
            else if (arg is "-v" or "--verbose")
            {
                options.Verbose = true;
            }
        }

        return options;
    }

    /// <summary>
    /// Displays command-line help instructions.
    /// </summary>
    public static void PrintUsage()
    {
        Console.WriteLine(@"
Pmad.AI Tool Calling Benchmark & Model Metrics Suite
====================================================
Usage:
  dotnet run --project benchmarks/Pmad.AI.Benchmark -- [options]

Options:
  -m, --model <id|path>    Model preset (e.g. 'qwen3.5-2b', 'gemma-4-e2b') or path to .gguf.
                           If omitted and no models are found, runs in mock simulation mode.
  --all, --all-presets     Benchmark all supported model presets.
  -d, --download           Download models automatically if missing from local cache.
  --mock, --dry-run        Run with mock simulated chat client (showcases tool calling & metrics immediately).
  -s, --scenario <name>    Filter scenario to run: 'weather', 'calculator', 'support', 'direct', or 'all' (default: all).
  -g, --gpu <layers>       GPU layers to offload (default: 99; use 0 for CPU).
  -c, --context <tokens>   Context size (default: 8192).
  -o, --output <file>      Export benchmark report to a Markdown file.
  -j, --json <file>        Export benchmark metrics to a JSON file.
  -v, --verbose            Print detailed turn-by-turn prompts, tool calls, and model outputs.
  -h, --help               Display this help message.

Examples:
  # Quick mock simulation showcasing all scenarios, tool calling loops, and metrics:
  dotnet run --project benchmarks/Pmad.AI.Benchmark -- --mock

  # Benchmark a downloaded preset with CPU/GPU and generate a Markdown report:
  dotnet run --project benchmarks/Pmad.AI.Benchmark -- --model qwen3.5-2b --output report.md

  # Download and benchmark all presets:
  dotnet run --project benchmarks/Pmad.AI.Benchmark -- --all --download --output report.md
");
    }
}
