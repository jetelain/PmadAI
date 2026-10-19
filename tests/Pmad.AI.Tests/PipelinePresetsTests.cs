using Microsoft.Extensions.AI;
using Pmad.AI.Compaction;
using Pmad.AI.Guardrails;
using Pmad.AI.Healing;
using Pmad.AI.Tools;

namespace Pmad.AI.Tests;

public class PipelinePresetsTests
{
    private class DummyChatClient : IChatClient
    {
        public ChatClientMetadata Metadata => new("DummyChatClient");
        public void Dispose() { }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var msg = new ChatMessage(ChatRole.Assistant, "<tool_call>{\"name\":\"get_weather\",\"arguments\":{\"city\":\"Paris\"}}</tool_call>");
            return Task.FromResult(new ChatResponse(msg));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }

    private class DummySummarizer : ISpeculativeSummarizer
    {
        public void EnqueueSummarization(string sessionId, string itemKey, string rawText) { }

        public bool TryGetSummary(string sessionId, string itemKey, out string? summary)
        {
            summary = "Summary of content";
            return true;
        }

        public void EvictSession(string sessionId) { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task UseRecommendedSlmPipeline_AppliesExpectedDefaultsAndResolvesServices()
    {
        var inner = new DummyChatClient();
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools = new Dictionary<string, ToolMapping>
            {
                ["get_weather"] = new(aliases: ["weather"])
            }
        });

        bool breakerConfigured = false;
        bool thinkingConfigured = false;
        bool toolResultConfigured = false;
        bool healingConfigured = false;

        var builder = new ChatClientBuilder(inner);
        builder.UseRecommendedSlmPipeline(
            dispatcher: dispatcher,
            configureBreaker: opts =>
            {
                breakerConfigured = true;
                Assert.Equal(3, opts.PingPongThreshold);
                Assert.True(opts.ResetOnUserPrompt);
                Assert.Equal(30, opts.MaxToolHistory);
            },
            configureThinking: opts =>
            {
                thinkingConfigured = true;
                Assert.Equal(5, opts.KeepVerbatimCount);
                Assert.Equal(1500, opts.FallbackCharacterLimit);
                Assert.Null(opts.Summarizer);
            },
            configureToolResult: opts =>
            {
                toolResultConfigured = true;
                Assert.Equal(5, opts.KeepVerbatimCount);
                Assert.Equal(4500, opts.FallbackCharacterLimit);
            },
            configureHealing: opts =>
            {
                healingConfigured = true;
                Assert.True(opts.EnforceAvailableToolsGuardrail);
            });

        var client = builder.Build();

        Assert.True(breakerConfigured);
        Assert.True(thinkingConfigured);
        Assert.True(toolResultConfigured);
        Assert.True(healingConfigured);

        var breaker = client.GetService<ToolLoopCircuitBreaker>();
        Assert.NotNull(breaker);

        // Verify that healing works end-to-end through the pipeline
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "What is the weather in Paris?")]);
        var call = Assert.Single(response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>());
        Assert.Equal("get_weather", call.Name);
    }

    [Fact]
    public void UseRecommendedSlmPipeline_WithPartialOptions_Works()
    {
        var inner = new DummyChatClient();
        var dispatcher = new LenientToolDispatcher();
        var builder = new ChatClientBuilder(inner);

        bool breakerConfigured = false;
        bool toolResultConfigured = false;

        builder.UseRecommendedSlmPipeline(
            dispatcher: dispatcher,
            configureBreaker: opts => breakerConfigured = true,
            configureToolResult: opts => toolResultConfigured = true);

        var client = builder.Build();
        Assert.True(breakerConfigured);
        Assert.True(toolResultConfigured);
        Assert.NotNull(client.GetService<ToolLoopCircuitBreaker>());
    }

    [Fact]
    public async Task UseRecommendedLocalLlmPipeline_WithSummarizer_ConfiguresFullPipeline()
    {
        var inner = new DummyChatClient();
        var summarizer = new DummySummarizer();
        var dispatcher = new LenientToolDispatcher();

        bool breakerConfigured = false;
        bool thinkingConfigured = false;
        bool toolResultConfigured = false;
        bool healingConfigured = false;

        var builder = new ChatClientBuilder(inner);
        builder.UseRecommendedLocalLlmPipeline(
            dispatcher: dispatcher,
            summarizer: summarizer,
            configureBreaker: opts =>
            {
                breakerConfigured = true;
                Assert.Equal(3, opts.PingPongThreshold);
                Assert.True(opts.ResetOnUserPrompt);
            },
            configureThinking: opts =>
            {
                thinkingConfigured = true;
                Assert.Equal(5, opts.KeepVerbatimCount);
                Assert.Same(summarizer, opts.Summarizer);
            },
            configureToolResult: opts =>
            {
                toolResultConfigured = true;
                Assert.Equal(5, opts.KeepVerbatimCount);
            },
            configureHealing: opts =>
            {
                healingConfigured = true;
            });

        var client = builder.Build();

        Assert.True(breakerConfigured);
        Assert.True(thinkingConfigured);
        Assert.True(toolResultConfigured);
        Assert.True(healingConfigured);

        var breaker = client.GetService<ToolLoopCircuitBreaker>();
        Assert.NotNull(breaker);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")]);
        var call = Assert.Single(response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>());
        Assert.Equal("get_weather", call.Name);
    }
}
