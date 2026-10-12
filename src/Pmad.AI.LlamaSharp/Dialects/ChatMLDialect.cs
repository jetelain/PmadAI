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
    /// Singleton instance of <see cref="ChatMLDialect"/>.
    /// </summary>
    public static ChatMLDialect Instance { get; } = new();

    /// <inheritdoc />
    public override string Name => "chatml";

    /// <inheritdoc />
    public override IReadOnlyList<string> StopTokens { get; } = ["<|im_end|>"];

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
        sb.Append("<|im_start|>assistant");
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
        sb.Append("<|im_start|>assistant\n");
    }
}
