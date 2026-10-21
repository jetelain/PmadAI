using System.Net;
using System.Text;
using System.Text.Json;
using Pmad.AI.Compaction;

namespace Pmad.AI.Tests.Compaction;

public class OllamaSpeculativeSummarizerTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
            : this((req, _) => handler(req))
        {
        }

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this((req, _) => Task.FromResult(handler(req)))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }

    [Fact]
    public void OllamaSpeculativeSummarizerOptions_DefaultValues_AreSensible()
    {
        var options = new OllamaSpeculativeSummarizerOptions();
        Assert.Equal("gemma4:e2b", options.Model);
        Assert.Equal(new Uri("http://localhost:11434"), options.Endpoint);
        Assert.Equal(4096, options.ContextSize);
        Assert.Equal(150, options.MaxSummaryTokens);
        Assert.Equal(100, options.QueueCapacity);
        Assert.Equal(0.2f, options.Temperature);
        Assert.Equal("15m", options.KeepAlive);
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

        summarizer.EnqueueSummarization("session_1", "item_a", "Extensive log output of 500 lines...");

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
                "model": "gemma4:e2b",
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

        summarizer.EnqueueSummarization("session_A", "key_1", "Raw 1");
        summarizer.EnqueueSummarization("session_B", "key_2", "Raw 2");

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
    public async Task ToolResultsAreIgnoredAndNotEnqueued()
    {
        var processedOrder = new List<string>();

        var handler = new MockHttpMessageHandler(async req =>
        {
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
                "model": "gemma4:e2b",
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

        // Enqueue thinking monologue and tool result
        summarizer.EnqueueSummarization("s1", "think1", "Thought 1");

        for (int i = 0; i < 40; i++)
        {
            if (summarizer.TryGetSummary("s1", "think1", out _))
            {
                break;
            }
            await Task.Delay(50);
        }

        // Only ThinkingMonologue should be queued and processed
        Assert.Single(processedOrder);
        Assert.Equal("Thought 1", processedOrder[0]);
        Assert.True(summarizer.TryGetSummary("s1", "think1", out _));
        Assert.False(summarizer.TryGetSummary("s1", "tool1", out _));
    }

    [Fact]
    public async Task PrioritizesLargeThinkingTracesOverShorterTraces()
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
                "model": "gemma4:e2b",
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
        summarizer.EnqueueSummarization("s0", "prime", "Prime");
        await firstRequestEntered.Task;

        // 2. While worker is blocked, enqueue all test items into queue
        var thinkLong = new string('T', 1000);
        var thinkLarge = new string('L', 5000);
        var thinkShort = new string('t', 20);

        summarizer.EnqueueSummarization("s1", "think_long", thinkLong);
        summarizer.EnqueueSummarization("s1", "think_large", thinkLarge);
        summarizer.EnqueueSummarization("s1", "think_short", thinkShort);

        // 3. Release the blocked worker
        tcsBlock.SetResult(true);

        for (int i = 0; i < 40; i++)
        {
            if (summarizer.TryGetSummary("s1", "think_long", out _) &&
                summarizer.TryGetSummary("s1", "think_large", out _) &&
                summarizer.TryGetSummary("s1", "think_short", out _))
            {
                break;
            }
            await Task.Delay(50);
        }

        Assert.Equal(3, processedOrder.Count);
        // Larger thinking monologues must precede shorter thinking monologues
        Assert.Equal(thinkLarge, processedOrder[0]);
        Assert.Equal(thinkLong, processedOrder[1]);
        Assert.Equal(thinkShort, processedOrder[2]);
        Assert.False(summarizer.TryGetSummary("s1", "tool_ignored", out _));
    }

    [Fact]
    public async Task StructuredPrompts_ThinkingMonologue_SendsKeyValueFormatAndStripsMarkdownFences()
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
                "model": "gemma4:e2b",
                "message": {
                    "role": "assistant",
                    "content": "```text\n[THINKING_SUMMARY]\n- Finding: Regex mismatch in parser\n- Decision: Updated regex pattern\n- Ruled_Out: Rewriting full parser\n[/THINKING_SUMMARY]\n```"
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

        summarizer.EnqueueSummarization("s1", "call_1", "Long internal reasoning trace exceeding the limits...");

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
        Assert.Contains("[THINKING_SUMMARY]", capturedSystemPrompt);
        Assert.Contains("- Finding:", capturedSystemPrompt);
        Assert.Contains("- Decision:", capturedSystemPrompt);
        Assert.Contains("- Ruled_Out:", capturedSystemPrompt);

        // Verify markdown code fences were stripped
        Assert.NotNull(summary);
        Assert.StartsWith("[THINKING_SUMMARY]", summary);
        Assert.EndsWith("[/THINKING_SUMMARY]", summary);
        Assert.DoesNotContain("```", summary);

        // Verify ThinkingTraceCompactor preserves the structured block cleanly
        var compactor = new ThinkingTraceCompactor(new ThinkingTraceCompactionOptions
        {
            Summarizer = summarizer,
            KeepVerbatimCount = 0,
            FallbackPrefixLength = 5,
            FallbackSuffixLength = 5,
            FallbackCharacterLimit = 10
        });

        var compacted = compactor.Compact([
            new Microsoft.Extensions.AI.ChatMessage(
                Microsoft.Extensions.AI.ChatRole.Assistant,
                [new Microsoft.Extensions.AI.TextReasoningContent("Long internal reasoning trace exceeding the limits...")])
            {
                AdditionalProperties = new Microsoft.Extensions.AI.AdditionalPropertiesDictionary
                {
                    ["id"] = "call_1"
                }
            }
        ], "s1");

        var trc = Assert.IsType<Microsoft.Extensions.AI.TextReasoningContent>(compacted[0].Contents[0]);
        var resultText = trc.Text;
        Assert.NotNull(resultText);
        Assert.StartsWith("[THINKING_SUMMARY]", resultText);
        Assert.DoesNotContain("[Reasoned Summary: [THINKING_SUMMARY]", resultText);
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

        summarizer.EnqueueSummarization("s1", "k1", "data");

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

    [Fact]
    public async Task OllamaApiClient_ChatAsync_SerializesNumGpuWhenSpecified()
    {
        string? capturedBody = null;
        var handler = new MockHttpMessageHandler(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            var responseJson = """
            {
                "model": "qwen3.5:2b",
                "message": { "role": "assistant", "content": "OK" },
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

        var result = await client.ChatAsync("test", numGpu: 0);

        Assert.Equal("OK", result);
        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody);
        Assert.Equal(0, doc.RootElement.GetProperty("options").GetProperty("num_gpu").GetInt32());
    }

    [Fact]
    public async Task OllamaApiClient_ChatAsync_TimesOutGracefully()
    {
        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            await Task.Delay(1500, ct); // Exceeds 200ms timeout
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = new OllamaApiClient(new Uri("http://localhost:11434"), "qwen3.5:2b", httpClient);

        var result = await client.ChatAsync("test", timeout: TimeSpan.FromMilliseconds(200));

        Assert.Null(result);
    }

    [Fact]
    public async Task ProcessQueueAsync_JobTimesOut_AdvancesQueueWithoutHanging()
    {
        int callCount = 0;
        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            var body = await req.Content!.ReadAsStringAsync(ct);
            Interlocked.Increment(ref callCount);

            if (body.Contains("hanging_job"))
            {
                await Task.Delay(2000, ct); // Simulates hung Ollama
            }

            var responseJson = """
            {
                "model": "qwen3.5:2b",
                "message": { "role": "assistant", "content": "FastSummary" },
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
            new OllamaSpeculativeSummarizerOptions
            {
                JobTimeout = TimeSpan.FromMilliseconds(200),
                RequestTimeout = TimeSpan.FromMilliseconds(200)
            },
            httpClient);

        summarizer.EnqueueSummarization("s1", "k_slow", "hanging_job text");
        summarizer.EnqueueSummarization("s1", "k_fast", "fast text");

        string? fastSummary = null;
        for (int i = 0; i < 60; i++)
        {
            if (summarizer.TryGetSummary("s1", "k_fast", out fastSummary))
            {
                break;
            }
            await Task.Delay(50);
        }

        Assert.Equal("FastSummary", fastSummary);
        Assert.False(summarizer.TryGetSummary("s1", "k_slow", out _));
    }

    [Fact]
    public async Task GenerateSummaryAsync_ClampsOversizedInputText()
    {
        string? capturedBody = null;
        var handler = new MockHttpMessageHandler(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            var responseJson = """
            {
                "model": "qwen3.5:2b",
                "message": { "role": "assistant", "content": "ClampedSummary" },
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
            new OllamaSpeculativeSummarizerOptions
            {
                ContextSize = 100 // 100 tokens -> max 400 chars
            },
            httpClient);

        var largeText = new string('A', 2000);
        summarizer.EnqueueSummarization("s1", "k_large", largeText);

        string? summary = null;
        for (int i = 0; i < 40; i++)
        {
            if (summarizer.TryGetSummary("s1", "k_large", out summary))
            {
                break;
            }
            await Task.Delay(50);
        }

        Assert.Equal("ClampedSummary", summary);
        Assert.NotNull(capturedBody);
        Assert.Contains("[truncated for summarization]", capturedBody);
    }

    [Fact]
    public async Task GetInstalledModelsAsync_SupportsBothNameAndModelProperties()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.EndsWith("/api/tags", req.RequestUri!.ToString());

            var json = """
            {
                "models": [
                    { "name": "gemma4:e2b" },
                    { "model": "qwen2.5:3b" },
                    { "name": "" }
                ]
            }
            """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        using var client = new OllamaApiClient(httpClient: httpClient);

        var models = await client.GetInstalledModelsAsync();

        Assert.Equal(2, models.Count);
        Assert.Contains("gemma4:e2b", models);
        Assert.Contains("qwen2.5:3b", models);
    }

    [Fact]
    public async Task ChatAsync_SerializesNumThreadOption()
    {
        string? capturedBody = null;
        var handler = new MockHttpMessageHandler(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            var json = """
            {
                "message": { "role": "assistant", "content": "Done" },
                "done": true
            }
            """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = new OllamaApiClient(httpClient: httpClient);

        var result = await client.ChatAsync("Hello", numThread: 4);

        Assert.Equal("Done", result);
        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody);
        Assert.True(doc.RootElement.TryGetProperty("options", out var optionsEl));
        Assert.True(optionsEl.TryGetProperty("num_thread", out var threadEl));
        Assert.Equal(4, threadEl.GetInt32());
    }

    [Fact]
    public async Task OllamaSpeculativeSummarizer_PassesNumThreadOption()
    {
        string? capturedBody = null;
        var handler = new MockHttpMessageHandler(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            var json = """
            {
                "message": { "role": "assistant", "content": "[THINKING_SUMMARY]\n- Finding: OK\n[/THINKING_SUMMARY]" },
                "done": true
            }
            """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        await using var summarizer = new OllamaSpeculativeSummarizer(
            new OllamaSpeculativeSummarizerOptions
            {
                NumThread = 6
            },
            httpClient);

        summarizer.EnqueueSummarization("s1", "k1", "Long reasoning trace text...");

        string? summary = null;
        for (int i = 0; i < 40; i++)
        {
            if (summarizer.TryGetSummary("s1", "k1", out summary))
            {
                break;
            }
            await Task.Delay(50);
        }

        Assert.NotNull(summary);
        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody);
        Assert.True(doc.RootElement.TryGetProperty("options", out var optionsEl));
        Assert.True(optionsEl.TryGetProperty("num_thread", out var threadEl));
        Assert.Equal(6, threadEl.GetInt32());
    }
}

