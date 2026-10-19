using Microsoft.Extensions.AI;
using Pmad.AI.Benchmark.Engine;
using Xunit;

namespace Pmad.AI.Benchmark.Tests;

public class OllamaChatClientFactoryTests
{
    [Fact]
    public void CreateOllama_ReturnsChatClientAndConfiguresTimeout()
    {
        var options = new LlmProviderOptions
        {
            Endpoint = "http://localhost:11434",
            ModelId = "qwen3.6",
            RequestTimeoutMinutes = 120,
            ContextSize = 16384,
            EnableReasoning = false
        };

        using var client = (IDisposable)OllamaChatClientFactory.CreateOllama(options);
        Assert.NotNull(client);
        Assert.True(client is IChatClient);

        // Verify underlying HttpClient timeout and endpoint
        var httpClient = ((IChatClient)client).GetService<HttpClient>();
        Assert.NotNull(httpClient);
        Assert.Equal(TimeSpan.FromHours(2), httpClient.Timeout);
        Assert.Equal(new Uri("http://localhost:11434/"), httpClient.BaseAddress);
    }

    [Fact]
    public void CreateOllama_WithRoutingTarget_AddsHeader()
    {
        var options = new LlmProviderOptions
        {
            Endpoint = "http://localhost:11434",
            ModelId = "qwen3.6"
        };

        using var client = (IDisposable)OllamaChatClientFactory.CreateOllama(options, routingTarget: OllamaRoutingTarget.Cloud);
        var httpClient = ((IChatClient)client).GetService<HttpClient>();
        Assert.NotNull(httpClient);
        Assert.True(httpClient.DefaultRequestHeaders.Contains("X-OllamaRouter-Target"));
        Assert.Equal("Cloud", httpClient.DefaultRequestHeaders.GetValues("X-OllamaRouter-Target").First());
    }

    [Fact]
    public void CreateDefaultChatOptions_SetsExpectedProperties()
    {
        var chatOpts = OllamaChatClientFactory.CreateDefaultChatOptions(
            modelId: "qwen",
            temperature: 0.2f,
            reasoningEffort: "xhigh",
            isOpenAi: true,
            contextSize: 32768,
            enableReasoning: true);

        Assert.Equal("qwen", chatOpts.ModelId);
        Assert.Equal(0.2f, chatOpts.Temperature);
        Assert.NotNull(chatOpts.AdditionalProperties);
        Assert.Equal(32768, chatOpts.AdditionalProperties["num_ctx"]);
        Assert.Equal(true, chatOpts.AdditionalProperties["think"]);
        // For OpenAI, "xhigh" normalizes to "high"
        Assert.Equal("high", chatOpts.AdditionalProperties["reasoning_effort"]);
    }

    [Theory]
    [InlineData("xhigh", true, "high")]
    [InlineData("xhigh", false, "xhigh")]
    [InlineData("low", true, "low")]
    [InlineData(null, false, "xhigh")]
    public void NormalizeReasoningEffort_ValidatesMappings(string? input, bool isOpenAi, string expected)
    {
        var result = OllamaChatClientFactory.NormalizeReasoningEffort(input, isOpenAi);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task PreloadAndOffload_CompleteWithoutThrowing()
    {
        // Safe executions even if endpoint is dummy
        await OllamaChatClientFactory.PreloadModelAsync("test", "http://127.0.0.1:59999");
        await OllamaChatClientFactory.OffloadModelAsync("test", "http://127.0.0.1:59999");
    }

    [Fact]
    public async Task IsOllamaRunning_And_GetAvailableModels_HandleLiveOrOfflineEndpoints()
    {
        var isOffline = await OllamaChatClientFactory.IsOllamaRunningAsync("http://127.0.0.1:59999");
        Assert.False(isOffline);

        var offlineModels = await OllamaChatClientFactory.GetAvailableModelsAsync("http://127.0.0.1:59999");
        Assert.Empty(offlineModels);

        // If pcportable is available on the local network, test live
        if (await OllamaChatClientFactory.IsOllamaRunningAsync("http://pcportable:11434/"))
        {
            var liveModels = await OllamaChatClientFactory.GetAvailableModelsAsync("http://pcportable:11434/");
            Assert.NotEmpty(liveModels);
            Assert.Contains(liveModels, m => m.Contains("Qwen", StringComparison.OrdinalIgnoreCase));
        }
    }
}
