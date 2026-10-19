using Microsoft.Extensions.AI;
using Moq;
using Pmad.AI.Compaction;

namespace Pmad.AI.Tests;

public class CompactionTests
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
    public async Task ThinkingTraceCompaction_PrunesOlderReasoning_FallbackPrefixSuffix()
    {
        IEnumerable<ChatMessage>? forwardedMessages = null;
        var fake = new FakeChatClient((msgs, _, _) =>
        {
            forwardedMessages = msgs.ToList();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done")));
        });

        var client = new ThinkingTraceCompactionChatClient(fake, new ThinkingTraceCompactionOptions
        {
            KeepVerbatimCount = 2,
            FallbackCharacterLimit = 50,
            FallbackPrefixLength = 20,
            FallbackSuffixLength = 15,
            MutateInPlace = false
        });

        // 3 assistant turns with reasoning
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [new TextReasoningContent("Turn 1 first line\nSecond line of detailed reasoning"), new TextContent("Reply 1")]),
            new(ChatRole.User, "Query 2"),
            new(ChatRole.Assistant, [new TextReasoningContent("Turn 2 reasoning"), new TextContent("Reply 2")]),
            new(ChatRole.User, "Query 3"),
            new(ChatRole.Assistant, [new TextReasoningContent("Turn 3 reasoning"), new TextContent("Reply 3")])
        };

        await client.GetResponseAsync(messages);

        Assert.NotNull(forwardedMessages);
        var fList = forwardedMessages.ToList();

        // Turn 1 (oldest reasoning turn) should be compacted with prefix-suffix truncation
        var t1Reasoning = fList[0].Contents.OfType<TextReasoningContent>().First();
        Assert.StartsWith("Turn 1 first line\nSe", t1Reasoning.Text);
        Assert.EndsWith("ailed reasoning", t1Reasoning.Text);
        Assert.Contains("[Truncated 16 characters]", t1Reasoning.Text);

        // Turn 2 & 3 (inside verbatim window 2) should be verbatim
        var t2Reasoning = fList[2].Contents.OfType<TextReasoningContent>().First();
        Assert.Equal("Turn 2 reasoning", t2Reasoning.Text);

        var t3Reasoning = fList[4].Contents.OfType<TextReasoningContent>().First();
        Assert.Equal("Turn 3 reasoning", t3Reasoning.Text);

        // Original message list was not mutated
        Assert.Equal("Turn 1 first line\nSecond line of detailed reasoning", messages[0].Contents.OfType<TextReasoningContent>().First().Text);
    }

    [Fact]
    public async Task ThinkingTraceCompaction_UsesSpeculativeSummary_WhenAvailable()
    {
        var mockSummarizer = new Mock<ISpeculativeSummarizer>();
        mockSummarizer.Setup(s => s.TryGetSummary(It.IsAny<string>(), It.IsAny<string>(), out It.Ref<string?>.IsAny))
            .Returns(new TryGetSummaryCallback((string sId, string key, out string? sum) =>
            {
                sum = "Analyzed architecture components";
                return true;
            }));

        IEnumerable<ChatMessage>? forwardedMessages = null;
        var fake = new FakeChatClient((msgs, _, _) =>
        {
            forwardedMessages = msgs.ToList();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done")));
        });

        var client = new ThinkingTraceCompactionChatClient(fake, new ThinkingTraceCompactionOptions
        {
            KeepVerbatimCount = 1,
            FallbackCharacterLimit = 10,
            FallbackPrefixLength = 5,
            FallbackSuffixLength = 5,
            Summarizer = mockSummarizer.Object
        });

        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [new TextReasoningContent("Old long monologue..."), new TextContent("Done 1")]),
            new(ChatRole.Assistant, [new TextReasoningContent("Recent monologue"), new TextContent("Done 2")])
        };

        await client.GetResponseAsync(messages);

        var fList = forwardedMessages!.ToList();
        var compacted = fList[0].Contents.OfType<TextReasoningContent>().First();
        Assert.Equal("[Reasoned Summary: Analyzed architecture components]", compacted.Text);
    }

    private delegate bool TryGetSummaryCallback(string sessionId, string itemKey, out string? summary);

    [Fact]
    public void ToolResultCompactor_TruncatesBulkyOutputsOutsideWindow()
    {
        var compactor = new ToolResultCompactor(new ToolResultCompactionOptions
        {
            KeepVerbatimCount = 1,
            FallbackCharacterLimit = 200,
            FallbackPrefixLength = 100,
            FallbackSuffixLength = 50,
            MutateInPlace = false
        });

        var bulkyText = new string('A', 500);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Tool, [new FunctionResultContent("call_old", bulkyText)]),
            new(ChatRole.Tool, [new FunctionResultContent("call_recent", bulkyText)])
        };

        var compacted = compactor.Compact(messages);

        // Recent tool result remains verbatim
        var recentResult = (compacted[1].Contents[0] as FunctionResultContent)!.Result?.ToString();
        Assert.Equal(bulkyText, recentResult);

        // Older tool result is truncated
        var oldResult = (compacted[0].Contents[0] as FunctionResultContent)!.Result?.ToString()!;
        Assert.StartsWith(new string('A', 100), oldResult);
        Assert.EndsWith(new string('A', 50), oldResult);
        Assert.Contains("[Truncated 350 characters]", oldResult);

        // Original list untouched
        var origOld = (messages[0].Contents[0] as FunctionResultContent)!.Result?.ToString();
        Assert.Equal(bulkyText, origOld);
    }

    [Fact]
    public void ToolResultCompactor_Validation_ThrowsOnInvalidLimit()
    {
        Assert.Throws<ArgumentException>(() => new ToolResultCompactor(new ToolResultCompactionOptions
        {
            FallbackPrefixLength = 1500,
            FallbackSuffixLength = 500,
            FallbackCharacterLimit = 1800 // Less than 1500 + 500
        }));
    }

    [Fact]
    public void ThinkingTraceCompactor_TruncatesBulkyOutputsOutsideWindow()
    {
        var compactor = new ThinkingTraceCompactor(new ThinkingTraceCompactionOptions
        {
            KeepVerbatimCount = 1,
            FallbackCharacterLimit = 200,
            FallbackPrefixLength = 100,
            FallbackSuffixLength = 50,
            MutateInPlace = false
        });

        var bulkyText = new string('T', 500);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [new TextReasoningContent(bulkyText), new TextContent("Old")]),
            new(ChatRole.Assistant, [new TextReasoningContent(bulkyText), new TextContent("Recent")])
        };

        var compacted = compactor.Compact(messages);

        // Recent reasoning remains verbatim
        var recentReasoning = compacted[1].Contents.OfType<TextReasoningContent>().First().Text;
        Assert.Equal(bulkyText, recentReasoning);

        // Older reasoning is truncated
        var oldReasoning = compacted[0].Contents.OfType<TextReasoningContent>().First().Text;
        Assert.StartsWith(new string('T', 100), oldReasoning);
        Assert.EndsWith(new string('T', 50), oldReasoning);
        Assert.Contains("[Truncated 350 characters]", oldReasoning);

        // Original list untouched
        var origOld = messages[0].Contents.OfType<TextReasoningContent>().First().Text;
        Assert.Equal(bulkyText, origOld);
    }

    [Fact]
    public void ThinkingTraceCompactor_Validation_ThrowsOnInvalidLimit()
    {
        Assert.Throws<ArgumentException>(() => new ThinkingTraceCompactor(new ThinkingTraceCompactionOptions
        {
            FallbackPrefixLength = 1500,
            FallbackSuffixLength = 500,
            FallbackCharacterLimit = 1800 // Less than 1500 + 500
        }));
    }

    [Fact]
    public void CompactionItemKeyResolver_ProducesStableDeterministicKeys()
    {
        var msg1 = new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("Reasoning sample text")]);
        var msg2 = new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("Reasoning sample text")]);

        var key1 = CompactionItemKeyResolver.ResolveItemKey(msg1, CompactionTarget.ThinkingMonologue, "Reasoning sample text");
        var key2 = CompactionItemKeyResolver.ResolveItemKey(msg2, CompactionTarget.ThinkingMonologue, "Reasoning sample text");

        Assert.Equal(key1, key2);

        // Explicit id in AdditionalProperties takes priority
        var msgWithId = new ChatMessage(ChatRole.Assistant, "Test")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { ["id"] = "explicit_123" }
        };
        var explicitKey = CompactionItemKeyResolver.ResolveItemKey(msgWithId, CompactionTarget.ThinkingMonologue, "Test");
        Assert.Equal("explicit_123", explicitKey);
    }

    [Fact]
    public void CompactionItemKeyResolver_ToolResultsInSameMessage_GetDistinctCallIdKeys()
    {
        var msg = new ChatMessage(ChatRole.Tool,
        [
            new FunctionResultContent("call_a", "A"),
            new FunctionResultContent("call_b", "B")
        ]);

        var keyA = CompactionItemKeyResolver.ResolveItemKey(msg, CompactionTarget.ToolResult, "A", "call_a");
        var keyB = CompactionItemKeyResolver.ResolveItemKey(msg, CompactionTarget.ToolResult, "B", "call_b");

        Assert.Equal("call_a", keyA);
        Assert.Equal("call_b", keyB);
    }

    [Fact]
    public void CompactionItemKeyResolver_DistinctTextContentsInSameMessage_GetDistinctKeys()
    {
        var msg = new ChatMessage(ChatRole.Tool,
        [
            new TextContent("Result 1 text"),
            new TextContent("Result 2 text")
        ]);

        var key1 = CompactionItemKeyResolver.ResolveItemKey(msg, CompactionTarget.ToolResult, "Result 1 text");
        var key2 = CompactionItemKeyResolver.ResolveItemKey(msg, CompactionTarget.ToolResult, "Result 2 text");

        Assert.NotEqual(key1, key2);
    }

    private sealed class RecordingSummarizer : ISpeculativeSummarizer
    {
        public List<(string Key, string Text)> Enqueued { get; } = [];
        public void EnqueueSummarization(string sessionId, string itemKey, string rawText) => Enqueued.Add((itemKey, rawText));
        public bool TryGetSummary(string sessionId, string itemKey, out string? summary) { summary = null; return false; }
        public void EvictSession(string sessionId) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThinkingTraceCompaction_EnqueuesOriginalUntruncatedReasoning(bool mutateInPlace)
    {
        var summarizer = new RecordingSummarizer();
        var fake = new FakeChatClient((_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))));
        var client = new ThinkingTraceCompactionChatClient(fake, new ThinkingTraceCompactionOptions
        {
            Summarizer = summarizer,
            KeepVerbatimCount = 1,
            FallbackCharacterLimit = 30,
            FallbackPrefixLength = 15,
            FallbackSuffixLength = 10,
            MutateInPlace = mutateInPlace
        });

        var bigReasoning = "This is a very long chain-of-thought monologue that exceeds thirty characters by far.";
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [new TextReasoningContent(bigReasoning), new TextContent("old response")]),
            new(ChatRole.Assistant, [new TextReasoningContent("recent reasoning"), new TextContent("recent response")])
        };

        await client.GetResponseAsync(messages);

        // Summarizer should receive the uncompacted text, not the truncated fallback
        var entry = summarizer.Enqueued.First(e => e.Text == bigReasoning);
        Assert.Equal(bigReasoning, entry.Text);
        Assert.DoesNotContain(summarizer.Enqueued, e => e.Text.StartsWith("[Reasoned:"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThinkingTraceCompaction_RetainsReasoningVerbatim_WhenUnderFallbackCharacterLimit(bool mutateInPlace)
    {
        IEnumerable<ChatMessage>? forwardedMessages = null;
        var fake = new FakeChatClient((msgs, _, _) =>
        {
            forwardedMessages = msgs.ToList();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done")));
        });

        var client = new ThinkingTraceCompactionChatClient(fake, new ThinkingTraceCompactionOptions
        {
            KeepVerbatimCount = 1,
            FallbackCharacterLimit = 200,
            FallbackPrefixLength = 100,
            FallbackSuffixLength = 50,
            MutateInPlace = mutateInPlace
        });

        const string shortReasoning = "Concise reasoning below threshold.";
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [new TextReasoningContent(shortReasoning), new TextContent("Reply 1")]),
            new(ChatRole.Assistant, [new TextReasoningContent("Recent thought"), new TextContent("Reply 2")])
        };

        await client.GetResponseAsync(messages);

        var fList = forwardedMessages!.ToList();
        var t1Reasoning = fList[0].Contents.OfType<TextReasoningContent>().First();
        Assert.Equal(shortReasoning, t1Reasoning.Text);

        if (!mutateInPlace)
        {
            Assert.Equal(shortReasoning, messages[0].Contents.OfType<TextReasoningContent>().First().Text);
        }
    }

    [Fact]
    public async Task ThinkingTraceCompaction_SkipsSpeculativeEnqueue_WhenUnderFallbackCharacterLimit()
    {
        var summarizer = new RecordingSummarizer();
        var fake = new FakeChatClient((_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))));
        var client = new ThinkingTraceCompactionChatClient(fake, new ThinkingTraceCompactionOptions
        {
            Summarizer = summarizer,
            KeepVerbatimCount = 1,
            FallbackCharacterLimit = 500,
            FallbackPrefixLength = 300,
            FallbackSuffixLength = 100
        });

        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [new TextReasoningContent("Short monologue that is well under 500 chars"), new TextContent("Reply 1")]),
            new(ChatRole.Assistant, [new TextReasoningContent("Recent reasoning"), new TextContent("Reply 2")])
        };

        await client.GetResponseAsync(messages);

        Assert.Empty(summarizer.Enqueued);
    }
}
