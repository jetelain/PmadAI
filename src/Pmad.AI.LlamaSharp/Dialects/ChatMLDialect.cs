using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Pmad.AI.LlamaSharp;

/// <summary>
/// ChatML template dialect (used by Qwen, Hermes, and standard ChatML fine-tunes).
/// Uses <c>&lt;|im_start|&gt;role\ncontent&lt;|im_end|&gt;</c>.
/// </summary>
public class ChatMLDialect : ChatDialect
{
    /// <summary>
    /// Singleton instance of <see cref="ChatMLDialect"/> (supports reasoning toggle; reasoning effort disabled by default).
    /// </summary>
    public static ChatMLDialect Instance { get; } = new(supportsReasoning: true, supportsReasoningEffort: false);

    /// <summary>
    /// Initializes a new instance of <see cref="ChatMLDialect"/>.
    /// </summary>
    public ChatMLDialect(bool supportsReasoning = true, bool supportsReasoningEffort = false)
    {
        SupportsReasoning = supportsReasoning;
        SupportsReasoningEffort = supportsReasoningEffort;
    }

    /// <inheritdoc />
    public override string Name => "chatml";

    /// <inheritdoc />
    public override bool SupportsReasoning { get; }

    /// <inheritdoc />
    public override bool SupportsReasoningEffort { get; }

    /// <inheritdoc />
    public override IReadOnlyList<string> StopTokens { get; } = ["<|im_end|>"];

    /// <inheritdoc />
    public override string? GetReasoningInstructions(string? reasoningEffort)
    {
        if (string.IsNullOrWhiteSpace(reasoningEffort)) return null;

        var normalized = reasoningEffort.Trim().ToLowerInvariant();
        return normalized switch
        {
            "xhigh" or "high" => "Reasoning effort is set to xhigh. Please think carefully through the task, validate key assumptions, consider plausible alternatives, and prioritize correctness, consistency, and clarity in the final answer.",
            "low" => "Reasoning effort is set to low. Keep your thinking brief and focused, moving directly to the conclusion without unnecessary elaboration.",
            "medium" or "none" or "off" => null,
            _ => $"Reasoning effort is set to {reasoningEffort}. Please think through the task accordingly."
        };
    }

    /// <inheritdoc />
    public override void RenderSystemMessage(StringBuilder sb, string content)
    {
        sb.Append($"<|im_start|>system\n{content}<|im_end|>\n");
    }

    /// <inheritdoc />
    public override void RenderUserMessage(StringBuilder sb, string content)
    {
        sb.Append($"<|im_start|>user\n{content}<|im_end|>\n");
    }

    /// <inheritdoc />
    public override void RenderAssistantMessage(StringBuilder sb, string? text, IReadOnlyList<FunctionCallContent>? toolCalls)
    {
        RenderAssistantMessage(sb, text, toolCalls, reasoning: null);
    }

    /// <inheritdoc />
    public override void RenderAssistantMessage(StringBuilder sb, string? text, IReadOnlyList<FunctionCallContent>? toolCalls, string? reasoning)
    {
        sb.Append("<|im_start|>assistant");
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
                var argsJson = call.Arguments != null ? JsonSerializer.Serialize(call.Arguments) : "{}";
                sb.Append($"\n<tool_call>\n{{\"name\": \"{call.Name}\", \"arguments\": {argsJson}}}\n</tool_call>");
            }
        }

        sb.Append("<|im_end|>\n");
    }

    /// <inheritdoc />
    public override void RenderToolMessage(StringBuilder sb, string toolContent, bool isFirstInGroup, bool isLastInGroup)
    {
        if (isFirstInGroup)
        {
            sb.Append("<|im_start|>user");
        }

        sb.Append($"\n<tool_response>\n{toolContent}\n</tool_response>");

        if (isLastInGroup)
        {
            sb.Append("<|im_end|>\n");
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
        sb.Append("<|im_start|>assistant\n");
        if (enableReasoning == false)
        {
            sb.Append("<think>\n\n</think>\n\n");
        }
        else if (enableReasoning == true)
        {
            sb.Append("<think>\n");
        }
    }
}
