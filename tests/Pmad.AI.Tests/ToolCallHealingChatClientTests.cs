using Microsoft.Extensions.AI;
using Moq;
using Pmad.AI.Healing;

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
        var msg = response.Messages.First();
        var fcc = msg.Contents.OfType<FunctionCallContent>().FirstOrDefault();
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
}
