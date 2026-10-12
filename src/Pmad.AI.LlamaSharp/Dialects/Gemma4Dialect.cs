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

    /// <inheritdoc />
    public override string Name => "gemma4";

    /// <inheritdoc />
    public override IReadOnlyList<string> StopTokens { get; } = ["<turn|>"];

    /// <inheritdoc />
    public override void RenderSystemMessage(StringBuilder sb, string content)
    {
        sb.Append($"<|turn|>system\n{content}<turn|>\n");
    }

    /// <inheritdoc />
    public override void RenderUserMessage(StringBuilder sb, string content)
    {
        sb.Append($"<|turn|>user\n{content}<turn|>\n");
    }

    /// <inheritdoc />
    public override void RenderAssistantMessage(StringBuilder sb, string? text, IReadOnlyList<FunctionCallContent>? toolCalls)
    {
        sb.Append("<|turn|>model");
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

        sb.Append("<turn|>\n");
    }

    /// <inheritdoc />
    public override void RenderToolMessage(StringBuilder sb, string toolContent, bool isFirstInGroup, bool isLastInGroup)
    {
        if (isFirstInGroup)
        {
            sb.Append("<|turn|>user");
        }

        sb.Append($"\n<tool_response>\n{toolContent}\n</tool_response>");

        if (isLastInGroup)
        {
            sb.Append("<turn|>\n");
        }
    }

    /// <inheritdoc />
    public override void RenderAssistantPrompt(StringBuilder sb)
    {
        sb.Append("<|turn|>model\n");
    }
}
