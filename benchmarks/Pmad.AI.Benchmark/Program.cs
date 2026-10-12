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

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(@"
╔═══════════════════════════════════════════════════════════════════╗
║          Pmad.AI - Local SLM Tool Calling Benchmark Suite         ║
╚═══════════════════════════════════════════════════════════════════╝");
        Console.ResetColor();

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

        foreach (var target in targets)
        {
            ModelBenchmarkSummary summary;

            if (target is ModelTarget.Mock mockTarget)
            {
                using var mockClient = new MockBenchmarkChatClient(mockTarget.Name);
                var runner = new BenchmarkRunner(mockClient, mockTarget.Name, options.Verbose);
                summary = await runner.RunAllAsync(scenarios);
            }
            else if (target is ModelTarget.PresetTarget presetTarget)
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

                Console.WriteLine($"\nInitializing LlamaSharp client for '{preset.Name}' (Format: {preset.PromptFormat}, GPU layers: {options.GpuLayerCount})...");
                var clientOptions = new LlamaSharpOptions
                {
                    ContextSize = options.ContextSize,
                    GpuLayerCount = options.GpuLayerCount,
                    PromptFormat = preset.PromptFormat
                };

                using var client = LlamaSharpChatClient.CreateFromFile(
                    modelPath: modelPath,
                    options: clientOptions,
                    promptFormat: preset.PromptFormat);

                var runner = new BenchmarkRunner(client, preset.Name, options.Verbose);
                summary = await runner.RunAllAsync(scenarios);
            }
            else if (target is ModelTarget.CustomPath customTarget)
            {
                if (!File.Exists(customTarget.FilePath))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Model file not found: {customTarget.FilePath}");
                    Console.ResetColor();
                    continue;
                }

                Console.WriteLine($"\nInitializing LlamaSharp client from file '{customTarget.FilePath}' (GPU layers: {options.GpuLayerCount})...");
                var clientOptions = new LlamaSharpOptions
                {
                    ContextSize = options.ContextSize,
                    GpuLayerCount = options.GpuLayerCount
                };

                using var client = LlamaSharpChatClient.CreateFromFile(
                    modelPath: customTarget.FilePath,
                    options: clientOptions);

                var runner = new BenchmarkRunner(client, Path.GetFileNameWithoutExtension(customTarget.FilePath), options.Verbose);
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

            if (!string.IsNullOrWhiteSpace(options.OutputMarkdownPath))
            {
                await MarkdownReportWriter.WriteToFileAsync(options.OutputMarkdownPath, allSummaries);
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
}
