using Microsoft.Extensions.AI;
using Pmad.AI.Benchmark.Configuration;
using Pmad.AI.Benchmark.Engine;
using Pmad.AI.Benchmark.Reporting;
using Pmad.AI.Benchmark.Scenarios;
using Pmad.AI.Benchmark.Scenarios.Bfcl;
using Pmad.AI.Benchmark.Scenarios.Ruler;

namespace Pmad.AI.Benchmark.Tests;

public class BenchmarkSuiteTests
{
    [Fact]
    public void BenchmarkOptions_Parse_HandlesValidArguments()
    {
        var args = new[]
        {
            "--model", "qwen3.5-2b",
            "--download",
            "--scenario", "weather",
            "--gpu", "40",
            "--context", "4096",
            "--markdown", "out.md",
            "--json", "out.json",
            "--verbose"
        };

        var options = BenchmarkOptions.Parse(args);

        Assert.Equal("qwen3.5-2b", options.Model);
        Assert.True(options.DownloadMissing);
        Assert.Equal("weather", options.ScenarioFilter);
        Assert.Equal(40, options.GpuLayerCount);
        Assert.Equal(4096, options.ContextSize);
        Assert.Equal("out.md", options.OutputMarkdownPath);
        Assert.Equal("out.json", options.OutputJsonPath);
        Assert.True(options.Verbose);
        Assert.False(options.ShowHelp);
    }

    [Fact]
    public void BenchmarkOptions_Parse_HelpFlag_SetsShowHelp()
    {
        var options = BenchmarkOptions.Parse(["-h"]);
        Assert.True(options.ShowHelp);

        var optionsLong = BenchmarkOptions.Parse(["--help"]);
        Assert.True(optionsLong.ShowHelp);
    }

    [Fact]
    public void BenchmarkOptions_Parse_MockFlag_SetsMockMode()
    {
        var options = BenchmarkOptions.Parse(["--mock"]);
        Assert.True(options.MockMode);
    }

    [Fact]
    public void BenchmarkScenario_GetAllScenarios_ReturnsExpectedDefinitions()
    {
        var scenarios = BenchmarkScenario.GetAllScenarios();
        Assert.Equal(18, scenarios.Count);

        Assert.Contains(scenarios, s => s.Id == "bfcl-simple");
        Assert.Contains(scenarios, s => s.Id == "bfcl-multiple");
        Assert.Contains(scenarios, s => s.Id == "bfcl-parallel");
        Assert.Contains(scenarios, s => s.Id == "bfcl-irrelevance");
        Assert.Contains(scenarios, s => s.Id == "bfcl-multiturn");
        Assert.Contains(scenarios, s => s.Id == "bfcl-strict");
        Assert.Contains(scenarios, s => s.Id == "swebench-diff");
        Assert.Contains(scenarios, s => s.Id == "swebench-logs");
        Assert.Contains(scenarios, s => s.Id == "swebench-loop");
        Assert.Contains(scenarios, s => s.Id == "swebench-deep");
        Assert.Contains(scenarios, s => s.Id == "swebench-succinct");
        Assert.Contains(scenarios, s => s.Id == "swebench-matrix");
        Assert.Contains(scenarios, s => s.Id == "swebench-syntax");
        Assert.Contains(scenarios, s => s.Id == "ruler-8k");
        Assert.Contains(scenarios, s => s.Id == "ruler-16k");
        Assert.Contains(scenarios, s => s.Id == "ruler-32k");
        Assert.Contains(scenarios, s => s.Id == "ruler-64k");
        Assert.Contains(scenarios, s => s.Id == "ruler-128k");

        foreach (var sc in scenarios)
        {
            Assert.NotEmpty(sc.Name);
            Assert.NotEmpty(sc.Description);
            Assert.NotEmpty(sc.Turns);
        }
    }

