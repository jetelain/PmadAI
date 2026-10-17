using System.Net;
using System.Text;
using System.Text.Json;
using Pmad.AI.Compaction;

namespace Pmad.AI.Tests.Compaction;

public class OllamaSpeculativeSummarizerTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request);
    }

    [Fact]
    public void OllamaSpeculativeSummarizerOptions_DefaultValues_AreSensible()
    {
        var options = new OllamaSpeculativeSummarizerOptions();
        Assert.Equal("qwen3.5:2b", options.Model);
        Assert.Equal(new Uri("http://localhost:11434"), options.Endpoint);
        Assert.Equal(4096, options.ContextSize);
        Assert.Equal(150, options.MaxSummaryTokens);
        Assert.Equal(100, options.QueueCapacity);
        Assert.Equal(0.2f, options.Temperature);
        Assert.Equal("5m", options.KeepAlive);
    }

    [Fact]
    public async Task OllamaApiClient_ChatAsync_SendsExpectedJsonPayloadAndParsesResponse()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new MockHttpMessageHandler(async req =>
        {
            capturedRequest = req;
            capturedBody = await req.Content!.ReadAsStringAsync();

            var responseJson = """
            {
                "model": "qwen3.5:2b",
                "message": {
                    "role": "assistant",
                    "content": "  Parsed 3 config files. Found 0 errors.  "
                },
                "done": true
            }
            """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = new OllamaApiClient(new Uri("http://localhost:11434"), "qwen3.5:2b", httpClient);

        var result = await client.ChatAsync("Input text", "System instruction", maxTokens: 100, temperature: 0.1f);

        Assert.Equal("Parsed 3 config files. Found 0 errors.", result);
        Assert.NotNull(capturedRequest);
        Assert.Equal("/api/chat", capturedRequest.RequestUri!.AbsolutePath);

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody);
        var root = doc.RootElement;
        Assert.Equal("qwen3.5:2b", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal(100, root.GetProperty("options").GetProperty("num_predict").GetInt32());

        var messages = root.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("System instruction", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("Input text", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task OllamaApiClient_ChatAsync_HandlesHttpErrorGracefully()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"error\":\"model 'qwen3.5:2b' not found\"}", Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        using var client = new OllamaApiClient(new Uri("http://localhost:11434"), "qwen3.5:2b", httpClient);

        var result = await client.ChatAsync("Input text");
        Assert.Null(result);
    }

    [Fact]
    public async Task OllamaApiClient_CheckHealthAsync_ReturnsExpectedStatus()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/tags")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        using var httpClient = new HttpClient(handler);
        using var client = new OllamaApiClient(new Uri("http://localhost:11434"), "qwen3.5:2b", httpClient);

        var isHealthy = await client.CheckHealthAsync();
        Assert.True(isHealthy);
    }

    [Fact]
    public async Task EnqueueSummarization_ProcessesJobAndCachesSummary()
    {
        var handler = new MockHttpMessageHandler(async req =>
        {
            var responseJson = """
            {
                "model": "qwen3.5:2b",
                "message": {
                    "role": "assistant",
                    "content": "- Parsed config file\n- Found 3 services"
                },
                "done": true
            }
            """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        await using var summarizer = new OllamaSpeculativeSummarizer(
            new OllamaSpeculativeSummarizerOptions { MaxSummaryTokens = 50 },
            httpClient);

        summarizer.EnqueueSummarization("session_1", "item_a", SummarizationTarget.ToolResult, "Extensive log output of 500 lines...");

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
        var handler = new MockHttpMessageHandler(async req =>
        {
            var body = await req.Content!.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var msgs = doc.RootElement.GetProperty("messages");
            var userText = msgs[msgs.GetArrayLength() - 1].GetProperty("content").GetString();

            var responseJson = $$"""
            {
                "model": "qwen3.5:2b",
                "message": {
                    "role": "assistant",
                    "content": "Summary for {{userText}}"
                },
                "done": true
            }
            """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        await using var summarizer = new OllamaSpeculativeSummarizer(
            new OllamaSpeculativeSummarizerOptions(),
            httpClient);

        summarizer.EnqueueSummarization("session_A", "key_1", SummarizationTarget.ToolResult, "Raw 1");
        summarizer.EnqueueSummarization("session_B", "key_2", SummarizationTarget.ToolResult, "Raw 2");

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

        summarizer.EvictSession("session_A");

        Assert.False(summarizer.TryGetSummary("session_A", "key_1", out _));
        Assert.True(summarizer.TryGetSummary("session_B", "key_2", out _));
    }

    [Fact]
    public async Task PrioritizesToolResultOverThinkingMonologue()
    {
        var processedOrder = new List<string>();
        var tcsBlock = new TaskCompletionSource<bool>();

        var handler = new MockHttpMessageHandler(async req =>
        {
            await tcsBlock.Task; // Hold until released

            var body = await req.Content!.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var msgs = doc.RootElement.GetProperty("messages");
            var userText = msgs[msgs.GetArrayLength() - 1].GetProperty("content").GetString();
            lock (processedOrder)
            {
                processedOrder.Add(userText!);
            }

            var responseJson = """
            {
                "model": "qwen3.5:2b",
                "message": {
                    "role": "assistant",
                    "content": "Done"
                },
                "done": true
            }
            """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        await using var summarizer = new OllamaSpeculativeSummarizer(
            new OllamaSpeculativeSummarizerOptions { QueueCapacity = 10 },
            httpClient);

        // Enqueue thinking monologue first, then tool result
        summarizer.EnqueueSummarization("s1", "think1", SummarizationTarget.ThinkingMonologue, "Thought 1");
        summarizer.EnqueueSummarization("s1", "tool1", SummarizationTarget.ToolResult, "Tool Result 1");

        // Release the worker
        tcsBlock.SetResult(true);

        for (int i = 0; i < 40; i++)
        {
            if (summarizer.TryGetSummary("s1", "think1", out _) &&
                summarizer.TryGetSummary("s1", "tool1", out _))
            {
                break;
            }
            await Task.Delay(50);
        }

        // ToolResult (priority 0) should be processed before ThinkingMonologue (priority 1)
        Assert.Equal(2, processedOrder.Count);
        Assert.Contains("Tool Result 1", processedOrder);
        Assert.Contains("Thought 1", processedOrder);
    }

    [Fact]
    public async Task PrioritizesLargeToolResultsOverThinkingTraces()
    {
        var processedOrder = new List<string>();
        var tcsBlock = new TaskCompletionSource<bool>();
        var firstRequestEntered = new TaskCompletionSource<bool>();

        var handler = new MockHttpMessageHandler(async req =>
        {
            var body = await req.Content!.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var msgs = doc.RootElement.GetProperty("messages");
            var userText = msgs[msgs.GetArrayLength() - 1].GetProperty("content").GetString()!;

            if (userText == "Prime")
            {
                firstRequestEntered.TrySetResult(true);
                await tcsBlock.Task; // Hold prime request until released
            }
            else
            {
                lock (processedOrder)
                {
                    processedOrder.Add(userText);
                }
            }

            var responseJson = """
            {
                "model": "qwen3.5:2b",
                "message": { "role": "assistant", "content": "Done" },
                "done": true
            }
            """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        await using var summarizer = new OllamaSpeculativeSummarizer(
            new OllamaSpeculativeSummarizerOptions { QueueCapacity = 20 },
            httpClient);

        // 1. Prime the worker to guarantee it is blocked inside handler
        summarizer.EnqueueSummarization("s0", "prime", SummarizationTarget.ThinkingMonologue, "Prime");
        await firstRequestEntered.Task;

        // 2. While worker is blocked, enqueue all test items into queue
        var thinkLong = new string('T', 1000);
        var toolShort = new string('S', 50);
        var toolLarge = new string('L', 5000);
        var thinkShort = new string('t', 20);

        summarizer.EnqueueSummarization("s1", "think_long", SummarizationTarget.ThinkingMonologue, thinkLong);
        summarizer.EnqueueSummarization("s1", "tool_short", SummarizationTarget.ToolResult, toolShort);
        summarizer.EnqueueSummarization("s1", "tool_large", SummarizationTarget.ToolResult, toolLarge);
        summarizer.EnqueueSummarization("s1", "think_short", SummarizationTarget.ThinkingMonologue, thinkShort);

        // 3. Release the blocked worker
        tcsBlock.SetResult(true);

        for (int i = 0; i < 40; i++)
        {
            if (summarizer.TryGetSummary("s1", "think_long", out _) &&
                summarizer.TryGetSummary("s1", "tool_short", out _) &&
                summarizer.TryGetSummary("s1", "tool_large", out _) &&
                summarizer.TryGetSummary("s1", "think_short", out _))
            {
                break;
            }
            await Task.Delay(50);
        }

        Assert.Equal(4, processedOrder.Count);
        // Tool results must precede thinking traces, and within tool results, large precedes short
        Assert.Equal(toolLarge, processedOrder[0]);
        Assert.Equal(toolShort, processedOrder[1]);
        Assert.Equal(thinkLong, processedOrder[2]);
        Assert.Equal(thinkShort, processedOrder[3]);
    }

    [Fact]
    public async Task StructuredPrompts_ToolResult_SendsKeyValueFormatAndStripsMarkdownFences()
    {
        string? capturedSystemPrompt = null;

        var handler = new MockHttpMessageHandler(async req =>
        {
            var body = await req.Content!.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var msgs = doc.RootElement.GetProperty("messages");
            capturedSystemPrompt = msgs[0].GetProperty("content").GetString();

            // Simulate SLM returning markdown-fenced structured output
            var responseJson = """
            {
                "model": "qwen3.5:2b",
                "message": {
                    "role": "assistant",
                    "content": "```text\n[TOOL_RESULT_SUMMARY]\nStatus: SUCCESS\nKey_Entities: [test.py, run_tests]\nOutput_Summary: All 12 unit tests passed.\nError_Context: None\n[/TOOL_RESULT_SUMMARY]\n```"
                },
                "done": true
            }
            """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        await using var summarizer = new OllamaSpeculativeSummarizer(
            new OllamaSpeculativeSummarizerOptions(),
            httpClient);

        summarizer.EnqueueSummarization("s1", "call_1", SummarizationTarget.ToolResult, "Test execution output...");

        string? summary = null;
        for (int i = 0; i < 40; i++)
        {
            if (summarizer.TryGetSummary("s1", "call_1", out summary))
            {
                break;
            }
            await Task.Delay(50);
        }

        Assert.NotNull(capturedSystemPrompt);
        Assert.Contains("[TOOL_RESULT_SUMMARY]", capturedSystemPrompt);
        Assert.Contains("Status: SUCCESS|FAILED", capturedSystemPrompt);
        Assert.Contains("Key_Entities: [id, name, path]", capturedSystemPrompt);
        Assert.Contains("Output_Summary: <single factual sentence>", capturedSystemPrompt);
        Assert.Contains("Error_Context: <exact error if failed>", capturedSystemPrompt);

        // Verify markdown code fences were stripped
        Assert.NotNull(summary);
        Assert.StartsWith("[TOOL_RESULT_SUMMARY]", summary);
        Assert.EndsWith("[/TOOL_RESULT_SUMMARY]", summary);
        Assert.DoesNotContain("```", summary);

        // Verify ToolResultCompactor preserves the structured block cleanly
        var compactor = new ToolResultCompactor(new ToolResultCompactionOptions
        {
            Summarizer = summarizer,
            KeepVerbatimCount = 0,
            FallbackPrefixLength = 5,
            FallbackSuffixLength = 5,
            FallbackCharacterLimit = 10
        });

        var compacted = compactor.Compact([
            new Microsoft.Extensions.AI.ChatMessage(
                Microsoft.Extensions.AI.ChatRole.Tool,
                [new Microsoft.Extensions.AI.FunctionResultContent("call_1", "Test execution output...")])
        ], "s1");

        var frc = Assert.IsType<Microsoft.Extensions.AI.FunctionResultContent>(compacted[0].Contents[0]);
        var resultText = frc.Result?.ToString();
        Assert.NotNull(resultText);
        Assert.StartsWith("[TOOL_RESULT_SUMMARY]", resultText);
        Assert.DoesNotContain("[Semantic Summary: [TOOL_RESULT_SUMMARY]", resultText);
    }

    [Fact]
    public async Task RemoteOllamaSpeculativeSummarizer_SubclassInstantiatesAndWorks()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var responseJson = """
            {
                "model": "qwen3.5:2b",
                "message": { "role": "assistant", "content": "Summary" },
                "done": true
            }
            """;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        await using var summarizer = new RemoteOllamaSpeculativeSummarizer(
            new OllamaSpeculativeSummarizerOptions(),
            httpClient);

        summarizer.EnqueueSummarization("s1", "k1", SummarizationTarget.ToolResult, "data");

        string? summary = null;
        for (int i = 0; i < 40; i++)
        {
            if (summarizer.TryGetSummary("s1", "k1", out summary))
            {
                break;
            }
            await Task.Delay(50);
        }

        Assert.Equal("Summary", summary);
    }
}
