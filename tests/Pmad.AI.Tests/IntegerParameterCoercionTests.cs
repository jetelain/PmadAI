using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Pmad.AI.Tools;

namespace Pmad.AI.Tests;

public class IntegerParameterCoercionTests
{
    public sealed class NestedCommandRequest
    {
        public string Command { get; set; } = string.Empty;
        public int? Timeout { get; set; }
    }

    public sealed class NestedCommandWithSiblingsRequest
    {
        public string Command { get; set; } = string.Empty;
        public int? Timeout { get; set; }
        public int Retries { get; set; }
    }

    public sealed class NestedLongRequest
    {
        public long Limit { get; set; }
    }

    public sealed class NestedTimeoutsRequest
    {
        public int[] Timeouts { get; set; } = [];
    }

    public sealed class BatchJobItem
    {
        public string Name { get; set; } = string.Empty;
        public int[] Timeouts { get; set; } = [];
    }

    private sealed class CustomSchemaFunction : AIFunction
    {
        private readonly string _name;
        private readonly JsonElement _schema;
        private readonly Func<AIFunctionArguments, object?> _handler;

        public CustomSchemaFunction(string name, string schemaJson, Func<AIFunctionArguments, object?>? handler = null)
        {
            _name = name;
            using var doc = JsonDocument.Parse(schemaJson);
            _schema = doc.RootElement.Clone();
            _handler = handler ?? (args => args);
        }

        public override string Name => _name;
        public override string Description => _name;
        public override JsonElement JsonSchema => _schema;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(_handler(arguments));
    }

    private sealed class CustomToStringNumber(string text)
    {
        public override string ToString() => text;
    }

    private sealed class FakePipelineChatClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>>? _getResponse;
        private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>? _getStreamingResponse;

        public FakePipelineChatClient(
            Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>>? getResponse = null,
            Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>? getStreamingResponse = null)
        {
            _getResponse = getResponse;
            _getStreamingResponse = getStreamingResponse;
        }

