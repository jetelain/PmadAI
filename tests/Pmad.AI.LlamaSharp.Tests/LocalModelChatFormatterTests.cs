using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Pmad.AI.LlamaSharp;

namespace Pmad.AI.LlamaSharp.Tests;

public class LocalModelChatFormatterTests
{
    [Fact]
    public void FormatPrompt_ChatML_FormatsSystemUserAssistantCorrectly()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a coding assistant."),
            new(ChatRole.User, "Write a hello world program."),
            new(ChatRole.Assistant, "Console.WriteLine(\"Hello, World!\");"),
            new(ChatRole.User, "Now in Python.")
        };

        var prompt = LocalModelChatFormatter.FormatPrompt(messages, format: "chatml");

        Assert.Contains("<|im_start|>system\nYou are a coding assistant.<|im_end|>\n", prompt);
        Assert.Contains("<|im_start|>user\nWrite a hello world program.<|im_end|>\n", prompt);
        Assert.Contains("<|im_start|>assistant\nConsole.WriteLine(\"Hello, World!\");<|im_end|>\n", prompt);
        Assert.Contains("<|im_start|>user\nNow in Python.<|im_end|>\n", prompt);
        Assert.EndsWith("<|im_start|>assistant\n", prompt);
    }

    [Fact]
    public void FormatPrompt_Phi3_FormatsExpectedDelimiters()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "Be concise."),
            new(ChatRole.User, "What is 2+2?"),
            new(ChatRole.Assistant, "4")
        };

        var prompt = LocalModelChatFormatter.FormatPrompt(messages, format: "phi3");

        Assert.Contains("<|system|>\nBe concise.<|end|>\n", prompt);
        Assert.Contains("<|user|>\nWhat is 2+2?<|end|>\n", prompt);
        Assert.Contains("<|assistant|>\n4<|end|>\n", prompt);
        Assert.EndsWith("<|assistant|>\n", prompt);
    }

    [Fact]
    public void FormatPrompt_Gemma4_FormatsExpectedDelimiters()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "Helpful AI"),
            new(ChatRole.User, "Hi"),
            new(ChatRole.Assistant, "Hello there!")
        };

        var prompt = LocalModelChatFormatter.FormatPrompt(messages, format: "gemma4");

        Assert.Contains("<|turn|>system\nHelpful AI<turn|>\n", prompt);
        Assert.Contains("<|turn|>user\nHi<turn|>\n", prompt);
        Assert.Contains("<|turn|>model\nHello there!<turn|>\n", prompt);
        Assert.EndsWith("<|turn|>model\n", prompt);
    }

    [Fact]
    public void FormatPrompt_WithTools_InjectsToolSignaturesAndInstructions()
    {
        var fn = AIFunctionFactory.Create(
            ([Description("The city to check")] string city) => $"Sunny in {city}",
            "get_weather",
            "Fetches current weather for a city");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "Custom system instructions."),
            new(ChatRole.User, "What's the weather in Paris?")
        };

        var prompt = LocalModelChatFormatter.FormatPrompt(messages, format: "chatml", tools: [fn]);

        Assert.Contains("<tools>", prompt);
        Assert.Contains("</tools>", prompt);
        Assert.Contains("\"name\":\"get_weather\"", prompt);
        Assert.Contains("\"description\":\"Fetches current weather for a city\"", prompt);
        Assert.Contains("<tool_call>", prompt);
        Assert.Contains("Custom system instructions.", prompt);
    }

    [Fact]
    public void FormatPrompt_WithTools_WithoutSystemMessage_UsesDefaultSystemInstructions()
    {
        var fn = AIFunctionFactory.Create(
            (int a, int b) => a + b,
            "add_numbers",
            "Adds two numbers");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Add 3 and 5")
        };

        var prompt = LocalModelChatFormatter.FormatPrompt(messages, format: "chatml", tools: [fn]);

        Assert.Contains("You are a helpful assistant.", prompt);
        Assert.Contains("<tools>", prompt);
        Assert.Contains("add_numbers", prompt);
    }

    [Fact]
    public void FormatPrompt_WithToolCallsInAssistantMessage_RendersToolCallBlocks()
    {
        var call = new FunctionCallContent("call_1", "get_stock_price", new Dictionary<string, object?> { ["symbol"] = "MSFT" });
        var assistantMsg = new ChatMessage(ChatRole.Assistant, [call]);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Check MSFT"),
            assistantMsg
        };

        var prompt = LocalModelChatFormatter.FormatPrompt(messages, format: "chatml");

        Assert.Contains("<tool_call>", prompt);
        Assert.Contains("\"name\": \"get_stock_price\"", prompt);
        Assert.Contains("\"symbol\":\"MSFT\"", prompt);
        Assert.Contains("</tool_call>", prompt);
    }

    [Fact]
    public void FormatPrompt_WithToolRoleMessages_FormatsToolResponse()
    {
        var resultContent = new FunctionResultContent("call_1", "420.50");
        var toolMsg = new ChatMessage(ChatRole.Tool, [resultContent]);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Check MSFT"),
            toolMsg
        };

        var promptChatml = LocalModelChatFormatter.FormatPrompt(messages, format: "chatml");
        Assert.Contains("<tool_response>\n420.50\n</tool_response>", promptChatml);
        Assert.Contains("<|im_start|>user\n<tool_response>", promptChatml);

        var promptPhi3 = LocalModelChatFormatter.FormatPrompt(messages, format: "phi3");
        Assert.Contains("<|user|>\n<tool_response>\n420.50\n</tool_response><|end|>\n", promptPhi3);

        var promptGemma4 = LocalModelChatFormatter.FormatPrompt(messages, format: "gemma4");
        Assert.Contains("<|turn|>user\n<tool_response>\n420.50\n</tool_response><turn|>\n", promptGemma4);
    }

    [Fact]
    public void ParseAssistantResponse_ExtractsThinkReasoningContent()
    {
        var raw = "<think>\nThinking about life, the universe, and everything.\n</think>\nThe answer is 42.";

        var parsed = LocalModelChatFormatter.ParseAssistantResponse(raw);

        var reasoning = parsed.Contents.OfType<TextReasoningContent>().FirstOrDefault();
        Assert.NotNull(reasoning);
        Assert.Equal("Thinking about life, the universe, and everything.", reasoning.Text);

        var textContent = parsed.Contents.OfType<TextContent>().FirstOrDefault();
        Assert.NotNull(textContent);
        Assert.Equal("The answer is 42.", textContent.Text);
    }

    [Fact]
    public void ParseAssistantResponse_ExtractsThoughtReasoningContent()
    {
        var raw = "<thought>Step 1: Calculate 5*5=25.</thought>25";

        var parsed = LocalModelChatFormatter.ParseAssistantResponse(raw);

        var reasoning = parsed.Contents.OfType<TextReasoningContent>().FirstOrDefault();
        Assert.NotNull(reasoning);
        Assert.Equal("Step 1: Calculate 5*5=25.", reasoning.Text);

        var textContent = parsed.Contents.OfType<TextContent>().FirstOrDefault();
        Assert.NotNull(textContent);
        Assert.Equal("25", textContent.Text);
    }

    [Fact]
    public void ParseAssistantResponse_ParsesToolCallXmlBlock()
    {
        var raw = "I will check the weather for you.\n<tool_call>\n{\"name\": \"get_weather\", \"arguments\": {\"location\": \"London\", \"unit\": \"celsius\"}}\n</tool_call>";

        var parsed = LocalModelChatFormatter.ParseAssistantResponse(raw);

        var textContent = parsed.Contents.OfType<TextContent>().FirstOrDefault();
        Assert.NotNull(textContent);
        Assert.Equal("I will check the weather for you.", textContent.Text);

        var call = parsed.Contents.OfType<FunctionCallContent>().FirstOrDefault();
        Assert.NotNull(call);
        Assert.Equal("get_weather", call.Name);
        Assert.NotNull(call.Arguments);
        Assert.Equal("London", call.Arguments["location"]?.ToString());
        Assert.Equal("celsius", call.Arguments["unit"]?.ToString());
    }

    [Fact]
    public void ParseAssistantResponse_ParsesMultipleToolCalls()
    {
        var raw = @"<tool_call>
{""name"": ""search"", ""arguments"": {""query"": ""dotnet 10""}}
</tool_call>
<tool_call>
{""name"": ""notify"", ""arguments"": {""message"": ""Searching...""}}
</tool_call>";

        var parsed = LocalModelChatFormatter.ParseAssistantResponse(raw);

        var calls = parsed.Contents.OfType<FunctionCallContent>().ToList();
        Assert.Equal(2, calls.Count);
        Assert.Equal("search", calls[0].Name);
        Assert.Equal("dotnet 10", calls[0].Arguments?["query"]?.ToString());
        Assert.Equal("notify", calls[1].Name);
        Assert.Equal("Searching...", calls[1].Arguments?["message"]?.ToString());
    }

    [Fact]
    public void ParseAssistantResponse_ParsesToolCodeXmlBlock_UsedByGemma()
    {
        var raw = @"<tool_code>
{""name"": ""get_weather"", ""arguments"": {""city"": ""Paris""}}
</tool_code>
<tool_code>
{""name"": ""get_weather"", ""arguments"": {""city"": ""Tokyo""}}
</tool_code>";

        var parsed = LocalModelChatFormatter.ParseAssistantResponse(raw);

        var calls = parsed.Contents.OfType<FunctionCallContent>().ToList();
        Assert.Equal(2, calls.Count);
        Assert.Equal("get_weather", calls[0].Name);
        Assert.Equal("Paris", calls[0].Arguments?["city"]?.ToString());
        Assert.Equal("get_weather", calls[1].Name);
        Assert.Equal("Tokyo", calls[1].Arguments?["city"]?.ToString());
    }

    [Fact]
    public void ParseAssistantResponse_ParsesGemmaNativeToolCallSyntax()
    {
        var raw = "<|tool_call>call:get_weather{city:<|\"|>Paris<|\"|>}<tool_call|><|tool_call>call:get_weather{city:<|\"|>Tokyo<|\"|>}<tool_call|>";

        var parsed = LocalModelChatFormatter.ParseAssistantResponse(raw);

        var calls = parsed.Contents.OfType<FunctionCallContent>().ToList();
        Assert.Equal(2, calls.Count);
        Assert.Equal("get_weather", calls[0].Name);
        Assert.Equal("Paris", calls[0].Arguments?["city"]?.ToString());
        Assert.Equal("get_weather", calls[1].Name);
        Assert.Equal("Tokyo", calls[1].Arguments?["city"]?.ToString());

        // Make sure no stray tokens remained as text
        var text = parsed.Contents.OfType<TextContent>().FirstOrDefault();
        Assert.True(text == null || string.IsNullOrWhiteSpace(text.Text));
    }

    [Fact]
    public void ParseAssistantResponse_ParsesRawCallSyntaxWithoutXmlTags()
    {
        var raw = "call:get_weather{city:<|\"|>London<|\"|>,unit:<|\"|>celsius<|\"|>}";

        var parsed = LocalModelChatFormatter.ParseAssistantResponse(raw);

        var call = parsed.Contents.OfType<FunctionCallContent>().FirstOrDefault();
        Assert.NotNull(call);
        Assert.Equal("get_weather", call.Name);
        Assert.Equal("London", call.Arguments?["city"]?.ToString());
        Assert.Equal("celsius", call.Arguments?["unit"]?.ToString());
    }

    [Fact]
    public void ParseAssistantResponse_FallbackJsonCodeBlock_ParsesFunctionCall()
    {
        var raw = "Here is the call:\n```json\n{\n  \"name\": \"calculate\",\n  \"arguments\": {\"expr\": \"10 * 10\"}\n}\n```";

        var parsed = LocalModelChatFormatter.ParseAssistantResponse(raw);

        var call = parsed.Contents.OfType<FunctionCallContent>().FirstOrDefault();
        Assert.NotNull(call);
        Assert.Equal("calculate", call.Name);
        Assert.Equal("10 * 10", call.Arguments?["expr"]?.ToString());

        var text = parsed.Contents.OfType<TextContent>().FirstOrDefault();
        Assert.NotNull(text);
        Assert.Equal("Here is the call:", text.Text);
    }

    [Fact]
    public void ParseAssistantResponse_FallbackToolCallCodeBlock_ParsesFunctionCall()
    {
        var raw = "```tool_call\n{\"name\": \"do_work\", \"arguments\": {\"item\": 1}}\n```";

        var parsed = LocalModelChatFormatter.ParseAssistantResponse(raw);

        var call = parsed.Contents.OfType<FunctionCallContent>().FirstOrDefault();
        Assert.NotNull(call);
        Assert.Equal("do_work", call.Name);
    }

    [Fact]
    public void ParseAssistantResponse_WithNormalizer_AppliesCustomTransformations()
    {
        var raw = "<tool_call>\n{\"name\": \"legacy_search_tool\", \"arguments\": {\"q\": \"llama\"}}\n</tool_call>";

        FunctionCallNormalizer normalizer = (name, args) =>
        {
            if (name == "legacy_search_tool")
            {
                var newArgs = new Dictionary<string, object?>(args)
                {
                    ["query"] = args["q"]
                };
                newArgs.Remove("q");
                return ("web_search", newArgs);
            }
            return null;
        };

        var parsed = LocalModelChatFormatter.ParseAssistantResponse(raw, normalizer);

        var call = parsed.Contents.OfType<FunctionCallContent>().FirstOrDefault();
        Assert.NotNull(call);
        Assert.Equal("web_search", call.Name);
        Assert.NotNull(call.Arguments);
        Assert.Equal("llama", call.Arguments["query"]?.ToString());
        Assert.False(call.Arguments.ContainsKey("q"));
    }

    [Fact]
    public void ParseAssistantResponse_WithNormalizerReturningNull_KeepsOriginal()
    {
        var raw = "<tool_call>\n{\"name\": \"unchanged_tool\", \"arguments\": {\"v\": 42}}\n</tool_call>";

        FunctionCallNormalizer normalizer = (name, args) => null;

        var parsed = LocalModelChatFormatter.ParseAssistantResponse(raw, normalizer);

        var call = parsed.Contents.OfType<FunctionCallContent>().FirstOrDefault();
        Assert.NotNull(call);
        Assert.Equal("unchanged_tool", call.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ParseAssistantResponse_EmptyOrWhitespace_ReturnsEmptyAssistantMessage(string? input)
    {
        var parsed = LocalModelChatFormatter.ParseAssistantResponse(input!);

        Assert.Equal(ChatRole.Assistant, parsed.Role);
        var text = parsed.Contents.OfType<TextContent>().FirstOrDefault();
        Assert.NotNull(text);
        Assert.Equal(string.Empty, text.Text);
    }

    [Fact]
    public void ParseAssistantResponse_StripsStrayTemplateTokens()
    {
        var raw = "Hello there!<|im_end|>";
        var parsed = LocalModelChatFormatter.ParseAssistantResponse(raw);

        var text = parsed.Contents.OfType<TextContent>().FirstOrDefault();
        Assert.NotNull(text);
        Assert.Equal("Hello there!", text.Text);
    }

    [Fact]
    public void TryParseFunctionCall_ParsesAlternativeSchemaFormats()
    {
        // 1. Function property with nested name/arguments
        var payload1 = "{\"function\": {\"name\": \"nested_fn\", \"arguments\": {\"a\": 1}}}";
        var call1 = LocalModelChatFormatter.TryParseFunctionCall(payload1);
        Assert.NotNull(call1);
        Assert.Equal("nested_fn", call1.Name);

        // 2. Parameters property instead of arguments
        var payload2 = "{\"name\": \"param_fn\", \"parameters\": {\"b\": 2}}";
        var call2 = LocalModelChatFormatter.TryParseFunctionCall(payload2);
        Assert.NotNull(call2);
        Assert.Equal("param_fn", call2.Name);

        // 3. String serialized arguments
        var payload3 = "{\"name\": \"string_args\", \"arguments\": \"{\\\"x\\\": \\\"test\\\"}\"}";
        var call3 = LocalModelChatFormatter.TryParseFunctionCall(payload3);
        Assert.NotNull(call3);
        Assert.Equal("string_args", call3.Name);
        Assert.Equal("test", call3.Arguments?["x"]?.ToString());

        // 4. Invalid json returns null
        var callInvalid = LocalModelChatFormatter.TryParseFunctionCall("{invalid json");
        Assert.Null(callInvalid);
    }

    [Fact]
    public void ParseAssistantResponse_WithNormalizerReturningReadOnlyDictionary_DoesNotThrowAndNormalizes()
    {
        var raw = "<tool_call>\n{\"name\": \"old_action\", \"arguments\": {\"param1\": 100}}\n</tool_call>";

        FunctionCallNormalizer normalizer = (name, args) =>
        {
            var dict = new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(
                new Dictionary<string, object?> { ["normalized_key"] = 200 });
            return ("new_action", dict);
        };

        var parsed = LocalModelChatFormatter.ParseAssistantResponse(raw, normalizer);

        var call = parsed.Contents.OfType<FunctionCallContent>().FirstOrDefault();
        Assert.NotNull(call);
        Assert.Equal("new_action", call.Name);
        Assert.Equal(200L, Convert.ToInt64(call.Arguments?["normalized_key"]));
    }

    [Fact]
    public void ParseAssistantResponse_WithMultipleReasoningBlocks_ExtractsAllReasoningContents()
    {
        var raw = "<think>First thought</think>Step 1 done.<thought>Second thought</thought>Step 2 done.";

        var parsed = LocalModelChatFormatter.ParseAssistantResponse(raw);

        var thoughts = parsed.Contents.OfType<TextReasoningContent>().ToList();
        Assert.Equal(2, thoughts.Count);
        Assert.Equal("First thought", thoughts[0].Text);
        Assert.Equal("Second thought", thoughts[1].Text);

        var text = parsed.Contents.OfType<TextContent>().FirstOrDefault();
        Assert.NotNull(text);
        Assert.Equal("Step 1 done.Step 2 done.", text.Text);
    }

    [Fact]
    public void ParseAssistantResponse_StripsEndOfTurnToken()
    {
        var raw = "Hello Gemma!<end_of_turn>";

        var parsed = LocalModelChatFormatter.ParseAssistantResponse(raw);

        var text = parsed.Contents.OfType<TextContent>().FirstOrDefault();
        Assert.NotNull(text);
        Assert.Equal("Hello Gemma!", text.Text);
    }

    [Fact]
    public void FormatPrompt_WithMultipleToolResultsInSingleMessage_FormatsAllResults()
    {
        var res1 = new FunctionResultContent("call_1", "Result 1");
        var res2 = new FunctionResultContent("call_2", "Result 2");
        var toolMsg = new ChatMessage(ChatRole.Tool, [res1, res2]);

        var prompt = LocalModelChatFormatter.FormatPrompt([toolMsg], format: "chatml");

        Assert.Contains("Result 1", prompt);
        Assert.Contains("Result 2", prompt);
    }

    [Fact]
    public void ParseAssistantResponse_PrefilledThinking_ExtractsReasoningAndText()
    {
        var raw = "Calculated 15 * 3 = 45.</think>\n\nThe product is 45.";

        var parsed = LocalModelChatFormatter.ParseAssistantResponse(raw);

        var reasoning = parsed.Contents.OfType<TextReasoningContent>().FirstOrDefault();
        var text = parsed.Contents.OfType<TextContent>().FirstOrDefault();

        Assert.NotNull(reasoning);
        Assert.Equal("Calculated 15 * 3 = 45.", reasoning.Text);
        Assert.NotNull(text);
        Assert.Equal("The product is 45.", text.Text);
    }

    [Fact]
    public void ParseAssistantResponse_UnclosedThinkingBlock_ExtractsReasoning()
    {
        var raw = "<think>Generating response and got cut off";

        var parsed = LocalModelChatFormatter.ParseAssistantResponse(raw);

        var reasoning = parsed.Contents.OfType<TextReasoningContent>().FirstOrDefault();

        Assert.NotNull(reasoning);
        Assert.Equal("Generating response and got cut off", reasoning.Text);
    }

    [Fact]
    public void ResolveReasoningParameters_FromChatOptionsReasoningEffort_ResolvesCorrectly()
    {
        var options = new ChatOptions
        {
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Low }
        };

        var (enable, effort) = LocalModelChatFormatter.ResolveReasoningParameters(options);

        Assert.True(enable);
        Assert.Equal("low", effort);
    }

    [Fact]
    public void ResolveReasoningParameters_FromChatOptionsReasoningEffortNone_DisablesReasoning()
    {
        var options = new ChatOptions
        {
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None }
        };

        var (enable, effort) = LocalModelChatFormatter.ResolveReasoningParameters(options);

        Assert.False(enable);
        Assert.Equal("none", effort);
    }

    [Fact]
    public void ResolveReasoningParameters_FromChatOptionsOutputNone_DisablesReasoning()
    {
        var options = new ChatOptions
        {
            Reasoning = new ReasoningOptions { Output = ReasoningOutput.None }
        };

        var (enable, _) = LocalModelChatFormatter.ResolveReasoningParameters(options);

        Assert.False(enable);
    }

    [Fact]
    public void ResolveReasoningParameters_FromAdditionalProperties_ResolvesFlags()
    {
        var options = new ChatOptions
        {
            AdditionalProperties = new()
            {
                ["enable_thinking"] = false,
                ["reasoning_effort"] = "high"
            }
        };

        var (enable, effort) = LocalModelChatFormatter.ResolveReasoningParameters(options);

        Assert.False(enable);
        Assert.Equal("high", effort);
    }

    [Fact]
    public void ResolveReasoningParameters_FallsBackToLlamaSharpOptions()
    {
        var llamaOptions = new LlamaSharpOptions
        {
            EnableReasoning = true,
            ReasoningEffort = "medium"
        };

        var (enable, effort) = LocalModelChatFormatter.ResolveReasoningParameters(null, llamaOptions);

        Assert.True(enable);
        Assert.Equal("medium", effort);
    }

    [Fact]
    public void FormatPrompt_WithChatOptions_AppliesReasoning()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello")
        };
        var options = new ChatOptions
        {
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.High }
        };

        var promptQwen38 = LocalModelChatFormatter.FormatPrompt(messages, "qwen3.8", options);
        Assert.Contains("Reasoning effort is set to xhigh.", promptQwen38);
        Assert.EndsWith("<|im_start|>assistant\n<think>\n", promptQwen38);

        // Standard ChatML (Qwen 3.5) supports reasoning prompt but ignores reasoning effort instruction
        var promptChatml = LocalModelChatFormatter.FormatPrompt(messages, "chatml", options);
        Assert.DoesNotContain("Reasoning effort is set to", promptChatml);
        Assert.EndsWith("<|im_start|>assistant\n<think>\n", promptChatml);
    }

    [Fact]
    public void ParseAssistantResponse_WithArrayOfToolCalls_ParsesAllCalls()
    {
        var raw = "<tool_call>[{\"name\":\"call_one\",\"arguments\":{\"a\":1}},{\"name\":\"call_two\",\"arguments\":{\"b\":2}}]</tool_call>";
        var msg = LocalModelChatFormatter.ParseAssistantResponse(raw);

        var calls = msg.Contents.OfType<FunctionCallContent>().ToList();
        Assert.Equal(2, calls.Count);
        Assert.Equal("call_one", calls[0].Name);
        Assert.Equal("1", calls[0].Arguments?["a"]?.ToString());
        Assert.Equal("call_two", calls[1].Name);
        Assert.Equal("2", calls[1].Arguments?["b"]?.ToString());
    }

    [Fact]
    public void ParseAssistantResponse_PreservesModelCallId()
    {
        var raw = "<tool_call>{\"id\":\"custom_id_123\",\"name\":\"do_something\",\"arguments\":{}}</tool_call>";
        var msg = LocalModelChatFormatter.ParseAssistantResponse(raw);

        var call = Assert.Single(msg.Contents.OfType<FunctionCallContent>());
        Assert.Equal("custom_id_123", call.CallId);
        Assert.Equal("do_something", call.Name);
    }

    [Fact]
    public void ParseAssistantResponse_WithNonJsonStringArguments_StoresUnderInputKey()
    {
        var raw = "<tool_call>{\"name\":\"raw_executor\",\"arguments\":\"SELECT * FROM table\"}</tool_call>";
        var msg = LocalModelChatFormatter.ParseAssistantResponse(raw);

        var call = Assert.Single(msg.Contents.OfType<FunctionCallContent>());
        Assert.Equal("raw_executor", call.Name);
        Assert.Equal("SELECT * FROM table", call.Arguments?["input"]);
    }

    [Fact]
    public void ParseAssistantResponse_WithTrailingCommas_RepairsAndParsesToolCall()
    {
        var raw = "<tool_call>{\"name\":\"fetch_weather\",\"arguments\":{\"city\":\"Rome\",},}</tool_call>";
        var msg = LocalModelChatFormatter.ParseAssistantResponse(raw);

        var call = Assert.Single(msg.Contents.OfType<FunctionCallContent>());
        Assert.Equal("fetch_weather", call.Name);
        Assert.Equal("Rome", call.Arguments?["city"]);
    }

    [Fact]
    public void ParseAssistantResponse_WithMarkdownFencesInArguments_StripsFencesAndParsesArguments()
    {
        var raw = "<tool_call>{\"name\":\"write_file\",\"arguments\":\"```json\\n{\\\"path\\\":\\\"app.cs\\\",\\\"lines\\\":10,}\\n```\"}</tool_call>";
        var msg = LocalModelChatFormatter.ParseAssistantResponse(raw);

        var call = Assert.Single(msg.Contents.OfType<FunctionCallContent>());
        Assert.Equal("write_file", call.Name);
        Assert.Equal("app.cs", call.Arguments?["path"]);
        Assert.Equal(10, call.Arguments?["lines"]);
    }

    [Fact]
    public void ParseAssistantResponse_WithReActActionSyntax_ExtractsToolCall()
    {
        var raw = "I will search now.\nAction: web_search\nAction Input: {\"query\":\"csharp 10\"}\nDone.";
        var msg = LocalModelChatFormatter.ParseAssistantResponse(raw);

        var call = Assert.Single(msg.Contents.OfType<FunctionCallContent>());
        Assert.Equal("web_search", call.Name);
        Assert.Equal("csharp 10", call.Arguments?["query"]);

        var text = msg.Contents.OfType<TextContent>().Single().Text;
        Assert.DoesNotContain("Action:", text);
        Assert.Contains("I will search now.", text);
    }
}
