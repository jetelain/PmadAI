using Microsoft.Extensions.AI;
using Pmad.AI.Benchmark.Configuration;
using Pmad.AI.Benchmark.Engine;
using Pmad.AI.Benchmark.Reporting;
using Pmad.AI.Benchmark.Scenarios;

namespace Pmad.AI.LlamaSharp.Tests;

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
        Assert.Equal(7, scenarios.Count);

        Assert.Contains(scenarios, s => s.Id == "weather");
        Assert.Contains(scenarios, s => s.Id == "calculator");
        Assert.Contains(scenarios, s => s.Id == "support");
        Assert.Contains(scenarios, s => s.Id == "direct");
        Assert.Contains(scenarios, s => s.Id == "compaction");
        Assert.Contains(scenarios, s => s.Id == "healing");
        Assert.Contains(scenarios, s => s.Id == "circuit-breaker");

        foreach (var sc in scenarios)
        {
            Assert.NotEmpty(sc.Name);
            Assert.NotEmpty(sc.Description);
            Assert.NotEmpty(sc.Tools);
            Assert.NotEmpty(sc.Turns);
        }
    }

    [Theory]
    [InlineData("weather", "weather")]
    [InlineData("calc", "calculator")]
    [InlineData("support", "support")]
    [InlineData("direct", "direct")]
    [InlineData("compaction", "compaction")]
    [InlineData("healing", "healing")]
    [InlineData("circuit", "circuit-breaker")]
    [InlineData("all", 7)]
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
        var scenarios = BenchmarkScenario.GetAllScenarios();
        var runner = new BenchmarkRunner(mockClient, "mock-test-client", verbose: false);

        var summary = await runner.RunAllAsync(scenarios);

        Assert.Equal(7, summary.ScenariosRun);
        Assert.Equal(7, summary.ScenariosPassed);
        Assert.Equal(0, summary.ScenariosFailed);
        Assert.Equal(100.0, summary.PassRatePercentage);
        Assert.True(summary.TotalToolCalls >= 5);
        Assert.True(summary.TotalOutputTokens > 0);
        Assert.True(summary.TotalPromptTokens > 0);
        Assert.True(summary.TotalTimeMs >= 0);
        Assert.True(summary.PeakMemoryMb > 0);

        foreach (var scResult in summary.ScenarioResults)
        {
            Assert.Equal(BenchmarkStatus.Passed, scResult.Status);
            Assert.True(scResult.ToolAccuracyPercentage >= 100);
            Assert.True(scResult.PeakMemoryMb > 0);
            Assert.NotEmpty(scResult.TurnResults);
        }
    }

    [Fact]
    public async Task BenchmarkRunner_DirectNegativeTest_FailsIfModelInvokesTool()
    {
        // Custom mock that incorrectly invokes a tool when none is expected
        var badMock = new BadMockClient();
        var scenario = new DirectResponseScenario();
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
}
