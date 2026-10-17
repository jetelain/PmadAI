using Microsoft.Extensions.AI;
using Pmad.AI.Compaction;

namespace Pmad.AI.LlamaSharp.Tests;

public class LlamaSharpSpeculativeSummarizerTests
{
    private class FakeChatClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> _getResponse;

        public FakeChatClient(Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> getResponse)
        {
            _getResponse = getResponse;
        }

        public ChatClientMetadata Metadata => new("FakeChatClient");
        public void Dispose() { }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => _getResponse(chatMessages, options, cancellationToken);
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }

    [Fact]
    public async Task EnqueueSummarization_ProcessesJobAndCachesSummary()
    {
        var fakeClient = new FakeChatClient((msgs, opts, ct) =>
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "- Parsed config file\n- Found 3 services")));
        });

        await using var summarizer = new LlamaSharpSpeculativeSummarizer(fakeClient, new LlamaSharpSpeculativeSummarizerOptions
        {
            MaxSummaryTokens = 50
        });

        // Enqueue job
        summarizer.EnqueueSummarization("session_1", "item_a", SummarizationTarget.ToolResult, "Extensive log output of 500 lines...");

        // Wait up to 2 seconds for worker to process
        string? summary = null;
        for (int i = 0; i < 40; i++)
        {
            if (summarizer.TryGetSummary("session_1", "item_a", out summary))
            {
                break;
            }
            await Task.Delay(50);
        }

        Assert.NotNull(summary);
        Assert.Contains("Parsed config file", summary);
    }

    [Fact]
    public async Task EvictSession_RemovesOnlyTargetSessionData()
    {
        var fakeClient = new FakeChatClient((msgs, opts, ct) =>
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Summary for " + msgs.Last().Text)));
        });

        await using var summarizer = new LlamaSharpSpeculativeSummarizer(fakeClient);

        summarizer.EnqueueSummarization("session_A", "key_1", SummarizationTarget.ToolResult, "Raw 1");
        summarizer.EnqueueSummarization("session_B", "key_2", SummarizationTarget.ToolResult, "Raw 2");

        // Wait until both are cached
        for (int i = 0; i < 40; i++)
        {
            if (summarizer.TryGetSummary("session_A", "key_1", out _) &&
                summarizer.TryGetSummary("session_B", "key_2", out _))
            {
                break;
            }
            await Task.Delay(50);
        }

        Assert.True(summarizer.TryGetSummary("session_A", "key_1", out _));
        Assert.True(summarizer.TryGetSummary("session_B", "key_2", out _));

        // Evict session_A
        summarizer.EvictSession("session_A");

        Assert.False(summarizer.TryGetSummary("session_A", "key_1", out _));
        Assert.True(summarizer.TryGetSummary("session_B", "key_2", out _));
    }
}
