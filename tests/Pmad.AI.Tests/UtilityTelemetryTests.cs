using Microsoft.Extensions.AI;
using Pmad.AI.Guardrails;
using Pmad.AI.Healing;
using Pmad.AI.Tools;

namespace Pmad.AI.Tests;

public class UtilityTelemetryTests
{
    [Fact]
    public void ToolCallHealingTelemetry_AggregatesAndFiltersCorrectly()
    {
        var telemetry = new ToolCallHealingTelemetry();
        telemetry.Record(ToolCallHealingEventType.PseudoTagExtracted, "get_weather", "s1");
        telemetry.Record(ToolCallHealingEventType.FenceStripped, "get_weather", "s1");
        telemetry.Record(ToolCallHealingEventType.JsonRepaired, "get_weather", "s1");
        telemetry.Record(ToolCallHealingEventType.CallIdGenerated, "get_weather", "s2");
        telemetry.Record(ToolCallHealingEventType.ActionBlockExtracted, "run_cmd", "s2");
        telemetry.Record(ToolCallHealingEventType.ArgumentUnwrapped, "run_cmd", "s2");

        var globalSnapshot = telemetry.CreateSnapshot();
        Assert.Equal(6, globalSnapshot.TotalInterventions);
        Assert.Equal(1, globalSnapshot.PseudoTagsExtracted);
        Assert.Equal(1, globalSnapshot.ActionBlocksExtracted);
        Assert.Equal(1, globalSnapshot.JsonRepairs);
        Assert.Equal(1, globalSnapshot.FencesStripped);
        Assert.Equal(1, globalSnapshot.CallIdsGenerated);
        Assert.Equal(1, globalSnapshot.ArgumentsUnwrapped);

        var s1Snapshot = telemetry.CreateSnapshot("s1");
        Assert.Equal(3, s1Snapshot.TotalInterventions);
        Assert.Equal(1, s1Snapshot.PseudoTagsExtracted);
        Assert.Equal(1, s1Snapshot.FencesStripped);
        Assert.Equal(1, s1Snapshot.JsonRepairs);
        Assert.Equal(0, s1Snapshot.CallIdsGenerated);
    }

    [Fact]
    public void CircuitBreakerTelemetry_AggregatesAndFiltersCorrectly()
    {
        var telemetry = new CircuitBreakerTelemetry();
        telemetry.Record(CircuitBreakerEventType.ToolPingPong, "search_code", "Ping pong detected", "s1");
        telemetry.Record(CircuitBreakerEventType.StreamingRepetition, null, "Streaming repetition", "s2");
        telemetry.Record(CircuitBreakerEventType.NoProgress, "search_code", "Identical observation streak", "s1");

        var globalSnapshot = telemetry.CreateSnapshot();
        Assert.Equal(3, globalSnapshot.TotalTrips);
        Assert.Equal(1, globalSnapshot.ToolPingPongTrips);
        Assert.Equal(1, globalSnapshot.StreamingRepetitionTrips);
        Assert.Equal(1, globalSnapshot.NoProgressTrips);

        var s1Snapshot = telemetry.CreateSnapshot("s1");
        Assert.Equal(2, s1Snapshot.TotalTrips);
        Assert.Equal(1, s1Snapshot.ToolPingPongTrips);
        Assert.Equal(0, s1Snapshot.StreamingRepetitionTrips);
        Assert.Equal(1, s1Snapshot.NoProgressTrips);
    }

    [Fact]
    public void LenientDispatchTelemetry_AggregatesAndFiltersCorrectly()
    {
        var telemetry = new LenientDispatchTelemetry();
        telemetry.Record(LenientDispatchEventType.ToolAliasResolved, "get_current_weather", "get_weather", "s1");
        telemetry.Record(LenientDispatchEventType.ParameterAliasResolved, "city", "location", "s1");
        telemetry.Record(LenientDispatchEventType.NamespaceStripped, "functions.calc", "calc", "s2");
        telemetry.Record(LenientDispatchEventType.CasingNormalized, "GET_WEATHER", "get_weather", "s2");
        telemetry.Record(LenientDispatchEventType.EnumValueSnapped, "staging", "Staging", "s1");
        telemetry.Record(LenientDispatchEventType.EnumValueUnmatched, "unknown_cluster", "unknown_cluster", "s2");

        var globalSnapshot = telemetry.CreateSnapshot();
        Assert.Equal(5, globalSnapshot.TotalHits);
        Assert.Equal(1, globalSnapshot.ToolAliasesResolved);
        Assert.Equal(1, globalSnapshot.ParameterAliasesResolved);
        Assert.Equal(1, globalSnapshot.NamespacesStripped);
        Assert.Equal(1, globalSnapshot.CasingNormalized);
        Assert.Equal(1, globalSnapshot.EnumValuesSnapped);
        Assert.Equal(1, globalSnapshot.EnumValuesUnmatched);

        var s1Snapshot = telemetry.CreateSnapshot("s1");
        Assert.Equal(3, s1Snapshot.TotalHits);
        Assert.Equal(1, s1Snapshot.ToolAliasesResolved);
        Assert.Equal(1, s1Snapshot.ParameterAliasesResolved);
        Assert.Equal(1, s1Snapshot.EnumValuesSnapped);
        Assert.Equal(0, s1Snapshot.EnumValuesUnmatched);
    }

