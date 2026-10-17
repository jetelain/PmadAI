using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Pmad.AI.Guardrails;

namespace Pmad.AI.Tests;

public class ToolLoopCircuitBreakerChatClientTests
{
    private class TestChatClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> _getResponse;
        private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> _getStreamingResponse;

        public TestChatClient(
            Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>>? getResponse = null,
            Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>? getStreamingResponse = null)
        {
            _getResponse = getResponse ?? ((_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Default"))));
            _getStreamingResponse = getStreamingResponse ?? ((_, _, _) => EmptyStream());
        }

        public ChatClientMetadata Metadata => new("TestChatClient");
        public void Dispose() { }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => _getResponse(chatMessages, options, cancellationToken);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => _getStreamingResponse(chatMessages, options, cancellationToken);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        private static async IAsyncEnumerable<ChatResponseUpdate> EmptyStream()
        {
            yield break;
        }
    }

    [Fact]
    public void Constructor_WithDefaultBreaker_InitializesDefaultBreaker()
    {
        var inner = new TestChatClient();
        var client = new ToolLoopCircuitBreakerChatClient(inner);

        Assert.NotNull(client.Breaker);
    }

    [Fact]
    public void Constructor_WithCustomBreaker_UsesProvidedBreaker()
    {
        var inner = new TestChatClient();
        var customBreaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions { PingPongThreshold = 5 });
        var client = new ToolLoopCircuitBreakerChatClient(inner, customBreaker);

        Assert.Same(customBreaker, client.Breaker);
    }

    [Fact]
    public async Task GetResponseAsync_NormalResponseWithoutToolCalls_PassesThroughUntouched()
    {
        var inner = new TestChatClient(getResponse: (_, _, _) =>
        {
            var msg = new ChatMessage(ChatRole.Assistant, "Hello, world!");
            return Task.FromResult(new ChatResponse([msg])
            {
                FinishReason = ChatFinishReason.Stop,
                Usage = new UsageDetails { TotalTokenCount = 15 }
            });
        });

        var client = new ToolLoopCircuitBreakerChatClient(inner);
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hi")]);

        Assert.NotNull(response);
        var msg = Assert.Single(response.Messages);
        Assert.Equal("Hello, world!", msg.Text);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
        Assert.Equal(15, response.Usage?.TotalTokenCount);
    }