        public ChatClientMetadata Metadata => new("FakePipelineChatClient");
        public void Dispose() { }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => _getResponse != null
                ? _getResponse(chatMessages, options, cancellationToken)
                : throw new NotImplementedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => _getStreamingResponse != null
                ? _getStreamingResponse(chatMessages, options, cancellationToken)
                : throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }

    private static LenientToolDispatcher CreateDispatcherWithRunCommand(out AIFunction runCommandTool)
    {
        var dispatcher = new LenientToolDispatcher();
        runCommandTool = AIFunctionFactory.Create(
            (string command, int? timeout = null) => $"{command}:{timeout}",
            "run_command");
        dispatcher.RegisterTool(runCommandTool);
        return dispatcher;
    }

    [Theory]
    [InlineData("240.0", 240)]
    [InlineData(" 60.00 ", 60)]
    [InlineData("-5.0", -5)]
    [InlineData("#42.0", 42)]
    [InlineData("1e2", 100)]
    public void NormalizeCall_ScalarIntegerParameter_CoercesIntegralFloatStringsToInt(string rawTimeout, int expected)
    {
        var dispatcher = CreateDispatcherWithRunCommand(out _);

        var dispatched = dispatcher.NormalizeCall("run_command", new Dictionary<string, object?>
        {
            ["command"] = "dotnet test",
            ["timeout"] = rawTimeout
        });

        var coerced = Assert.IsType<int>(dispatched.Arguments["timeout"]);
        Assert.Equal(expected, coerced);
    }

    [Fact]
    public async Task NormalizeCall_ScalarIntegerParameter_CoercesNumericFloatDecimalAndJsonElementToInt()
    {
        var dispatcher = CreateDispatcherWithRunCommand(out var tool);

        // Unquoted JSON 240.0 via raw JSON string
        var fromJson = dispatcher.NormalizeCall("run_command", """{"command":"echo hi","timeout":240.0}""");
        Assert.Equal(240, Assert.IsType<int>(fromJson.Arguments["timeout"]));
        Assert.Equal("echo hi:240", (await tool.InvokeAsync(fromJson.Arguments))?.ToString());

        // JsonElement holding 240.0
        using var doc = JsonDocument.Parse("""{"command":"echo hi","timeout":240.0}""");
        var jsonArgs = new Dictionary<string, object?>
        {
            ["command"] = doc.RootElement.GetProperty("command").Clone(),
            ["timeout"] = doc.RootElement.GetProperty("timeout").Clone()
        };
        var fromJsonElement = dispatcher.NormalizeCall("run_command", jsonArgs);
        Assert.Equal(240, Assert.IsType<int>(fromJsonElement.Arguments["timeout"]));
        Assert.Equal("echo hi:240", (await tool.InvokeAsync(fromJsonElement.Arguments))?.ToString());

        // CLR double, float, decimal
        foreach (object numericValue in new object[] { 240.0d, 240.0f, 240.0m })
        {
            var dispatched = dispatcher.NormalizeCall("run_command", new Dictionary<string, object?>
            {
                ["command"] = "echo hi",
                ["timeout"] = numericValue
            });
            Assert.Equal(240, Assert.IsType<int>(dispatched.Arguments["timeout"]));
        }
    }

    [Fact]
    public void NormalizeCall_ScalarIntegerParameter_CoercesLargeIntegralValuesToLong()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.RegisterTool(AIFunctionFactory.Create((long limit) => limit, "set_limit"));

        foreach (object value in new object[] { "3000000000.0", 3000000000.0d, 3000000000.0m })
        {
            var dispatched = dispatcher.NormalizeCall("set_limit", new Dictionary<string, object?>
            {
                ["limit"] = value
            });

            var coerced = Assert.IsType<long>(dispatched.Arguments["limit"]);
            Assert.Equal(3000000000L, coerced);
        }
    }

    [Theory]
    [InlineData("240.5")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e30")]
    [InlineData("9223372036854775808.0")]
    public void NormalizeCall_ScalarIntegerParameter_RejectsInvalidOrOutOfRangeStrings(string invalidTimeout)
    {
        var dispatcher = CreateDispatcherWithRunCommand(out _);

        var ex = Assert.Throws<ArgumentException>(() =>
            dispatcher.NormalizeCall("run_command", new Dictionary<string, object?>
            {
                ["command"] = "dotnet test",
                ["timeout"] = invalidTimeout
            }));

        Assert.Contains("timeout", ex.Message);
        Assert.Contains("valid integer", ex.Message);
    }

    [Fact]
    public void NormalizeCall_ScalarIntegerParameter_RejectsNonIntegralNonFiniteAndOverflowNumbers()
    {
        var dispatcher = CreateDispatcherWithRunCommand(out _);

        object[] invalidValues =
        [
            240.5d,
            240.5f,
            240.5m,
            double.NaN,
            double.PositiveInfinity,
            double.NegativeInfinity,
            float.NaN,
            float.PositiveInfinity,
            9.223372036854776E+18d,
            -1e25d,
            decimal.MaxValue
        ];

        foreach (var invalid in invalidValues)
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                dispatcher.NormalizeCall("run_command", new Dictionary<string, object?>
                {
                    ["command"] = "dotnet test",
                    ["timeout"] = invalid
                }));

            Assert.Contains("timeout", ex.Message);
            Assert.Contains("valid integer", ex.Message);
        }
    }

    [Fact]
    public void NormalizeCall_SchemaAllowingString_PreservesNonIntegralStringWithoutThrowing()
    {
        var dispatcher = new LenientToolDispatcher();

        dispatcher.RegisterTool(new CustomSchemaFunction(
            "union_tool",
            """
            {
              "type": "object",
              "properties": {
                "value": { "type": ["integer", "string"] }
              }
            }
            """));

        dispatcher.RegisterTool(new CustomSchemaFunction(
            "string_timeout_tool",
            """
            {
              "type": "object",
              "properties": {
                "timeout": { "type": "string" }
              }
            }
            """));

        var unionCall = dispatcher.NormalizeCall("union_tool", new Dictionary<string, object?>
        {
            ["value"] = "240.5"
        });
        Assert.Equal("240.5", Assert.IsType<string>(unionCall.Arguments["value"]));

        var unionIntegralCall = dispatcher.NormalizeCall("union_tool", new Dictionary<string, object?>
        {
            ["value"] = "240.0"
        });
        Assert.Equal(240, Assert.IsType<int>(unionIntegralCall.Arguments["value"]));

        var stringTimeoutCall = dispatcher.NormalizeCall("string_timeout_tool", new Dictionary<string, object?>
        {
            ["timeout"] = "240.5"
        });
        Assert.Equal("240.5", Assert.IsType<string>(stringTimeoutCall.Arguments["timeout"]));
    }

    [Fact]
    public async Task NormalizeCall_IntegerArrayParameter_CoercesStringFloatDecimalAndCommaSeparatedItems()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (int[] ids) => string.Join(",", ids),
            "process_ids");
        dispatcher.RegisterTool(tool);

        // String array items: ["1.0", "2.0"]
        var fromStringArray = dispatcher.NormalizeCall("process_ids", new Dictionary<string, object?>
        {
            ["ids"] = new List<object?> { "1.0", " 2.00 " }
        });
        var strArrItems = Assert.IsAssignableFrom<IList<object?>>(fromStringArray.Arguments["ids"]);
        Assert.Equal(1, Assert.IsType<int>(strArrItems[0]));
        Assert.Equal(2, Assert.IsType<int>(strArrItems[1]));
        Assert.Equal("1,2", (await tool.InvokeAsync(fromStringArray.Arguments))?.ToString());

        // Double array items: [1.0, 2.0] via JSON
        var fromJsonDoubleArray = dispatcher.NormalizeCall("process_ids", """{"ids":[1.0, 2.0]}""");
        var dblArrItems = Assert.IsAssignableFrom<IList<object?>>(fromJsonDoubleArray.Arguments["ids"]);
        Assert.Equal(1, Assert.IsType<int>(dblArrItems[0]));
        Assert.Equal(2, Assert.IsType<int>(dblArrItems[1]));
        Assert.Equal("1,2", (await tool.InvokeAsync(fromJsonDoubleArray.Arguments))?.ToString());

        // Comma-separated string: "1.0, 2.0"
        var fromCommaString = dispatcher.NormalizeCall("process_ids", new Dictionary<string, object?>
        {
            ["ids"] = "1.0, 2.0"
        });
        var commaItems = Assert.IsAssignableFrom<IList<object?>>(fromCommaString.Arguments["ids"]);
        Assert.Equal(1, Assert.IsType<int>(commaItems[0]));
        Assert.Equal(2, Assert.IsType<int>(commaItems[1]));
        Assert.Equal("1,2", (await tool.InvokeAsync(fromCommaString.Arguments))?.ToString());

        // CLR float and decimal array items
        var fromFloatDecimalArray = dispatcher.NormalizeCall("process_ids", new Dictionary<string, object?>
        {
            ["ids"] = new List<object?> { 1.0f, 2.0m }
        });
        var fdItems = Assert.IsAssignableFrom<IList<object?>>(fromFloatDecimalArray.Arguments["ids"]);
        Assert.Equal(1, Assert.IsType<int>(fdItems[0]));
        Assert.Equal(2, Assert.IsType<int>(fdItems[1]));

        // Reject double overflow 2^63 (9.223372036854776E+18) and fractional float/decimal in integer[]
        foreach (object badItem in new object[] { 9.223372036854776E+18d, 1.5f, 2.5m, "1.5" })
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                dispatcher.NormalizeCall("process_ids", new Dictionary<string, object?>
                {
                    ["ids"] = new List<object?> { 1, badItem }
                }));
            Assert.Contains("ids", ex.Message);
            Assert.Contains("valid integer", ex.Message);
        }
    }

    [Fact]
    public void NormalizeCall_SchemaLessNumericParameterName_CoercesIntegralToIntAndFractionalToDouble()
    {
        var dispatcher = new LenientToolDispatcher();

        var integralCall = dispatcher.NormalizeCall("unregistered_shell", new Dictionary<string, object?>
        {
            ["command"] = "echo hi",
            ["timeout"] = "240.0"
        });
        Assert.Equal(240, Assert.IsType<int>(integralCall.Arguments["timeout"]));

        var fractionalCall = dispatcher.NormalizeCall("unregistered_shell", new Dictionary<string, object?>
        {
            ["command"] = "echo hi",
            ["timeout"] = "240.5"
        });
        Assert.Equal(240.5d, Assert.IsType<double>(fractionalCall.Arguments["timeout"]));
    }

    [Fact]
    public void ToolParameterComputationContext_TryParseIntAndTryParseLong_SupportIntegralFloatStringsAndRejectFractional()
    {
        Assert.True(ToolParameterComputationContext.TryParseInt("10.0", out var i1));
        Assert.Equal(10, i1);

        Assert.True(ToolParameterComputationContext.TryParseInt("#50.0", out var i2));
        Assert.Equal(50, i2);

        Assert.True(ToolParameterComputationContext.TryParseInt("1e2", out var i3));
        Assert.Equal(100, i3);

        Assert.True(ToolParameterComputationContext.TryParseInt(new CustomToStringNumber("42.0"), out var i4));
        Assert.Equal(42, i4);

        Assert.True(ToolParameterComputationContext.TryParseLong("3000000000.0", out var l1));
        Assert.Equal(3000000000L, l1);

        Assert.True(ToolParameterComputationContext.TryParseLong("#50.0", out var l2));
        Assert.Equal(50L, l2);

        Assert.True(ToolParameterComputationContext.TryParseLong("1e2", out var l3));
        Assert.Equal(100L, l3);

        // Reject fractional values (both string and floating-point CLR types) and 2^63 double overflow
        Assert.False(ToolParameterComputationContext.TryParseInt("10.5", out _));
        Assert.False(ToolParameterComputationContext.TryParseInt(10.5d, out _));
        Assert.False(ToolParameterComputationContext.TryParseInt(10.5f, out _));
        Assert.False(ToolParameterComputationContext.TryParseInt(10.5m, out _));
        Assert.False(ToolParameterComputationContext.TryParseInt(9.223372036854776E+18d, out _));

        Assert.False(ToolParameterComputationContext.TryParseLong("10.5", out _));
        Assert.False(ToolParameterComputationContext.TryParseLong(10.5d, out _));
        Assert.False(ToolParameterComputationContext.TryParseLong(10.5f, out _));
        Assert.False(ToolParameterComputationContext.TryParseLong(10.5m, out _));
        Assert.False(ToolParameterComputationContext.TryParseLong(9.223372036854776E+18d, out _));
        Assert.False(ToolParameterComputationContext.TryParseLong("9223372036854775808.0", out _));
    }

    [Fact]
    public void MapEndLineToLimit_WithIntegralFloatStrings_ComputesOffsetAndLimit()
    {
        var mapping = new ToolMapping();
        mapping.MapEndLineToLimit("offset", "limit", ["end_line"]);

        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["read_file"] = mapping.AddParameterAliases("offset", "start_line")
            }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string path, int offset = 1, int limit = 100) => $"{path}:{offset}:{limit}",
            "read_file"));

        var dispatched = dispatcher.NormalizeCall("read_file", new Dictionary<string, object?>
        {
            ["path"] = "Program.cs",
            ["start_line"] = "10.0",
            ["end_line"] = "50.0"
        });

        Assert.Equal(10, Assert.IsType<int>(dispatched.Arguments["offset"]));
        Assert.Equal(41, Assert.IsType<int>(dispatched.Arguments["limit"]));
    }

    [Fact]
    public async Task NormalizeCall_NestedObjectWithStringifiedAndNumericIntegralFloat_CoercesToInt()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (NestedCommandRequest request) => $"{request.Command}:{request.Timeout}",
            "exec_nested");
        dispatcher.RegisterTool(tool);

        using var doc = JsonDocument.Parse("""
            {
              "request": {
                "command": "echo hi",
                "timeout": "240.0"
              }
            }
            """);

        var call1 = dispatcher.NormalizeCall("exec_nested", new Dictionary<string, object?>
        {
            ["request"] = doc.RootElement.GetProperty("request").Clone()
        });
        var dict1 = Assert.IsAssignableFrom<IDictionary<string, object?>>(call1.Arguments["request"]);
        Assert.Equal(240, Assert.IsType<int>(dict1["Timeout"]));
        Assert.Equal("echo hi:240", (await tool.InvokeAsync(call1.Arguments))?.ToString());

        var call2 = dispatcher.NormalizeCall("exec_nested", """{"request":{"command":"echo hi","timeout":240.0}}""");
        var dict2 = Assert.IsAssignableFrom<IDictionary<string, object?>>(call2.Arguments["request"]);
        Assert.Equal(240, Assert.IsType<int>(dict2["Timeout"]));
        Assert.Equal("echo hi:240", (await tool.InvokeAsync(call2.Arguments))?.ToString());
    }

    [Theory]
    [InlineData("""{"command":"echo hi","timeout":"240.0"}""")]
    [InlineData("""{"command":"echo hi","timeout":240.0}""")]
    public async Task LenientToolDispatchChatClient_EndToEnd_NonStreamingAndStreaming_InvokesRealAIFunctionCleanly(string argsJson)
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            ([Description("Shell command")] string command, [Description("Timeout seconds")] int? timeout = null) =>
                $"{command}:{timeout}",
            "run_command");

        using var doc = JsonDocument.Parse(argsJson);
        Dictionary<string, object?> CreateRawArguments() => new()
        {
            ["command"] = doc.RootElement.GetProperty("command").Clone(),
            ["timeout"] = doc.RootElement.GetProperty("timeout").Clone()
        };

        async IAsyncEnumerable<ChatResponseUpdate> GenerateUpdates()
        {
            yield return new ChatResponseUpdate
            {
                Contents = [new FunctionCallContent("call_stream", "run_command", CreateRawArguments())]
            };
        }

        var innerClient = new FakePipelineChatClient(
            getResponse: (_, _, _) => Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call_resp", "run_command", CreateRawArguments())]))),
            getStreamingResponse: (_, _, _) => GenerateUpdates());

        var pipeline = new ChatClientBuilder(innerClient)
            .UseLenientToolDispatch(dispatcher)
            .Build();

        var options = new ChatOptions { Tools = [tool] };

        // Non-streaming path
        var response = await pipeline.GetResponseAsync([new ChatMessage(ChatRole.User, "run")], options);
        var respCall = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Single();
        var respArgs = Assert.IsType<AIFunctionArguments>(respCall.Arguments);
        Assert.Equal(240, Assert.IsType<int>(respArgs["timeout"]));
        var respResult = await tool.InvokeAsync(respArgs);
        Assert.Equal("echo hi:240", respResult?.ToString());

        // Streaming path
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in pipeline.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "run")], options))
        {
            updates.Add(update);
        }
        var streamCall = updates.SelectMany(u => u.Contents).OfType<FunctionCallContent>().Single();
        var streamArgs = Assert.IsType<AIFunctionArguments>(streamCall.Arguments);
        Assert.Equal(240, Assert.IsType<int>(streamArgs["timeout"]));
        var streamResult = await tool.InvokeAsync(streamArgs);
        Assert.Equal("echo hi:240", streamResult?.ToString());
    }

    [Theory]
    [InlineData("240.5", 241)]
    [InlineData("240.4", 240)]
    [InlineData("-240.5", -241)]
    [InlineData("#42.6", 43)]
    public async Task AllowIntegerRounding_ScalarStrings_RoundsConfiguredParameterAndLeavesOthersStrict(
        string rawTimeout,
        int expectedTimeout)
    {
        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["run_command"] = new ToolMapping().AllowIntegerRounding("timeout")
            }
        });

        var tool = AIFunctionFactory.Create(
            (string command, int? timeout = null, int retries = 0) => $"{command}:{timeout}:{retries}",
            "run_command");
        dispatcher.RegisterTool(tool);

        var dispatched = dispatcher.NormalizeCall("run_command", new Dictionary<string, object?>
        {
            ["command"] = "dotnet test",
            ["timeout"] = rawTimeout,
            ["retries"] = 1
        });

        Assert.Equal(expectedTimeout, Assert.IsType<int>(dispatched.Arguments["timeout"]));
        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal($"dotnet test:{expectedTimeout}:1", result?.ToString());

        // Non-configured integer parameter on the same tool remains strict
        var ex = Assert.Throws<ArgumentException>(() =>
            dispatcher.NormalizeCall("run_command", new Dictionary<string, object?>
            {
                ["command"] = "dotnet test",
                ["timeout"] = rawTimeout,
                ["retries"] = "2.5"
            }));
        Assert.Contains("retries", ex.Message);
        Assert.Contains("valid integer", ex.Message);
    }

    [Fact]
    public void AllowIntegerRounding_ScalarNumericTypesAndLargeLong_RoundsConfiguredParameter()
    {
        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["run_command"] = new ToolMapping().AllowIntegerRounding("timeout"),
                ["set_budget"] = new ToolMapping().AllowIntegerRounding("limit")
            }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string command, int? timeout = null) => $"{command}:{timeout}",
            "run_command"));
        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (long limit) => limit,
            "set_budget"));

        foreach (object numericTimeout in new object[] { 240.5d, 240.5f, 240.5m })
        {
            var dispatched = dispatcher.NormalizeCall("run_command", new Dictionary<string, object?>
            {
                ["command"] = "echo hi",
                ["timeout"] = numericTimeout
            });
            Assert.Equal(241, Assert.IsType<int>(dispatched.Arguments["timeout"]));
        }

        var longCall = dispatcher.NormalizeCall("set_budget", new Dictionary<string, object?>
        {
            ["limit"] = "3000000000.5"
        });
        Assert.Equal(3000000001L, Assert.IsType<long>(longCall.Arguments["limit"]));
    }

    [Fact]
    public void AllowIntegerRounding_StillRejectsNonFiniteAndOverflowValues()
    {
        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["run_command"] = new ToolMapping().AllowIntegerRounding("timeout")
            }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string command, int? timeout = null) => $"{command}:{timeout}",
            "run_command"));

        object[] invalidValues =
        [
            "NaN",
            "Infinity",
            "1e30",
            double.NaN,
            double.PositiveInfinity,
            9.223372036854776E+18d
        ];

        foreach (var invalid in invalidValues)
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                dispatcher.NormalizeCall("run_command", new Dictionary<string, object?>
                {
                    ["command"] = "echo hi",
                    ["timeout"] = invalid
                }));
            Assert.Contains("timeout", ex.Message);
            Assert.Contains("valid integer", ex.Message);
        }
    }

    [Fact]
    public async Task AllowIntegerRounding_ArrayParameters_RoundsStringDoubleAndCommaSeparatedItems()
    {
        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["schedule"] = new ToolMapping().AllowIntegerRounding("timeouts")
            }
        });

        var tool = AIFunctionFactory.Create(
            (int[] timeouts) => string.Join(",", timeouts),
            "schedule");
        dispatcher.RegisterTool(tool);

        var fromStringList = dispatcher.NormalizeCall("schedule", new Dictionary<string, object?>
        {
            ["timeouts"] = new List<object?> { "1.4", "2.5" }
        });
        Assert.Equal("1,3", (await tool.InvokeAsync(fromStringList.Arguments))?.ToString());

        var fromJsonDoubles = dispatcher.NormalizeCall("schedule", """{"timeouts":[1.4, 2.5]}""");
        Assert.Equal("1,3", (await tool.InvokeAsync(fromJsonDoubles.Arguments))?.ToString());

        var fromCommaString = dispatcher.NormalizeCall("schedule", new Dictionary<string, object?>
        {
            ["timeouts"] = "1.4, 2.5"
        });
        Assert.Equal("1,3", (await tool.InvokeAsync(fromCommaString.Arguments))?.ToString());
    }

    [Fact]
    public async Task AllowIntegerRounding_NestedObjectPath_RoundsTargetPropertyOnly()
    {
        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["exec_nested"] = new ToolMapping().AllowIntegerRounding("request.timeout")
            }
        });

        var tool = AIFunctionFactory.Create(
            (NestedCommandWithSiblingsRequest request, int? timeout = null) =>
                $"{request.Command}:{request.Timeout}:{request.Retries}:{timeout}",
            "exec_nested");
        dispatcher.RegisterTool(tool);

        var callString = dispatcher.NormalizeCall("exec_nested", """{"request":{"command":"echo hi","timeout":"240.5","retries":2}}""");
        var dict1 = Assert.IsAssignableFrom<IDictionary<string, object?>>(callString.Arguments["request"]);
        Assert.Equal(241, Assert.IsType<int>(dict1["Timeout"]));
        Assert.Equal("echo hi:241:2:", (await tool.InvokeAsync(callString.Arguments))?.ToString());

        var callDouble = dispatcher.NormalizeCall("exec_nested", """{"request":{"command":"echo hi","timeout":240.5,"retries":2}}""");
        var dict2 = Assert.IsAssignableFrom<IDictionary<string, object?>>(callDouble.Arguments["request"]);
        Assert.Equal(241, Assert.IsType<int>(dict2["Timeout"]));
        Assert.Equal("echo hi:241:2:", (await tool.InvokeAsync(callDouble.Arguments))?.ToString());

        // Sibling integer property on the same nested object remains strict
        var siblingEx = Assert.Throws<ArgumentException>(() =>
            dispatcher.NormalizeCall("exec_nested", """{"request":{"command":"echo hi","timeout":240.5,"retries":1.5}}"""));
        Assert.Contains("Retries", siblingEx.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("valid integer", siblingEx.Message);

        // Top-level parameter with the same leaf name remains strict
        var topLevelEx = Assert.Throws<ArgumentException>(() =>
            dispatcher.NormalizeCall("exec_nested", """{"request":{"command":"echo hi","timeout":240.5,"retries":2},"timeout":30.5}"""));
        Assert.Contains("timeout", topLevelEx.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("valid integer", topLevelEx.Message);
    }

    [Fact]
    public void AllowIntegerRounding_ConfigurationSnapshotAndClone_IsolatesMutations()
    {
        var mapping = new ToolMapping();
        var config = new ToolDispatcherConfiguration
        {
            Tools = { ["run_command"] = mapping }
        };

        var dispatcher = new LenientToolDispatcher(config);
        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string command, int? timeout = null) => $"{command}:{timeout}",
            "run_command"));

        // Mutating original mapping after dispatcher snapshot must not enable rounding on dispatcher
        mapping.AllowIntegerRounding("timeout");

        Assert.Throws<ArgumentException>(() =>
            dispatcher.NormalizeCall("run_command", new Dictionary<string, object?>
            {
                ["command"] = "echo hi",
                ["timeout"] = "240.5"
            }));

        // Cloned mapping also carries RoundedIntegerParameters independently
        var clonedMapping = mapping.Clone();
        clonedMapping.RoundedIntegerParameters.Clear();
        Assert.True(mapping.ShouldRoundIntegerParameter(string.Empty, "timeout"));
        Assert.False(clonedMapping.ShouldRoundIntegerParameter(string.Empty, "timeout"));
    }

    [Theory]
    [InlineData("1e-29")]
    [InlineData("1e-30")]
    [InlineData("-1e-30")]
    [InlineData("1e-500")]
    [InlineData("1e-9999999999999999999999")]
    [InlineData("0.00000000000000000000000000001")]
    [InlineData("1.00000000000000000000000000001")]
    public void NormalizeCall_StrictMode_RejectsTinyOrUnderflowingFractionalStrings(string nonIntegral)
    {
        var dispatcher = CreateDispatcherWithRunCommand(out _);

        var ex = Assert.Throws<ArgumentException>(() =>
            dispatcher.NormalizeCall("run_command", new Dictionary<string, object?>
            {
                ["command"] = "echo hi",
                ["timeout"] = nonIntegral
            }));
        Assert.Contains("timeout", ex.Message);
        Assert.Contains("valid integer", ex.Message);

        Assert.False(ToolParameterComputationContext.TryParseInt(nonIntegral, out _));
        Assert.False(ToolParameterComputationContext.TryParseLong(nonIntegral, out _));
    }

    [Theory]
    [InlineData("0e-30", 0)]
    [InlineData("-0.0e-500", 0)]
    [InlineData("1.5e2", 150)]
    [InlineData("1.50e1", 15)]
    [InlineData("1000e-3", 1)]
    [InlineData("1.00000000000000000000000000000", 1)]
    public void NormalizeCall_StrictMode_AcceptsExactScientificAndZeroIntegralStrings(string exactIntegral, int expected)
    {
        var dispatcher = CreateDispatcherWithRunCommand(out _);

        var dispatched = dispatcher.NormalizeCall("run_command", new Dictionary<string, object?>
        {
            ["command"] = "echo hi",
            ["timeout"] = exactIntegral
        });

        Assert.Equal(expected, Assert.IsType<int>(dispatched.Arguments["timeout"]));
        Assert.True(ToolParameterComputationContext.TryParseInt(exactIntegral, out var parsedInt));
        Assert.Equal(expected, parsedInt);
    }

    [Fact]
    public void AllowIntegerRounding_TinyFractionalUnderflow_RoundsToZero()
    {
        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["run_command"] = new ToolMapping().AllowIntegerRounding("timeout")
            }
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string command, int? timeout = null) => $"{command}:{timeout}",
            "run_command"));

        var dispatched = dispatcher.NormalizeCall("run_command", new Dictionary<string, object?>
        {
            ["command"] = "echo hi",
            ["timeout"] = "1e-30"
        });
        Assert.Equal(0, Assert.IsType<int>(dispatched.Arguments["timeout"]));
    }

    [Fact]
    public async Task JsonElementNumbers_Above53BitsAndSubnormal_PreservePrecisionAndStrictness()
    {
        var dispatcher = new LenientToolDispatcher();
        var scalarTool = AIFunctionFactory.Create((long limit) => limit.ToString(CultureInfo.InvariantCulture), "set_limit");
        var nestedTool = AIFunctionFactory.Create((NestedLongRequest request) => request.Limit.ToString(CultureInfo.InvariantCulture), "set_nested_limit");
        var arrayTool = AIFunctionFactory.Create((long[] limits) => string.Join(",", limits), "set_limits");
        dispatcher.RegisterTools([scalarTool, nestedTool, arrayTool]);

        const long p53Plus1 = 9007199254740993L;
        using var doc = JsonDocument.Parse("""
            {
              "scalar": 9007199254740993.0,
              "maxLong": 9223372036854775807.0,
              "nested": { "limit": 9007199254740993.0 },
              "arr": [9007199254740993.0, 9223372036854775807.0],
              "subnormal": 1e-400
            }
            """);

        // Top-level JsonElement
        var scalarCall = dispatcher.NormalizeCall("set_limit", new Dictionary<string, object?>
        {
            ["limit"] = doc.RootElement.GetProperty("scalar").Clone()
        });
        Assert.Equal(p53Plus1, Assert.IsType<long>(scalarCall.Arguments["limit"]));
        Assert.Equal("9007199254740993", (await scalarTool.InvokeAsync(scalarCall.Arguments))?.ToString());

        var maxCall = dispatcher.NormalizeCall("set_limit", new Dictionary<string, object?>
        {
            ["limit"] = doc.RootElement.GetProperty("maxLong").Clone()
        });
        Assert.Equal(long.MaxValue, Assert.IsType<long>(maxCall.Arguments["limit"]));

        // Nested JsonElement object
        var nestedCall = dispatcher.NormalizeCall("set_nested_limit", new Dictionary<string, object?>
        {
            ["request"] = doc.RootElement.GetProperty("nested").Clone()
        });
        var nestedDict = Assert.IsAssignableFrom<IDictionary<string, object?>>(nestedCall.Arguments["request"]);
        Assert.Equal(p53Plus1, Assert.IsType<long>(nestedDict["Limit"]));
        Assert.Equal("9007199254740993", (await nestedTool.InvokeAsync(nestedCall.Arguments))?.ToString());

        // Array JsonElement
        var arrayCall = dispatcher.NormalizeCall("set_limits", new Dictionary<string, object?>
        {
            ["limits"] = doc.RootElement.GetProperty("arr").Clone()
        });
        var arrayItems = Assert.IsAssignableFrom<IList<object?>>(arrayCall.Arguments["limits"]);
        Assert.Equal(p53Plus1, Assert.IsType<long>(arrayItems[0]));
        Assert.Equal(long.MaxValue, Assert.IsType<long>(arrayItems[1]));
        Assert.Equal("9007199254740993,9223372036854775807", (await arrayTool.InvokeAsync(arrayCall.Arguments))?.ToString());

        // Stringified JSON object
        var stringifiedObjCall = dispatcher.NormalizeCall("set_nested_limit", new Dictionary<string, object?>
        {
            ["request"] = """{"limit":9007199254740993.0}"""
        });
        var strNestedDict = Assert.IsAssignableFrom<IDictionary<string, object?>>(stringifiedObjCall.Arguments["request"]);
        Assert.Equal(p53Plus1, Assert.IsType<long>(strNestedDict["Limit"]));
        Assert.Equal("9007199254740993", (await nestedTool.InvokeAsync(stringifiedObjCall.Arguments))?.ToString());

        // Stringified JSON array
        var stringifiedCall = dispatcher.NormalizeCall("set_limits", new Dictionary<string, object?>
        {
            ["limits"] = "[9007199254740993.0]"
        });
        var strArrayItems = Assert.IsAssignableFrom<IList<object?>>(stringifiedCall.Arguments["limits"]);
        Assert.Equal(p53Plus1, Assert.IsType<long>(strArrayItems[0]));

        // Subnormal 1e-400 JsonElement rejected in strict mode
        var subnormalEl = doc.RootElement.GetProperty("subnormal").Clone();
        Assert.Throws<ArgumentException>(() =>
            dispatcher.NormalizeCall("set_limit", new Dictionary<string, object?> { ["limit"] = subnormalEl }));
        Assert.False(ToolParameterComputationContext.TryParseLong(subnormalEl, out _));
        Assert.False(ToolParameterComputationContext.TryParseInt(subnormalEl, out _));

        // Direct computation context checks for 53-bit+ JsonElement
        Assert.True(ToolParameterComputationContext.TryParseLong(doc.RootElement.GetProperty("scalar").Clone(), out var parsedP53));
        Assert.Equal(p53Plus1, parsedP53);
        Assert.True(ToolParameterComputationContext.TryParseLong(doc.RootElement.GetProperty("maxLong").Clone(), out var parsedMax));
        Assert.Equal(long.MaxValue, parsedMax);

        // Subnormal 1e-400 JsonElement rounds to 0 when AllowIntegerRounding is enabled
        var roundingDispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools = { ["set_limit"] = new ToolMapping().AllowIntegerRounding("limit") }
        });
        roundingDispatcher.RegisterTool(scalarTool);
        var roundedSubnormal = roundingDispatcher.NormalizeCall("set_limit", new Dictionary<string, object?>
        {
            ["limit"] = subnormalEl
        });
        Assert.Equal(0, Assert.IsType<int>(roundedSubnormal.Arguments["limit"]));
    }

    [Fact]
    public void ClrIntegralTypes_SbyteUshortUintUlong_CoerceCorrectlyAndRejectOverflow()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.RegisterTool(AIFunctionFactory.Create((long[] values) => values.Length, "consume_longs"));

        var validCall = dispatcher.NormalizeCall("consume_longs", new Dictionary<string, object?>
        {
            ["values"] = new List<object?> { (sbyte)-5, (ushort)12, (uint)42, 3000000000UL }
        });
        var items = Assert.IsAssignableFrom<IList<object?>>(validCall.Arguments["values"]);
        Assert.Equal(-5, Assert.IsType<int>(items[0]));
        Assert.Equal(12, Assert.IsType<int>(items[1]));
        Assert.Equal(42, Assert.IsType<int>(items[2]));
        Assert.Equal(3000000000L, Assert.IsType<long>(items[3]));

        Assert.True(ToolParameterComputationContext.TryParseInt((sbyte)-5, out var i1));
        Assert.Equal(-5, i1);
        Assert.True(ToolParameterComputationContext.TryParseInt((ushort)12, out var i2));
        Assert.Equal(12, i2);
        Assert.True(ToolParameterComputationContext.TryParseLong(3000000000UL, out var l1));
        Assert.Equal(3000000000L, l1);

        Assert.False(ToolParameterComputationContext.TryParseLong(ulong.MaxValue, out _));
        Assert.False(ToolParameterComputationContext.TryParseInt((uint)int.MaxValue + 1U, out _));

        Assert.Throws<ArgumentException>(() =>
            dispatcher.NormalizeCall("consume_longs", new Dictionary<string, object?>
            {
                ["values"] = new List<object?> { ulong.MaxValue }
            }));
    }

    [Theory]
    [InlineData("request.timeouts")]
    [InlineData("request.timeouts[]")]
    public async Task AllowIntegerRounding_NestedArrayPath_RoundsAllArrayFormats(string configuredPath)
    {
        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["exec_timeouts"] = new ToolMapping().AllowIntegerRounding(configuredPath)
            }
        });
        var tool = AIFunctionFactory.Create(
            (NestedTimeoutsRequest request) => string.Join(",", request.Timeouts),
            "exec_timeouts");
        dispatcher.RegisterTool(tool);

        var fromComma = dispatcher.NormalizeCall("exec_timeouts", """{"request":{"timeouts":"1.4, 2.5"}}""");
        Assert.Equal("1,3", (await tool.InvokeAsync(fromComma.Arguments))?.ToString());

        var fromArray = dispatcher.NormalizeCall("exec_timeouts", """{"request":{"timeouts":[1.4, 2.5]}}""");
        Assert.Equal("1,3", (await tool.InvokeAsync(fromArray.Arguments))?.ToString());

        var fromStringifiedJson = dispatcher.NormalizeCall("exec_timeouts", """{"request":{"timeouts":"[1.4, 2.5]"}}""");
        Assert.Equal("1,3", (await tool.InvokeAsync(fromStringifiedJson.Arguments))?.ToString());
    }

    [Fact]
    public async Task AllowIntegerRounding_PropagatedCollectionArray_RoundsStringifiedItems()
    {
        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["run_jobs"] = new ToolMapping().AllowIntegerRounding("jobs[].timeouts")
            }
        });
        var tool = AIFunctionFactory.Create(
            (BatchJobItem[] jobs) => string.Join(";", jobs.Select(j => $"{j.Name}:{string.Join(",", j.Timeouts)}")),
            "run_jobs");
        dispatcher.RegisterTool(tool);

        var dispatched = dispatcher.NormalizeCall("run_jobs", """{"jobs":[{"name":"a"}],"timeouts":"[1.4, 2.5]"}""");
        Assert.Equal("a:1,3", (await tool.InvokeAsync(dispatched.Arguments))?.ToString());
    }

    [Fact]
    public void StringifiedJsonIntegerArray_AcceptsIntegralAndThrowsArgumentExceptionOnFractional()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.RegisterTool(AIFunctionFactory.Create((int[] ids) => ids.Length, "process_ids"));

        var valid = dispatcher.NormalizeCall("process_ids", new Dictionary<string, object?>
        {
            ["ids"] = "[1.0, 2.0]"
        });
        var items = Assert.IsAssignableFrom<IList<object?>>(valid.Arguments["ids"]);
        Assert.Equal(1, Assert.IsType<int>(items[0]));
        Assert.Equal(2, Assert.IsType<int>(items[1]));

        var ex = Assert.Throws<ArgumentException>(() =>
            dispatcher.NormalizeCall("process_ids", new Dictionary<string, object?>
            {
                ["ids"] = "[1.5]"
            }));
        Assert.Contains("ids", ex.Message);
        Assert.Contains("valid integer", ex.Message);
    }

    [Fact]
    public void ErrorMessages_FormatNumbersWithInvariantCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            var dispatcher = new LenientToolDispatcher();
            dispatcher.RegisterTool(AIFunctionFactory.Create((int timeout) => timeout, "run_scalar"));
            dispatcher.RegisterTool(AIFunctionFactory.Create((int[] timeouts) => timeouts.Length, "run_array"));

            var scalarEx = Assert.Throws<ArgumentException>(() =>
                dispatcher.NormalizeCall("run_scalar", new Dictionary<string, object?>
                {
                    ["timeout"] = 240.5d
                }));
            Assert.Contains("240.5", scalarEx.Message);
            Assert.DoesNotContain("240,5", scalarEx.Message);

            var arrayEx = Assert.Throws<ArgumentException>(() =>
                dispatcher.NormalizeCall("run_array", new Dictionary<string, object?>
                {
                    ["timeouts"] = new List<object?> { 240.5d }
                }));
            Assert.Contains("240.5", arrayEx.Message);
            Assert.DoesNotContain("240,5", arrayEx.Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void AllowIntegerRounding_SchemaLessTool_RoundsBothStringsAndNumbers()
    {
        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["unregistered_tool"] = new ToolMapping().AllowIntegerRounding("timeout", "custom_wait")
            }
        });

        var callFromString = dispatcher.NormalizeCall("unregistered_tool", new Dictionary<string, object?>
        {
            ["timeout"] = "240.5",
            ["custom_wait"] = "12.6"
        });
        Assert.Equal(241, Assert.IsType<int>(callFromString.Arguments["timeout"]));
        Assert.Equal(13, Assert.IsType<int>(callFromString.Arguments["custom_wait"]));

        var callFromDouble = dispatcher.NormalizeCall("unregistered_tool", new Dictionary<string, object?>
        {
            ["timeout"] = 240.5d,
            ["custom_wait"] = 12.6d
        });
        Assert.Equal(241, Assert.IsType<int>(callFromDouble.Arguments["timeout"]));
        Assert.Equal(13, Assert.IsType<int>(callFromDouble.Arguments["custom_wait"]));
    }

    [Fact]
    public void ComputationContextAndRangeToLimit_HonorAllowIntegerRounding()
    {
        var mapping = new ToolMapping()
            .AllowIntegerRounding("timeout", "big_limit", "end_line", "offset")
            .MapEndLineToLimit("offset", "limit", ["end_line"])
            .AddParameterAliases("offset", "start_line");

        var ctx = new ToolParameterComputationContext(new Dictionary<string, object?>
        {
            ["timeout"] = "10.6",
            ["strict_count"] = "10.6",
            ["big_limit"] = "3000000000.6",
            ["strict_long"] = "3000000000.6"
        }, mapping);

        Assert.Equal(11, ctx.GetInt("timeout"));
        Assert.Equal(-1, ctx.GetInt("strict_count", defaultValue: -1));
        Assert.Equal(3000000001L, ctx.GetLong("big_limit"));
        Assert.Equal(-1L, ctx.GetLong("strict_long", defaultValue: -1L));

        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools = { ["read_file"] = mapping }
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string path, int offset = 1, int limit = 100) => $"{path}:{offset}:{limit}",
            "read_file"));

        var dispatched = dispatcher.NormalizeCall("read_file", new Dictionary<string, object?>
        {
            ["path"] = "Program.cs",
            ["start_line"] = "10.4",
            ["end_line"] = "50.6"
        });
        Assert.Equal(10, Assert.IsType<int>(dispatched.Arguments["offset"]));
        Assert.Equal(42, Assert.IsType<int>(dispatched.Arguments["limit"]));
    }
}
