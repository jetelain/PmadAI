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
}
