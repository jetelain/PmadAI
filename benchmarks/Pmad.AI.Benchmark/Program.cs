using Pmad.AI.Benchmark.Configuration;
using Pmad.AI.Benchmark.Engine;
using Pmad.AI.Benchmark.Reporting;
using Pmad.AI.Benchmark.Scenarios;
using Pmad.AI.LlamaSharp;

namespace Pmad.AI.Benchmark;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var options = BenchmarkOptions.Parse(args);
        if (options.ShowHelp)
        {
            BenchmarkOptions.PrintUsage();
            return 0;
        }

        if (options.RunUtilities)
        {
            UtilityMicroBenchmarks.RunAll();
            return 0;
        }

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(@"
╔═══════════════════════════════════════════════════════════════════╗
║          Pmad.AI - Local SLM Tool Calling Benchmark Suite         ║
╚═══════════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        var hardware = SystemHardwareInfo.Current;
        Console.WriteLine($"System:   {hardware.OsDescription}");
        Console.WriteLine($"CPU:      {hardware.CpuSummary}");
        Console.WriteLine($"GPU:      {hardware.GpuSummary}");
        Console.WriteLine();

        var scenarios = BenchmarkScenario.FilterScenarios(options.ScenarioFilter);
        Console.WriteLine($"Loaded {scenarios.Count} scenario(s): {string.Join(", ", scenarios.Select(s => s.Id))}");

        var modelsDir = LocalModelPathResolver.ResolveModelsDirectory(null);
        var allSummaries = new List<ModelBenchmarkSummary>();

        // Scenario execution targets
        var targets = ResolveExecutionTargets(options, modelsDir);

        if (targets.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\nNo local GGUF model files found in '{modelsDir}'.");
            Console.WriteLine("Switching to mock simulation mode to demonstrate tool calling workflows and benchmark metrics.");
            Console.WriteLine("Tip: Provide '--download' to automatically download and test curated presets.");
            Console.ResetColor();

            targets.Add(new ModelTarget.Mock("mock-qwen-3.5"));
            targets.Add(new ModelTarget.Mock("mock-gemma-4"));
        }

        var executionPlans = BuildExecutionPlans(targets, options);
        Console.WriteLine($"Planned {executionPlans.Count} benchmark run(s) across {targets.Count} target(s).");

        for (int i = 0; i < executionPlans.Count; i++)
        {
            var plan = executionPlans[i];
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"\n==========================================================================================================");
            Console.WriteLine($" [{i + 1}/{executionPlans.Count}] RUNNING BENCHMARK: {plan.TargetName.ToUpperInvariant()} [{plan.ConfigurationLabel.ToUpperInvariant()}]");
            Console.WriteLine($"==========================================================================================================");
            Console.ResetColor();

            ModelBenchmarkSummary summary;

            if (plan.Target is ModelTarget.Mock mockTarget)
            {
                using var mockClient = new MockBenchmarkChatClient(
                    modelId: mockTarget.Name,
                    enableReasoning: plan.EnableReasoning ?? false,
                    backend: plan.Backend);

                var runner = new BenchmarkRunner(
                    client: mockClient,
                    modelId: mockTarget.Name,
                    verbose: options.Verbose,
                    backend: plan.Backend,
                    gpuLayers: plan.GpuLayers,
                    reasoningEnabled: plan.EnableReasoning,
                    reasoningEffort: plan.ReasoningEffort,
                    configurationLabel: plan.ConfigurationLabel);

                summary = await runner.RunAllAsync(scenarios);
            }
            else if (plan.Target is ModelTarget.PresetTarget presetTarget)
            {
                var preset = presetTarget.Preset;
                var modelPath = preset.GetLocalPath(modelsDir);

                if (!File.Exists(modelPath))
                {
                    if (options.DownloadMissing)
                    {
                        Console.WriteLine($"\n[ModelDownloader] Downloading preset '{preset.Id}' ({preset.ApproxSize})...");
                        var progress = new Progress<ModelDownloadProgress>(p =>
                        {
                            if (p.ProgressPercentage.HasValue)
                            {
                                Console.Write($"\rDownloading {p.FileName}: {p.ProgressPercentage:F1}%");
                            }
                        });
                        var downloader = new ModelDownloader(statusLogger: Console.WriteLine, progress: progress);
                        await downloader.DownloadModelAsync(preset, destinationDir: modelsDir, downloadVision: false);
                        Console.WriteLine("\nDownload complete!");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"\nSkipping preset '{preset.Id}': file '{modelPath}' not found. (Use --download to fetch).");
                        Console.ResetColor();
                        continue;
                    }
                }

                Console.WriteLine($"\nInitializing LlamaSharp client for '{preset.Name}' (Backend: {plan.Backend}, GPU layers: {plan.GpuLayers}, Reasoning: {plan.EnableReasoning?.ToString() ?? "Default"})...");
                var clientOptions = new LlamaSharpOptions
                {
                    ContextSize = options.ContextSize,
                    GpuLayerCount = plan.GpuLayers,
                    PromptFormat = preset.PromptFormat,
                    EnableReasoning = plan.EnableReasoning,
                    ReasoningEffort = plan.ReasoningEffort
                };

                using var client = LlamaSharpChatClient.CreateFromFile(
                    modelPath: modelPath,
                    options: clientOptions,
                    promptFormat: preset.PromptFormat);

                var runner = new BenchmarkRunner(
                    client: client,
                    modelId: preset.Name,
                    verbose: options.Verbose,
                    backend: plan.Backend,
                    gpuLayers: plan.GpuLayers,
                    reasoningEnabled: plan.EnableReasoning,
                    reasoningEffort: plan.ReasoningEffort,
                    configurationLabel: plan.ConfigurationLabel);

                summary = await runner.RunAllAsync(scenarios);
            }
            else if (plan.Target is ModelTarget.CustomPath customTarget)
            {
                if (!File.Exists(customTarget.FilePath))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Model file not found: {customTarget.FilePath}");
                    Console.ResetColor();
                    continue;
                }

                Console.WriteLine($"\nInitializing LlamaSharp client from file '{customTarget.FilePath}' (Backend: {plan.Backend}, GPU layers: {plan.GpuLayers}, Reasoning: {plan.EnableReasoning?.ToString() ?? "Default"})...");
                var clientOptions = new LlamaSharpOptions
                {
                    ContextSize = options.ContextSize,
                    GpuLayerCount = plan.GpuLayers,
                    EnableReasoning = plan.EnableReasoning,
                    ReasoningEffort = plan.ReasoningEffort
                };

                using var client = LlamaSharpChatClient.CreateFromFile(
                    modelPath: customTarget.FilePath,
                    options: clientOptions);

                var runner = new BenchmarkRunner(
                    client: client,
                    modelId: Path.GetFileNameWithoutExtension(customTarget.FilePath),
                    verbose: options.Verbose,
                    backend: plan.Backend,
                    gpuLayers: plan.GpuLayers,
                    reasoningEnabled: plan.EnableReasoning,
                    reasoningEffort: plan.ReasoningEffort,
                    configurationLabel: plan.ConfigurationLabel);

                summary = await runner.RunAllAsync(scenarios);
            }
            else
            {
                continue;
            }

            ConsoleReportFormatter.PrintScenarioResultsTable(summary);
            allSummaries.Add(summary);

            // Reclaim native allocations between model runs
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        if (allSummaries.Count > 0)
        {
            ConsoleReportFormatter.PrintModelsComparisonTable(allSummaries);
            ConsoleReportFormatter.PrintBackendComparisonTable(allSummaries);
            ConsoleReportFormatter.PrintReasoningComparisonTable(allSummaries);

            // Determine transcript export path
            var transcriptPath = options.OutputTranscriptPath;
            if (string.IsNullOrWhiteSpace(transcriptPath) && !string.IsNullOrWhiteSpace(options.OutputMarkdownPath))
            {
                var dir = Path.GetDirectoryName(options.OutputMarkdownPath);
                var nameWithoutExt = Path.GetFileNameWithoutExtension(options.OutputMarkdownPath);
                var transcriptFile = $"{nameWithoutExt}.transcript.md";
                transcriptPath = string.IsNullOrWhiteSpace(dir) ? transcriptFile : Path.Combine(dir, transcriptFile);
            }

            if (!string.IsNullOrWhiteSpace(transcriptPath))
            {
                await TranscriptReportWriter.WriteToFileAsync(transcriptPath, allSummaries, hardware);
                Console.WriteLine($"Full execution transcript exported to: {Path.GetFullPath(transcriptPath)}");
            }

            if (!string.IsNullOrWhiteSpace(options.OutputMarkdownPath))
            {
                var transcriptFileName = !string.IsNullOrWhiteSpace(transcriptPath) ? Path.GetFileName(transcriptPath) : null;
                await MarkdownReportWriter.WriteToFileAsync(options.OutputMarkdownPath, allSummaries, hardware, transcriptFileName);
                Console.WriteLine($"Markdown benchmark report exported to: {Path.GetFullPath(options.OutputMarkdownPath)}");
            }

            if (!string.IsNullOrWhiteSpace(options.OutputJsonPath))
            {
                await JsonReportWriter.WriteToFileAsync(options.OutputJsonPath, allSummaries);
                Console.WriteLine($"JSON benchmark report exported to: {Path.GetFullPath(options.OutputJsonPath)}");
            }
        }

        var anyFailed = allSummaries.Any(s => s.ScenariosFailed > 0);
        return anyFailed ? 1 : 0;
    }

    private static List<BenchmarkExecutionPlan> BuildExecutionPlans(
        List<ModelTarget> targets,
        BenchmarkOptions options)
    {
        var plans = new List<BenchmarkExecutionPlan>();

        // Determine backends to evaluate
        var backends = new List<(string Name, int Layers)>();
        if (options.CompareBackends)
        {
            backends.Add(("CPU", 0));
            backends.Add(("Vulkan", options.GpuLayerCount > 0 ? options.GpuLayerCount : 99));
        }
        else if (string.Equals(options.Backend, "cpu", StringComparison.OrdinalIgnoreCase) || options.GpuLayerCount == 0)
        {
            backends.Add(("CPU", 0));
        }
        else
        {
            backends.Add(("Vulkan", options.GpuLayerCount > 0 ? options.GpuLayerCount : 99));
        }

        foreach (var target in targets)
        {
            // Determine reasoning modes to evaluate
            var reasoningModes = new List<bool?>();
            if (options.CompareReasoning)
            {
                if (target is ModelTarget.PresetTarget pt && !pt.Preset.SupportsReasoning)
                {
                    reasoningModes.Add(false);
                }
                else
                {
                    reasoningModes.Add(false);
                    reasoningModes.Add(true);
                }
            }
            else if (options.EnableReasoning.HasValue)
            {
                reasoningModes.Add(options.EnableReasoning.Value);
            }
            else
            {
                reasoningModes.Add(null);
            }

            foreach (var backend in backends)
            {
                foreach (var reasoning in reasoningModes)
                {
                    string label;
                    var hasBackendVariance = options.CompareBackends;
                    var hasReasoningVariance = options.CompareReasoning;

                    if (hasBackendVariance && hasReasoningVariance)
                    {
                        var reasName = reasoning == true ? "Reasoning" : "No Reasoning";
                        label = $"{backend.Name} | {reasName}";
                    }
                    else if (hasBackendVariance)
                    {
                        label = backend.Name;
                    }
                    else if (hasReasoningVariance)
                    {
                        label = reasoning == true ? "Reasoning" : "No Reasoning";
                    }
                    else
                    {
                        label = backend.Name;
                    }

                    plans.Add(new BenchmarkExecutionPlan(
                        target,
                        backend.Name,
                        backend.Layers,
                        reasoning,
                        options.ReasoningEffort,
                        label));
                }
            }
        }

        return plans;
    }

    private static List<ModelTarget> ResolveExecutionTargets(BenchmarkOptions options, string modelsDir)
    {
        var targets = new List<ModelTarget>();

        if (options.MockMode)
        {
            targets.Add(new ModelTarget.Mock("mock-qwen-3.5"));
            targets.Add(new ModelTarget.Mock("mock-gemma-4"));
            return targets;
        }

        if (options.AllPresets)
        {
            foreach (var preset in LlamaModelPreset.Presets)
            {
                targets.Add(new ModelTarget.PresetTarget(preset));
            }
            return targets;
        }

        if (!string.IsNullOrWhiteSpace(options.Model))
        {
            var matchedPreset = LlamaModelPreset.Find(options.Model);
            if (matchedPreset != null)
            {
                targets.Add(new ModelTarget.PresetTarget(matchedPreset));
            }
            else
            {
                targets.Add(new ModelTarget.CustomPath(options.Model));
            }
            return targets;
        }

        // Auto-detect existing models in cache directory
        if (Directory.Exists(modelsDir))
        {
            foreach (var preset in LlamaModelPreset.Presets)
            {
                var expectedPath = preset.GetLocalPath(modelsDir);
                if (File.Exists(expectedPath))
                {
                    targets.Add(new ModelTarget.PresetTarget(preset));
                }
            }
        }

        return targets;
    }

    private abstract record ModelTarget
    {
        public sealed record Mock(string Name) : ModelTarget;
        public sealed record PresetTarget(LlamaModelPreset Preset) : ModelTarget;
        public sealed record CustomPath(string FilePath) : ModelTarget;
    }

    private sealed record BenchmarkExecutionPlan(
        ModelTarget Target,
        string Backend,
        int GpuLayers,
        bool? EnableReasoning,
        string? ReasoningEffort,
        string ConfigurationLabel)
    {
        public string TargetName => Target switch
        {
            ModelTarget.Mock m => m.Name,
            ModelTarget.PresetTarget p => p.Preset.Name,
            ModelTarget.CustomPath c => Path.GetFileNameWithoutExtension(c.FilePath),
            _ => "Unknown"
        };
    }
}
