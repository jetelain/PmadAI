using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Pmad.AI.LlamaSharp;

/// <summary>
/// Google Gemma 4 / Gemma template dialect.
/// Uses <c>&lt;|turn|&gt;role\ncontent&lt;turn|&gt;</c>.
/// </summary>
public class Gemma4Dialect : ChatDialect
{
    /// <summary>
    /// Singleton instance of <see cref="Gemma4Dialect"/>.
    /// </summary>
    public static Gemma4Dialect Instance { get; } = new();

    /// <summary>
    /// Initializes a new instance of <see cref="Gemma4Dialect"/>.
    /// </summary>
    public Gemma4Dialect(bool supportsReasoning = true)
    {
        SupportsReasoning = supportsReasoning;
    }

    /// <inheritdoc />
    public override string Name => "gemma4";

    /// <inheritdoc />
    public override bool SupportsReasoning { get; }

    /// <inheritdoc />
    public override bool SupportsReasoningEffort => false;

    /// <inheritdoc />
    public override IReadOnlyList<string> StopTokens { get; } = ["<turn|>", "<end_of_turn>"];

    /// <inheritdoc />
    public override void RenderSystemMessage(StringBuilder sb, string content)
    {
        sb.Append("<|turn|>system\n").Append(content).Append("<turn|>\n");
    }

    /// <inheritdoc />
    public override void RenderUserMessage(StringBuilder sb, string content)
    {
        sb.Append("<|turn|>user\n").Append(content).Append("<turn|>\n");
    }

    /// <inheritdoc />
    public override void RenderAssistantMessage(StringBuilder sb, string? text, IReadOnlyList<FunctionCallContent>? toolCalls)
    {
        RenderAssistantMessage(sb, text, toolCalls, reasoning: null);
    }

    /// <inheritdoc />
    public override void RenderAssistantMessage(StringBuilder sb, string? text, IReadOnlyList<FunctionCallContent>? toolCalls, string? reasoning)
    {
        sb.Append("<|turn|>model");
        if (!string.IsNullOrWhiteSpace(reasoning))
        {
            sb.Append("\n<think>\n").Append(reasoning.Trim()).Append("\n</think>\n");
        }
        if (!string.IsNullOrWhiteSpace(text))
        {
            sb.Append('\n').Append(text);
        }

        if (toolCalls is { Count: > 0 })
        {
            foreach (var call in toolCalls)
            {
                var argsJson = AotJsonUtilities.SerializeArguments(call.Arguments);
                sb.Append("\n<tool_call>\n{\"name\": \"").Append(call.Name).Append("\", \"arguments\": ").Append(argsJson).Append("}\n</tool_call>");
            }
        }

        sb.Append("<turn|>\n");
    }

    /// <inheritdoc />
    public override void RenderToolMessage(StringBuilder sb, string toolContent, bool isFirstInGroup, bool isLastInGroup)
    {
        if (isFirstInGroup)
        {
            sb.Append("<|turn|>user");
        }

        sb.Append("\n<tool_response>\n").Append(toolContent).Append("\n</tool_response>");

        if (isLastInGroup)
        {
            sb.Append("<turn|>\n");
        }
    }

    /// <inheritdoc />
    public override void RenderAssistantPrompt(StringBuilder sb)
    {
        RenderAssistantPrompt(sb, enableReasoning: null);
    }

    /// <inheritdoc />
    public override void RenderAssistantPrompt(StringBuilder sb, bool? enableReasoning)
    {
        sb.Append("<|turn|>model\n");
        if (enableReasoning == false)
        {
            sb.Append("<think>\n\n</think>\n\n");
        }
    }
}