    [Theory]
    [InlineData("simple", "bfcl-simple")]
    [InlineData("multiple", "bfcl-multiple")]
    [InlineData("parallel", "bfcl-parallel")]
    [InlineData("irrelevance", "bfcl-irrelevance")]
    [InlineData("multiturn", "bfcl-multiturn")]
    [InlineData("strict", "bfcl-strict")]
    [InlineData("ruler-8k", "ruler-8k")]
    [InlineData("ruler-16k", "ruler-16k")]
    [InlineData("bfcl", 6)]
    [InlineData("deep", 3)]
    [InlineData("ruler", 5)]
    [InlineData("swebench", 7)]
    [InlineData("all", 18)]
    [InlineData("all-small", 15)]
    [InlineData("small", 15)]
    [InlineData("full", 18)]
    [InlineData("bfcl,swebench", 13)]
    public void BenchmarkScenario_FilterScenarios_FiltersCorrectly(string filter, object expected)
    {
        var filtered = BenchmarkScenario.FilterScenarios(filter);
        if (expected is int count)
        {
            Assert.Equal(count, filtered.Count);
        }
        else if (expected is string expectedId)
        {
            Assert.Single(filtered);
            Assert.Equal(expectedId, filtered[0].Id);
        }
    }

    [Fact]
    public async Task BenchmarkRunner_WithMockClient_RunsAllScenariosSuccessfully()
    {
        using var mockClient = new MockBenchmarkChatClient("mock-test-client", simulatedDelayMs: 0);
        var pipeline = BenchmarkPipelineBuilder.BuildPipeline(mockClient, OptimizationConfig.All);
        var scenarios = BenchmarkScenario.GetAllScenarios();
        var runner = new BenchmarkRunner(pipeline, "mock-test-client", verbose: false);

        var summary = await runner.RunAllAsync(scenarios);

        Assert.Equal(18, summary.ScenariosRun);
        var failures = string.Join("\n", summary.ScenarioResults
            .Where(r => r.Status != BenchmarkStatus.Passed)
            .Select(r => $"{r.ScenarioId}: {r.ErrorMessage ?? string.Join("; ", r.TurnResults.Where(t => t.Status != BenchmarkStatus.Passed).Select(t => $"Turn {t.TurnIndex}: {t.ValidationMessage}"))}"));
        Assert.True(summary.ScenariosFailed == 0, $"Failures:\n{failures}");
        Assert.Equal(18, summary.ScenariosPassed);
        Assert.Equal(100.0, summary.TaskSuccessRatePercentage);
        Assert.Equal(100.0, summary.StepAccuracyPercentage);
        Assert.Equal(100.0, summary.PassRatePercentage);
        Assert.True(summary.TotalToolCalls >= 5);
        Assert.True(summary.TotalOutputTokens > 0);
        Assert.True(summary.TotalPromptTokens > 0);
        Assert.True(summary.TotalTimeMs >= 0);
        Assert.True(summary.PeakMemoryMb > 0);

        foreach (var scResult in summary.ScenarioResults)
        {
            Assert.Equal(BenchmarkStatus.Passed, scResult.Status);
            Assert.True(scResult.TaskSuccess, $"Expected TaskSuccess for {scResult.ScenarioId}");
            if (scResult.ScenarioId != "swebench-loop")
            {
                Assert.True(scResult.ToolAccuracyPercentage >= 70, $"{scResult.ScenarioId} accuracy was {scResult.ToolAccuracyPercentage}%");
            }
            Assert.True(scResult.PeakMemoryMb > 0);
            Assert.NotEmpty(scResult.TurnResults);
        }
    }

