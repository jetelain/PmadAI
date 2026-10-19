using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Pmad.AI.Guardrails;
using Pmad.AI.Tools;

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

    [Fact]
    public async Task GetResponseAsync_AdaptiveResetOnTurnHistory_AllowsExploration()
    {
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions { PingPongThreshold = 2 });
        var inner = new TestChatClient(getResponse: (_, _, _) => Task.FromResult(new ChatResponse([
            new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent("c1", "search", new Dictionary<string, object?> { ["q"] = "foo" })
            ])
        ])));
        var client = new ToolLoopCircuitBreakerChatClient(inner, breaker);

        // Turn 1: Initial call
        var r1 = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "find foo")]);
        Assert.Single(r1.Messages.First().Contents.OfType<FunctionCallContent>());

        // Turn 2: Tool returned distinct observation "found foo at line 10"
        var historyTurn2 = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "find foo"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "search", new Dictionary<string, object?> { ["q"] = "foo" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c1", "found foo at line 10")])
        };
        var r2 = await client.GetResponseAsync(historyTurn2);
        // Valid exploration: breaker did NOT trip because observation was distinct
        Assert.Single(r2.Messages.First().Contents.OfType<FunctionCallContent>());

        // Turn 3: Tool returned the SAME observation "found foo at line 10"
        var historyTurn3 = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "find foo"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "search", new Dictionary<string, object?> { ["q"] = "foo" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c1", "found foo at line 10")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c2", "search", new Dictionary<string, object?> { ["q"] = "foo" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c2", "found foo at line 10")])
        };
        var r3 = await client.GetResponseAsync(historyTurn3);
        // Unchanged observation: breaker TRIPPED!
        Assert.Empty(r3.Messages.First().Contents.OfType<FunctionCallContent>());
        Assert.Contains("[Circuit Breaker Tripped]", r3.Messages.First().Text);
    }

    [Fact]
    public async Task GetResponseAsync_TripsOnThirdInvocationByDefault_AllowsSecondInvocation()
    {
        var inner = new TestChatClient(getResponse: (_, _, _) => Task.FromResult(new ChatResponse([
            new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent("call", "read_file", new Dictionary<string, object?> { ["path"] = "main.cs" })
            ])
        ])));

        // Default breaker has PingPongThreshold = 3
        var client = new ToolLoopCircuitBreakerChatClient(inner);

        // Turn 1: 1st invocation before any observation
        var r1 = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "read main.cs")]);
        Assert.Single(r1.Messages.First().Contents.OfType<FunctionCallContent>());

        // Turn 2: observation recorded; 1st invocation on this observation
        var r2 = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "read main.cs"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call1", "read_file", new Dictionary<string, object?> { ["path"] = "main.cs" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call1", "file content")])
        ]);
        Assert.Single(r2.Messages.First().Contents.OfType<FunctionCallContent>());

        // Turn 3: 2nd invocation on same observation -> allowed under PingPongThreshold = 3
        var r3 = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "read main.cs"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call1", "read_file", new Dictionary<string, object?> { ["path"] = "main.cs" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call1", "file content")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call2", "read_file", new Dictionary<string, object?> { ["path"] = "main.cs" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call2", "file content")])
        ]);
        Assert.Single(r3.Messages.First().Contents.OfType<FunctionCallContent>());

        // Turn 4: 3rd invocation on unchanged observation -> trips circuit breaker!
        var r4 = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "read main.cs"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call1", "read_file", new Dictionary<string, object?> { ["path"] = "main.cs" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call1", "file content")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call2", "read_file", new Dictionary<string, object?> { ["path"] = "main.cs" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call2", "file content")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call3", "read_file", new Dictionary<string, object?> { ["path"] = "main.cs" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call3", "file content")])
        ]);
        Assert.Empty(r4.Messages.First().Contents.OfType<FunctionCallContent>());
        Assert.Contains("[Circuit Breaker Tripped]", r4.Messages.First().Text);
    }

    [Fact]
    public async Task GetResponseAsync_WithLenientToolDispatcher_NormalizesToolAliasesBeforeLoopEvaluation()
    {
        int callCount = 0;
        var inner = new TestChatClient(getResponse: (_, _, _) =>
        {
            callCount++;
            // Call 1, 3: "cat"; Call 2, 4: "read_file"
            string toolName = (callCount % 2 == 1) ? "cat" : "read_file";
            return Task.FromResult(new ChatResponse([
                new ChatMessage(ChatRole.Assistant, [
                    new FunctionCallContent($"c{callCount}", toolName, new Dictionary<string, object?> { ["path"] = "main.cs" })
                ])
            ]));
        });

        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools = new()
            {
                ["read_file"] = new ToolMapping(aliases: ["cat"])
            }
        });

        // Default breaker threshold = 3, injected dispatcher
        var client = new ToolLoopCircuitBreakerChatClient(inner, dispatcher: dispatcher);

        // Turn 1: model returns "cat" -> normalized to "read_file" -> allowed
        var r1 = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "show me main.cs")]);
        Assert.Single(r1.Messages.First().Contents.OfType<FunctionCallContent>());

        // Turn 2: observation recorded; model returns "read_file" -> normalized to "read_file" (count 1 on this observation) -> allowed
        var r2 = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "show me main.cs"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "cat", new Dictionary<string, object?> { ["path"] = "main.cs" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c1", "content")])
        ]);
        Assert.Single(r2.Messages.First().Contents.OfType<FunctionCallContent>());

        // Turn 3: model returns "cat" -> normalized to "read_file" (count 2 on this observation) -> allowed
        var r3 = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "show me main.cs"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "cat", new Dictionary<string, object?> { ["path"] = "main.cs" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c1", "content")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c2", "read_file", new Dictionary<string, object?> { ["path"] = "main.cs" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c2", "content")])
        ]);
        Assert.Single(r3.Messages.First().Contents.OfType<FunctionCallContent>());

        // Turn 4: model returns "read_file" -> normalized to "read_file" (count 3 on this observation) -> TRIPS!
        var r4 = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "show me main.cs"),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "cat", new Dictionary<string, object?> { ["path"] = "main.cs" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c1", "content")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c2", "read_file", new Dictionary<string, object?> { ["path"] = "main.cs" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c2", "content")]),
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c3", "cat", new Dictionary<string, object?> { ["path"] = "main.cs" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c3", "content")])
        ]);

        Assert.Empty(r4.Messages.First().Contents.OfType<FunctionCallContent>());
        Assert.Contains("[Circuit Breaker Tripped]", r4.Messages.First().Text);
        Assert.Contains("read_file", r4.Messages.First().Text);
    }

    [Fact]
    public async Task GetResponseAsync_ResetOnUserPrompt_ResetsLoopCounterWhenNewGoalIsIssued()
    {
        var inner = new TestChatClient(getResponse: (_, _, _) => Task.FromResult(new ChatResponse([
            new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent("c1", "search", new Dictionary<string, object?> { ["q"] = "test" })
            ])
        ])));

        // Breaker with PingPongThreshold = 2, ResetOnUserPrompt = true (default)
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            PingPongThreshold = 2,
            ResetOnUserPrompt = true
        });
        var client = new ToolLoopCircuitBreakerChatClient(inner, breaker);

        // Turn 1: user "find bug in parser"
        var r1 = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "find bug in parser")]);
        Assert.Single(r1.Messages.First().Contents.OfType<FunctionCallContent>());

        // Turn 2: user issues distinct new task "now clean up build artifacts"
        // Without ResetOnUserPrompt, this identical tool call would trip (count=2).
        // With ResetOnUserPrompt, consecutive counter reset on distinct user prompt!
        var r2 = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "now clean up build artifacts")
        ]);
        Assert.Single(r2.Messages.First().Contents.OfType<FunctionCallContent>());
        Assert.DoesNotContain("[Circuit Breaker Tripped]", r2.Messages.First().Text ?? string.Empty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetResponseAsync_WithEmptyOrWhitespaceToolName_SkipsSafelyWithoutException(string emptyToolName)
    {
        var inner = new TestChatClient(getResponse: (_, _, _) => Task.FromResult(new ChatResponse([
            new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent("c1", emptyToolName, new Dictionary<string, object?>())
            ])
        ])));

        var dispatcher = new LenientToolDispatcher();
        var client = new ToolLoopCircuitBreakerChatClient(inner, dispatcher: dispatcher);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);

        Assert.NotNull(response);
        var call = Assert.Single(response.Messages.First().Contents.OfType<FunctionCallContent>());
        Assert.Equal(emptyToolName, call.Name);
    }

    [Fact]
    public async Task GetResponseAsync_WithFailingTranslation_FallsBackToRawInvocationWithoutThrowing()
    {
        var inner = new TestChatClient(getResponse: (_, _, _) => Task.FromResult(new ChatResponse([
            new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent("c1", "cat", new Dictionary<string, object?>()) // missing required 'path' parameter
            ])
        ])));

        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Translations =
            [
                new ToolTranslation
                {
                    SourceTool = "cat",
                    TargetTool = "read_file",
                    CommandTemplate = "{path}"
                }
            ]
        });

        var client = new ToolLoopCircuitBreakerChatClient(inner, dispatcher: dispatcher);

        // Should not throw KeyNotFoundException when template parameter is missing
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);

        Assert.NotNull(response);
        var call = Assert.Single(response.Messages.First().Contents.OfType<FunctionCallContent>());
        Assert.Equal("cat", call.Name);
    }
}

