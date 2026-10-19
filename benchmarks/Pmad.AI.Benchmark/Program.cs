using Microsoft.Extensions.Logging;
using Pmad.AI.Benchmark.Configuration;
using Pmad.AI.Benchmark.Engine;
using Pmad.AI.Benchmark.Reporting;
using Pmad.AI.Benchmark.Scenarios;
using Pmad.AI.Compaction;
using Pmad.AI.LlamaSharp;
using Pmad.AI.Transcripts;

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

        if (options.CalibrateTokens)
        {
            Tools.TokenCalibrationRunner.Run();
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

        var scenarios = BenchmarkScenario.FilterScenarios(options.ScenarioFilter, options.SweSandbox, options.DockerContainer);
        if (scenarios.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: No scenarios match the specified filter. Exiting.");
            Console.ResetColor();
            return 1;
        }
        Console.WriteLine($"Loaded {scenarios.Count} scenario(s): {string.Join(", ", scenarios.Select(s => s.Id))}");

        var modelsDir = LocalModelPathResolver.ResolveModelsDirectory(null);
        var allSummaries = new List<ModelBenchmarkSummary>();

        // Checkpoint initialization
        var checkpointFile = !string.IsNullOrWhiteSpace(options.CheckpointPath)
            ? options.CheckpointPath
            : "benchmark-progress.json";

        var completedSummaries = new List<ModelBenchmarkSummary>();
        if (options.Resume)
        {
            completedSummaries = BenchmarkCheckpointManager.LoadCheckpoint(checkpointFile);
            if (completedSummaries.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[Checkpoint] Resumed {completedSummaries.Count} previously completed benchmark run(s) from '{checkpointFile}'.");
                Console.ResetColor();
            }
        }

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

        // Determine transcript export path
        var transcriptPath = options.OutputTranscriptPath;
        if (string.IsNullOrWhiteSpace(transcriptPath) && !string.IsNullOrWhiteSpace(options.OutputMarkdownPath))
        {
            var dir = Path.GetDirectoryName(options.OutputMarkdownPath);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(options.OutputMarkdownPath);
            var transcriptFile = $"{nameWithoutExt}.transcript.md";
            transcriptPath = string.IsNullOrWhiteSpace(dir) ? transcriptFile : Path.Combine(dir, transcriptFile);
        }

        var baseReportDir = !string.IsNullOrWhiteSpace(options.OutputMarkdownPath)
            ? (Path.GetDirectoryName(Path.GetFullPath(options.OutputMarkdownPath)) ?? Directory.GetCurrentDirectory())
            : (!string.IsNullOrWhiteSpace(transcriptPath)
                ? (Path.GetDirectoryName(Path.GetFullPath(transcriptPath)) ?? Directory.GetCurrentDirectory())
                : Directory.GetCurrentDirectory());

        var transcriptsDir = Path.Combine(baseReportDir, "transcripts");
        Directory.CreateDirectory(transcriptsDir);

        var baselineMultiRunStatsByModel = new Dictionary<string, MultiRunStatisticalSummary>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < executionPlans.Count; i++)
        {
            var plan = executionPlans[i];
            var planSlug = GetPlanSlug(plan.TargetName, plan.Backend, plan.OptConfig.Id, plan.EnableReasoning);
            var planTranscriptFileName = $"{planSlug}.transcript.md";
            var planTranscriptFullPath = Path.Combine(transcriptsDir, planTranscriptFileName);
            var planTranscriptRelativePath = Path.Combine("transcripts", planTranscriptFileName).Replace('\\', '/');

            var transcriptOptions = new VerbatimTranscriptOptions
            {
                FilePath = planTranscriptFullPath,
                SessionTitle = $"{plan.TargetName} [{plan.ConfigurationLabel}]",
                Label = plan.OptConfig.Id,
                IncludeSystemPrompt = true,
                IncludeToolSchemas = true,
                IncludeTimestamps = true,
                IncludeTokenUsage = true,
                AutoSaveOnDispose = true,
                FlushAfterEachTurn = true
            };

            // Check if this execution plan was already completed in the checkpoint
            if (options.Resume && completedSummaries.Count > 0)
            {
                var alreadyDone = completedSummaries.FirstOrDefault(s =>
                    string.Equals(
                        BenchmarkCheckpointManager.GetPlanKey(
                            s.ModelName,
                            s.Backend ?? "CPU",
                            s.GpuLayers,
                            s.ReasoningEnabled,
                            s.OptimizationConfigId ?? s.ConfigurationLabel ?? ""),
                        plan.CheckpointKey,
                        StringComparison.OrdinalIgnoreCase));

                if (alreadyDone != null)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"\n[{i + 1}/{executionPlans.Count}] SKIPPING (Already in Checkpoint): {plan.TargetName} [{plan.ConfigurationLabel}]");
                    Console.ResetColor();
                    alreadyDone.TranscriptPath = planTranscriptRelativePath;
                    allSummaries.Add(alreadyDone);
                    continue;
                }
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"\n==========================================================================================================");
            Console.WriteLine($" [{i + 1}/{executionPlans.Count}] RUNNING BENCHMARK: {plan.TargetName.ToUpperInvariant()} [{plan.ConfigurationLabel.ToUpperInvariant()}]");
            Console.WriteLine($"==========================================================================================================");
            Console.ResetColor();

            ModelBenchmarkSummary summary;

            // Scenario-level incremental checkpointing callback
            Func<BenchmarkScenarioResult, ModelBenchmarkSummary, Task> onScenarioCheckpoint = async (scenarioRes, currentSummary) =>
            {
                currentSummary.TranscriptPath = planTranscriptRelativePath;
                var existingIdx = completedSummaries.FindIndex(s =>
                    string.Equals(
                        BenchmarkCheckpointManager.GetPlanKey(
                            s.ModelName,
                            s.Backend ?? "CPU",
                            s.GpuLayers,
                            s.ReasoningEnabled,
                            s.OptimizationConfigId ?? s.ConfigurationLabel ?? ""),
                        plan.CheckpointKey,
                        StringComparison.OrdinalIgnoreCase));

                if (existingIdx >= 0)
                {
                    completedSummaries[existingIdx] = currentSummary;
                }
                else
                {
                    completedSummaries.Add(currentSummary);
                }

                await BenchmarkCheckpointManager.SaveCheckpointAsync(checkpointFile, completedSummaries);

                if (!string.IsNullOrWhiteSpace(options.OutputMarkdownPath))
                {
                    var transcriptFileName = !string.IsNullOrWhiteSpace(transcriptPath) ? Path.GetFileName(transcriptPath) : null;
                    await MarkdownReportWriter.WriteToFileAsync(options.OutputMarkdownPath, completedSummaries, hardware, transcriptFileName);
                }
            };

            // Optional speculative SLM summarizer for background compaction (Ollama lightweight HTTP client)
            await using var speculativeSummarizer = CreateSpeculativeSummarizer(plan.OptConfig, options);

            // Helper to execute repeated runs when options.Runs > 1 and compute statistical summary
            Func<BenchmarkRunner, Task<ModelBenchmarkSummary>> executePlanRuns = async (runner) =>
            {
                var runSummaries = new List<ModelBenchmarkSummary>(options.Runs);
                for (int r = 0; r < options.Runs; r++)
                {
                    if (options.Runs > 1)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"\n--- Run {r + 1}/{options.Runs} for {plan.TargetName} [{plan.ConfigurationLabel}] ---");
                        Console.ResetColor();
                    }

                    var res = await runner.RunAllAsync(scenarios, onScenarioCheckpoint);
                    runSummaries.Add(res);
                }

                var primary = runSummaries[0];
                if (runSummaries.Count > 1)
                {
                    baselineMultiRunStatsByModel.TryGetValue(plan.TargetName, out var baseStats);
                    var stats = MultiRunStatisticalSummary.Compute(runSummaries, baseStats);
                    primary.MultiRunStats = stats;
                    if (string.Equals(plan.OptConfig.Id, "baseline", StringComparison.OrdinalIgnoreCase))
                    {
                        baselineMultiRunStatsByModel[plan.TargetName] = stats;
                    }
                }
                return primary;
            };

            if (plan.Target is ModelTarget.Mock mockTarget)
            {
                using var mockClient = new MockBenchmarkChatClient(
                    modelId: mockTarget.Name,
                    enableReasoning: plan.EnableReasoning ?? false,
                    backend: plan.Backend);

                var pipeline = BenchmarkPipelineBuilder.BuildPipeline(
                    mockClient,
                    plan.OptConfig,
                    speculativeSummarizer,
                    transcriptOptions: transcriptOptions);

                var runner = new BenchmarkRunner(
                    client: pipeline,
                    modelId: mockTarget.Name,
                    verbose: options.Verbose,
                    backend: plan.Backend,
                    gpuLayers: plan.GpuLayers,
                    reasoningEnabled: plan.EnableReasoning,
                    reasoningEffort: plan.ReasoningEffort,
                    configurationLabel: plan.ConfigurationLabel,
                    optimizationConfigId: plan.OptConfig.Id,
                    optimizationConfigLabel: plan.OptConfig.Label,
                    transcriptPath: planTranscriptRelativePath,
                    temperature: options.Temperature,
                    seed: options.Seed);

                summary = await executePlanRuns(runner);
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

                var contextSize = ResolveContextSize(plan.Target, options);
                Console.WriteLine($"\nInitializing LlamaSharp client for '{preset.Name}' (Context: {contextSize}, Backend: {plan.Backend}, GPU layers: {plan.GpuLayers}, Reasoning: {plan.EnableReasoning?.ToString() ?? "Default"})...");

                var clientOptions = new LlamaSharpOptions
                {
                    ContextSize = contextSize,
                    GpuLayerCount = plan.GpuLayers,
                    PromptFormat = preset.PromptFormat,
                    EnableReasoning = plan.EnableReasoning,
                    ReasoningEffort = plan.ReasoningEffort,
                    MtpPath = preset.GetMtpLocalPath(modelsDir),
                    EnableBuiltInHealing = false // Strict parsing baseline; pipeline middleware manages healing
                };

                using var rawClient = LlamaSharpChatClient.CreateFromFile(
                    modelPath: modelPath,
                    options: clientOptions,
                    promptFormat: preset.PromptFormat);

                var pipeline = BenchmarkPipelineBuilder.BuildPipeline(
                    rawClient,
                    plan.OptConfig,
                    speculativeSummarizer,
                    transcriptOptions: transcriptOptions);

                var runner = new BenchmarkRunner(
                    client: pipeline,
                    modelId: preset.Name,
                    verbose: options.Verbose,
                    backend: plan.Backend,
                    gpuLayers: plan.GpuLayers,
                    reasoningEnabled: plan.EnableReasoning,
                    reasoningEffort: plan.ReasoningEffort,
                    configurationLabel: plan.ConfigurationLabel,
                    optimizationConfigId: plan.OptConfig.Id,
                    optimizationConfigLabel: plan.OptConfig.Label,
                    transcriptPath: planTranscriptRelativePath,
                    temperature: options.Temperature,
                    seed: options.Seed);

                summary = await executePlanRuns(runner);
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

                var contextSize = ResolveContextSize(plan.Target, options);
                Console.WriteLine($"\nInitializing LlamaSharp client from file '{customTarget.FilePath}' (Context: {contextSize}, Backend: {plan.Backend}, GPU layers: {plan.GpuLayers}, Reasoning: {plan.EnableReasoning?.ToString() ?? "Default"})...");

                var clientOptions = new LlamaSharpOptions
                {
                    ContextSize = contextSize,
                    GpuLayerCount = plan.GpuLayers,
                    EnableReasoning = plan.EnableReasoning,
                    ReasoningEffort = plan.ReasoningEffort,
                    EnableBuiltInHealing = false
                };

                using var rawClient = LlamaSharpChatClient.CreateFromFile(
                    modelPath: customTarget.FilePath,
                    options: clientOptions);

                var pipeline = BenchmarkPipelineBuilder.BuildPipeline(
                    rawClient,
                    plan.OptConfig,
                    speculativeSummarizer,
                    transcriptOptions: transcriptOptions);

                var runner = new BenchmarkRunner(
                    client: pipeline,
                    modelId: Path.GetFileNameWithoutExtension(customTarget.FilePath),
                    verbose: options.Verbose,
                    backend: plan.Backend,
                    gpuLayers: plan.GpuLayers,
                    reasoningEnabled: plan.EnableReasoning,
                    reasoningEffort: plan.ReasoningEffort,
                    configurationLabel: plan.ConfigurationLabel,
                    optimizationConfigId: plan.OptConfig.Id,
                    optimizationConfigLabel: plan.OptConfig.Label,
                    transcriptPath: planTranscriptRelativePath,
                    temperature: options.Temperature,
                    seed: options.Seed);

                summary = await executePlanRuns(runner);
            }
            else if (plan.Target is ModelTarget.Ollama ollamaTarget)
            {
                var contextSize = ResolveContextSize(plan.Target, options);
                Console.WriteLine($"\n[Ollama] Preloading weights for '{ollamaTarget.ModelName}' into VRAM (keep_alive: 1h)...");
                await OllamaBenchmarkChatClient.PreloadModelAsync(ollamaTarget.ModelName, ollamaTarget.BaseUrl, keepAlive: "1h");

                try
                {
                    Console.WriteLine($"Connecting to Ollama model '{ollamaTarget.ModelName}' at '{ollamaTarget.BaseUrl}' (Context: {contextSize}, Reasoning: {plan.EnableReasoning?.ToString() ?? "Default"})...");

                    using var rawClient = new OllamaBenchmarkChatClient(
                        modelId: ollamaTarget.ModelName,
                        baseUrl: ollamaTarget.BaseUrl,
                        contextSize: contextSize);

                    var pipeline = BenchmarkPipelineBuilder.BuildPipeline(
                        rawClient,
                        plan.OptConfig,
                        speculativeSummarizer,
                        transcriptOptions: transcriptOptions);

                    var runner = new BenchmarkRunner(
                        client: pipeline,
                        modelId: ollamaTarget.ModelName,
                        verbose: options.Verbose,
                        backend: "Ollama",
                        gpuLayers: 0,
                        reasoningEnabled: plan.EnableReasoning,
                        reasoningEffort: plan.ReasoningEffort,
                        configurationLabel: plan.ConfigurationLabel,
                        optimizationConfigId: plan.OptConfig.Id,
                        optimizationConfigLabel: plan.OptConfig.Label,
                        transcriptPath: planTranscriptRelativePath,
                        temperature: options.Temperature,
                        seed: options.Seed);

                    summary = await executePlanRuns(runner);
                }
                finally
                {
                    Console.WriteLine($"[Ollama] Offloading model '{ollamaTarget.ModelName}' from VRAM (freeing GPU memory)...");
                    await OllamaBenchmarkChatClient.OffloadModelAsync(ollamaTarget.ModelName, ollamaTarget.BaseUrl);
                }
            }
            else
            {
                continue;
            }

            summary.TranscriptPath = planTranscriptRelativePath;
            ConsoleReportFormatter.PrintScenarioResultsTable(summary);
            allSummaries.Add(summary);

            if (File.Exists(planTranscriptFullPath))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[Transcript] Verbatim transcript saved to '{planTranscriptFullPath}'.");
                Console.ResetColor();
            }

            // Ensure checkpoint and report are fully synced
            var summaryIndex = completedSummaries.FindIndex(s =>
                string.Equals(
                    BenchmarkCheckpointManager.GetPlanKey(
                        s.ModelName,
                        s.Backend ?? "CPU",
                        s.GpuLayers,
                        s.ReasoningEnabled,
                        s.OptimizationConfigId ?? s.ConfigurationLabel ?? ""),
                    plan.CheckpointKey,
                    StringComparison.OrdinalIgnoreCase));

            if (summaryIndex >= 0)
            {
                completedSummaries[summaryIndex] = summary;
            }
            else
            {
                completedSummaries.Add(summary);
            }

            await BenchmarkCheckpointManager.SaveCheckpointAsync(checkpointFile, completedSummaries);

            if (!string.IsNullOrWhiteSpace(options.OutputMarkdownPath))
            {
                var transcriptFileName = !string.IsNullOrWhiteSpace(transcriptPath) ? Path.GetFileName(transcriptPath) : null;
                await MarkdownReportWriter.WriteToFileAsync(options.OutputMarkdownPath, completedSummaries, hardware, transcriptFileName);
            }

            // Reclaim native allocations between model runs
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        if (allSummaries.Count > 0)
        {
            ConsoleReportFormatter.PrintModelsComparisonTable(allSummaries);
            ConsoleReportFormatter.PrintSuiteComparisonTable(allSummaries);
            ConsoleReportFormatter.PrintBackendComparisonTable(allSummaries);
            ConsoleReportFormatter.PrintReasoningComparisonTable(allSummaries);
            ConsoleReportFormatter.PrintMultiRunStatisticalTable(allSummaries);

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

        // In opt-matrix mode, baseline failures on malformed outputs are expected data points, not suite failures
        if (options.OptMatrix)
        {
            return 0;
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

        // Determine optimization configurations to evaluate
        var optConfigs = new List<OptimizationConfig>();
        if (options.OptMatrix)
        {
            optConfigs.AddRange(OptimizationConfig.AllConfigs);
        }
        else if (!string.IsNullOrWhiteSpace(options.ConfigId))
        {
            var matched = OptimizationConfig.Find(options.ConfigId);
            if (matched != null)
            {
                optConfigs.Add(matched);
            }
            else
            {
                optConfigs.Add(OptimizationConfig.Baseline);
            }
        }
        else
        {
            optConfigs.Add(OptimizationConfig.All);
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

            var targetBackends = target is ModelTarget.Ollama
                ? [("Ollama", 0)]
                : backends;

            foreach (var backend in targetBackends)
            {
                foreach (var reasoning in reasoningModes)
                {
                    foreach (var optConfig in optConfigs)
                    {
                        var parts = new List<string>();

                        if (options.CompareBackends || target is ModelTarget.Ollama)
                        {
                            parts.Add(backend.Name);
                        }

                        if (options.CompareReasoning)
                        {
                            parts.Add(reasoning == true ? "Reasoning" : "No Reasoning");
                        }

                        if (options.OptMatrix || !string.IsNullOrWhiteSpace(options.ConfigId))
                        {
                            parts.Add(optConfig.Label);
                        }
                        else if (parts.Count == 0)
                        {
                            parts.Add(backend.Name);
                        }

                        var label = string.Join(" | ", parts);

                        plans.Add(new BenchmarkExecutionPlan(
                            target,
                            backend.Name,
                            backend.Layers,
                            reasoning,
                            options.ReasoningEffort,
                            label,
                            optConfig));
                    }
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

        if (options.AllTargets)
        {
            foreach (var preset in LlamaModelPreset.Presets)
            {
                targets.Add(new ModelTarget.PresetTarget(preset));
            }

            var designatedOllama = new List<string>();
            if (!string.IsNullOrWhiteSpace(options.OllamaModelsList))
            {
                designatedOllama.AddRange(options.OllamaModelsList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
            else if (!string.IsNullOrWhiteSpace(options.OllamaModel))
            {
                designatedOllama.Add(options.OllamaModel.Trim());
            }
            else
            {
                designatedOllama.Add("Qwen3.8-27B:latest");
                designatedOllama.Add("jetelain/Qwen3.8-27B:iq3");
            }

            foreach (var m in designatedOllama)
            {
                targets.Add(new ModelTarget.Ollama(m, options.OllamaBaseUrl));
            }

            return targets;
        }

        if (!string.IsNullOrWhiteSpace(options.OllamaModelsList))
        {
            foreach (var m in options.OllamaModelsList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                targets.Add(new ModelTarget.Ollama(m, options.OllamaBaseUrl));
            }
        }

        if (!string.IsNullOrWhiteSpace(options.OllamaModel))
        {
            targets.Add(new ModelTarget.Ollama(options.OllamaModel, options.OllamaBaseUrl));
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

        // If an explicit target like Ollama was added, return now
        if (targets.Count > 0)
        {
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

    private static int ResolveContextSize(ModelTarget target, BenchmarkOptions options)
    {
        if (options.IsContextSizeExplicit)
        {
            return options.ContextSize;
        }

        // Tiers: SLMs (<9B) run at 64k, LLMs (9B+) and Ollama at 128k, regardless of the preset's default context.
        return target switch
        {
            ModelTarget.Ollama => 131072,
            ModelTarget.PresetTarget pt when pt.Preset.Id.Contains("9b") || pt.Preset.Id.Contains("12b") => 131072,
            _ => 65536
        };
    }

    private static ISpeculativeSummarizer? CreateSpeculativeSummarizer(OptimizationConfig config, BenchmarkOptions options)
    {
        if (config.Id is not ("opt-speculative-slm" or "all-optimizations" or "opt-all-local-llm-with-slm"))
        {
            return null;
        }

        try
        {
            var endpoint = new Uri(options.OllamaBaseUrl);
            var logger = new ConsoleBenchmarkLogger();

            return new OllamaSpeculativeSummarizer(new OllamaSpeculativeSummarizerOptions
            {
                Endpoint = endpoint,
                Model = "qwen3.5:2b",
                ContextSize = 4096,
                NumGpu = 0,
                RequestTimeout = TimeSpan.FromSeconds(30),
                JobTimeout = TimeSpan.FromSeconds(30)
            }, logger: logger);
        }
        catch (Exception ex)
        {
            WarnNoSummarizer(config, ex.Message);
            return null;
        }
    }

    private static void WarnNoSummarizer(OptimizationConfig config, string reason)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[Warning] '{config.Id}': speculative summarizer unavailable ({reason}); falling back to non-semantic compaction.");
        Console.ResetColor();
    }

    private abstract record ModelTarget
    {
        public sealed record Mock(string Name) : ModelTarget;
        public sealed record PresetTarget(LlamaModelPreset Preset) : ModelTarget;
        public sealed record CustomPath(string FilePath) : ModelTarget;
        public sealed record Ollama(string ModelName, string BaseUrl) : ModelTarget;
    }

    private sealed record BenchmarkExecutionPlan(
        ModelTarget Target,
        string Backend,
        int GpuLayers,
        bool? EnableReasoning,
        string? ReasoningEffort,
        string ConfigurationLabel,
        OptimizationConfig OptConfig)
    {
        public string TargetName => Target switch
        {
            ModelTarget.Mock m => m.Name,
            ModelTarget.PresetTarget p => p.Preset.Name,
            ModelTarget.CustomPath c => Path.GetFileNameWithoutExtension(c.FilePath),
            ModelTarget.Ollama o => $"Ollama:{o.ModelName}",
            _ => "Unknown"
        };

        public string CheckpointKey =>
            BenchmarkCheckpointManager.GetPlanKey(TargetName, Backend, GpuLayers, EnableReasoning, OptConfig.Id);
    }

    private static string GetPlanSlug(string modelName, string backend, string configId, bool? reasoning = null)
    {
        var modelSlug = SanitizeSlug(modelName);
        var backendSlug = SanitizeSlug(backend);
        var configSlug = SanitizeSlug(configId);
        var reasSlug = reasoning.HasValue ? (reasoning.Value ? "_reasoning" : "_no-reasoning") : "";
        return $"{modelSlug}_{backendSlug}_{configSlug}{reasSlug}";
    }

    private static string SanitizeSlug(string text)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = text.ToLowerInvariant().Select(c => invalid.Contains(c) || c == ' ' || c == '/' || c == '\\' || c == ':' ? '-' : c).ToArray();
        return new string(chars).Trim('-');
    }

    private sealed class ConsoleBenchmarkLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            Console.ForegroundColor = logLevel >= LogLevel.Error ? ConsoleColor.Red : ConsoleColor.Yellow;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{logLevel}] {message}");
            if (exception != null) Console.WriteLine(exception);
            Console.ResetColor();
        }
    }
}
