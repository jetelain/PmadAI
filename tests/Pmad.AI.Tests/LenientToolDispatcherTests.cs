using System.ComponentModel;
using Microsoft.Extensions.AI;
using Pmad.AI.Guardrails;
using Pmad.AI.Tools;

namespace Pmad.AI.Tests;

public class LenientToolDispatcherTests
{
    [Fact]
    public void NormalizeCall_ResolvesToolAndParameterAliases()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["read"] = new ToolMapping(
                    aliases: ["read_file", "view_file"],
                    parameterAliases: new Dictionary<string, IEnumerable<string>>
                    {
                        ["path"] = ["filePath", "file_path", "filename"]
                    })
            }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            ([Description("File path")] string path) => $"content of {path}",
            "read"));

        var dispatched = dispatcher.NormalizeCall("view_file", "{\"filePath\": \"src/app.cs\"}");

        Assert.Equal("read", dispatched.CanonicalToolName);
        Assert.Equal("src/app.cs", dispatched.Arguments["path"]);
        Assert.False(dispatched.WasTranslated);
    }

    [Fact]
    public void NormalizeCall_StripsNamespacePrefix()
    {
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            StripNamespacePrefixes = true
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create((string path) => path, "read"));

        var dispatched = dispatcher.NormalizeCall("workspace__read", "{\"path\": \"test.txt\"}");
        Assert.Equal("read", dispatched.CanonicalToolName);
    }

    [Fact]
    public void NormalizeCall_RendersTranslationWithEscaping()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Translations =
            [
                new ToolTranslation
                {
                    SourceTool = "list_dir",
                    TargetTool = "bash",
                    CommandTemplate = "ls -la -- {path:.}",
                    EscapeMethod = ParameterEscapeMethod.Bash
                }
            ]
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            ([Description("Shell command")] string command) => $"ran {command}",
            "bash"));

        var dispatched = dispatcher.NormalizeCall("list_dir", "{\"path\": \"my folder's\"}");

        Assert.True(dispatched.WasTranslated);
        Assert.Equal("bash", dispatched.CanonicalToolName);
        Assert.Equal("ls -la -- 'my folder'\\''s'", dispatched.Arguments["command"]);
        Assert.Equal("ls -la -- 'my folder'\\''s'", dispatched.RenderedCommand);
    }

    [Fact]
    public void NormalizeCall_RejectsOptionInjectionInTranslation()
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
                    CommandTemplate = "rg -- {query}",
                    EscapeMethod = ParameterEscapeMethod.Bash
                }
            ]
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create((string command) => command, "bash"));

        // Passing an argument starting with '-' is rejected to prevent option injection
        Assert.Throws<ArgumentException>(() =>
            dispatcher.NormalizeCall("grep_search", "{\"query\": \"--pre=evil\"}"));
    }

    [Fact]
    public void Validate_CatchesMissingToolAndInvalidParameterSchema()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["non_existent_tool"] = new ToolMapping(aliases: ["fake"]),
                ["real_tool"] = new ToolMapping(
                    parameterAliases: new Dictionary<string, IEnumerable<string>>
                    {
                        ["non_existent_param"] = ["p_alias"]
                    })
            }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(([Description("Valid param")] string validParam) => validParam, "real_tool"));

        var report = dispatcher.Validate(throwOnError: false);

        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, i => i.ToolName == "non_existent_tool" && i.Severity == ToolValidationSeverity.Error);
        Assert.Contains(report.Issues, i => i.ToolName == "real_tool" && i.ParameterName == "non_existent_param" && i.Severity == ToolValidationSeverity.Error);
    }

    [Fact]
    public async Task ExecuteAsync_NormalizesAndInvokesTargetTool()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["add"] = new ToolMapping(
                    aliases: ["plus"],
                    parameterAliases: new Dictionary<string, IEnumerable<string>>
                    {
                        ["a"] = ["x"],
                        ["b"] = ["y"]
                    })
            }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (int a, int b) => a + b,
            "add"));

        var result = await dispatcher.ExecuteAsync("plus", "{\"x\": 10, \"y\": 32}");

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task UseLenientToolDispatch_RewritesToolCallContentInPipeline()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["canonical_tool"] = new ToolMapping(
                    aliases: ["aliased_tool"],
                    parameterAliases: new Dictionary<string, IEnumerable<string>>
                    {
                        ["target"] = ["file"]
                    })
            }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create((string target) => target, "canonical_tool"));

        var fakeClient = new FakePipelineChatClient(getResponse: (_, _, _) =>
        {
            var msg = new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent("call_1", "aliased_tool", new Dictionary<string, object?> { ["file"] = "data.csv" })
            ]);
            return Task.FromResult(new ChatResponse(msg));
        });

        var pipeline = new ChatClientBuilder(fakeClient)
            .UseLenientToolDispatch(dispatcher)
            .Build();

        var response = await pipeline.GetResponseAsync([new ChatMessage(ChatRole.User, "Do it")]);
        var fcc = response.Messages.First().Contents.OfType<FunctionCallContent>().First();

        Assert.Equal("canonical_tool", fcc.Name);
        Assert.Equal("data.csv", fcc.Arguments?["target"]?.ToString());
    }

    [Fact]
    public void NormalizeCall_Translation_ResolvesCasingDifferencesAndParameterAliases()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["grep_search"] = new ToolMapping(
                    parameterAliases: new Dictionary<string, IEnumerable<string>>
                    {
                        ["query"] = ["pattern", "search_term"]
                    })
            },
            Translations =
            [
                new ToolTranslation
                {
                    SourceTool = "grep_search",
                    TargetTool = "bash",
                    CommandTemplate = "rg -- {query} {path:.}",
                    EscapeMethod = ParameterEscapeMethod.Bash
                }
            ]
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(([Description("Shell command")] string command) => command, "bash"));

        // Caller supplies "Pattern" with capital P (alias of query) and "Path" with capital P
        var rawArgs = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Pattern"] = "my search",
            ["Path"] = "src/"
        };

        var dispatched = dispatcher.NormalizeCall("grep_search", rawArgs);

        Assert.True(dispatched.WasTranslated);
        Assert.Equal("bash", dispatched.CanonicalToolName);
        Assert.Equal("rg -- 'my search' 'src/'", dispatched.Arguments["command"]);
    }

    [Fact]
    public async Task Streaming_HandlesToolCallDeltaWithNullOrWhitespaceName()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.RegisterTool(AIFunctionFactory.Create(([Description("File path")] string path) => path, "read"));

        static async IAsyncEnumerable<ChatResponseUpdate> GenerateStreamingDeltas()
        {
            // Chunk 1: Function call with tool name
            yield return new ChatResponseUpdate
            {
                Contents = { new FunctionCallContent("call_1", "read", new Dictionary<string, object?> { ["path"] = "a.cs" }) }
            };
            // Chunk 2: Streaming delta where Name is empty
            yield return new ChatResponseUpdate
            {
                Contents = { new FunctionCallContent("call_1", "", new Dictionary<string, object?>()) }
            };
        }

        var fakeClient = new FakePipelineChatClient(
            getResponse: (_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, ""))),
            getStreamingResponse: (_, _, _) => GenerateStreamingDeltas());

        var pipeline = new ChatClientBuilder(fakeClient)
            .UseLenientToolDispatch(dispatcher)
            .Build();

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in pipeline.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "read")]))
        {
            updates.Add(update);
        }

        Assert.Equal(2, updates.Count);
        var firstCall = updates[0].Contents.OfType<FunctionCallContent>().First();
        Assert.Equal("read", firstCall.Name);
    }

    [Fact]
    public void NormalizeCall_ConfiguredToolWithoutRegisteredInstance_ResolvesCanonicalToolAndMapping()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["unregistered_tool"] = new ToolMapping(
                    aliases: ["alias_tool"],
                    parameterAliases: new Dictionary<string, IEnumerable<string>>
                    {
                        ["param"] = ["arg"]
                    })
            }
        });

        var dispatched = dispatcher.NormalizeCall("unregistered_tool", "{\"arg\": \"val\"}");

        Assert.Equal("unregistered_tool", dispatched.CanonicalToolName);
        Assert.Equal("val", dispatched.Arguments["param"]);
    }

    public enum SampleRunMode
    {
        Fast,
        Accurate
    }

    [Fact]
    public void NormalizeCall_CoercesParametersHeuristically()
    {
        var dispatcher = new LenientToolDispatcher();

        // 1. String-to-number: "start_line": "80" -> 80
        var d1 = dispatcher.NormalizeCall("read_file", "{\"start_line\": \"80\"}");
        Assert.Equal(80, d1.Arguments["start_line"]);

        // 2. Comma-delimited strings to arrays: "cities": "Paris, Tokyo" -> ["Paris", "Tokyo"]
        var d2 = dispatcher.NormalizeCall("search_locations", "{\"cities\": \"Paris, Tokyo\"}");
        Assert.IsAssignableFrom<IEnumerable<object>>(d2.Arguments["cities"]);
        var cities = ((IEnumerable<object>)d2.Arguments["cities"]!).Select(x => x?.ToString() ?? string.Empty).ToArray();
        Assert.Equal(new[] { "Paris", "Tokyo" }, cities);

        // 3. Case-insensitive enum / boolean matching: "cached": "False" -> false
        var d3 = dispatcher.NormalizeCall("fetch_data", "{\"cached\": \"False\"}");
        Assert.Equal(false, d3.Arguments["cached"]);
    }

    [Fact]
    public void NormalizeCall_WithRegisteredFunctionSchema_CoercesStrictTypes()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (int start_line, string[] cities, bool cached, SampleRunMode mode) => "done",
            "process_batch"));

        var json = "{\"start_line\": \"80\", \"cities\": \"Paris, Tokyo\", \"cached\": \"False\", \"mode\": \"fast\"}";
        var dispatched = dispatcher.NormalizeCall("process_batch", json);

        Assert.Equal("process_batch", dispatched.CanonicalToolName);
        Assert.Equal(80, dispatched.Arguments["start_line"]);
        Assert.Equal(new[] { "Paris", "Tokyo" }, (string[])dispatched.Arguments["cities"]!);
        Assert.Equal(false, dispatched.Arguments["cached"]);
        Assert.Equal("Fast", dispatched.Arguments["mode"]);
    }

    public record TestEditChunk(string OldText, string NewText);

    [Fact]
    public async Task NormalizeCall_DeepSchemaRenamesNestedSnakeCaseProperties()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (List<TestEditChunk> edits) => $"Applied {edits.Count} edits: {edits[0].OldText} -> {edits[0].NewText}",
            "apply_edits");

        dispatcher.RegisterTool(tool);

        var json = "{\"edits\": [{\"old_text\": \"hello\", \"new_text\": \"world\"}]}";
        var dispatched = dispatcher.NormalizeCall("apply_edits", json);

        Assert.Equal("apply_edits", dispatched.CanonicalToolName);
        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal("Applied 1 edits: hello -> world", result?.ToString());
    }

    [Fact]
    public async Task NormalizeCall_DeepSchemaParsesStringifiedJsonAndRenamesNestedAliases()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["apply_edits"] = new ToolMapping(
                    nestedParameterAliases: new Dictionary<string, IDictionary<string, IEnumerable<string>>>
                    {
                        ["edits[]"] = new Dictionary<string, IEnumerable<string>>
                        {
                            ["OldText"] = ["targetContent", "search"],
                            ["NewText"] = ["replacementContent", "replace"]
                        }
                    })
            }
        });

        var tool = AIFunctionFactory.Create(
            (List<TestEditChunk> edits) => $"{edits[0].OldText} => {edits[0].NewText}",
            "apply_edits");

        dispatcher.RegisterTool(tool);

        // Stringified JSON array with aliases
        var json = "{\"edits\": \"[{\\\"targetContent\\\": \\\"search_pattern\\\", \\\"replacementContent\\\": \\\"replaced_text\\\"}]\"}";
        var dispatched = dispatcher.NormalizeCall("apply_edits", json);

        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal("search_pattern => replaced_text", result?.ToString());
    }

    [Fact]
    public void NormalizeCall_EvaluatesComputedParametersForLineSlicing()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["slice_lines"] = new ToolMapping(
                    computedParameters: new Dictionary<string, ParameterComputer>
                    {
                        ["offset"] = raw => raw.TryGetValue("start_line", out var sl) && sl != null ? Convert.ToInt32(sl) : null,
                        ["limit"] = raw =>
                        {
                            if (raw.TryGetValue("start_line", out var sl) && raw.TryGetValue("end_line", out var el) && sl != null && el != null)
                            {
                                return Convert.ToInt32(el) - Convert.ToInt32(sl) + 1;
                            }
                            return null;
                        }
                    })
            }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (int offset, int limit) => $"{offset}:{limit}",
            "slice_lines"));

        var dispatched = dispatcher.NormalizeCall("slice_lines", "{\"start_line\": 10, \"end_line\": 25}");

        Assert.Equal(10, dispatched.Arguments["offset"]);
        Assert.Equal(16, dispatched.Arguments["limit"]);
    }

    [Fact]
    public async Task LenientAIFunction_NormalizesArgumentsBeforeDirectInvocation()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["read_file"] = new ToolMapping(
                    parameterAliases: new Dictionary<string, IEnumerable<string>>
                    {
                        ["path"] = ["filePath", "file_path"]
                    })
            }
        });

        var baseTool = AIFunctionFactory.Create(
            (string path) => $"Read: {path}",
            "read_file");

        dispatcher.RegisterTool(baseTool);

        var lenientTool = baseTool.AsLenient(dispatcher);

        var args = new AIFunctionArguments { ["filePath"] = "src/main.cs" };
        var result = await lenientTool.InvokeAsync(args);

        Assert.Equal("Read: src/main.cs", result?.ToString());
    }

    [Fact]
    public async Task LenientAIFunction_CatchesExecutionExceptionsGracefully()
    {
        var dispatcher = new LenientToolDispatcher();
        var baseTool = AIFunctionFactory.Create(
            string (string path) => throw new FileNotFoundException($"File not found: {path}"),
            "read_file");

        var lenientTool = baseTool.AsLenient(dispatcher);
        var result = await lenientTool.InvokeAsync(new AIFunctionArguments { ["path"] = "missing.txt" });

        var str = result?.ToString();
        Assert.NotNull(str);
        Assert.StartsWith("Error executing tool 'read_file':", str);
        Assert.Contains("File not found: missing.txt", str);
    }

    [Fact]
    public async Task NormalizeCall_DoesNotConvertJsonStringToDictionary_WhenSchemaExpectsString()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (string path, string content) => $"Wrote {content.Length} chars to {path}: {content}",
            "write_file");

        dispatcher.RegisterTool(tool);

        var json = "{\"path\": \"appsettings.json\", \"content\": \"{\\\"logging\\\": true}\"}";
        var dispatched = dispatcher.NormalizeCall("write_file", json);

        Assert.IsType<string>(dispatched.Arguments["content"]);
        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal("Wrote 17 chars to appsettings.json: {\"logging\": true}", result?.ToString());
    }

    [Fact]
    public async Task NormalizeCall_WrapsSingleObjectShortcutIntoArray_WhenSchemaExpectsArray()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (List<TestEditChunk> edits) => $"Applied {edits.Count} edits: {edits[0].OldText} -> {edits[0].NewText}",
            "apply_edits");

        dispatcher.RegisterTool(tool);

        var json = "{\"edits\": {\"old_text\": \"foo\", \"new_text\": \"bar\"}}";
        var dispatched = dispatcher.NormalizeCall("apply_edits", json);

        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal("Applied 1 edits: foo -> bar", result?.ToString());
    }

    [Fact]
    public async Task NormalizeCall_ResolvesNullableComplexParameterSchemas()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (TestEditChunk? chunk) => chunk != null ? $"{chunk.OldText} => {chunk.NewText}" : "none",
            "process_chunk");

        dispatcher.RegisterTool(tool);

        var json = "{\"chunk\": {\"old_text\": \"hello\", \"new_text\": \"world\"}}";
        var dispatched = dispatcher.NormalizeCall("process_chunk", json);

        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal("hello => world", result?.ToString());
    }

    [Fact]
    public void NormalizeCall_ComputerExceptionSafetyAndCaseInsensitivity()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["slice_lines"] = new ToolMapping(
                    computedParameters: new Dictionary<string, ParameterComputer>
                    {
                        ["offset"] = raw => raw.TryGetValue("start_line", out var sl) && sl != null ? Convert.ToInt32(sl) : null,
                        ["limit"] = raw =>
                        {
                            if (raw.TryGetValue("start_line", out var sl) && raw.TryGetValue("end_line", out var el) && sl != null && el != null)
                            {
                                return Convert.ToInt32(el) - Convert.ToInt32(sl) + 1;
                            }
                            return null;
                        },
                        ["buggy"] = raw => throw new InvalidOperationException("boom")
                    })
            }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (int offset, int limit) => $"{offset}:{limit}",
            "slice_lines"));

        // Case-insensitive raw argument keys ("Start_Line" and "END_LINE")
        var dispatched = dispatcher.NormalizeCall("slice_lines", "{\"Start_Line\": 5, \"END_LINE\": 15}");

        Assert.Equal(5, dispatched.Arguments["offset"]);
        Assert.Equal(11, dispatched.Arguments["limit"]);
        Assert.False(dispatched.Arguments.ContainsKey("buggy"));
    }

    [Fact]
    public void Validate_DoesNotWarn_OnNestedPropertyPaths()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["apply_edits"] = new ToolMapping(
                    nestedParameterAliases: new Dictionary<string, IDictionary<string, IEnumerable<string>>>
                    {
                        ["edits[].chunk"] = new Dictionary<string, IEnumerable<string>>
                        {
                            ["OldText"] = ["targetContent"]
                        }
                    })
            }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (List<TestEditChunk> edits) => edits.Count,
            "apply_edits"));

        var report = dispatcher.Validate(throwOnError: false);
        Assert.True(report.IsValid);
        Assert.DoesNotContain(report.Issues, i => i.Message.Contains("non-existent root parameter"));
    }

    [Fact]
    public async Task LenientAIFunction_AutoRegistersSchema_WhenNotRegisteredBeforehand()
    {
        var dispatcher = new LenientToolDispatcher();
        // Do NOT call dispatcher.RegisterTool
        var baseTool = AIFunctionFactory.Create(
            (List<TestEditChunk> edits) => $"Processed: {edits[0].OldText}",
            "auto_reg_edits");

        var lenientTool = baseTool.AsLenient(dispatcher);

        var args = new AIFunctionArguments
        {
            ["edits"] = new List<Dictionary<string, object?>>
            {
                new() { ["old_text"] = "unregistered_auto_cached" }
            }
        };

        var result = await lenientTool.InvokeAsync(args);
        Assert.Equal("Processed: unregistered_auto_cached", result?.ToString());
    }

    [Fact]
    public async Task LenientAIFunction_DispatchesToTargetTool_WhenTranslated()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Translations =
            [
                new ToolTranslation
                {
                    SourceTool = "virtual_grep",
                    TargetTool = "bash",
                    CommandTemplate = "grep {pattern} {path}"
                }
            ]
        });

        var bashTool = AIFunctionFactory.Create(
            (string command) => $"Executed bash: {command}",
            "bash");
        dispatcher.RegisterTool(bashTool);

        var virtualGrep = AIFunctionFactory.Create(
            (string pattern, string path) => "raw grep",
            "virtual_grep");

        var lenientTool = virtualGrep.AsLenient(dispatcher);

        var args = new AIFunctionArguments { ["pattern"] = "antigravity", ["path"] = "main.cs" };
        var result = await lenientTool.InvokeAsync(args);

        Assert.Equal("Executed bash: grep 'antigravity' 'main.cs'", result?.ToString());
    }

    private class FakePipelineChatClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> _getResponse;
        private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>? _getStreamingResponse;

        public FakePipelineChatClient(
            Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> getResponse,
            Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>? getStreamingResponse = null)
        {
            _getResponse = getResponse;
            _getStreamingResponse = getStreamingResponse;
        }

        public ChatClientMetadata Metadata => new("FakePipelineChatClient");
        public void Dispose() { }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => _getResponse(chatMessages, options, cancellationToken);
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => _getStreamingResponse != null ? _getStreamingResponse(chatMessages, options, cancellationToken) : throw new NotImplementedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }
}
