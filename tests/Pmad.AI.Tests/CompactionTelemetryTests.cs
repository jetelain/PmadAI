using System.Globalization;
using Microsoft.Extensions.AI;
using Moq;
using Pmad.AI.Compaction;
using Pmad.AI.Transcripts;

namespace Pmad.AI.Tests;

public class CompactionTelemetryTests
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

    private delegate bool TryGetSummaryCallback(string sessionId, string itemKey, out string? summary);

    [Fact]
    public void CompactionTelemetry_RecordAndAggregates_Correctly()
    {
        var telemetry = new CompactionTelemetry();
        CompactionEvent? lastEvent = null;
        telemetry.EventRecorded += evt => lastEvent = evt;

        telemetry.Record(new CompactionEvent(
            DateTimeOffset.UtcNow,
            "session-1",
            SummarizationTarget.ToolResult,
            "call_123",
            OriginalChars: 5420,
            CompactedChars: 240,
            WasSpeculativeHit: true,
            CompactionMethods.SemanticSummary));

        Assert.NotNull(lastEvent);
        Assert.Equal("call_123", lastEvent.ItemKey);
        Assert.Equal(5180, lastEvent.PrunedChars);
        Assert.Equal((int)Math.Round(5180 / 4.0), lastEvent.EstimatedPrunedTokens);

        telemetry.Record(new CompactionEvent(
            DateTimeOffset.UtcNow,
            "session-1",
            SummarizationTarget.ToolResult,
            "call_456",
            OriginalChars: 3000,
            CompactedChars: 1750,
            WasSpeculativeHit: false,
            CompactionMethods.PrefixSuffixTruncation));

        telemetry.Record(new CompactionEvent(
            DateTimeOffset.UtcNow,
            "session-2",
            SummarizationTarget.ThinkingMonologue,
            "think_1",
            OriginalChars: 4000,
            CompactedChars: 120,
            WasSpeculativeHit: false,
            CompactionMethods.DeterministicStub));

        Assert.Equal(3, telemetry.TotalEvents);
        Assert.Equal(5420 + 3000 + 4000, telemetry.TotalOriginalChars);
        Assert.Equal(240 + 1750 + 120, telemetry.TotalCompactedChars);
        Assert.Equal((5420 - 240) + (3000 - 1750) + (4000 - 120), telemetry.TotalPrunedChars);
        Assert.Equal(1, telemetry.SpeculativeHits);
        Assert.Equal(33.3, telemetry.SpeculativeHitRatePercentage, 1);
        Assert.Equal(1, telemetry.ThinkingTracesCollapsed);
        Assert.Equal(1, telemetry.ToolResultsTruncated);
        Assert.Equal(1, telemetry.ToolResultsSummarized);
        Assert.Equal(2, telemetry.ToolResultsCompacted);

        // Session snapshot filtering
        var s1Snapshot = telemetry.CreateSnapshot("session-1");
        Assert.Equal(2, s1Snapshot.TotalEvents);
        Assert.Equal(1, s1Snapshot.SpeculativeHits);
        Assert.Equal(50.0, s1Snapshot.SpeculativeHitRatePercentage, 1);
        Assert.Equal(0, s1Snapshot.ThinkingTracesCollapsed);
        Assert.Equal(1, s1Snapshot.ToolResultsTruncated);
        Assert.Equal(1, s1Snapshot.ToolResultsSummarized);

        var s2Snapshot = telemetry.CreateSnapshot("session-2");
        Assert.Equal(1, s2Snapshot.TotalEvents);
        Assert.Equal(1, s2Snapshot.ThinkingTracesCollapsed);
        Assert.Equal(0, s2Snapshot.SpeculativeHits);
    }

    [Fact]
    public void CompactionEvent_ToMarkdownBanner_FormatsExpectedBanners()
    {
        var toolSemanticEvent = new CompactionEvent(
            DateTimeOffset.UtcNow,
            "s1",
            SummarizationTarget.ToolResult,
            "call_123",
            OriginalChars: 5420,
            CompactedChars: 240,
            WasSpeculativeHit: true,
            CompactionMethods.SemanticSummary);

        var banner = toolSemanticEvent.ToMarkdownBanner();
        Assert.Equal("> ✂️ **[Compaction Event: Tool Result for `call_123` pruned: 5,420 chars -> 240 chars (Semantic Summary)]**", banner);

        var toolTruncatedEvent = new CompactionEvent(
            DateTimeOffset.UtcNow,
            "s1",
            SummarizationTarget.ToolResult,
            "call_999",
            OriginalChars: 3500,
            CompactedChars: 1750,
            WasSpeculativeHit: false,
            CompactionMethods.PrefixSuffixTruncation);

        var truncBanner = toolTruncatedEvent.ToMarkdownBanner();
        Assert.Equal("> ✂️ **[Compaction Event: Tool Result for `call_999` pruned: 3,500 chars -> 1,750 chars (Prefix-Suffix Truncation)]**", truncBanner);

        var thinkingStubEvent = new CompactionEvent(
            DateTimeOffset.UtcNow,
            "s1",
            SummarizationTarget.ThinkingMonologue,
            "reasoning_1",
            OriginalChars: 4120,
            CompactedChars: 120,
            WasSpeculativeHit: false,
            CompactionMethods.DeterministicStub);

        var thinkBanner = thinkingStubEvent.ToMarkdownBanner();
        Assert.Equal("> ✂️ **[Compaction Event: Thinking Trace pruned: 4,120 chars -> 120 chars (Deterministic Stub)]**", thinkBanner);
    }

    [Fact]
    public async Task ToolResultCompactor_EmitsCompactionEvents_OnFallbackAndSpeculative()
    {
        var telemetry = new CompactionTelemetry();
        var mockSummarizer = new Mock<ISpeculativeSummarizer>();
        mockSummarizer.Setup(s => s.TryGetSummary("session-test", "call_spec", out It.Ref<string?>.IsAny))
            .Returns(new TryGetSummaryCallback((string sId, string key, out string? sum) =>
            {
                sum = "[TOOL_RESULT_SUMMARY]\nStatus: SUCCESS\nKey_Entities: [test]\nOutput_Summary: Completed successfully\n[/TOOL_RESULT_SUMMARY]";
                return true;
            }));

        var fake = new FakeChatClient((msgs, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "OK"))));
        var client = new ToolResultCompactionChatClient(fake, new ToolResultCompactionOptions
        {
            KeepVerbatimCount = 1,
            FallbackCharacterLimit = 100,
            FallbackPrefixLength = 50,
            FallbackSuffixLength = 20,
            Summarizer = mockSummarizer.Object,
            Telemetry = telemetry
        });

        // 3 tool results: Turn 1 (speculative hit), Turn 2 (fallback truncation), Turn 3 (recent, verbatim)
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Tool, [new FunctionResultContent("call_spec", new string('A', 500))]),
            new(ChatRole.Tool, [new FunctionResultContent("call_fallback", new string('B', 500))]),
            new(ChatRole.Tool, [new FunctionResultContent("call_recent", new string('C', 500))])
        };

        var options = new ChatOptions { ConversationId = "session-test" };
        await client.GetResponseAsync(messages, options);

        Assert.Equal(2, telemetry.TotalEvents);

        var specEvent = telemetry.Events.First(e => e.ItemKey == "call_spec");
        Assert.True(specEvent.WasSpeculativeHit);
        Assert.Equal(CompactionMethods.SemanticSummary, specEvent.Method);
        Assert.Equal(500, specEvent.OriginalChars);

        var fallbackEvent = telemetry.Events.First(e => e.ItemKey == "call_fallback");
        Assert.False(fallbackEvent.WasSpeculativeHit);
        Assert.Equal(CompactionMethods.PrefixSuffixTruncation, fallbackEvent.Method);
        Assert.Equal(500, fallbackEvent.OriginalChars);
    }

    [Fact]
    public async Task ThinkingTraceCompactor_EmitsCompactionEvents_OnFallbackAndSpeculative()
    {
        var telemetry = new CompactionTelemetry();
        var mockSummarizer = new Mock<ISpeculativeSummarizer>();
        mockSummarizer.Setup(s => s.TryGetSummary("session-think", It.IsAny<string>(), out It.Ref<string?>.IsAny))
            .Returns(new TryGetSummaryCallback((string sId, string key, out string? sum) =>
            {
                sum = "[Reasoned Summary: Explored path A and selected method B]";
                return true;
            }));

        var fake = new FakeChatClient((msgs, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "OK"))));
        var client = new ThinkingTraceCompactionChatClient(fake, new ThinkingTraceCompactionOptions
        {
            KeepVerbatimCount = 1,
            FallbackCharacterLimit = 30,
            Summarizer = mockSummarizer.Object,
            Telemetry = telemetry
        });

        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [new TextReasoningContent("Long monologue 1 that exceeds thirty chars"), new TextContent("A")]),
            new(ChatRole.Assistant, [new TextReasoningContent("Recent monologue 2"), new TextContent("B")])
        };

        var options = new ChatOptions { ConversationId = "session-think" };
        await client.GetResponseAsync(messages, options);

        var thinkEvent = Assert.Single(telemetry.Events);
        Assert.Equal(SummarizationTarget.ThinkingMonologue, thinkEvent.Target);
        Assert.True(thinkEvent.WasSpeculativeHit);
        Assert.Equal(CompactionMethods.SemanticSummary, thinkEvent.Method);
    }

    [Fact]
    public async Task VerbatimTranscriptChatClient_ReceivesCompactionEvents_AndEmbedsBanners()
    {
        var telemetry = new CompactionTelemetry();
        var fake = new FakeChatClient((msgs, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done"))));

        var transcriptOptions = new VerbatimTranscriptOptions
        {
            SessionTitle = "Compaction Test Session",
            CompactionTelemetry = telemetry,
            AutoSaveOnDispose = false
        };

        var transcriptClient = new VerbatimTranscriptChatClient(fake, transcriptOptions);

        // Turn 1: Tool result that gets recorded in transcript
        var toolTurn = new List<ChatMessage>
        {
            new(ChatRole.Tool, [new FunctionResultContent("call_abc", "Raw initial output from execution")])
        };
        await transcriptClient.GetResponseAsync(toolTurn, new ChatOptions { ConversationId = "session-abc" });

        // Now simulate compaction event arriving from pipeline
        telemetry.Record(new CompactionEvent(
            DateTimeOffset.UtcNow,
            "session-abc",
            SummarizationTarget.ToolResult,
            "call_abc",
            OriginalChars: 5420,
            CompactedChars: 240,
            WasSpeculativeHit: true,
            CompactionMethods.SemanticSummary));

        var md = transcriptClient.ToMarkdown();

        // Must contain frontmatter tracking
        Assert.Contains("compaction_events: 1", md);
        Assert.Contains("compaction_pruned_chars: 5180", md);

        // Must contain collapsible compaction events section
        Assert.Contains("<details open>", md);
        Assert.Contains("<summary>✂️ Compaction Events (1)</summary>", md);

        // Must contain the exact required banner format
        Assert.Contains("> ✂️ **[Compaction Event: Tool Result for `call_abc` pruned: 5,420 chars -> 240 chars (Semantic Summary)]**", md);
    }
}
