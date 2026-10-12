using System.ComponentModel;
using Microsoft.Extensions.AI;
using Pmad.AI.LlamaSharp;

namespace Pmad.AI.LlamaSharp.Tests;

public class ChatDialectTests
{
    [Fact]
    public void ChatDialect_StaticSingletons_AreConfiguredCorrectly()
    {
        Assert.NotNull(ChatDialect.ChatML);
        Assert.NotNull(ChatDialect.Phi3);
        Assert.NotNull(ChatDialect.Gemma4);
        Assert.Same(ChatDialect.ChatML, ChatDialect.Default);

        Assert.Equal("chatml", ChatDialect.ChatML.Name);
        Assert.Equal("phi3", ChatDialect.Phi3.Name);
        Assert.Equal("gemma4", ChatDialect.Gemma4.Name);

        Assert.Contains("<|im_end|>", ChatDialect.ChatML.StopTokens);
        Assert.Contains("<|end|>", ChatDialect.Phi3.StopTokens);
        Assert.Contains("<turn|>", ChatDialect.Gemma4.StopTokens);
    }

    [Theory]
    [InlineData("chatml", "chatml")]
    [InlineData("CHATML", "chatml")]
    [InlineData("phi3", "phi3")]
    [InlineData("phi-3", "phi3")]
    [InlineData("PHI", "phi3")]
    [InlineData("gemma4", "gemma4")]
    [InlineData("gemma-4", "gemma4")]
    [InlineData("GEMMA", "gemma4")]
    [InlineData(null, "chatml")]
    [InlineData("", "chatml")]
    [InlineData("   ", "chatml")]
    [InlineData("unknown-dialect", "chatml")]
    public void FromName_ResolvesExpectedDialect(string? input, string expectedName)
    {
        var dialect = ChatDialect.FromName(input);
        Assert.NotNull(dialect);
        Assert.Equal(expectedName, dialect.Name);

        var viaFormatter = LocalModelChatFormatter.GetDialect(input);
        Assert.Same(dialect, viaFormatter);
    }

