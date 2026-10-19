using Microsoft.Extensions.AI;
using Moq;
using Pmad.AI.Healing;
using Pmad.AI.Tools;

namespace Pmad.AI.Tests;

public class ToolCallHealingChatClientTests
{
    private class FakeChatClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> _getResponse;
        private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> _getStreamingResponse;

        public FakeChatClient(
            Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>>? getResponse = null,
            Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>? getStreamingResponse = null)
        {
            _getResponse = getResponse ?? ((_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Default"))));
            _getStreamingResponse = getStreamingResponse ?? ((_, _, _) => EmptyStream());
        }

        public ChatClientMetadata Metadata => new("FakeClient");

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
    public async Task GetResponseAsync_HealsPseudoToolCallInMessage()
    {
        var innerClient = new FakeChatClient(getResponse: (_, _, _) =>
        {
            var msg = new ChatMessage(ChatRole.Assistant, "Looking up file...\n<tool_call>{\"name\": \"read\", \"arguments\": {\"path\": \"a.cs\"}}</tool_call>");
            return Task.FromResult(new ChatResponse(msg));
        });

        var client = new ToolCallHealingChatClient(innerClient);
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Read a.cs")]);

        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
        Assert.Equal(2, response.Messages.Count);
        Assert.Equal("Looking up file...", response.Messages[0].Text);

        var toolMsg = response.Messages[1];
        var fcc = toolMsg.Contents.OfType<FunctionCallContent>().FirstOrDefault();
        Assert.NotNull(fcc);
        Assert.Equal("read", fcc.Name);
        Assert.Equal("a.cs", fcc.Arguments?["path"]?.ToString());
    }


    [Fact]
    public async Task GetResponseAsync_HealsFunctionCallTag()
    {
        var innerClient = new FakeChatClient(getResponse: (_, _, _) =>
        {
            var msg = new ChatMessage(ChatRole.Assistant, "<function_call>{\"name\": \"search\", \"arguments\": {\"query\": \"test\"}}</function_call>");
            return Task.FromResult(new ChatResponse(msg));
        });

        var client = new ToolCallHealingChatClient(innerClient);
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "search")]);

