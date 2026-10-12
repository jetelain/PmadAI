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
        Assert.Equal(4, scenarios.Count);

        Assert.Contains(scenarios, s => s.Id == "weather");
        Assert.Contains(scenarios, s => s.Id == "calculator");
        Assert.Contains(scenarios, s => s.Id == "support");
        Assert.Contains(scenarios, s => s.Id == "direct");

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
    [InlineData("all", 4)]
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

        Assert.Equal(4, summary.ScenariosRun);
        Assert.Equal(4, summary.ScenariosPassed);
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
        Assert.Contains("Peak RAM", md);
        Assert.Contains("✅ PASS", md);
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