    [Theory]
    [InlineData("ruler-32k")]
    [InlineData("ruler-64k")]
    public async Task BenchmarkRunner_DeepContextScenarios_PassWithMockUnderAllConfigs(string id)
    {
        var scenario = Assert.Single(BenchmarkScenario.FilterScenarios(id));
        Assert.Contains(scenario.Turns, t => t.IsRecallProbe);

        foreach (var config in OptimizationConfig.AllConfigs)
        {
            using var mockClient = new MockBenchmarkChatClient("mock-test-client", simulatedDelayMs: 0);
            var pipeline = BenchmarkPipelineBuilder.BuildPipeline(mockClient, config);
            var runner = new BenchmarkRunner(pipeline, "mock-test-client", verbose: false);

            var result = await runner.RunScenarioAsync(scenario);

            Assert.True(result.Status == BenchmarkStatus.Passed && result.SemanticRecallPassed,
                $"{id} failed under {config.Id}: {string.Join("; ", result.TurnResults.Where(t => t.Status != BenchmarkStatus.Passed).Select(t => t.ValidationMessage))}");
        }
    }

    [Fact]
    public void BenchmarkScenario_AllIncludesEverythingAndCategorySubsets()
    {
        Assert.Equal(18, BenchmarkScenario.FilterScenarios("all").Count);
        Assert.Equal(15, BenchmarkScenario.FilterScenarios("all-small").Count);
        Assert.Equal(3, BenchmarkScenario.FilterScenarios("deep").Count);
        Assert.Equal(7, BenchmarkScenario.FilterScenarios("swebench").Count);
        Assert.Equal(6, BenchmarkScenario.FilterScenarios("bfcl").Count);
        Assert.Equal(5, BenchmarkScenario.FilterScenarios("ruler").Count);
    }

    [Fact]
    public async Task BenchmarkRunner_BfclAllCategories_PassWithMock()
    {
        using var mockClient = new MockBenchmarkChatClient("mock-test-client", simulatedDelayMs: 0);
        var pipeline = BenchmarkPipelineBuilder.BuildPipeline(mockClient, OptimizationConfig.Baseline);
        var scenario = new BfclScenario();
        var runner = new BenchmarkRunner(pipeline, "mock-test-client", verbose: false);

        var result = await runner.RunScenarioAsync(scenario);

        Assert.Equal(BenchmarkStatus.Passed, result.Status);
    }