        var fcc = response.Messages.First().Contents.OfType<FunctionCallContent>().FirstOrDefault();
        Assert.NotNull(fcc);
        Assert.Equal("search", fcc.Name);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_PrefixTriggered_PreservesProseWithoutDelay()
    {
        static async IAsyncEnumerable<ChatResponseUpdate> GenerateUpdates()
        {
            yield return new ChatResponseUpdate { Contents = { new TextContent("Hello ") } };
            yield return new ChatResponseUpdate { Contents = { new TextContent("world, ") } };
            yield return new ChatResponseUpdate { Contents = { new TextContent("how are you?") } };
        }

        var innerClient = new FakeChatClient(getStreamingResponse: (_, _, _) => GenerateUpdates());
        var client = new ToolCallHealingChatClient(innerClient, new ToolCallHealingOptions
        {
            StreamingMode = StreamingHealingMode.PrefixTriggered
        });

        var received = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                received.Add(update.Text);
            }
        }

        Assert.Equal(["Hello ", "world, ", "how are you?"], received);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_PrefixTriggered_HandlesTagAcrossChunkBoundary()
    {
        static async IAsyncEnumerable<ChatResponseUpdate> GenerateUpdates()
        {
            yield return new ChatResponseUpdate { Contents = { new TextContent("Sure! Let me run that: <tool") } };
            yield return new ChatResponseUpdate { Contents = { new TextContent("_call>{\"name\": \"run\", \"arguments\": {\"cmd\": \"ls\"}}</tool_call> and that's it.") } };
        }

        var innerClient = new FakeChatClient(getStreamingResponse: (_, _, _) => GenerateUpdates());
        var client = new ToolCallHealingChatClient(innerClient, new ToolCallHealingOptions
        {
            StreamingMode = StreamingHealingMode.PrefixTriggered
        });

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "run")]))
        {
            updates.Add(update);
        }

        var functionCall = updates.SelectMany(u => u.Contents).OfType<FunctionCallContent>().FirstOrDefault();
        Assert.NotNull(functionCall);
        Assert.Equal("run", functionCall.Name);

        var combinedText = string.Concat(updates.Select(u => u.Text ?? string.Empty));
        Assert.Contains("Sure! Let me run that: ", combinedText);
        Assert.Contains(" and that's it.", combinedText);
        Assert.DoesNotContain("<tool_call>", combinedText);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_PrefixTriggered_FlushesFalseAlarmPrefix()
    {
        static async IAsyncEnumerable<ChatResponseUpdate> GenerateUpdates()
        {
            yield return new ChatResponseUpdate { Contents = { new TextContent("Look at <tool") } };
            yield return new ChatResponseUpdate { Contents = { new TextContent("box inside the room.") } };
        }

        var innerClient = new FakeChatClient(getStreamingResponse: (_, _, _) => GenerateUpdates());
        var client = new ToolCallHealingChatClient(innerClient, new ToolCallHealingOptions
        {
            StreamingMode = StreamingHealingMode.PrefixTriggered
        });

        var textChunks = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "look")]))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                textChunks.Add(update.Text);
            }
        }

        var fullText = string.Concat(textChunks);
        Assert.Equal("Look at <toolbox inside the room.", fullText);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_FullBuffer_BuffersAndExtracts()
    {
        static async IAsyncEnumerable<ChatResponseUpdate> GenerateUpdates()
        {
            yield return new ChatResponseUpdate { Contents = { new TextContent("<tool_call>") } };
            yield return new ChatResponseUpdate { Contents = { new TextContent("{\"name\": \"exec\", \"arguments\": {\"id\": 1}}") } };
            yield return new ChatResponseUpdate { Contents = { new TextContent("</tool_call>") } };
        }

        var innerClient = new FakeChatClient(getStreamingResponse: (_, _, _) => GenerateUpdates());
        var client = new ToolCallHealingChatClient(innerClient, new ToolCallHealingOptions
        {
            StreamingMode = StreamingHealingMode.FullBuffer
        });

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "exec")]))
        {
            updates.Add(update);
        }

        Assert.Single(updates);
        var fcc = updates[0].Contents.OfType<FunctionCallContent>().FirstOrDefault();
        Assert.NotNull(fcc);
        Assert.Equal("exec", fcc.Name);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_Disabled_PassesUntouched()
    {
        static async IAsyncEnumerable<ChatResponseUpdate> GenerateUpdates()
        {
            yield return new ChatResponseUpdate { Contents = { new TextContent("<tool_call>{\"name\":\"test\"}</tool_call>") } };
        }

        var innerClient = new FakeChatClient(getStreamingResponse: (_, _, _) => GenerateUpdates());
        var client = new ToolCallHealingChatClient(innerClient, new ToolCallHealingOptions
        {
            StreamingMode = StreamingHealingMode.Disabled
        });

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "test")]))
        {
            updates.Add(update);
        }

        Assert.Single(updates);
        Assert.Contains("<tool_call>", updates[0].Text);
        Assert.Empty(updates[0].Contents.OfType<FunctionCallContent>());
    }

    [Fact]
    public void BuilderExtension_RegistersInPipeline()
    {
        var innerClient = new FakeChatClient();
        var pipeline = new ChatClientBuilder(innerClient)
            .UseToolCallHealing(opts => opts.StreamingMode = StreamingHealingMode.PrefixTriggered)
            .Build();

        Assert.IsType<ToolCallHealingChatClient>(pipeline);
    }

    [Fact]
    public async Task GetResponseAsync_ConversationalResponse_NotCoercedIntoToolCall()
    {
        // Conversational response with an example mentioning an undeclared tool/action
        var conversationalText = "Here is a complete guide on how to query domain DNS records:\n" +
            "First, make an HTTP request to the VirusTotal API endpoint using your API key.\n" +
            "For example, in ReAct format one might write:\n" +
            "Action: query_dns\nAction Input: {\"domain\": \"Twitter.com\"}\n" +
            "However, you should use curl or the official Python SDK instead of any local tools.";

        var innerClient = new FakeChatClient(getResponse: (_, _, _) =>
        {
            var msg = new ChatMessage(ChatRole.Assistant, conversationalText);
            return Task.FromResult(new ChatResponse(msg));
        });

        var client = new ToolCallHealingChatClient(innerClient);
        var chatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(([System.ComponentModel.Description("City")] string city) => $"weather for {city}", "get_weather")]
        };

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "How do I query DNS?")], chatOptions);

        // Conversational text should NOT be coerced into a tool call because query_dns is not a declared tool
        Assert.NotEqual(ChatFinishReason.ToolCalls, response.FinishReason);
        var msg = response.Messages.First();
        Assert.Empty(msg.Contents.OfType<FunctionCallContent>());
        var textContent = msg.Contents.OfType<TextContent>().FirstOrDefault();
        Assert.NotNull(textContent);
        Assert.Contains("Here is a complete guide", textContent.Text);
    }

    [Fact]
    public async Task GetResponseAsync_UndeclaredTool_NotCoercedWhenToolsProvided()
    {
        var innerClient = new FakeChatClient(getResponse: (_, _, _) =>
        {
            var msg = new ChatMessage(ChatRole.Assistant, "<tool_call>{\"name\": \"format_hard_drive\", \"arguments\": {}}</tool_call>");
            return Task.FromResult(new ChatResponse(msg));
        });

        var client = new ToolCallHealingChatClient(innerClient);
        var chatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "result", "get_weather")]
        };

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], chatOptions);

        // Undeclared tool format_hard_drive must NOT be promoted when tools are declared
        Assert.NotEqual(ChatFinishReason.ToolCalls, response.FinishReason);
        var msg = response.Messages.First();
        Assert.Empty(msg.Contents.OfType<FunctionCallContent>());
    }

    [Fact]
    public async Task GetResponseAsync_ValidToolCallWithBriefPreamble_PromotedSuccessfully()
    {
        var innerClient = new FakeChatClient(getResponse: (_, _, _) =>
        {
            var msg = new ChatMessage(ChatRole.Assistant, "Looking up weather...\n<tool_call>{\"name\": \"get_weather\", \"arguments\": {\"city\": \"Paris\"}}</tool_call>");
            return Task.FromResult(new ChatResponse(msg));
        });

        var client = new ToolCallHealingChatClient(innerClient);
        var chatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(([System.ComponentModel.Description("City")] string city) => $"weather for {city}", "get_weather")]
        };

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "weather in Paris")], chatOptions);

        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
        Assert.Equal(2, response.Messages.Count);
        Assert.Equal("Looking up weather...", response.Messages[0].Text);

        var call = response.Messages[1].Contents.OfType<FunctionCallContent>().FirstOrDefault();
        Assert.NotNull(call);
        Assert.Equal("get_weather", call.Name);
        Assert.Equal("Paris", call.Arguments?["city"]?.ToString());
    }

    [Fact]
    public async Task GetResponseAsync_WithProseBeforeAndAfter_SplitsIntoThreeSequentialMessages()
    {
        var innerClient = new FakeChatClient(getResponse: (_, _, _) =>
        {
            var msg = new ChatMessage(ChatRole.Assistant, "I will check the forecast now.\n<tool_call>{\"name\": \"get_weather\", \"arguments\": {\"city\": \"Tokyo\"}}</tool_call>\nPlease wait while I retrieve it.");
            return Task.FromResult(new ChatResponse(msg));
        });

        var client = new ToolCallHealingChatClient(innerClient);
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Tokyo weather")]);

        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
        Assert.Equal(3, response.Messages.Count);

        // Message 1: Preceding prose
        Assert.Equal("I will check the forecast now.", response.Messages[0].Text);
        Assert.Empty(response.Messages[0].Contents.OfType<FunctionCallContent>());

        // Message 2: Tool call
        var call = response.Messages[1].Contents.OfType<FunctionCallContent>().Single();
        Assert.Equal("get_weather", call.Name);
        Assert.Equal("Tokyo", call.Arguments?["city"]?.ToString());

        // Message 3: Succeeding prose
        Assert.Equal("Please wait while I retrieve it.", response.Messages[2].Text);
        Assert.Empty(response.Messages[2].Contents.OfType<FunctionCallContent>());
    }

    [Fact]
    public async Task GetResponseAsync_WithLenientToolDispatcher_AliasIsPromotedAndDispatched()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["run_command"] = new ToolMapping(aliases: ["execute_command", "bash"])
            }
        });

        var innerClient = new FakeChatClient(getResponse: (_, _, _) =>
        {
            var msg = new ChatMessage(ChatRole.Assistant, "<tool_call>{\"name\": \"execute_command\", \"arguments\": {\"command\": \"ls\"}}</tool_call>");
            return Task.FromResult(new ChatResponse(msg));
        });

        var client = new ToolCallHealingChatClient(innerClient, new ToolCallHealingOptions
        {
            Dispatcher = dispatcher
        });

        var chatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "result", "run_command")]
        };

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "run ls")], chatOptions);

        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
        var fcc = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Single();
        Assert.Equal("execute_command", fcc.Name);
    }

    [Fact]
    public async Task GetResponseAsync_WithLenientToolDispatcher_VirtualTranslationIsPromoted()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Translations =
            [
                new ToolTranslation
                {
                    SourceTool = "grep_search",
                    TargetTool = "bash",
                    CommandTemplate = "grep -rn '{query}' ."
                }
            ]
        });

        var innerClient = new FakeChatClient(getResponse: (_, _, _) =>
        {
            var msg = new ChatMessage(ChatRole.Assistant, "<tool_call>{\"name\": \"grep_search\", \"arguments\": {\"query\": \"Hello\"}}</tool_call>");
            return Task.FromResult(new ChatResponse(msg));
        });

        var client = new ToolCallHealingChatClient(innerClient, new ToolCallHealingOptions
        {
            Dispatcher = dispatcher
        });

        var chatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "result", "bash")]
        };

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "grep Hello")], chatOptions);

        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
        var fcc = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Single();
        Assert.Equal("grep_search", fcc.Name);
    }

    [Fact]
    public async Task GetResponseAsync_WithNamespacePrefix_IsPromotedWhenCanonicalMatchesDeclared()
    {
        var innerClient = new FakeChatClient(getResponse: (_, _, _) =>
        {
            var msg = new ChatMessage(ChatRole.Assistant, "<tool_call>{\"name\": \"workspace__read_file\", \"arguments\": {\"path\": \"a.cs\"}}</tool_call>");
            return Task.FromResult(new ChatResponse(msg));
        });

        var client = new ToolCallHealingChatClient(innerClient);

        var chatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "content", "read_file")]
        };

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "read a.cs")], chatOptions);

        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
        var fcc = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Single();
        Assert.Equal("workspace__read_file", fcc.Name);
    }

    [Fact]
    public async Task GetResponseAsync_WithPipeline_DispatcherPassedViaLenientToolDispatchChatClient()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["run_command"] = new ToolMapping(aliases: ["execute_command"])
            }
        });

        var innerClient = new FakeChatClient(getResponse: (_, _, _) =>
        {
            var msg = new ChatMessage(ChatRole.Assistant, "Running command...\n<tool_call>{\"name\": \"execute_command\", \"arguments\": {\"command\": \"dir\"}}</tool_call>");
            return Task.FromResult(new ChatResponse(msg));
        });

        // Pipeline: LenientToolDispatch wraps ToolCallHealing wraps innerClient
        var healingClient = new ToolCallHealingChatClient(innerClient);
        var pipeline = new LenientToolDispatchChatClient(healingClient, dispatcher);

        var chatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "ok", "run_command")]
        };

        var response = await pipeline.GetResponseAsync([new ChatMessage(ChatRole.User, "run dir")], chatOptions);

        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
        // ToolCallHealing promoted the call, and LenientToolDispatch normalized it to canonical run_command
        var fcc = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Single();
        Assert.Equal("run_command", fcc.Name);
        Assert.Equal("dir", fcc.Arguments?["command"]?.ToString());
    }

    [Fact]
    public async Task GetResponseAsync_Payload1_TruncatedUnclosedPseudoTag_PromotesToFunctionCall()
    {
        // Payload 1: Truncated / Unclosed Pseudo-Tag (BFCL Simple FC Turn 15)
        var payload = "<tool_call>\n{\"name\": \"find_real_estate\", \"arguments\": {\"location\": \"San Diego, CA\", \"property_type\": \"villa\", \"bedrooms\": 3, \"budget\": {\"min\": 300000, \"max\": 400000}}}";

        var innerClient = new FakeChatClient(getResponse: (_, _, _) =>
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, payload)));
        });

        var client = new ToolCallHealingChatClient(innerClient);
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Find real estate in San Diego")]);

        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
        var call = Assert.Single(response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>());
        Assert.Equal("find_real_estate", call.Name);
        Assert.Equal("San Diego, CA", call.Arguments?["location"]?.ToString());
        Assert.Equal("villa", call.Arguments?["property_type"]?.ToString());
    }

    [Fact]
    public async Task GetResponseAsync_Payload2_ReActActionWithFencedCodeBlock_PromotesAndPreservesProse()
    {
        // Payload 2: ReAct Action with Fenced Code Block (Mock SWE-bench Turn 3)
        var payload = "I will search for the relevant class in the repository.\n\nAction: search_code\nAction Input: ```json\n{\n  \"query\": \"class ConnectionPoolManager\",\n  \"path\": \"src/Pmad.Security\"\n}\n```\nLet me know if you need more details.";

        var innerClient = new FakeChatClient(getResponse: (_, _, _) =>
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, payload)));
        });

        var client = new ToolCallHealingChatClient(innerClient);
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Search code")]);

        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
        var call = Assert.Single(response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>());
        Assert.Equal("search_code", call.Name);
        Assert.Equal("class ConnectionPoolManager", call.Arguments?["query"]?.ToString());
        Assert.Equal("src/Pmad.Security", call.Arguments?["path"]?.ToString());

        var texts = response.Messages.SelectMany(m => m.Contents).OfType<TextContent>().Select(t => t.Text).ToList();
        Assert.Contains(texts, t => t.Contains("I will search for the relevant class in the repository."));
        Assert.Contains(texts, t => t.Contains("Let me know if you need more details."));
    }

    [Fact]
    public async Task GetResponseAsync_Payload3_ThinkingTraceRunawayWithTruncation_PreservesTextSafely()
    {
        // Payload 3: Thinking Trace Runaway with Truncation (BFCL Irrelevance Turn 2)
        var payload = "<think>\nThinking Process:\n1. Analyze request: explain synchronous vs asynchronous in two sentences.\n2. Draft sentence 1: Synchronous programming executes tasks sequentially...\n3. Draft sentence 2: Asynchronous programming allows tasks to run concurrently...\n4. Refining comparison: blocking vs non-blocking...";

        var innerClient = new FakeChatClient(getResponse: (_, _, _) =>
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, payload)));
        });

        var client = new ToolCallHealingChatClient(innerClient);
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Explain sync vs async")]);

        // Must not produce an empty message or violate protocol
        Assert.NotEmpty(response.Messages);
        var lastMsg = response.Messages.Last();
        Assert.False(string.IsNullOrWhiteSpace(lastMsg.Text), "Assistant must not return empty output on unclosed thinking trace runaway.");
        Assert.Contains("Synchronous programming executes tasks sequentially", lastMsg.Text);
    }

    [Fact]
    public async Task GetResponseAsync_Payload4_TrailingCommasAndMultilineStringsInArguments_ParsesCleanly()
    {
        // Payload 4: Trailing Commas & Unescaped Quotes / Newlines in Arguments
        var payload = "<tool_call>\n{\n  \"name\": \"apply_patch\",\n  \"arguments\": {\n    \"file_path\": \"sympy/matrices/dense.py\",\n    \"patch\": \"--- a/dense.py\\n+++ b/dense.py\\n@@ -10,3 +10,3 @@\\n- old_code()\\n+ new_code()\\n\",\n  }\n}\n</tool_call>";

        var innerClient = new FakeChatClient(getResponse: (_, _, _) =>
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, payload)));
        });

        var client = new ToolCallHealingChatClient(innerClient);
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Apply patch")]);

        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
        var call = Assert.Single(response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>());
        Assert.Equal("apply_patch", call.Name);
        Assert.Equal("sympy/matrices/dense.py", call.Arguments?["file_path"]?.ToString());
        Assert.Contains("+ new_code()", call.Arguments?["patch"]?.ToString());
    }

    [Fact]
    public async Task GetResponseAsync_UnclosedToolCallWithTrailingBackslash_ParsesCleanly()
    {
        // Truncated tool call where string cuts off right on an escape character '\'
        var payload = "<tool_call>\n{\n  \"name\": \"search_code\",\n  \"arguments\": {\n    \"pattern\": \"hello\\";

        var innerClient = new FakeChatClient(getResponse: (_, _, _) =>
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, payload)));
        });

        var client = new ToolCallHealingChatClient(innerClient);
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "search")]);

        Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);
        var call = Assert.Single(response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>());
        Assert.Equal("search_code", call.Name);
        Assert.NotNull(call.Arguments);
        Assert.True(call.Arguments.ContainsKey("pattern"));
    }
}