    [Fact]
    public async Task GetResponseAsync_NonLoopingToolCall_PassesThroughUntouched()
    {
        var inner = new TestChatClient(getResponse: (_, _, _) =>
        {
            var msg = new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent("call_1", "read_file", new Dictionary<string, object?> { ["path"] = "a.cs" })
            ]);
            return Task.FromResult(new ChatResponse([msg]) { FinishReason = ChatFinishReason.ToolCalls });
        });

        var client = new ToolLoopCircuitBreakerChatClient(inner);
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Read a.cs")]);

        Assert.NotNull(response);
        var msg = Assert.Single(response.Messages);
        var call = Assert.IsType<FunctionCallContent>(Assert.Single(msg.Contents));
        Assert.Equal("read_file", call.Name);
        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
    }

    [Fact]
    public async Task GetResponseAsync_DuplicateToolCall_TripsAndReplacesWithDiagnostic()
    {
        var inner = new TestChatClient(getResponse: (_, _, _) =>
        {
            var msg = new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent("call_1", "read_file", new Dictionary<string, object?> { ["path"] = "same.cs" })
            ]);
            return Task.FromResult(new ChatResponse([msg])
            {
                FinishReason = ChatFinishReason.ToolCalls,
                Usage = new UsageDetails { TotalTokenCount = 42 }
            });
        });

        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions { PingPongThreshold = 2 });
        var client = new ToolLoopCircuitBreakerChatClient(inner, breaker);

        // Turn 1: Initial call - not tripped
        var r1 = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Turn 1")]);
        Assert.Equal(ChatFinishReason.ToolCalls, r1.FinishReason);
        Assert.IsType<FunctionCallContent>(r1.Messages[0].Contents[0]);

        // Turn 2: Duplicate call on unchanged state - trips breaker
        var r2 = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Turn 2")]);
        Assert.Equal(ChatFinishReason.Stop, r2.FinishReason);
        Assert.Equal(42, r2.Usage?.TotalTokenCount);

        var msg = Assert.Single(r2.Messages);
        var textContent = Assert.IsType<TextContent>(Assert.Single(msg.Contents));
        Assert.Contains("[Circuit Breaker Tripped]", textContent.Text);
        Assert.Contains("Duplicate tool invocation detected: 'read_file'", textContent.Text);
    }

    [Fact]
    public async Task GetResponseAsync_AlternatingPingPongCycle_TripsAndReplacesWithDiagnostic()
    {
        int turn = 0;
        var inner = new TestChatClient(getResponse: (_, _, _) =>
        {
            turn++;
            string toolName = (turn % 2 == 1) ? "tool_a" : "tool_b";
            var msg = new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent($"call_{turn}", toolName, new Dictionary<string, object?> { ["arg"] = 1 })
            ]);
            return Task.FromResult(new ChatResponse([msg]) { FinishReason = ChatFinishReason.ToolCalls });
        });

        var breaker = new ToolLoopCircuitBreaker();
        var client = new ToolLoopCircuitBreakerChatClient(inner, breaker);

        // Turn 1: tool_a
        var r1 = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "1")]);
        Assert.Equal(ChatFinishReason.ToolCalls, r1.FinishReason);

        // Turn 2: tool_b
        var r2 = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "2")]);
        Assert.Equal(ChatFinishReason.ToolCalls, r2.FinishReason);

        // Turn 3: tool_a
        var r3 = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "3")]);
        Assert.Equal(ChatFinishReason.ToolCalls, r3.FinishReason);

        // Turn 4: tool_b -> completes cycle of length 2 repeated twice on unchanged state -> tripped
        var r4 = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "4")]);
        Assert.Equal(ChatFinishReason.Stop, r4.FinishReason);
        var msg = Assert.Single(r4.Messages);
        var text = Assert.IsType<TextContent>(Assert.Single(msg.Contents)).Text;
        Assert.Contains("[Circuit Breaker Tripped]", text);
        Assert.Contains("Tool ping-pong loop detected: cycle of 2", text);
    }

    [Fact]
    public async Task GetResponseAsync_MultipleToolCallsInTurn_EvaluatesEach()
    {
        int turn = 0;
        var inner = new TestChatClient(getResponse: (_, _, _) =>
        {
            turn++;
            var msg = new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent($"call_{turn}_1", "tool_x", new Dictionary<string, object?> { ["id"] = 1 }),
                new FunctionCallContent($"call_{turn}_2", "tool_x", new Dictionary<string, object?> { ["id"] = 1 })
            ]);
            return Task.FromResult(new ChatResponse([msg]) { FinishReason = ChatFinishReason.ToolCalls });
        });

        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions { PingPongThreshold = 2 });
        var client = new ToolLoopCircuitBreakerChatClient(inner, breaker);

        // In turn 1: first call evaluates cleanly, second call is immediate consecutive duplicate!
        var r1 = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Go")]);
        Assert.Equal(ChatFinishReason.Stop, r1.FinishReason);
        var text = Assert.IsType<TextContent>(Assert.Single(r1.Messages[0].Contents)).Text;
        Assert.Contains("[Circuit Breaker Tripped]", text);
    }

    [Fact]
    public async Task GetResponseAsync_EmptyMessages_ReturnsResponseSafely()
    {
        var inner = new TestChatClient(getResponse: (_, _, _) =>
        {
            return Task.FromResult(new ChatResponse([]));
        });

        var client = new ToolLoopCircuitBreakerChatClient(inner);
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Test")]);

        Assert.NotNull(response);
        Assert.Empty(response.Messages);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_NormalProse_StreamsCompletely()
    {
        async IAsyncEnumerable<ChatResponseUpdate> GetStream(
            IEnumerable<ChatMessage> _,
            ChatOptions? __,
            [EnumeratorCancellation] CancellationToken ___ = default)
        {
            yield return new ChatResponseUpdate { Contents = [new TextContent("Hello ")] };
            yield return new ChatResponseUpdate { Contents = [new TextContent("from ")] };
            yield return new ChatResponseUpdate { Contents = [new TextContent("stream!")] };
        }

        var inner = new TestChatClient(getStreamingResponse: GetStream);
        var client = new ToolLoopCircuitBreakerChatClient(inner);

        var received = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Hi")]))
        {
            foreach (var content in update.Contents.OfType<TextContent>())
            {
                received.Add(content.Text ?? string.Empty);
            }
        }

        Assert.Equal(["Hello ", "from ", "stream!"], received);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_StreamingRepetition_TripsAndTerminatesEarly()
    {
        var repeatedPattern = "repetitive-token-sequence-123456789";
        async IAsyncEnumerable<ChatResponseUpdate> GetStream(
            IEnumerable<ChatMessage> _,
            ChatOptions? __,
            [EnumeratorCancellation] CancellationToken ___ = default)
        {
            yield return new ChatResponseUpdate { Contents = [new TextContent(repeatedPattern)] };
            yield return new ChatResponseUpdate { Contents = [new TextContent(repeatedPattern)] };
            yield return new ChatResponseUpdate { Contents = [new TextContent(repeatedPattern)] };
            yield return new ChatResponseUpdate { Contents = [new TextContent("MUST_NOT_BE_EMITTED")] };
        }

        var inner = new TestChatClient(getStreamingResponse: GetStream);
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            NgramMinLength = 16,
            NgramMinOccurrences = 3
        });
        var client = new ToolLoopCircuitBreakerChatClient(inner, breaker);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Stream")]))
        {
            updates.Add(update);
        }

        var last = updates.Last();
        Assert.Equal(ChatFinishReason.Stop, last.FinishReason);
        Assert.Equal(ChatRole.Assistant, last.Role);
        var tripText = Assert.IsType<TextContent>(Assert.Single(last.Contents)).Text;
        Assert.Contains("[Circuit Breaker Tripped]", tripText);
        Assert.Contains("Streaming repetition loop detected", tripText);

        // Ensure subsequent chunks after tripping were not yielded
        Assert.DoesNotContain(updates, u => u.Contents.OfType<TextContent>().Any(t => t.Text == "MUST_NOT_BE_EMITTED"));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_NonTextContents_YieldedWithoutCrashing()
    {
        async IAsyncEnumerable<ChatResponseUpdate> GetStream(
            IEnumerable<ChatMessage> _,
            ChatOptions? __,
            [EnumeratorCancellation] CancellationToken ___ = default)
        {
            yield return new ChatResponseUpdate { Contents = [new TextContent("Prose ")] };
            yield return new ChatResponseUpdate { Contents = [] }; // empty contents
            yield return new ChatResponseUpdate { Contents = [new TextContent("")] }; // empty text
            yield return new ChatResponseUpdate { Contents = [new TextContent("more prose")] };
        }

        var inner = new TestChatClient(getStreamingResponse: GetStream);
        var client = new ToolLoopCircuitBreakerChatClient(inner);

        var count = 0;
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Hi")]))
        {
            count++;
        }

        Assert.Equal(4, count);
    }

    [Fact]
    public void UseToolLoopCircuitBreaker_DefaultBreaker_RegistersInBuilderPipeline()
    {
        var inner = new TestChatClient();
        var client = new ChatClientBuilder(inner)
            .UseToolLoopCircuitBreaker()
            .Build();

        Assert.NotNull(client);
    }

    [Fact]
    public void UseToolLoopCircuitBreaker_CustomBreaker_RegistersInBuilderPipeline()
    {
        var inner = new TestChatClient();
        var customBreaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions { PingPongThreshold = 4 });
        var client = new ChatClientBuilder(inner)
            .UseToolLoopCircuitBreaker(customBreaker)
            .Build();

        Assert.NotNull(client);
    }

    [Fact]
    public void UseToolLoopCircuitBreaker_NullBuilder_ThrowsArgumentNullException()
    {
        ChatClientBuilder builder = null!;
        Assert.Throws<ArgumentNullException>(() => builder.UseToolLoopCircuitBreaker());
    }
}
