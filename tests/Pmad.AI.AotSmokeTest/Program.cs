using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Pmad.AI;
using Pmad.AI.Compaction;
using Pmad.AI.Guardrails;
using Pmad.AI.Healing;
using Pmad.AI.Tools;
using Pmad.AI.Transcripts;

namespace Pmad.AI.AotSmokeTest;

public record SmokeArgs(string Query, int Count);

[JsonSerializable(typeof(SmokeArgs))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(bool))]
internal partial class SmokeJsonContext : JsonSerializerContext
{
}

public static class Program
{
    public static async Task<int> Main()
    {
        Console.WriteLine("[AOT Smoke Test] Starting Native AOT smoke test...");

        // 1. ToolCallHealingTransform
        var healed = ToolCallHealingTransform.HealResponseText("{\"choices\":[{\"message\":{\"content\":\"<tool_call>{\\\"name\\\":\\\"search\\\",\\\"arguments\\\":{\\\"query\\\":\\\"hello\\\",}}</tool_call>\"}}]}");
        if (!healed.Contains("\"tool_calls\""))
        {
            throw new InvalidOperationException("ToolCallHealingTransform failed: " + healed);
        }
        Console.WriteLine("✓ ToolCallHealingTransform verified");

        // 2. ReadOnlyCommandValidator
        var bashValidator = new ReadOnlyCommandValidator(new ReadOnlyCommandValidatorOptions
        {
            Dialect = ShellDialect.Bash
        });
        var safeRes = bashValidator.ValidateCommand("git status");
        if (!safeRes.IsAllowed)
        {
            throw new InvalidOperationException("ReadOnlyCommandValidator falsely blocked safe command: " + safeRes.Reason);
        }
        var badRes = bashValidator.ValidateCommand("rm -rf /");
        if (badRes.IsAllowed)
        {
            throw new InvalidOperationException("ReadOnlyCommandValidator failed to block dangerous command");
        }
        Console.WriteLine("✓ ReadOnlyCommandValidator verified");

        // 3. ToolLoopCircuitBreaker
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            PingPongThreshold = 2
        });
        var res1 = breaker.EvaluateToolInvocation("search", "{\"q\": \"test\"}");
        var res2 = breaker.EvaluateToolInvocation("search", "{\"q\": \"test\"}");
        if (!res2.IsLoopDetected)
        {
            throw new InvalidOperationException("ToolLoopCircuitBreaker missed loop on 2nd identical call");
        }
        Console.WriteLine("✓ ToolLoopCircuitBreaker verified");

        // 4. AIFunctionFactory with source-generated serializer and LenientToolDispatcher
        var serializerOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = SmokeJsonContext.Default
        };

        var searchFunc = AIFunctionFactory.Create(
            (string query, int count) => $"Results for {query} ({count})",
            "search",
            "Search database",
            serializerOptions: serializerOptions);

        var dispatcher = new LenientToolDispatcher();
        dispatcher.RegisterTools([searchFunc]);
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools = new Dictionary<string, ToolMapping>
            {
                ["search"] = new ToolMapping
                {
                    Aliases = ["find"],
                    ParameterAliases = new Dictionary<string, HashSet<string>>
                    {
                        ["query"] = ["q"],
                        ["count"] = ["limit"]
                    }
                }
            }
        });
        dispatcher.Validate(throwOnError: true);

        var dispatchedCall = dispatcher.NormalizeCall("find", new Dictionary<string, object?>
        {
            ["q"] = "dotnet",
            ["limit"] = 5
        });

        if (dispatchedCall.CanonicalToolName != "search" || (int)dispatchedCall.Arguments["count"]! != 5)
        {
            throw new InvalidOperationException("LenientToolDispatcher normalization failed");
        }

        var execResult = await dispatcher.ExecuteAsync("find", dispatchedCall.Arguments);
        if (execResult?.ToString() != "Results for dotnet (5)")
        {
            throw new InvalidOperationException($"LenientToolDispatcher execution unexpected: {execResult}");
        }
        Console.WriteLine("✓ LenientToolDispatcher and AIFunction AOT dispatch verified");

        // 5. Compaction (ThinkingTrace & ToolResult)
        var compactor = new ToolResultCompactor(new ToolResultCompactionOptions
        {
            FallbackPrefixLength = 10,
            FallbackSuffixLength = 10,
            FallbackCharacterLimit = 50,
            KeepVerbatimCount = 1
        });

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, [new FunctionCallContent("1", "read", new Dictionary<string, object?>())]),
            new(ChatRole.Tool, [new FunctionResultContent("1", new string('X', 200))]),
            new(ChatRole.User, "Next turn")
        };

        var compacted = compactor.Compact(messages, "session-1");
        if (compacted.Count != 4)
        {
            throw new InvalidOperationException("Compactor changed message count");
        }
        Console.WriteLine("✓ ToolResultCompactor verified");

        var thinkCompactor = new ThinkingTraceCompactor(new ThinkingTraceCompactionOptions
        {
            FallbackPrefixLength = 10,
            FallbackSuffixLength = 10,
            FallbackCharacterLimit = 50,
            KeepVerbatimCount = 1
        });
        var thinkMessages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, [new TextReasoningContent(new string('T', 200)), new TextContent("Result")]),
            new(ChatRole.User, "Follow-up"),
            new(ChatRole.Assistant, [new TextReasoningContent("Recent"), new TextContent("Recent result")])
        };
        var compactedThink = thinkCompactor.Compact(thinkMessages, "session-think");
        if (compactedThink.Count != 4)
        {
            throw new InvalidOperationException("ThinkingTraceCompactor changed message count");
        }
        Console.WriteLine("✓ ThinkingTraceCompactor verified");

        // 6. VerbatimTranscriptChatClient with mock inner client
        var mockInner = new MockEchoChatClient();
        var tempDir = Path.Combine(Path.GetTempPath(), "aot_smoke_transcript_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var transcriptClient = new VerbatimTranscriptChatClient(mockInner, new VerbatimTranscriptOptions
            {
                OutputDirectory = tempDir,
                SessionTitle = "AOT Smoke Test",
                AutoSaveOnDispose = true,
                FlushAfterEachTurn = true
            });

            var resp = await transcriptClient.GetResponseAsync([new ChatMessage(ChatRole.User, "Echo this")]);
            if (resp.Messages.Count == 0)
            {
                throw new InvalidOperationException("VerbatimTranscriptChatClient returned empty response");
            }
            Console.WriteLine("✓ VerbatimTranscriptChatClient verified");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }

        // 7. OllamaSpeculativeSummarizer and OllamaApiClient AOT check
        await using var summarizer = new OllamaSpeculativeSummarizer(new OllamaSpeculativeSummarizerOptions
        {
            Model = "qwen3.5:2b"
        });
        summarizer.EnqueueSummarization("session-aot", "key-1", "Test raw text");
        _ = summarizer.TryGetSummary("session-aot", "key-1", out _);
        summarizer.EvictSession("session-aot");
        Console.WriteLine("✓ OllamaSpeculativeSummarizer verified");

        Console.WriteLine("[AOT Smoke Test] ALL AOT CHECKS PASSED SUCCESSFULLY!");
        return 0;
    }

    private sealed class MockEchoChatClient : IChatClient
    {
        public ChatClientMetadata Metadata => new("mock", new Uri("http://localhost"), "mock");

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var assistantMsg = new ChatMessage(ChatRole.Assistant, "Echoed response");
            return Task.FromResult(new ChatResponse([assistantMsg])
            {
                Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5, TotalTokenCount = 15 }
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextContent("Echoed response")] };
            await Task.Yield();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
