using System.Text.Json;
using Microsoft.Extensions.AI;
using Pmad.AI.Transcripts;

namespace Pmad.AI.Tests;

public class OpenAiJsonlTranscriptChatClientTests
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
            yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextContent("Default response")] };
            await Task.Yield();
        }
    }

    [Fact]
    public async Task ToJsonl_MatchesOpenAiFormatWithToolCalling()
    {
        int callIndex = 0;
        var fake = new SimpleFakeClient(
            getResponse: (msgs, opts, ct) =>
            {
                callIndex++;
                if (callIndex == 1)
                {
                    var assistantMsg = new ChatMessage(ChatRole.Assistant, [
                        new FunctionCallContent("call_abc123", "read_file", new Dictionary<string, object?> { ["path"] = "config.json" })
                    ]);
                    return Task.FromResult(new ChatResponse([assistantMsg]));
                }
                else
                {
                    var finalMsg = new ChatMessage(ChatRole.Assistant, "Voici le contenu: port 8080.");
                    return Task.FromResult(new ChatResponse([finalMsg]));
                }
            });

        var client = new OpenAiJsonlTranscriptChatClient(fake);

        var response1 = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "Affiche le contenu du fichier config.json")
        ]);

        // Turn 2: tool returns result
        var response2 = await client.GetResponseAsync([
            new ChatMessage(ChatRole.User, "Affiche le contenu du fichier config.json"),
            new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent("call_abc123", "read_file", new Dictionary<string, object?> { ["path"] = "config.json" })
            ]),
            new ChatMessage(ChatRole.Tool, [
                new FunctionResultContent("call_abc123", "{\"status\": \"success\", \"data\": \"{ \\\"port\\\": 8080 }\"}")
            ])
        ]);

        var jsonl = await client.ToJsonlAsync();

        Assert.False(jsonl.Contains('\n')); // Single line!

        using var doc = JsonDocument.Parse(jsonl);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("messages", out var messages));
        Assert.Equal(4, messages.GetArrayLength());

        // Message 0: User
        var m0 = messages[0];
        Assert.Equal("user", m0.GetProperty("role").GetString());
        Assert.Equal("Affiche le contenu du fichier config.json", m0.GetProperty("content").GetString());

        // Message 1: Assistant with tool call and content null
        var m1 = messages[1];
        Assert.Equal("assistant", m1.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, m1.GetProperty("content").ValueKind);
        Assert.True(m1.TryGetProperty("tool_calls", out var toolCalls));
        Assert.Equal(1, toolCalls.GetArrayLength());
        var tc = toolCalls[0];
        Assert.Equal("call_abc123", tc.GetProperty("id").GetString());
        Assert.Equal("function", tc.GetProperty("type").GetString());
        var fn = tc.GetProperty("function");
        Assert.Equal("read_file", fn.GetProperty("name").GetString());
        Assert.Equal("{\"path\": \"config.json\"}", fn.GetProperty("arguments").GetString());

        // Message 2: Tool result
        var m2 = messages[2];
        Assert.Equal("tool", m2.GetProperty("role").GetString());
        Assert.Equal("call_abc123", m2.GetProperty("tool_call_id").GetString());
        Assert.Equal("read_file", m2.GetProperty("name").GetString());
        Assert.Equal("{\"status\": \"success\", \"data\": \"{ \\\"port\\\": 8080 }\"}", m2.GetProperty("content").GetString());

        // Message 3: Final Assistant
        var m3 = messages[3];
        Assert.Equal("assistant", m3.GetProperty("role").GetString());
        Assert.Equal("Voici le contenu: port 8080.", m3.GetProperty("content").GetString());
    }

    [Fact]
    public async Task ToJsonl_IncludesSystemPrompt_WhenConfigured()
    {
        var fake = new SimpleFakeClient((msgs, opts, ct) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello!"))));

        var client = new OpenAiJsonlTranscriptChatClient(fake, new OpenAiJsonlTranscriptOptions
        {
            IncludeSystemPrompt = true
        });

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hi")],
            new ChatOptions { Instructions = "You are a friendly bot." });

        var jsonl = await client.ToJsonlAsync();
        using var doc = JsonDocument.Parse(jsonl);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(3, messages.GetArrayLength());

        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("You are a friendly bot.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("assistant", messages[2].GetProperty("role").GetString());
    }

    [Fact]
    public async Task ToJsonl_ReasoningModes_WorkCorrectly()
    {
        var fake = new SimpleFakeClient((msgs, opts, ct) =>
        {
            var msg = new ChatMessage(ChatRole.Assistant, [
                new TextReasoningContent("Thinking steps..."),
                new TextContent("The answer is 42.")
            ]);
            return Task.FromResult(new ChatResponse([msg]));
        });

        // 1. None (default)
        var clientNone = new OpenAiJsonlTranscriptChatClient(fake);
        await clientNone.GetResponseAsync([new ChatMessage(ChatRole.User, "Query")]);
        var jsonlNone = await clientNone.ToJsonlAsync();
        Assert.DoesNotContain("Thinking steps", jsonlNone);
        Assert.Contains("The answer is 42.", jsonlNone);

        // 2. ReasoningContent
        var clientReasoning = new OpenAiJsonlTranscriptChatClient(fake, new OpenAiJsonlTranscriptOptions
        {
            ReasoningMode = OpenAiJsonlReasoningMode.ReasoningContent
        });
        await clientReasoning.GetResponseAsync([new ChatMessage(ChatRole.User, "Query")]);
        var jsonlReasoning = await clientReasoning.ToJsonlAsync();
        using var docReasoning = JsonDocument.Parse(jsonlReasoning);
        var assistantMsg = docReasoning.RootElement.GetProperty("messages")[1];
        Assert.Equal("Thinking steps...", assistantMsg.GetProperty("reasoning_content").GetString());

        // 3. ThinkTags
        var clientThink = new OpenAiJsonlTranscriptChatClient(fake, new OpenAiJsonlTranscriptOptions
        {
            ReasoningMode = OpenAiJsonlReasoningMode.ThinkTags
        });
        await clientThink.GetResponseAsync([new ChatMessage(ChatRole.User, "Query")]);
        var jsonlThink = await clientThink.ToJsonlAsync();
        using var docThink = JsonDocument.Parse(jsonlThink);
        var content = docThink.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
        Assert.StartsWith("<think>\nThinking steps...\n</think>\n", content);
    }

    [Fact]
    public async Task ToJsonl_StreamingResponse_CoalescesChunksCorrectly()
    {
        var fake = new SimpleFakeClient(
            getStreamingResponse: (msgs, opts, ct) => CreateStream());

        static async IAsyncEnumerable<ChatResponseUpdate> CreateStream()
        {
            yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextReasoningContent("Thinking...")] };
            yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextContent("Hello ")] };
            yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextContent("world!")] };
            await Task.Yield();
        }

        var client = new OpenAiJsonlTranscriptChatClient(fake, new OpenAiJsonlTranscriptOptions
        {
            ReasoningMode = OpenAiJsonlReasoningMode.ReasoningContent
        });

        var chunks = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Hi")]))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                chunks.Add(update.Text);
            }
        }

        Assert.Equal(2, chunks.Count);

        var jsonl = await client.ToJsonlAsync();
        using var doc = JsonDocument.Parse(jsonl);
        var assistantMsg = doc.RootElement.GetProperty("messages")[1];
        Assert.Equal("Hello world!", assistantMsg.GetProperty("content").GetString());
        Assert.Equal("Thinking...", assistantMsg.GetProperty("reasoning_content").GetString());
    }

    [Fact]
    public async Task ToJsonl_Redactor_SanitizesSensitiveData()
    {
        var fake = new SimpleFakeClient((msgs, opts, ct) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Token: secret_12345"))));

        var client = new OpenAiJsonlTranscriptChatClient(fake, new OpenAiJsonlTranscriptOptions
        {
            Redactor = text => text.Replace("secret_12345", "[REDACTED]")
        });

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Key: secret_12345")]);

        var jsonl = await client.ToJsonlAsync();
        Assert.DoesNotContain("secret_12345", jsonl);
        Assert.Contains("[REDACTED]", jsonl);
    }

    [Fact]
    public async Task DisposeAsync_FlushesSingleLineToEndOfFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"openai_jsonl_test_{Guid.NewGuid():N}.jsonl");
        try
        {
            var fake = new SimpleFakeClient((msgs, opts, ct) =>
                Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Conversation 1 Answer"))));

            // Conversation 1
            await using (var client1 = new OpenAiJsonlTranscriptChatClient(fake, tempFile))
            {
                await client1.GetResponseAsync([new ChatMessage(ChatRole.User, "Conv 1 Query")]);
            }

            // File now has 1 line
            var linesAfter1 = await File.ReadAllLinesAsync(tempFile);
            Assert.Single(linesAfter1);
            Assert.Contains("Conversation 1 Answer", linesAfter1[0]);

            // Conversation 2 with separate client writing to same dataset file
            var fake2 = new SimpleFakeClient((msgs, opts, ct) =>
                Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Conversation 2 Answer"))));

            await using (var client2 = new OpenAiJsonlTranscriptChatClient(fake2, tempFile))
            {
                await client2.GetResponseAsync([new ChatMessage(ChatRole.User, "Conv 2 Query")]);
            }

            // File now has 2 distinct lines
            var linesAfter2 = await File.ReadAllLinesAsync(tempFile);
            Assert.Equal(2, linesAfter2.Length);
            Assert.Contains("Conversation 1 Answer", linesAfter2[0]);
            Assert.Contains("Conversation 2 Answer", linesAfter2[1]);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task BuilderExtensions_RegistersAndProducesExpectedOutput()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"openai_jsonl_ext_{Guid.NewGuid():N}.jsonl");
        try
        {
            var fake = new SimpleFakeClient((msgs, opts, ct) =>
                Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Ext Answer"))));

            var builder = new ChatClientBuilder(fake);
            builder.UseJsonlTranscript(tempFile);
            var client = builder.Build();

            var resp = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Ext Query")]);
            Assert.NotNull(resp);

            var transcriptClient = client.GetService<OpenAiJsonlTranscriptChatClient>();
            Assert.NotNull(transcriptClient);

            await transcriptClient.FlushAsync();

            var lines = await File.ReadAllLinesAsync(tempFile);
            Assert.Single(lines);
            Assert.Contains("Ext Answer", lines[0]);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task TempFile_StagingCapturesPrompt_OnCrashOrException()
    {
        var masterFile = Path.Combine(Path.GetTempPath(), $"master_{Guid.NewGuid():N}.jsonl");
        var customTempFile = Path.Combine(Path.GetTempPath(), $"staging_forensics_{Guid.NewGuid():N}.tmp");

        try
        {
            var crashingFake = new SimpleFakeClient((msgs, opts, ct) =>
                throw new InvalidOperationException("Simulated runtime crash/kill during LLM inference"));

            var client = new OpenAiJsonlTranscriptChatClient(crashingFake, new OpenAiJsonlTranscriptOptions
            {
                FilePath = masterFile,
                TempFilePath = customTempFile
            });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.GetResponseAsync([new ChatMessage(ChatRole.User, "Forensics Prompt That Crashed")]));

            Assert.Equal("Simulated runtime crash/kill during LLM inference", ex.Message);

            // Verify crash forensics: Temp file exists and contains the prompt!
            Assert.True(File.Exists(customTempFile));
            var stagedContent = await File.ReadAllTextAsync(customTempFile);
            Assert.Contains("Forensics Prompt That Crashed", stagedContent);
            Assert.Contains("\"role\":\"user\"", stagedContent);

            // Master file should NOT contain any line because conversation never completed
            Assert.False(File.Exists(masterFile));
        }
        finally
        {
            if (File.Exists(customTempFile)) File.Delete(customTempFile);
            if (File.Exists(masterFile)) File.Delete(masterFile);
        }
    }

    [Fact]
    public async Task TempFile_CustomPathAndCleanupOnSuccess_WorksAsExpected()
    {
        var masterFile = Path.Combine(Path.GetTempPath(), $"master_{Guid.NewGuid():N}.jsonl");
        var customTempFile = Path.Combine(Path.GetTempPath(), $"custom_staging_{Guid.NewGuid():N}.tmp");

        try
        {
            var fake = new SimpleFakeClient((msgs, opts, ct) =>
                Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Clean Completion"))));

            await using (var client = new OpenAiJsonlTranscriptChatClient(fake, new OpenAiJsonlTranscriptOptions
            {
                FilePath = masterFile,
                TempFilePath = customTempFile,
                DeleteTempFileOnSuccess = true
            }))
            {
                Assert.Equal(customTempFile, client.TempFilePath);
                await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Success Query")]);

                // While conversation is active, temp file exists
                Assert.True(File.Exists(customTempFile));
            }

            // After dispose: master file has the line, temp file is cleaned up!
            Assert.True(File.Exists(masterFile));
            var lines = await File.ReadAllLinesAsync(masterFile);
            Assert.Single(lines);
            Assert.Contains("Clean Completion", lines[0]);

            Assert.False(File.Exists(customTempFile));
        }
        finally
        {
            if (File.Exists(customTempFile)) File.Delete(customTempFile);
            if (File.Exists(masterFile)) File.Delete(masterFile);
        }
    }

    [Fact]
    public async Task TempFile_RetentionWhenDeleteOnSuccessIsFalse_PreservesTempFile()
    {
        var masterFile = Path.Combine(Path.GetTempPath(), $"master_{Guid.NewGuid():N}.jsonl");
        var customTempFile = Path.Combine(Path.GetTempPath(), $"retain_staging_{Guid.NewGuid():N}.tmp");

        try
        {
            var fake = new SimpleFakeClient((msgs, opts, ct) =>
                Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Retained Completion"))));

            await using (var client = new OpenAiJsonlTranscriptChatClient(fake, new OpenAiJsonlTranscriptOptions
            {
                FilePath = masterFile,
                TempFilePath = customTempFile,
                DeleteTempFileOnSuccess = false
            }))
            {
                await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Query")]);
            }

            // Both master and temp file must exist
            Assert.True(File.Exists(masterFile));
            Assert.True(File.Exists(customTempFile));
        }
        finally
        {
            if (File.Exists(customTempFile)) File.Delete(customTempFile);
            if (File.Exists(masterFile)) File.Delete(masterFile);
        }
    }
}