    [Fact]
    public async Task BenchmarkRunner_DirectNegativeTest_FailsIfModelInvokesTool()
    {
        // Custom mock that incorrectly invokes a tool when none is expected
        var badMock = new BadMockClient();
        var scenario = new BfclIrrelevanceScenario();
        var runner = new BenchmarkRunner(badMock, "bad-model");

        var result = await runner.RunScenarioAsync(scenario);

        Assert.Equal(BenchmarkStatus.Failed, result.Status);
        Assert.Contains("tool", result.TurnResults[0].ValidationMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarkdownReportWriter_GeneratesExpectedMarkdown()
    {
        var summary = new ModelBenchmarkSummary
        {
            ModelId = "qwen3.5-2b",
            ModelName = "Qwen 3.5 2B (Q4_K_XL)",
            ScenariosRun = 2,
            ScenariosPassed = 2,
            TotalToolCalls = 3,
            TotalOutputTokens = 120,
            AverageGenerationTokensPerSecond = 45.2,
            TotalTimeMs = 2650,
            PeakMemoryMb = 1536.0,
            ScenarioResults =
            [
                new BenchmarkScenarioResult
                {
                    ScenarioId = "weather",
                    ScenarioName = "Weather Multi-City",
                    ModelId = "qwen3.5-2b",
                    Status = BenchmarkStatus.Passed,
                    TotalToolCalls = 2,
                    TotalDurationMs = 1500,
                    TotalPromptTokens = 300,
                    TotalOutputTokens = 75,
                    PeakMemoryMb = 1536.0
                }
            ]
        };

        var md = MarkdownReportWriter.GenerateMarkdown([summary]);

        Assert.Contains("# Pmad.AI Tool Calling Benchmark Report", md);
        Assert.Contains("Qwen 3.5 2B (Q4_K_XL)", md);
        Assert.Contains("Weather Multi-City", md);
        Assert.Contains("RAM", md);
        Assert.Contains("✅ PASS", md);
    }

    [Fact]
    public void BenchmarkOptions_Parse_ComparisonFlags_SetsExpectedProperties()
    {
        var compareBackends = BenchmarkOptions.Parse(["--compare-backends"]);
        Assert.True(compareBackends.CompareBackends);

        var compareReasoning = BenchmarkOptions.Parse(["--compare-reasoning"]);
        Assert.True(compareReasoning.CompareReasoning);

        var compareAll = BenchmarkOptions.Parse(["--compare-all"]);
        Assert.True(compareAll.CompareAll);
        Assert.True(compareAll.CompareBackends);
        Assert.True(compareAll.CompareReasoning);

        var matrix = BenchmarkOptions.Parse(["--matrix"]);
        Assert.True(matrix.CompareAll);
        Assert.True(matrix.CompareBackends);
        Assert.True(matrix.CompareReasoning);

        var backendCpu = BenchmarkOptions.Parse(["-b", "cpu"]);
        Assert.Equal("cpu", backendCpu.Backend);
        Assert.False(backendCpu.CompareBackends);

        var backendVulkan = BenchmarkOptions.Parse(["--backend", "vulkan"]);
        Assert.Equal("vulkan", backendVulkan.Backend);

        var backendAll = BenchmarkOptions.Parse(["--backends", "all"]);
        Assert.True(backendAll.CompareBackends);
    }

    [Fact]
    public void NvidiaSmiMonitor_SmokeTest_DoesNotThrow()
    {
        var available = NvidiaSmiMonitor.IsAvailable;
        var vram = NvidiaSmiMonitor.GetUsedVramMb();

        if (available)
        {
            Assert.NotNull(vram);
            Assert.True(vram.Value > 0);
        }
    }

    [Fact]
    public async Task MockBenchmarkChatClient_ReasoningToggle_AffectsTokensAndReasoningContent()
    {
        using var clientNoReas = new MockBenchmarkChatClient(enableReasoning: false);
        var respNoReas = await clientNoReas.GetResponseAsync([new ChatMessage(ChatRole.User, "What is the capital of France?")]);
        Assert.DoesNotContain(respNoReas.Messages[0].Contents, c => c is TextReasoningContent);
        var noReasTokens = respNoReas.Usage!.OutputTokenCount!.Value;

        using var clientReas = new MockBenchmarkChatClient(enableReasoning: true);
        var respReas = await clientReas.GetResponseAsync([new ChatMessage(ChatRole.User, "What is the capital of France?")]);
        Assert.Contains(respReas.Messages[0].Contents, c => c is TextReasoningContent);
        var reasTokens = respReas.Usage!.OutputTokenCount!.Value;

        Assert.True(reasTokens > noReasTokens);
    }

    [Fact]
    public void ReportFormatters_ComparisonTables_GenerateProperOutput()
    {
        var cpuNoReas = new ModelBenchmarkSummary
        {
            ModelId = "qwen3.5-2b",
            ModelName = "Qwen 3.5 2B",
            Backend = "CPU",
            GpuLayers = 0,
            ReasoningEnabled = false,
            ConfigurationLabel = "CPU | No Reasoning",
            ScenariosRun = 1,
            ScenariosPassed = 1,
            TotalOutputTokens = 100,
            AverageGenerationTokensPerSecond = 10.0,
            TotalTimeMs = 10000,
            PeakMemoryMb = 1500,
            PeakVramMb = 0
        };

        var vulkanNoReas = new ModelBenchmarkSummary
        {
            ModelId = "qwen3.5-2b",
            ModelName = "Qwen 3.5 2B",
            Backend = "Vulkan",
            GpuLayers = 99,
            ReasoningEnabled = false,
            ConfigurationLabel = "Vulkan | No Reasoning",
            ScenariosRun = 1,
            ScenariosPassed = 1,
            TotalOutputTokens = 100,
            AverageGenerationTokensPerSecond = 40.0,
            TotalTimeMs = 2500,
            PeakMemoryMb = 1400,
            PeakVramMb = 3500
        };

        var vulkanReas = new ModelBenchmarkSummary
        {
            ModelId = "qwen3.5-2b",
            ModelName = "Qwen 3.5 2B",
            Backend = "Vulkan",
            GpuLayers = 99,
            ReasoningEnabled = true,
            ConfigurationLabel = "Vulkan | Reasoning",
            ScenariosRun = 1,
            ScenariosPassed = 1,
            TotalOutputTokens = 350,
            AverageGenerationTokensPerSecond = 35.0,
            TotalTimeMs = 10000,
            PeakMemoryMb = 1450,
            PeakVramMb = 3600
        };

        var summaries = new[] { cpuNoReas, vulkanNoReas, vulkanReas };

        // Test Console tables do not throw
        ConsoleReportFormatter.PrintModelsComparisonTable(summaries);
        ConsoleReportFormatter.PrintBackendComparisonTable(summaries);
        ConsoleReportFormatter.PrintReasoningComparisonTable(summaries);

        // Test Markdown report contains comparison sections
        var md = MarkdownReportWriter.GenerateMarkdown(summaries);
        Assert.Contains("Hardware Acceleration Comparison", md);
        Assert.Contains("Vulkan Speedup", md);
        Assert.Contains("Reasoning Impact Comparison", md);
        Assert.Contains("GPU VRAM", md);
        Assert.Contains("Environment", md);
        Assert.Contains("CPU", md);
    }

    [Fact]
    public void SystemHardwareInfo_DetectsSystemProperties()
    {
        var hw = SystemHardwareInfo.Current;
        Assert.NotNull(hw);
        Assert.NotEmpty(hw.CpuName);
        Assert.True(hw.LogicalCores > 0);
        Assert.NotEmpty(hw.OsDescription);
        Assert.NotEmpty(hw.CpuSummary);
        Assert.NotEmpty(hw.GpuSummary);
    }

    [Fact]
    public void TranscriptReportWriter_GeneratesFullTurnTranscriptAndFailureIndex()
    {
        var scResult = new BenchmarkScenarioResult
        {
            ScenarioId = "calc",
            ScenarioName = "Calculator Math",
            ModelId = "qwen",
            Status = BenchmarkStatus.Failed,
            ErrorMessage = "Turn 1 failed."
        };

        scResult.TurnResults.Add(new TurnResult
        {
            TurnIndex = 1,
            UserPrompt = "Compute 2 + 2",
            Status = BenchmarkStatus.Failed,
            AssistantReasoning = "Thinking about addition...",
            AssistantFinalText = "5",
            ValidationMessage = "Expected 4, got 5.",
            ToolExecutions =
            [
                new ToolCallRecord("c1", "calculate", "{\"expr\":\"2+2\"}", "{\"result\":4}")
            ]
        });

        var summary = new ModelBenchmarkSummary
        {
            ModelId = "qwen",
            ModelName = "Qwen Test",
            Backend = "Vulkan",
            GpuLayers = 99,
            ReasoningEnabled = true,
            ConfigurationLabel = "Vulkan | Reasoning",
            ScenariosRun = 1,
            ScenariosPassed = 0,
            ScenariosFailed = 1,
            TotalOutputTokens = 50,
            AverageGenerationTokensPerSecond = 20.0,
            TotalTimeMs = 2500,
            ScenarioResults = [scResult]
        };

        var transcript = TranscriptReportWriter.GenerateMarkdown([summary]);

        Assert.Contains("# Pmad.AI Benchmark Execution Transcript & Failure Analysis", transcript);
        Assert.Contains("System Environment", transcript);
        Assert.Contains("Failure Analysis Index", transcript);
        Assert.Contains("Expected 4, got 5", transcript);
        Assert.Contains("Thinking about addition...", transcript);
        Assert.Contains("calculate", transcript);
        Assert.Contains("{\"result\":4}", transcript);
        Assert.Contains("FAILED:", transcript);
    }

    [Fact]
    public void BenchmarkOptions_Parse_TranscriptOption_SetsProperty()
    {
        var shortOpt = BenchmarkOptions.Parse(["-t", "my-transcript.md"]);
        Assert.Equal("my-transcript.md", shortOpt.OutputTranscriptPath);

        var longOpt = BenchmarkOptions.Parse(["--transcript", "full-transcript.md"]);
        Assert.Equal("full-transcript.md", longOpt.OutputTranscriptPath);
    }

    [Fact]
    public void BenchmarkScenario_Weight_DerivedFromSubtestsAndComplexity()
    {
        var simple = new BfclSimpleScenario();
        Assert.Equal(0.5, simple.ComplexityMultiplier);
        Assert.Equal(10.0, simple.Weight);

        var multiple = new BfclMultipleScenario();
        Assert.Equal(1.25, multiple.ComplexityMultiplier);
        Assert.Equal(25.0, multiple.Weight);

        var parallel = new BfclParallelScenario();
        Assert.Equal(1.5, parallel.ComplexityMultiplier);
        Assert.Equal(22.5, parallel.Weight);

        var irrelevance = new BfclIrrelevanceScenario();
        Assert.Equal(1.0, irrelevance.ComplexityMultiplier);
        Assert.Equal(15.0, irrelevance.Weight);

        var ruler8k = new Ruler8kScenario();
        Assert.Equal(5.0, ruler8k.ComplexityMultiplier);
        Assert.Equal(10.0, ruler8k.Weight);

        var ruler128k = new Ruler128kScenario();
        Assert.Equal(30.0, ruler128k.ComplexityMultiplier);
        Assert.Equal(60.0, ruler128k.Weight);

        var sweScenarios = BenchmarkScenario.GetSweBenchScenarios();
        foreach (var sc in sweScenarios)
        {
            Assert.True(sc.ComplexityMultiplier >= 2.0);
            Assert.True(sc.Weight > 0);
        }
    }

    [Fact]
    public void ModelBenchmarkSummary_WeightedPassRate_CalculatesExpectedFormula()
    {
        var sc1 = new BenchmarkScenarioResult
        {
            ScenarioId = "sc1",
            ScenarioName = "Scenario 1",
            ModelId = "test-model",
            Status = BenchmarkStatus.Partial,
            Score = 50.0,
            Weight = 20.0,
            TurnsPassed = 1,
            TotalTurns = 2
        };

        var sc2 = new BenchmarkScenarioResult
        {
            ScenarioId = "sc2",
            ScenarioName = "Scenario 2",
            ModelId = "test-model",
            Status = BenchmarkStatus.Passed,
            Score = 100.0,
            Weight = 30.0,
            TurnsPassed = 4,
            TotalTurns = 4
        };

        var summary = new ModelBenchmarkSummary
        {
            ModelId = "test-model",
            ModelName = "Test Model",
            ScenariosRun = 2,
            ScenariosPassed = 1,
            ScenariosPartial = 1,
            ScenariosFailed = 0,
            ScenarioResults = [sc1, sc2]
        };

        // Formula: (50 * 20 + 100 * 30) / (20 + 30) = (1000 + 3000) / 50 = 4000 / 50 = 80.0%
        Assert.Equal(80.0, summary.PassRatePercentage);
    }

    [Fact]
    public async Task BenchmarkRunner_MultiTurnScenario_CalculatesPartialScoreAndStatus()
    {
        // Custom scenario with 2 turns: 1 passes, 1 fails
        var testTurns = new List<BenchmarkTurn>
        {
            new()
            {
                UserPrompt = "Say hello",
                ExpectedToolNames = [],
                ToolExecutor = _ => null,
                CustomValidator = ctx => ValidationResult.Success()
            },
            new()
            {
                UserPrompt = "Say goodbye",
                ExpectedToolNames = [],
                ToolExecutor = _ => null,
                CustomValidator = ctx => ValidationResult.Failure("Simulated turn 2 failure")
            }
        };

        var customScenario = new TestMultiTurnScenario("test-multi", "Test Multi Turn", testTurns);

        using var mockClient = new MockBenchmarkChatClient("mock-test", simulatedDelayMs: 0);
        var runner = new BenchmarkRunner(mockClient, "mock-test", verbose: false);

        var summary = await runner.RunAllAsync([customScenario]);

        Assert.Equal(1, summary.ScenariosRun);
        Assert.Equal(0, summary.ScenariosPassed);
        Assert.Equal(1, summary.ScenariosPartial);
        Assert.Equal(0, summary.ScenariosFailed);

        var result = summary.ScenarioResults[0];
        Assert.Equal(BenchmarkStatus.Partial, result.Status);
        Assert.Equal(15.0, result.Score); // 70% TaskSuccess (0%) + 30% StepAccuracy (50%) = 15.0%
        Assert.Equal(50.0, result.StepAccuracy);
        Assert.False(result.TaskSuccess);
        Assert.Equal(1, result.TurnsPassed);
        Assert.Equal(2, result.TotalTurns);
        Assert.Equal(15.0, summary.PassRatePercentage);
    }

    private sealed class TestMultiTurnScenario : BenchmarkScenario
    {
        private readonly List<BenchmarkTurn> _turns;
        public override string Id { get; }
        public override string Name { get; }
        public override string Description => "Test scenario";
        public override IReadOnlyList<AITool> Tools => [];
        public override IReadOnlyList<BenchmarkTurn> Turns => _turns;
        public override double ComplexityMultiplier => 2.0;

        public TestMultiTurnScenario(string id, string name, List<BenchmarkTurn> turns)
        {
            Id = id;
            Name = name;
            _turns = turns;
        }
    }

    private sealed class BadMockClient : IChatClient
    {
        public ChatClientMetadata Metadata => new("bad", new Uri("http://localhost"), "bad-model");

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var call = new FunctionCallContent("call-1", "search_wikipedia", new Dictionary<string, object?> { ["query"] = "Paris" });
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [call])]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public void RebalancedScenarioWeights_AdhereToTrackASpecification()
    {
        var bfclSimple = Assert.Single(BenchmarkScenario.FilterScenarios("bfcl-simple"));
        Assert.Equal(10.0, bfclSimple.Weight);

        var ruler8k = Assert.Single(BenchmarkScenario.FilterScenarios("ruler-8k"));
        Assert.Equal(10.0, ruler8k.Weight);

        var sweScenarios = BenchmarkScenario.FilterScenarios("swebench");
        Assert.Equal(7, sweScenarios.Count);
        foreach (var sc in sweScenarios)
        {
            Assert.InRange(sc.Weight, 35.0, 50.0);
        }
    }

    [Fact]
    public void DualScoringMetrics_WeightsTaskSuccessAt70PercentAndStepAccuracyAt30Percent()
    {
        var result = new BenchmarkScenarioResult
        {
            ScenarioId = "test-scenario",
            ScenarioName = "Test Scenario",
            ModelId = "test-model",
            Weight = 40.0,
            TotalTurns = 6,
            TurnsPassed = 5,
            StepAccuracy = Math.Round(5 * 100.0 / 6, 1), // 83.3%
            TaskSuccess = false // Patch not applied or verification failed
        };

        // Dual scoring formula: 70% TaskSuccess + 30% StepAccuracy
        var score = Math.Round((0.70 * (result.TaskSuccess ? 100.0 : 0.0)) + (0.30 * result.StepAccuracy), 1);
        result.Score = score;

        // 0.7 * 0 + 0.3 * 83.3 = 25.0%
        Assert.Equal(25.0, result.Score);

        var summary = new ModelBenchmarkSummary
        {
            ModelId = "test-model",
            ModelName = "Test Model"
        };
        summary.ScenarioResults.Add(result);

        Assert.Equal(0.0, summary.TaskSuccessRatePercentage);
        Assert.Equal(83.3, summary.StepAccuracyPercentage);
        Assert.Equal(25.0, summary.PassRatePercentage);

        // If TaskSuccess becomes true:
        result.TaskSuccess = true;
        result.Score = Math.Round((0.70 * 100.0) + (0.30 * result.StepAccuracy), 1); // 70 + 25.0 = 95.0%
        Assert.Equal(95.0, result.Score);
        Assert.Equal(100.0, summary.TaskSuccessRatePercentage);
        Assert.Equal(95.0, summary.PassRatePercentage);
    }

    [Fact]
    public void BfclStrictScenario_ValidatesStrictTypesAndRejectsDistractors()
    {
        var scenario = Assert.Single(BenchmarkScenario.FilterScenarios("bfcl-strict"));
        Assert.Equal(15.0, scenario.Weight);
        Assert.Equal(3, scenario.Turns.Count);

        // Turn 1: Calling distractor should fail validation
        var turn1 = scenario.Turns[0];
        var distractorContext = new TurnValidationContext(
            TurnIndex: 1,
            UserPrompt: turn1.UserPrompt,
            ActualToolCalls: [new FunctionCallContent("call_d1", "deploy_serverless_function", new Dictionary<string, object?>())],
            AssistantFinalText: "",
            AssistantReasoning: null,
            CompletedWithoutError: true);

        var failResult = turn1.CustomValidator!(distractorContext);
        Assert.False(failResult.IsSuccess);
        Assert.Contains("distractor", failResult.Message, StringComparison.OrdinalIgnoreCase);

        // Calling target tool with invalid types should fail validation
        var invalidTypeContext = new TurnValidationContext(
            TurnIndex: 1,
            UserPrompt: turn1.UserPrompt,
            ActualToolCalls: [new FunctionCallContent("call_t1", "deploy_kubernetes_workload", new Dictionary<string, object?>
            {
                ["spec"] = new Dictionary<string, object?>
                {
                    ["name"] = "api-gateway",
                    ["replicas"] = "not_an_int",
                    ["resources"] = new Dictionary<string, object?> { ["cpu_cores"] = 2.5, ["memory_gib"] = 4.0 },
                    ["environment"] = "production"
                }
            })],
            AssistantFinalText: "",
            AssistantReasoning: null,
            CompletedWithoutError: true);

        var typeFailResult = turn1.CustomValidator!(invalidTypeContext);
        Assert.False(typeFailResult.IsSuccess);
        Assert.Contains("Expected integer", typeFailResult.Message);
    }

    [Fact]
    public void BfclMultiTurnScenario_ChainsOutputsCorrectly()
    {
        var scenario = Assert.Single(BenchmarkScenario.FilterScenarios("bfcl-multiturn"));
        Assert.Equal(22.5, scenario.Weight);
        Assert.Equal(9, scenario.Turns.Count);

        // Turn 2 expects wallet_id: "wal_55410" derived from Turn 1
        var turn2 = scenario.Turns[1];
        Assert.False(turn2.ClearHistoryBeforeTurn);

        var wrongWalletContext = new TurnValidationContext(
            TurnIndex: 2,
            UserPrompt: turn2.UserPrompt,
            ActualToolCalls: [new FunctionCallContent("call_w1", "get_wallet_balance", new Dictionary<string, object?> { ["wallet_id"] = "wrong_id" })],
            AssistantFinalText: "",
            AssistantReasoning: null,
            CompletedWithoutError: true);

        var fail = turn2.CustomValidator!(wrongWalletContext);
        Assert.False(fail.IsSuccess);
        Assert.Contains("wal_55410", fail.Message);
    }
}
