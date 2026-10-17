using Microsoft.Extensions.AI;
using Pmad.AI.Transcripts;

namespace Pmad.AI.Tests;

public class VerbatimTranscriptChatClientTests
{
    private class SimpleFakeClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> _getResponse;
        private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> _getStreamingResponse;

        public SimpleFakeClient(
            Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>>? getResponse = null,
            Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>? getStreamingResponse = null)
        {
            _getResponse = getResponse ?? ((_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Default response"))));
            _getStreamingResponse = getStreamingResponse ?? ((_, _, _) => EmptyStream());
        }

        public ChatClientMetadata Metadata => new("SimpleFakeClient");

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
    public async Task ToMarkdown_IncludesYamlFrontmatterAndTurns()
    {
        var fake = new SimpleFakeClient(getResponse: (_, _, _) =>
        {
            var msg = new ChatMessage(ChatRole.Assistant, "Here is your answer.");
            msg.Contents.Add(new TextReasoningContent("I am thinking about the query."));
            msg.Contents.Add(new FunctionCallContent("call_1", "calc", new Dictionary<string, object?> { ["x"] = 42 }));
            var response = new ChatResponse(msg)
            {
                Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 20, TotalTokenCount = 30 }
            };
            return Task.FromResult(response);
        });

        var client = new VerbatimTranscriptChatClient(fake, new VerbatimTranscriptOptions
        {
            SessionTitle = "Math Test",
            Label = "audit",
            IncludeSystemPrompt = true,
            IncludeToolSchemas = true
        });

        var options = new ChatOptions
        {
            Instructions = "You are a helpful calculator.",
            Tools = [AIFunctionFactory.Create(([System.ComponentModel.Description("A number")] int x) => x * 2, "calc")]
        };

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "What is 42 * 2?")], options);

        var markdown = client.ToMarkdown();

        Assert.Contains("---", markdown);
        Assert.Contains("session_title: \"Math Test\"", markdown);
        Assert.Contains("label: \"audit\"", markdown);
        Assert.Contains("input_tokens: 10", markdown);
        Assert.Contains("output_tokens: 20", markdown);
        Assert.Contains("<summary>⚙️ System Instructions & Declared Tools</summary>", markdown);
        Assert.Contains("You are a helpful calculator.", markdown);
        Assert.Contains("calc", markdown);
        Assert.Contains("### 👤 User", markdown);
        Assert.Contains("What is 42 * 2?", markdown);
        Assert.Contains("### 🤖 Assistant", markdown);
        Assert.Contains("💭 **Reasoning:**", markdown);
        Assert.Contains("I am thinking about the query.", markdown);
        Assert.Contains("Here is your answer.", markdown);
        Assert.Contains("#### 🛠️ Tool Call: `calc`", markdown);
    }

    [Fact]
    public async Task SaveTranscriptAsync_WritesFileToDisk()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "pmad_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var fake = new SimpleFakeClient();
            var client = new VerbatimTranscriptChatClient(fake, new VerbatimTranscriptOptions
            {
                OutputDirectory = tempDir,
                SessionTitle = "SaveTest"
            });

            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")]);
            var path = await client.SaveTranscriptAsync();

            Assert.True(File.Exists(path));
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("SaveTest", path);
            Assert.Contains("Hello", content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task Streaming_CoalescesIntoSingleTurn()
    {
        static async IAsyncEnumerable<ChatResponseUpdate> Stream()
        {
            yield return new ChatResponseUpdate { Contents = { new TextReasoningContent("Reasoning step 1. ") } };
            yield return new ChatResponseUpdate { Contents = { new TextReasoningContent("Reasoning step 2.") } };
            yield return new ChatResponseUpdate { Contents = { new TextContent("Result is ") } };
            yield return new ChatResponseUpdate { Contents = { new TextContent("42.") } };
        }

        var fake = new SimpleFakeClient(getStreamingResponse: (_, _, _) => Stream());
        var client = new VerbatimTranscriptChatClient(fake, new VerbatimTranscriptOptions());

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Query")]))
        {
        }

        var markdown = client.ToMarkdown();
        Assert.Contains("Reasoning step 1. Reasoning step 2.", markdown);
        Assert.Contains("Result is 42.", markdown);
        // Ensure there is only 1 Assistant header
        Assert.Equal(1, markdown.Split("### 🤖 Assistant").Length - 1);
    }

    [Fact]
    public async Task Redactor_MasksSensitiveInformation()
    {
        var fake = new SimpleFakeClient(getResponse: (_, _, _) =>
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Secret key is sk-12345")));
        });

        var client = new VerbatimTranscriptChatClient(fake, new VerbatimTranscriptOptions
        {
            Redactor = text => text.Replace("sk-12345", "[REDACTED]")
        });

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "What is my key?")]);
        var markdown = client.ToMarkdown();

        Assert.DoesNotContain("sk-12345", markdown);
        Assert.Contains("[REDACTED]", markdown);
    }

    [Fact]
    public async Task MultiTurn_PreservesAllToolTurnsAndDuplicateUserMessages()
    {
        var fake = new SimpleFakeClient(getResponse: (_, _, _) =>
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Understood.")));
        });

        var client = new VerbatimTranscriptChatClient(fake);

        // Turn 1: User says "next"
        var history = new List<ChatMessage> { new(ChatRole.User, "next") };
        await client.GetResponseAsync(history);

        // Turn 2: Model previously called tool 1, now tool 1 result arrives, then user says "next" again
        history.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call_1", "read", new Dictionary<string, object?> { ["p"] = "1" })]));
        history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_1", "result_1")]));
        history.Add(new ChatMessage(ChatRole.User, "next"));
        await client.GetResponseAsync(history);

        // Turn 3: Tool 2 result arrives
        history.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call_2", "read", new Dictionary<string, object?> { ["p"] = "2" })]));
        history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_2", "result_2")]));
        await client.GetResponseAsync(history);

        var markdown = client.ToMarkdown();

        // Both tool results must be present in the markdown
        Assert.Contains("result_1", markdown);
        Assert.Contains("result_2", markdown);

        // Both "next" user prompts must be present (2 occurrences of "next")
        var userHeaderCount = markdown.Split("### 👤 User").Length - 1;
        Assert.Equal(2, userHeaderCount);
    }

    [Fact]
    public async Task Streaming_UsageContent_RecordsTokens()
    {
        static async IAsyncEnumerable<ChatResponseUpdate> Stream()
        {
            yield return new ChatResponseUpdate { Contents = { new TextContent("Result") } };
            yield return new ChatResponseUpdate
            {
                Contents =
                {
                    new UsageContent(new UsageDetails
                    {
                        InputTokenCount = 15,
                        OutputTokenCount = 25,
                        TotalTokenCount = 40
                    })
                }
            };
        }

        var fake = new SimpleFakeClient(getStreamingResponse: (_, _, _) => Stream());
        var client = new VerbatimTranscriptChatClient(fake);

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "calc")]))
        {
        }

        var markdown = client.ToMarkdown();
        Assert.Contains("input_tokens: 15", markdown);
        Assert.Contains("output_tokens: 25", markdown);
        Assert.Contains("total_tokens: 40", markdown);
    }
}
