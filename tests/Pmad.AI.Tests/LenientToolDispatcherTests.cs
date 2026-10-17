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