    [Fact]
    public void ChatMLDialect_FormatPrompt_RendersChatMLTags()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "System text"),
            new(ChatRole.User, "User text"),
            new(ChatRole.Assistant, "Assistant text")
        };

        var prompt = ChatDialect.ChatML.FormatPrompt(messages);

        Assert.Contains("<|im_start|>system\nSystem text<|im_end|>\n", prompt);
        Assert.Contains("<|im_start|>user\nUser text<|im_end|>\n", prompt);
        Assert.Contains("<|im_start|>assistant\nAssistant text<|im_end|>\n", prompt);
        Assert.EndsWith("<|im_start|>assistant\n", prompt);
    }

    [Fact]
    public void Phi3Dialect_FormatPrompt_RendersPhi3Tags()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "System text"),
            new(ChatRole.User, "User text"),
            new(ChatRole.Assistant, "Assistant text")
        };

        var prompt = ChatDialect.Phi3.FormatPrompt(messages);

        Assert.Contains("<|system|>\nSystem text<|end|>\n", prompt);
        Assert.Contains("<|user|>\nUser text<|end|>\n", prompt);
        Assert.Contains("<|assistant|>\nAssistant text<|end|>\n", prompt);
        Assert.EndsWith("<|assistant|>\n", prompt);
    }

    [Fact]
    public void Gemma4Dialect_FormatPrompt_RendersGemma4Tags()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "System text"),
            new(ChatRole.User, "User text"),
            new(ChatRole.Assistant, "Assistant text")
        };

        var prompt = ChatDialect.Gemma4.FormatPrompt(messages);

        Assert.Contains("<|turn|>system\nSystem text<turn|>\n", prompt);
        Assert.Contains("<|turn|>user\nUser text<turn|>\n", prompt);
        Assert.Contains("<|turn|>model\nAssistant text<turn|>\n", prompt);
        Assert.EndsWith("<|turn|>model\n", prompt);
    }

    [Fact]
    public void FormatPrompt_WithDialectInstance_ProducesEquivalentResultToFormatName()
    {
        var fn = AIFunctionFactory.Create(
            ([Description("Item query")] string item) => $"Found {item}",
            "search_catalog",
            "Search inventory");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "System prompt"),
            new(ChatRole.User, "Search shoes"),
            new(ChatRole.Assistant, [new FunctionCallContent("call1", "search_catalog", new Dictionary<string, object?> { ["item"] = "shoes" })]),
            new(ChatRole.Tool, [new FunctionResultContent("call1", "Found 5 shoes")]),
            new(ChatRole.Assistant, "Here are shoes")
        };

        foreach (var format in new[] { "chatml", "phi3", "gemma4" })
        {
            var dialect = ChatDialect.FromName(format);
            var promptViaName = LocalModelChatFormatter.FormatPrompt(messages, format: format, tools: [fn]);
            var promptViaDialect = LocalModelChatFormatter.FormatPrompt(messages, dialect: dialect, tools: [fn]);
            var promptDirectFromDialect = dialect.FormatPrompt(messages, tools: [fn]);

            Assert.Equal(promptViaName, promptViaDialect);
            Assert.Equal(promptViaName, promptDirectFromDialect);
        }
    }

    [Fact]
    public void StripStopTokens_RemovesDialectSpecificTokens()
    {
        var rawChatml = "Some response text<|im_end|>";
        Assert.Equal("Some response text", ChatDialect.ChatML.StripStopTokens(rawChatml));

        var rawPhi3 = "Some response text<|end|>";
        Assert.Equal("Some response text", ChatDialect.Phi3.StripStopTokens(rawPhi3));

        var rawGemma = "Some response text<turn|>";
        Assert.Equal("Some response text", ChatDialect.Gemma4.StripStopTokens(rawGemma));
    }

    private sealed class CustomTestDialect : ChatDialect
    {
        public override string Name => "custom";

        public override IReadOnlyList<string> StopTokens { get; } = ["[STOP]"];

        public override void RenderSystemMessage(System.Text.StringBuilder sb, string content)
        {
            sb.Append($"### SYSTEM:\n{content}\n");
        }

        public override void RenderUserMessage(System.Text.StringBuilder sb, string content)
        {
            sb.Append($"### USER:\n{content}\n");
        }

        public override void RenderAssistantMessage(System.Text.StringBuilder sb, string? text, IReadOnlyList<FunctionCallContent>? toolCalls)
        {
            sb.Append($"### ASSISTANT:\n{text}\n");
        }

        public override void RenderToolMessage(System.Text.StringBuilder sb, string toolContent, bool isFirstInGroup, bool isLastInGroup)
        {
            sb.Append($"### TOOL:\n{toolContent}\n");
        }

        public override void RenderAssistantPrompt(System.Text.StringBuilder sb)
        {
            sb.Append("### ASSISTANT:\n");
        }
    }

    [Fact]
    public void CustomDialect_CanExtendChatDialectAndFormatCorrectly()
    {
        var custom = new CustomTestDialect();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "Custom system instructions."),
            new(ChatRole.User, "Custom user query.")
        };

        var prompt = LocalModelChatFormatter.FormatPrompt(messages, custom);

        Assert.Contains("### SYSTEM:\nCustom system instructions.\n", prompt);
        Assert.Contains("### USER:\nCustom user query.\n", prompt);
        Assert.EndsWith("### ASSISTANT:\n", prompt);
    }

    [Fact]
    public void LlamaSharpOptions_Dialect_CanBeSet()
    {
        var options = new LlamaSharpOptions
        {
            Dialect = ChatDialect.Phi3
        };

        Assert.Same(ChatDialect.Phi3, options.Dialect);
    }

    [Fact]
    public void ChatDialect_ReasoningCapabilities_ReturnExpectedValues()
    {
        Assert.True(ChatDialect.ChatML.SupportsReasoning);
        Assert.False(ChatDialect.ChatML.SupportsReasoningEffort);

        Assert.True(ChatDialect.Qwen38.SupportsReasoning);
        Assert.True(ChatDialect.Qwen38.SupportsReasoningEffort);

        Assert.True(ChatDialect.Gemma4.SupportsReasoning);
        Assert.False(ChatDialect.Gemma4.SupportsReasoningEffort);

        Assert.False(ChatDialect.Phi3.SupportsReasoning);
        Assert.False(ChatDialect.Phi3.SupportsReasoningEffort);
    }

    [Fact]
    public void ChatDialect_FromName_ResolvesQwen38Correctly()
    {
        Assert.Same(ChatDialect.Qwen38, ChatDialect.FromName("qwen3.8"));
        Assert.Same(ChatDialect.Qwen38, ChatDialect.FromName("qwen-3.8"));
        Assert.Same(ChatDialect.Qwen38, ChatDialect.FromName("qwen38"));
    }

    [Fact]
    public void ChatMLDialect_FormatPrompt_WithReasoningDisabled_PrefillsClosedThinkBlock()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "What is 2+2?")
        };

        var prompt = ChatDialect.ChatML.FormatPrompt(messages, enableReasoning: false);

        Assert.EndsWith("<|im_start|>assistant\n<think>\n\n</think>\n\n", prompt);
    }

    [Fact]
    public void ChatMLDialect_FormatPrompt_WithReasoningEnabled_PrefillsOpenThinkBlock()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "What is 2+2?")
        };

        var prompt = ChatDialect.ChatML.FormatPrompt(messages, enableReasoning: true);

        Assert.EndsWith("<|im_start|>assistant\n<think>\n", prompt);
    }

    [Fact]
    public void ChatMLDialect_FormatPrompt_WithReasoningDefault_OutputsStandardAssistantPrompt()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "What is 2+2?")
        };

        var prompt = ChatDialect.ChatML.FormatPrompt(messages, enableReasoning: null);

        Assert.EndsWith("<|im_start|>assistant\n", prompt);
    }

    [Theory]
    [InlineData("xhigh", "Reasoning effort is set to xhigh.")]
    [InlineData("high", "Reasoning effort is set to xhigh.")]
    [InlineData("low", "Reasoning effort is set to low.")]
    public void Qwen38Dialect_FormatPrompt_WithReasoningEffort_InjectsInstructionIntoSystemPrompt(string effort, string expectedInstruction)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "Be accurate."),
            new(ChatRole.User, "Solve problem.")
        };

        var prompt = ChatDialect.Qwen38.FormatPrompt(messages, reasoningEffort: effort);

        Assert.Contains(expectedInstruction, prompt);
        Assert.Contains("Be accurate.", prompt);
    }

    [Fact]
    public void ChatMLDialect_Standard_DoesNotInjectReasoningEffort()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "Be accurate."),
            new(ChatRole.User, "Solve problem.")
        };

        // Standard ChatML (Qwen 3.5) does not inject effort instructions
        var prompt = ChatDialect.ChatML.FormatPrompt(messages, reasoningEffort: "high");

        Assert.DoesNotContain("Reasoning effort is set to", prompt);
        Assert.Contains("Be accurate.", prompt);
    }

    [Fact]
    public void ChatMLDialect_FormatPrompt_WithMediumEffort_DoesNotInjectExtraInstruction()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "Be accurate."),
            new(ChatRole.User, "Solve problem.")
        };

        var prompt = ChatDialect.ChatML.FormatPrompt(messages, reasoningEffort: "medium");

        Assert.DoesNotContain("Reasoning effort is set to", prompt);
        Assert.Contains("Be accurate.", prompt);
    }

    [Fact]
    public void Qwen38Dialect_FormatPrompt_WithReasoningEffortAndTools_InjectsInstructionBeforeTools()
    {
        var fn = AIFunctionFactory.Create(() => "ok", "ping", "Ping test");
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Test tools")
        };

        var prompt = ChatDialect.Qwen38.FormatPrompt(messages, tools: [fn], reasoningEffort: "low");

        Assert.Contains("Reasoning effort is set to low.", prompt);
        Assert.Contains("<tools>", prompt);
        int effortIndex = prompt.IndexOf("Reasoning effort is set to low.");
        int toolsIndex = prompt.IndexOf("<tools>");
        Assert.True(effortIndex < toolsIndex, "Reasoning effort instructions should precede tools definition block.");
    }

    [Fact]
    public void Qwen38Dialect_FormatPrompt_WithReasoningEffortWithoutSystemMessage_CreatesSystemBlock()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Solve riddle")
        };

        var prompt = ChatDialect.Qwen38.FormatPrompt(messages, reasoningEffort: "high");

        Assert.Contains("<|im_start|>system\nReasoning effort is set to xhigh.", prompt);
    }

    [Fact]
    public void ChatMLDialect_FormatPrompt_WithReasoningInHistory_PreservesThinkBlockInAssistantTurn()
    {
        var assistantMsg = new ChatMessage(ChatRole.Assistant, new List<AIContent>
        {
            new TextReasoningContent("Calculating 2 + 2 = 4"),
            new TextContent("The answer is 4.")
        });

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "What is 2+2?"),
            assistantMsg,
            new(ChatRole.User, "Now multiply by 3.")
        };

        var prompt = ChatDialect.ChatML.FormatPrompt(messages);

        Assert.Contains("<|im_start|>assistant\n<think>\nCalculating 2 + 2 = 4\n</think>\n\nThe answer is 4.<|im_end|>\n", prompt);
    }

    [Fact]
    public void Gemma4Dialect_FormatPrompt_WithReasoningDisabled_DoesNotPrefillThinkBlock()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Tell me a joke")
        };

        var prompt = ChatDialect.Gemma4.FormatPrompt(messages, enableReasoning: false);

        Assert.EndsWith("<|turn|>model\n", prompt);
        Assert.DoesNotContain("<think>", prompt);
    }

    [Fact]
    public void Gemma4Dialect_FormatPrompt_WithReasoningInHistory_RendersThoughtBlock()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "What is 2+2?"),
            new(ChatRole.Assistant, new List<AIContent>
            {
                new TextReasoningContent("2 + 2 = 4"),
                new TextContent("The answer is 4.")
            })
        };

        var prompt = ChatDialect.Gemma4.FormatPrompt(messages);
        Assert.Contains("<|turn|>model\n<thought>\n2 + 2 = 4\n</thought>\n\nThe answer is 4.<turn|>\n", prompt);
    }

    [Fact]
    public void Gemma4Dialect_FormatPrompt_DoesNotInjectReasoningEffortInstructions()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "System prompt"),
            new(ChatRole.User, "Hello")
        };

        var prompt = ChatDialect.Gemma4.FormatPrompt(messages, reasoningEffort: "high");

        Assert.DoesNotContain("Reasoning effort is set to", prompt);
    }

    [Fact]
    public void Phi3Dialect_FormatPrompt_IgnoresReasoningParameters()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello")
        };

        var prompt = ChatDialect.Phi3.FormatPrompt(messages, enableReasoning: false, reasoningEffort: "low");

        Assert.EndsWith("<|assistant|>\n", prompt);
        Assert.DoesNotContain("<think>", prompt);
        Assert.DoesNotContain("Reasoning effort is set to", prompt);
    }
}