    [Fact]
    public void ToolCallHealing_RecordsTelemetry_WhenInterventionsOccur()
    {
        var telemetry = new ToolCallHealingTelemetry();
        var options = new ToolCallHealingOptions
        {
            Telemetry = telemetry,
            SessionId = "test-session"
        };

        var rawMessage = new ChatMessage(ChatRole.Assistant,
            "Here is the call: <tool_call>{\"name\": \"get_weather\", \"arguments\": \"```json {\\\"location\\\": \\\"Paris\\\",} ```\"}</tool_call>");

        var healed = ToolCallHealingTransform.HealChatMessage(rawMessage, null, options);

        var snapshot = telemetry.CreateSnapshot("test-session");
        Assert.True(snapshot.TotalInterventions >= 2);
        Assert.Equal(1, snapshot.PseudoTagsExtracted);
        Assert.True(snapshot.FencesStripped >= 1 || snapshot.JsonRepairs >= 1);
    }

    [Fact]
    public void ToolLoopCircuitBreaker_RecordsTelemetry_WhenPingPongTrips()
    {
        var telemetry = new CircuitBreakerTelemetry();
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            PingPongThreshold = 2,
            Telemetry = telemetry,
            SessionId = "loop-session"
        });

        var r1 = breaker.EvaluateToolInvocation("search", "{\"q\": \"test\"}");
        Assert.False(r1.IsLoopDetected);

        var r2 = breaker.EvaluateToolInvocation("search", "{\"q\": \"test\"}");
        Assert.True(r2.IsLoopDetected);

        var snapshot = telemetry.CreateSnapshot("loop-session");
        Assert.Equal(1, snapshot.TotalTrips);
        Assert.Equal(1, snapshot.ToolPingPongTrips);
    }

    [Fact]
    public void LenientToolDispatcher_RecordsTelemetry_WhenAliasResolved()
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            Telemetry = telemetry,
            SessionId = "disp-session"
        });

        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools = new Dictionary<string, ToolMapping>(StringComparer.OrdinalIgnoreCase)
            {
                ["get_weather"] = new(
                    aliases: ["get_current_weather"],
                    parameterAliases: new Dictionary<string, IEnumerable<string>>
                    {
                        ["location"] = ["city"]
                    })
            }
        });

        var dispatched = dispatcher.NormalizeCall("get_current_weather", new Dictionary<string, object?> { ["city"] = "Paris" });
        Assert.Equal("get_weather", dispatched.CanonicalToolName);
        Assert.True(dispatched.Arguments.ContainsKey("location"));

        var snapshot = telemetry.CreateSnapshot("disp-session");
        Assert.Equal(2, snapshot.TotalHits);
        Assert.Equal(1, snapshot.ToolAliasesResolved);
        Assert.Equal(1, snapshot.ParameterAliasesResolved);
    }

    [Fact]
    public void LenientToolDispatcher_NormalizesToolAndParameterCasing_AndRecordsTelemetry()
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            Telemetry = telemetry,
            SessionId = "casing-session"
        });

        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools = new Dictionary<string, ToolMapping>(StringComparer.OrdinalIgnoreCase)
            {
                ["get_weather"] = new(
                    parameterAliases: new Dictionary<string, IEnumerable<string>>
                    {
                        ["location"] = ["city"]
                    })
            }
        });

        var dispatched = dispatcher.NormalizeCall("GET_WEATHER", new Dictionary<string, object?> { ["LOCATION"] = "Tokyo" });
        Assert.Equal("get_weather", dispatched.CanonicalToolName);
        Assert.True(dispatched.Arguments.ContainsKey("location"));
        Assert.Equal("Tokyo", dispatched.Arguments["location"]);

        var snapshot = telemetry.CreateSnapshot("casing-session");
        Assert.Equal(2, snapshot.TotalHits);
        Assert.Equal(2, snapshot.CasingNormalized);
    }

    [Fact]
    public async Task Pipeline_PropagatesConversationId_ToTelemetrySnapshots()
    {
        var healingTelemetry = new ToolCallHealingTelemetry();
        var breakerTelemetry = new CircuitBreakerTelemetry();
        var dispatchTelemetry = new LenientDispatchTelemetry();

        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions { Telemetry = dispatchTelemetry });
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools = new Dictionary<string, ToolMapping>(StringComparer.OrdinalIgnoreCase)
            {
                ["get_weather"] = new(aliases: ["weather"])
            }
        });

        var builder = new ChatClientBuilder(new DummyMockClient());
        builder
            .UseLenientToolDispatch(dispatcher)
            .UseToolLoopCircuitBreaker(opts => opts.Telemetry = breakerTelemetry)
            .UseToolCallHealing(dispatcher, opts => opts.Telemetry = healingTelemetry);

        var client = builder.Build();

        // Turn for session-A: healing extracts pseudo-tag and dispatcher remaps "weather" -> "get_weather"
        var chatOptionsA = new ChatOptions { ConversationId = "session-A" };
        var responseA = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Check weather")], chatOptionsA);

        var snapA = healingTelemetry.CreateSnapshot("session-A");
        var snapB = healingTelemetry.CreateSnapshot("session-B");

        Assert.True(snapA.TotalInterventions >= 1);
        Assert.Equal(0, snapB.TotalInterventions); // Verifies strict session isolation without cross-contamination

        var dispatchSnapA = dispatchTelemetry.CreateSnapshot("session-A");
        var dispatchSnapB = dispatchTelemetry.CreateSnapshot("session-B");
        Assert.True(dispatchSnapA.TotalHits >= 1);
        Assert.Equal(0, dispatchSnapB.TotalHits);
    }

    private class DummyMockClient : IChatClient
    {
        public ChatClientMetadata Metadata => new("Dummy");
        public void Dispose() { }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var msg = new ChatMessage(ChatRole.Assistant, "<tool_call>{\"name\":\"weather\",\"arguments\":{\"city\":\"Paris\"}}</tool_call>");
            return Task.FromResult(new ChatResponse(msg));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }
}
