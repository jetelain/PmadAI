using System.ComponentModel;
using System.Text.Json;
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

    public enum TestResourceType
    {
        [ToolValueAlias("memory", "memory_limit", "mem", "ram")]
        MemoryMb,

        [ToolValueAlias("cpu", "cpu_limit", "cores", "vcpu")]
        CpuCores,

        [ToolValueAlias("storage", "disk", "disk_size")]
        StorageGb
    }

    [Fact]
    public void NormalizeCall_ResolvesEnumValueAliases_FromEnumAttributes()
    {
        var dispatcher = new LenientToolDispatcher();
        var mapping = new ToolMapping();
        mapping.AddEnumAliases<TestResourceType>("resource_type");
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools = { ["provision"] = mapping }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (TestResourceType resource_type) => $"provisioned: {resource_type}",
            "provision"));

        // Test alias "memory_limit" -> "MemoryMb"
        var dispatched1 = dispatcher.NormalizeCall("provision", "{\"resource_type\": \"memory_limit\"}");
        Assert.Equal("MemoryMb", dispatched1.Arguments["resource_type"]);

        // Test alias "ram" -> "MemoryMb"
        var dispatched2 = dispatcher.NormalizeCall("provision", "{\"resource_type\": \"ram\"}");
        Assert.Equal("MemoryMb", dispatched2.Arguments["resource_type"]);

        // Test alias "vcpu" -> "CpuCores"
        var dispatched3 = dispatcher.NormalizeCall("provision", "{\"resource_type\": \"vcpu\"}");
        Assert.Equal("CpuCores", dispatched3.Arguments["resource_type"]);
    }

    [Fact]
    public void NormalizeCall_ResolvesStringValueAliases_ConfiguredOnToolMapping()
    {
        var dispatcher = new LenientToolDispatcher();
        var mapping = new ToolMapping();
        mapping.AddParameterValueAlias("species", "wild tiger", "wild tigers", "tiger", "tigers");
        mapping.AddParameterValueAlias("species", "african elephant", "african elephants", "elephant");
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools = { ["calculate_effective_population_size"] = mapping }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string species, int censusCount) => $"{species}: {censusCount}",
            "calculate_effective_population_size"));

        var dispatched = dispatcher.NormalizeCall("calculate_effective_population_size", "{\"species\": \"wild tigers\", \"censusCount\": 500}");
        Assert.Equal("wild tiger", dispatched.Arguments["species"]);
    }

    [Fact]
    public void NormalizeCall_ResolvesValueAliases_AcrossPathCasingAndSnakeCasePlurals()
    {
        var dispatcher = new LenientToolDispatcher();
        var mapping = new ToolMapping();
        // Registered with camelCase path "targetSpecies"
        mapping.AddParameterValueAlias("targetSpecies", "wild tiger", "tiger");
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools = { ["track_animal"] = mapping }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string target_species) => target_species,
            "track_animal"));

        // Call with snake_case parameter path "target_species" and snake_case plural "wild_tigers"
        var dispatched = dispatcher.NormalizeCall("track_animal", "{\"target_species\": \"wild_tigers\"}");
        Assert.Equal("wild tiger", dispatched.Arguments["target_species"]);
    }

    [Fact]
    public async Task NormalizeCall_PerformsDeepObjectAndArrayNormalization()
    {
        var dispatcher = new LenientToolDispatcher();
        var mapping = new ToolMapping(
            nestedParameterAliases: new Dictionary<string, IDictionary<string, IEnumerable<string>>>
            {
                ["edits[]"] = new Dictionary<string, IEnumerable<string>>
                {
                    ["OldText"] = ["target_content", "old_text"],
                    ["NewText"] = ["replacement_content", "new_text"]
                }
            });

        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools = { ["apply_patch"] = mapping }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (List<TestEditChunk> edits) => edits.Count,
            "apply_patch"));

        var json = """
        {
            "edits": [
                {
                    "target_content": "oldCode();",
                    "replacement_content": "newCode();"
                }
            ]
        }
        """;

        var dispatched = dispatcher.NormalizeCall("apply_patch", json);
        Assert.True(dispatched.Arguments.ContainsKey("edits"));
        var editsList = Assert.IsAssignableFrom<IEnumerable<object?>>(dispatched.Arguments["edits"]).ToList();
        Assert.Single(editsList);
        var firstEdit = Assert.IsAssignableFrom<IDictionary<string, object?>>(editsList[0]);
        var oldVal = firstEdit.TryGetValue("oldText", out var ov) ? ov : firstEdit["OldText"];
        var newVal = firstEdit.TryGetValue("newText", out var nv) ? nv : firstEdit["NewText"];
        Assert.Equal("oldCode();", oldVal);
        Assert.Equal("newCode();", newVal);

        var result = await dispatcher.ExecuteAsync("apply_patch", json);
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task LenientToolDispatcher_ExecutesSilently_WithoutInjectingApologiesOrNotices()
    {
        var dispatcher = new LenientToolDispatcher();
        var mapping = new ToolMapping();
        mapping.AddParameterValueAlias("species", "wild tiger", "wild tigers");
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools = { ["lookup"] = mapping }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string species) => $"found {species}",
            "lookup"));

        var client = new FakePipelineChatClient((messages, options, ct) =>
        {
            // Verify no apology, notice, or system correction message was injected into history
            var msgList = messages.ToList();
            Assert.DoesNotContain(msgList, m => m.Text != null && (m.Text.Contains("normalized") || m.Text.Contains("apologize") || m.Text.Contains("repaired")));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
        });

        var pipeline = new ChatClientBuilder(client)
            .UseLenientToolDispatch(dispatcher)
            .Build();

        var call = new FunctionCallContent("call_1", "lookup", new Dictionary<string, object?> { ["species"] = "wild tigers" });
        var response = await pipeline.GetResponseAsync([
            new ChatMessage(ChatRole.User, "find species"),
            new ChatMessage(ChatRole.Assistant, [call])
        ]);

        Assert.Equal("done", response.Text);
    }

    [Fact]
    public void CasingHelpers_CorrectlyHandleWhitespaceAndMultipleSeparators()
    {
        Assert.Equal("wild_tiger", LenientToolDispatcher.ToSnakeCase("wild tiger"));
        Assert.Equal("wild_tiger", LenientToolDispatcher.ToSnakeCase("wild   tiger"));
        Assert.Equal("wild_tiger", LenientToolDispatcher.ToSnakeCase("wild  _  tiger"));
        Assert.Equal("wildTiger", LenientToolDispatcher.ToCamelCase("wild tiger"));
        Assert.Equal("wildTiger", LenientToolDispatcher.ToCamelCase("wild   tiger"));
        Assert.Equal("WildTiger", LenientToolDispatcher.ToPascalCase("wild tiger"));
        Assert.Equal("WildTiger", LenientToolDispatcher.ToPascalCase("wild   tiger"));
    }

    [Fact]
    public void RegisterTool_IdempotentRegistration_SameInstance_DoesNotReparseSchema()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(([Description("File path")] string path) => path, "read");

        dispatcher.RegisterTool(tool);
        Assert.True(dispatcher.TryGetCachedSchemaProperties("read", out var props1));
        Assert.NotNull(props1);

        // Register same instance again
        dispatcher.RegisterTool(tool);
        Assert.True(dispatcher.TryGetCachedSchemaProperties("read", out var props2));

        Assert.Same(props1, props2);
    }

    [Fact]
    public void RegisterTool_IdempotentRegistration_EquivalentSchema_DoesNotReallocateProperties()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool1 = AIFunctionFactory.Create(([Description("Query string")] string query, [Description("Limit count")] int limit) => query, "search");
        var tool2 = AIFunctionFactory.Create(([Description("Query string")] string query, [Description("Limit count")] int limit) => query, "search");

        dispatcher.RegisterTool(tool1);
        Assert.True(dispatcher.TryGetCachedSchemaProperties("search", out var props1));
        Assert.NotNull(props1);

        // Register distinct instance with equivalent schema
        dispatcher.RegisterTool(tool2);
        Assert.True(dispatcher.TryGetCachedSchemaProperties("search", out var props2));

        Assert.Same(props1, props2);
        Assert.True(dispatcher.TryGetRegisteredTool("search", out var activeTool));
        Assert.Same(tool2, activeTool);
    }

    [Fact]
    public void RegisterTool_SynchronizesCanonicalToolNames_AndResolvesCaseInsensitively()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create((string location) => location, "get_weather");

        dispatcher.RegisterTool(tool);

        Assert.Equal("get_weather", dispatcher.ResolveCanonicalToolName("GET_WEATHER"));
        Assert.Equal("get_weather", dispatcher.ResolveCanonicalToolName("Get_Weather"));
        Assert.True(dispatcher.IsToolRegistered("GET_WEATHER"));
        Assert.True(dispatcher.IsToolRegistered("get_weather"));

        // Normalizing tool call with casing drift records telemetry
        var telemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = telemetry;

        var dispatched = dispatcher.NormalizeCall("GET_WEATHER", new Dictionary<string, object?> { ["Location"] = "Paris" });
        Assert.Equal("get_weather", dispatched.CanonicalToolName);
        Assert.Equal("Paris", dispatched.Arguments["location"]);

        var snapshot = telemetry.CreateSnapshot();
        Assert.True(snapshot.CasingNormalized >= 2); // 1 for tool name, 1 for property name
        Assert.True(snapshot.TotalHits >= 2);
    }

    [Fact]
    public async Task LenientToolDispatchChatClient_RegistersRequestTools_NonStreaming_NormalizesPropertiesAndCoercesTypes()
    {
        var dispatcher = new LenientToolDispatcher();
        var telemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = telemetry;

        var dynamicTool = AIFunctionFactory.Create(
            ([Description("Item identifier")] int id, [Description("Item label")] string label) => $"{id}:{label}",
            "fetch_item");

        var client = new FakePipelineChatClient((messages, options, ct) =>
        {
            var call = new FunctionCallContent("call_1", "Fetch_Item", new Dictionary<string, object?>
            {
                ["Id"] = "123", // string that should be coerced to int based on dynamic tool schema
                ["Label"] = "Widget"
            });
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])));
        });

        var pipeline = new ChatClientBuilder(client)
            .UseLenientToolDispatch(dispatcher)
            .Build();

        var options = new ChatOptions
        {
            Tools = [dynamicTool]
        };

        var response = await pipeline.GetResponseAsync([new ChatMessage(ChatRole.User, "fetch")], options);

        var fcc = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Single();
        Assert.Equal("fetch_item", fcc.Name);
        Assert.NotNull(fcc.Arguments);
        Assert.Equal(123, fcc.Arguments["id"]); // int coerced from string "123"
        Assert.Equal("Widget", fcc.Arguments["label"]);

        // Dispatcher has registered the tool and cached properties
        Assert.True(dispatcher.IsToolRegistered("fetch_item"));
        var snapshot = telemetry.CreateSnapshot();
        Assert.True(snapshot.CasingNormalized >= 2); // tool casing + property casing
        Assert.True(snapshot.TotalHits >= 2);
    }

    [Fact]
    public async Task LenientToolDispatchChatClient_RegistersRequestTools_Streaming_NormalizesToolAndPropertyNames()
    {
        var dispatcher = new LenientToolDispatcher();
        var telemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = telemetry;

        var dynamicTool = AIFunctionFactory.Create(
            ([Description("Query text")] string queryText) => queryText,
            "search_documents");

        async IAsyncEnumerable<ChatResponseUpdate> GenerateUpdates()
        {
            var fcc = new FunctionCallContent("call_1", "SEARCH_DOCUMENTS", new Dictionary<string, object?>
            {
                ["Query_Text"] = "antigravity"
            });
            yield return new ChatResponseUpdate { Contents = [fcc] };
            yield return new ChatResponseUpdate { Contents = [new TextContent("done")] };
        }

        var client = new FakePipelineChatClient(
            getResponse: (_, _, _) => throw new NotImplementedException(),
            getStreamingResponse: (_, _, _) => GenerateUpdates());

        var pipeline = new ChatClientBuilder(client)
            .UseLenientToolDispatch(dispatcher)
            .Build();

        var options = new ChatOptions
        {
            Tools = [dynamicTool]
        };

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in pipeline.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "search")], options))
        {
            updates.Add(update);
        }

        var fcc = updates.SelectMany(u => u.Contents).OfType<FunctionCallContent>().Single();
        Assert.Equal("search_documents", fcc.Name);
        Assert.NotNull(fcc.Arguments);
        Assert.Equal("antigravity", fcc.Arguments["queryText"]);

        var snapshot = telemetry.CreateSnapshot();
        Assert.True(snapshot.CasingNormalized >= 1);
        Assert.True(snapshot.TotalHits >= 1);
    }

    [Fact]
    public async Task LenientToolDispatchChatClient_PreservesStaticConfig_AlongsideDynamicTools()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["read"] = new ToolMapping(aliases: ["cat"])
            }
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create((string path) => path, "read"));

        var dynamicTool = AIFunctionFactory.Create((string name) => name, "create_user");

        var client = new FakePipelineChatClient((messages, options, ct) =>
        {
            // Call the static alias 'cat' and the dynamic tool 'Create_User'
            var call1 = new FunctionCallContent("call_1", "cat", new Dictionary<string, object?> { ["path"] = "file.txt" });
            var call2 = new FunctionCallContent("call_2", "Create_User", new Dictionary<string, object?> { ["name"] = "Alice" });
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call1, call2])));
        });

        var pipeline = new ChatClientBuilder(client)
            .UseLenientToolDispatch(dispatcher)
            .Build();

        var options = new ChatOptions
        {
            Tools = [dynamicTool]
        };

        var response = await pipeline.GetResponseAsync([new ChatMessage(ChatRole.User, "run")], options);

        var calls = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();
        Assert.Equal(2, calls.Count);
        Assert.Equal("read", calls[0].Name);
        Assert.Equal("create_user", calls[1].Name);
    }

    public enum ServerEnvironment
    {
        Development,
        Staging,
        Production
    }

    [Fact]
    public void SnapEnumValue_DirectCall_OrdinalCaseInsensitive_SnapsToUniqueCandidate()
    {
        var dispatcher = new LenientToolDispatcher();
        var telemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = telemetry;

        var candidates = new[] { "Development", "Staging", "Production" };
        var result = dispatcher.SnapEnumValue("production", candidates);

        Assert.Equal("Production", result);
        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(1, snapshot.EnumValuesSnapped);
        Assert.Equal(0, snapshot.EnumValuesUnmatched);
        Assert.Equal(1, snapshot.TotalHits);
    }

    [Fact]
    public void SnapEnumValue_DirectCall_ExactMatch_DoesNotRecordTelemetry()
    {
        var dispatcher = new LenientToolDispatcher();
        var telemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = telemetry;

        var candidates = new[] { "Development", "Staging", "Production" };
        var result = dispatcher.SnapEnumValue("Production", candidates);

        Assert.Equal("Production", result);
        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(0, snapshot.EnumValuesSnapped);
        Assert.Equal(0, snapshot.EnumValuesUnmatched);
        Assert.Equal(0, snapshot.TotalHits);
    }

    [Fact]
    public void SnapEnumValue_DirectCall_CaseInsensitiveTie_LeavesUntouched_AndRecordsUnmatched()
    {
        var dispatcher = new LenientToolDispatcher();
        var telemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = telemetry;

        var candidates = new[] { "foo", "FOO" };
        var result = dispatcher.SnapEnumValue("Foo", candidates);

        Assert.Equal("Foo", result);
        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(0, snapshot.EnumValuesSnapped);
        Assert.Equal(1, snapshot.EnumValuesUnmatched);
    }

    [Fact]
    public void SnapEnumValue_DirectCall_Normalization_CollapsesSeparatorsAndWhitespace()
    {
        var dispatcher = new LenientToolDispatcher();
        var telemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = telemetry;

        var candidates = new[] { "in_progress", "completed", "not_started" };
        Assert.Equal("in_progress", dispatcher.SnapEnumValue("  in-progress  ", candidates));
        Assert.Equal("in_progress", dispatcher.SnapEnumValue("in progress", candidates));
        Assert.Equal("in_progress", dispatcher.SnapEnumValue("inProgress", candidates));
        Assert.Equal("in_progress", dispatcher.SnapEnumValue("in___progress", candidates));

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(4, snapshot.EnumValuesSnapped);
    }

    [Fact]
    public void SnapEnumValue_DirectCall_NaivePluralStripping()
    {
        var dispatcher = new LenientToolDispatcher();
        var telemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = telemetry;

        var candidates = new[] { "wild_tiger", "african_elephant" };
        // Plural input -> singular candidate
        Assert.Equal("wild_tiger", dispatcher.SnapEnumValue("wild tigers", candidates));
        Assert.Equal("wild_tiger", dispatcher.SnapEnumValue("wild_tigers", candidates));

        // Singular input -> plural candidate
        var pluralCandidates = new[] { "wild_tigers", "african_elephants" };
        Assert.Equal("wild_tigers", dispatcher.SnapEnumValue("wild tiger", pluralCandidates));
        Assert.Equal("wild_tigers", dispatcher.SnapEnumValue("wild_tiger", pluralCandidates));

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(4, snapshot.EnumValuesSnapped);
    }

    [Fact]
    public void SnapEnumValue_DirectCall_UniqueSubstringContainment()
    {
        var dispatcher = new LenientToolDispatcher();
        var telemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = telemetry;

        var candidates = new[] { "Battle of Stalingrad", "Battle of Midway", "Siege of Leningrad" };
        // Stalingrad uniquely contained in "Battle of Stalingrad"
        Assert.Equal("Battle of Stalingrad", dispatcher.SnapEnumValue("Stalingrad", candidates));
        // Midway uniquely contained in "Battle of Midway"
        Assert.Equal("Battle of Midway", dispatcher.SnapEnumValue("Midway", candidates));

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(2, snapshot.EnumValuesSnapped);
    }

    [Fact]
    public void SnapEnumValue_DirectCall_AmbiguousSubstringContainmentTie_LeavesUntouched()
    {
        var dispatcher = new LenientToolDispatcher();
        var telemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = telemetry;

        // "Stalingrad" matches both
        var candidates = new[] { "Battle of Stalingrad", "Siege of Stalingrad" };
        var result = dispatcher.SnapEnumValue("Stalingrad", candidates);

        Assert.Equal("Stalingrad", result);
        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(0, snapshot.EnumValuesSnapped);
        Assert.Equal(1, snapshot.EnumValuesUnmatched);
    }

    [Fact]
    public void SnapEnumValue_DirectCall_BoundedEditDistance_SnapsWhenWithinBound()
    {
        var dispatcher = new LenientToolDispatcher();
        var telemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = telemetry;

        var candidates = new[] { "pending", "approved", "rejected" };
        // "pedning" -> transposition of d/n (distance 1, len 7, 1/7 <= 0.25)
        Assert.Equal("pending", dispatcher.SnapEnumValue("pedning", candidates));

        // "pendng" -> deletion of i (distance 1, len 7, 1/7 <= 0.25)
        Assert.Equal("pending", dispatcher.SnapEnumValue("pendng", candidates));

        // Long string with distance 2: "stalingrad" (len 10) vs "stalingrdd" (distance 2, 2/11 <= 0.25)
        var longCandidates = new[] { "stalingrad", "normandy" };
        Assert.Equal("stalingrad", dispatcher.SnapEnumValue("stalingrdd", longCandidates));

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(3, snapshot.EnumValuesSnapped);
    }

    [Fact]
    public void SnapEnumValue_DirectCall_BoundedEditDistance_RejectsWhenExceedingLengthBound()
    {
        var dispatcher = new LenientToolDispatcher();
        var telemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = telemetry;

        var candidates = new[] { "cat", "dog" };
        // distance 1 on length 3 is 33% > 25% bound -> rejected
        var result = dispatcher.SnapEnumValue("kat", candidates);

        Assert.Equal("kat", result);
        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(0, snapshot.EnumValuesSnapped);
        Assert.Equal(1, snapshot.EnumValuesUnmatched);
    }

    [Fact]
    public void SnapEnumValue_DirectCall_BoundedEditDistance_TieLeavesUntouched()
    {
        var dispatcher = new LenientToolDispatcher();
        var telemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = telemetry;

        // "cax1234" is distance 1 from both "cat1234" and "car1234"
        var candidates = new[] { "cat1234", "car1234" };
        var result = dispatcher.SnapEnumValue("cax1234", candidates);

        Assert.Equal("cax1234", result);
        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(0, snapshot.EnumValuesSnapped);
        Assert.Equal(1, snapshot.EnumValuesUnmatched);
    }

    [Fact]
    public void SnapEnumValue_DirectCall_PreventsFalseSubstringSnappingOnShortWords()
    {
        var dispatcher = new LenientToolDispatcher();
        var telemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = telemetry;

        // "connection" should NOT snap to "on" simply because "connection" contains "on"
        Assert.Equal("connection", dispatcher.SnapEnumValue("connection", ["on", "off"]));

        // "admin" should NOT snap to "in"
        Assert.Equal("admin", dispatcher.SnapEnumValue("admin", ["in", "out"]));

        // "catalog" should NOT snap to "cat"
        Assert.Equal("catalog", dispatcher.SnapEnumValue("catalog", ["cat", "dog"]));

        // "thread" should NOT snap to "read"
        Assert.Equal("thread", dispatcher.SnapEnumValue("thread", ["read", "write"]));

        // "streetcar" should NOT snap to "car"
        Assert.Equal("streetcar", dispatcher.SnapEnumValue("streetcar", ["car", "bus", "plane"]));

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(0, snapshot.EnumValuesSnapped);
        Assert.Equal(5, snapshot.EnumValuesUnmatched);
    }

    [Fact]
    public void SnapEnumValue_DirectCall_TokenAndAffixContainment_SnapsLegitimatePhrases()
    {
        var dispatcher = new LenientToolDispatcher();
        var telemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = telemetry;

        // Bounded delimiter tokens
        Assert.Equal("turn-on", dispatcher.SnapEnumValue("on", ["turn-on", "turn-off"]));
        Assert.Equal("wild_tiger", dispatcher.SnapEnumValue("tiger", ["wild_tiger", "african_elephant"]));

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(2, snapshot.EnumValuesSnapped);
    }

    [Fact]
    public void SnapEnumValue_DirectCall_StripPluralSuffix_HandlesUpperCase()
    {
        var dispatcher = new LenientToolDispatcher();
        var telemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = telemetry;

        // Upper-case plural candidate/value snapping
        Assert.Equal("TIGER", dispatcher.SnapEnumValue("TIGERS", ["TIGER", "ELEPHANT"]));
        Assert.Equal("STATUS", dispatcher.SnapEnumValue("STATUS", ["STATUS", "PROCESS"]));

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(1, snapshot.EnumValuesSnapped);
    }

    [Fact]
    public void SnapEnumValue_JsonSchema_SupportsEnumConstAndOneOf()
    {
        var dispatcher = new LenientToolDispatcher();

        // 1. Array enum schema
        using var doc1 = JsonDocument.Parse("""{"type": "string", "enum": ["Active", "Inactive", "Suspended"]}""");
        Assert.Equal("Active", dispatcher.SnapEnumValue("active", doc1.RootElement));

        // 2. Const schema
        using var doc2 = JsonDocument.Parse("""{"type": "string", "const": "production"}""");
        Assert.Equal("production", dispatcher.SnapEnumValue("PRODUCTION", doc2.RootElement));

        // 3. oneOf schema
        using var doc3 = JsonDocument.Parse("""
        {
            "oneOf": [
                {"const": "read_only"},
                {"const": "read_write"}
            ]
        }
        """);
        Assert.Equal("read_only", dispatcher.SnapEnumValue("read-only", doc3.RootElement));

        // 4. allOf schema
        using var doc4 = JsonDocument.Parse("""
        {
            "allOf": [
                {
                    "type": "string",
                    "enum": ["Draft", "Published", "Archived"]
                }
            ]
        }
        """);
        Assert.Equal("Draft", dispatcher.SnapEnumValue("draft", doc4.RootElement));
    }

    [Fact]
    public async Task NormalizeCall_EndToEnd_SnapsEnumParameterToAIFunction()
    {
        var dispatcher = new LenientToolDispatcher();
        var telemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = telemetry;

        var tool = AIFunctionFactory.Create(
            (ServerEnvironment env) => $"Deployed to {env}",
            "deploy_server");

        dispatcher.RegisterTool(tool);

        // Model passes lowercase with typo "producton" or casing "staging"
        var dispatched1 = dispatcher.NormalizeCall("deploy_server", new Dictionary<string, object?> { ["env"] = "staging" });
        Assert.Equal("Staging", dispatched1.Arguments["env"]);

        var result1 = await tool.InvokeAsync(dispatched1.Arguments);
        Assert.Equal("Deployed to Staging", result1?.ToString());

        var dispatched2 = dispatcher.NormalizeCall("deploy_server", new Dictionary<string, object?> { ["env"] = "producton" }); // 1 typo in production (len 10)
        Assert.Equal("Production", dispatched2.Arguments["env"]);

        var result2 = await tool.InvokeAsync(dispatched2.Arguments);
        Assert.Equal("Deployed to Production", result2?.ToString());

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(2, snapshot.EnumValuesSnapped);
        Assert.Equal(2, snapshot.TotalHits);
    }

    public class ComplexConfigItem
    {
        public ServerEnvironment Env { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void NormalizeCall_EndToEnd_SnapsNestedObjectEnumProperty()
    {
        var dispatcher = new LenientToolDispatcher();
        var telemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = telemetry;

        var tool = AIFunctionFactory.Create(
            (ComplexConfigItem config) => $"{config.Name}:{config.Env}",
            "configure_app");

        dispatcher.RegisterTool(tool);

        var dispatched = dispatcher.NormalizeCall("configure_app", new Dictionary<string, object?>
        {
            ["config"] = new Dictionary<string, object?>
            {
                ["name"] = "MyService",
                ["env"] = "development" // lowercase should snap to Development
            }
        });

        var configDict = Assert.IsAssignableFrom<IDictionary<string, object?>>(dispatched.Arguments["config"]);
        var envVal = configDict.TryGetValue("env", out var ev) ? ev : configDict["Env"];
        Assert.Equal("Development", envVal);

        var snapshot = telemetry.CreateSnapshot();
        Assert.True(snapshot.EnumValuesSnapped >= 1);
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
