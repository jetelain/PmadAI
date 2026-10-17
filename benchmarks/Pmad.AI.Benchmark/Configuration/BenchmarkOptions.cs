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
    /// When true, benchmarks all available execution targets (all GGUF presets + designated Ollama models).
    /// </summary>
    public bool AllTargets { get; set; }

    /// <summary>
    /// Comma-separated list of designated Ollama model names to benchmark (e.g. "Qwen3.8-27B:latest,jetelain/Qwen3.8-27B:iq3").
    /// </summary>
    public string? OllamaModelsList { get; set; }

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
    /// Number of CPU threads allocated to background speculative SLM summarization (default 2).
    /// </summary>
    public int SlmThreads { get; set; } = 2;

    /// <summary>
    /// Optional file path to export benchmark results as Markdown.
    /// </summary>
    public string? OutputMarkdownPath { get; set; }

    /// <summary>
    /// Optional file path to export benchmark results as JSON.
    /// </summary>
    public string? OutputJsonPath { get; set; }

    /// <summary>
    /// Optional file path to export full turn-by-turn execution transcript and failure analysis.
    /// Defaults to companion .transcript.md file when OutputMarkdownPath is provided.
    /// </summary>
    public string? OutputTranscriptPath { get; set; }

    /// <summary>
    /// Reasoning effort level (e.g. "low", "medium", "high", "xhigh", "none").
    /// </summary>
    public string? ReasoningEffort { get; set; }

    /// <summary>
    /// When set, explicitly enables or disables internal reasoning / chain-of-thought for models supporting it.
    /// </summary>
    public bool? EnableReasoning { get; set; }

    /// <summary>
    /// When true, executes each target both with and without reasoning enabled to compare impact on tokens, speed, and accuracy.
    /// </summary>
    public bool CompareReasoning { get; set; }

    /// <summary>
    /// Hardware backend to target: "cpu", "vulkan", or "all" (runs both CPU and Vulkan).
    /// </summary>
    public string? Backend { get; set; }

    /// <summary>
    /// When true, benchmarks each model target on both CPU (0 GPU layers) and Vulkan GPU acceleration.
    /// </summary>
    public bool CompareBackends { get; set; }

    /// <summary>
    /// When true, executes a full comparison matrix across both backends (CPU vs Vulkan) and reasoning states (With vs Without Reasoning).
    /// </summary>
    public bool CompareAll { get; set; }

    /// <summary>
    /// When true, outputs verbose logging including formatted prompts, raw generation chunks, and full turns.
    /// </summary>
    public bool Verbose { get; set; }

    /// <summary>
    /// When true, executes targeted micro-benchmarks for agent utilities (healing, loop detection, compaction allocations, shell security, fuzzy dispatch).
    /// </summary>
    public bool RunUtilities { get; set; }

    /// <summary>
    /// When true, executes the systematic optimization matrix (Baseline, individual utilities, and all combined).
    /// </summary>
    public bool OptMatrix { get; set; }

    /// <summary>
    /// Identifier of a specific optimization configuration to execute (e.g. "baseline", "opt-healing", "all-optimizations").
    /// </summary>
    public string? ConfigId { get; set; }

    /// <summary>
    /// Name or tag of local Ollama model to benchmark (e.g. "Qwen3.8-27B:latest", "jetelain/Qwen3.8-27B:iq3").
    /// </summary>
    public string? OllamaModel { get; set; }

    /// <summary>
    /// Base URL for Ollama daemon (default: http://localhost:11434).
    /// </summary>
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// When true, resumes execution from the latest checkpoint file, skipping already completed runs.
    /// </summary>
    public bool Resume { get; set; }

    /// <summary>
    /// Optional custom path for the checkpoint progress JSON file.
    /// Defaults to "benchmark-progress.json".
    /// </summary>
    public string? CheckpointPath { get; set; }

    /// <summary>
    /// True if context size was explicitly set via CLI options.
    /// </summary>
    public bool IsContextSizeExplicit { get; set; }

    /// <summary>
    /// Execution mode for SWE-bench tool actions: "mock" (default state-aware in-memory executor) or "docker" (container sandbox).
    /// </summary>
    public string SweSandbox { get; set; } = "mock";

    /// <summary>
    /// Optional container name or image tag for Docker SWE-bench sandbox execution.
    /// </summary>
    public string? DockerContainer { get; set; }

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

            if (arg is "-m" or "--model" or "--preset" && i + 1 < args.Length)
            {
                options.Model = args[++i];
            }
            else if (arg is "--all-targets" or "--all-models")
            {
                options.AllTargets = true;
                options.AllPresets = true;
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
            else if (arg is "-b" or "--backend" or "--backends" && i + 1 < args.Length)
            {
                var val = args[++i];
                if (val.Equals("all", StringComparison.OrdinalIgnoreCase) || val.Equals("compare", StringComparison.OrdinalIgnoreCase))
                {
                    options.CompareBackends = true;
                }
                else
                {
                    options.Backend = val;
                }
            }
            else if (arg is "--compare-backends" or "--cpu-vs-vulkan" or "--vulkan-vs-cpu")
            {
                options.CompareBackends = true;
            }
            else if (arg is "--compare-reasoning" or "--reasoning-compare")
            {
                options.CompareReasoning = true;
            }
            else if (arg is "--compare-all" or "--matrix" or "--full-matrix")
            {
                options.CompareAll = true;
                options.CompareBackends = true;
                options.CompareReasoning = true;
            }
            else if (arg is "--opt-matrix" or "--optimization-matrix")
            {
                options.OptMatrix = true;
            }
            else if (arg is "--config" or "--opt" && i + 1 < args.Length)
            {
                var val = args[++i];
                if (val.Equals("matrix", StringComparison.OrdinalIgnoreCase) || val.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    options.OptMatrix = true;
                }
                else
                {
                    options.ConfigId = val;
                }
            }
            else if (arg is "--ollama-models" or "--ollama-list" && i + 1 < args.Length)
            {
                options.OllamaModelsList = args[++i];
            }
            else if (arg is "--ollama" && i + 1 < args.Length)
            {
                options.OllamaModel = args[++i];
            }
            else if (arg is "--ollama-url" && i + 1 < args.Length)
            {
                options.OllamaBaseUrl = args[++i];
            }
            else if (arg is "--resume")
            {
                options.Resume = true;
            }
            else if (arg is "--checkpoint" && i + 1 < args.Length)
            {
                options.CheckpointPath = args[++i];
            }
            else if (arg is "--sandbox" or "--swe-sandbox" && i + 1 < args.Length)
            {
                options.SweSandbox = args[++i];
            }
            else if (arg is "--docker" or "--docker-sandbox")
            {
                options.SweSandbox = "docker";
            }
            else if (arg is "--docker-container" or "--container" && i + 1 < args.Length)
            {
                options.DockerContainer = args[++i];
            }
            else if (arg is "-g" or "--gpu" && i + 1 < args.Length && int.TryParse(args[++i], out var gpu))
            {
                options.GpuLayerCount = gpu;
            }
            else if (arg is "-c" or "--context" && i + 1 < args.Length && int.TryParse(args[++i], out var ctx))
            {
                options.ContextSize = ctx;
                options.IsContextSizeExplicit = true;
            }
            else if (arg is "-o" or "--output" or "--markdown" or "--out-markdown" && i + 1 < args.Length)
            {
                options.OutputMarkdownPath = args[++i];
            }
            else if (arg is "-j" or "--json" or "--out-json" && i + 1 < args.Length)
            {
                options.OutputJsonPath = args[++i];
            }
            else if (arg is "-t" or "--transcript" or "--out-transcript" && i + 1 < args.Length)
            {
                options.OutputTranscriptPath = args[++i];
            }
            else if (arg is "-re" or "--reasoning-effort" or "--reasoning" && i + 1 < args.Length)
            {
                options.ReasoningEffort = args[++i];
            }
            else if (arg is "--enable-reasoning")
            {
                options.EnableReasoning = true;
            }
            else if (arg is "--no-reasoning" or "--disable-reasoning")
            {
                options.EnableReasoning = false;
            }
            else if (arg is "-u" or "--utilities" or "--utility-benchmarks")
            {
                options.RunUtilities = true;
            }
            else if (arg is "--slm-threads" or "--speculative-threads" && i + 1 < args.Length && int.TryParse(args[++i], out var slmThreads))
            {
                options.SlmThreads = slmThreads;
            }
            else if (arg is "-v" or "--verbose")
            {
                options.Verbose = true;
            }
        }

        if (options.CompareAll)
        {
            options.CompareBackends = true;
            options.CompareReasoning = true;
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

Target Models & Providers:
  -m, --model <id|path>    Model preset (e.g. 'qwen3.5-2b', 'gemma-4-e2b') or path to .gguf.
                           If omitted and no models are found, runs in mock simulation mode.
  --all-targets            Benchmark all targets: all GGUF presets + designated Ollama models.
  --all, --all-presets     Benchmark all supported in-process GGUF presets.
  --ollama <model>         Target local Ollama model (e.g. 'Qwen3.8-27B:latest').
  --ollama-models <m1,m2>  Target multiple designated Ollama models (comma-separated).
  --ollama-url <url>       Ollama endpoint URL (default: http://localhost:11434).
  -d, --download           Download GGUF models automatically if missing from local cache.
  --mock, --dry-run        Run with mock simulated chat client (showcases tool calling & metrics immediately).
  -s, --scenario <name>    Filter scenario: 'all' (all 18 scenarios across BFCL, SWE-bench & RULER),
                           'all-small' (15 scenarios: BFCL + SWE-bench + RULER 8k/16k, excluding deep),
                           'bfcl' (6 scenarios: Simple, Multiple, Parallel, Irrelevance, Multi-Turn, Strict),
                           'swebench' (7 trace replays), 'ruler' (5 context depths),
                           'deep' (32k/64k/128k), or comma-separated.

Optimization Matrix:
  --opt-matrix             Run full 7-configuration optimization matrix (Baseline, single utilities, and all combined).
  --config <id>            Execute specific configuration: 'baseline', 'opt-healing', 'opt-thinking-compaction',
                           'opt-tool-compaction', 'opt-speculative-slm', 'opt-lenient-dispatch', 'all-optimizations'.

Hardware & Backend:
  -b, --backend <name>     Backend to use: 'cpu', 'vulkan', or 'all' (default: vulkan if GPU layers > 0, else cpu).
  --compare-backends       Run each model on both CPU (0 GPU layers) and Vulkan GPU acceleration.
  -g, --gpu <layers>       GPU layers to offload (default: 99; use 0 for CPU).
  -c, --context <tokens>   Context size (default: 65536 for SLMs < 9B; 131072 for LLMs 9B+).
  --slm-threads <count>    CPU threads for background speculative SLM summarizer (default: 2).

Reasoning:
  --compare-reasoning      Run each model with and without reasoning enabled to compare speed & token overhead.
  --enable-reasoning       Explicitly enable reasoning / thinking tokens for reasoning models.
  --no-reasoning           Explicitly disable reasoning / thinking tokens for reasoning models.
  -re, --reasoning-effort  Reasoning effort level ('low', 'medium', 'high', 'xhigh', 'none').

Resilience & Checkpointing:
  --resume                 Resume benchmark from latest checkpoint, skipping already completed runs.
  --checkpoint <path>      Path to checkpoint progress JSON (default: benchmark-progress.json).

Reporting & Diagnostics:
  -o, --output <file>      Export benchmark report to a Markdown file.
  -t, --transcript <file>  Export full turn-by-turn prompts, reasoning traces, tool inputs/outputs, and failure analysis.
  -j, --json <file>        Export benchmark metrics to a JSON file.
  -v, --verbose            Print detailed turn-by-turn prompts, tool calls, and model outputs.
  -h, --help               Display this help message.
");
    }
}
