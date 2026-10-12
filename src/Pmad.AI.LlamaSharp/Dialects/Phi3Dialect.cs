using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Pmad.AI.LlamaSharp;

/// <summary>
/// Microsoft Phi-3 / Phi-3.5 template dialect.
/// Uses <c>&lt;|role|&gt;\ncontent&lt;|end|&gt;</c>.
/// </summary>
public class Phi3Dialect : ChatDialect
{
    /// <summary>
    /// Singleton instance of <see cref="Phi3Dialect"/>.
    /// </summary>
    public static Phi3Dialect Instance { get; } = new();

    /// <inheritdoc />
    public override string Name => "phi3";

    /// <inheritdoc />
    public override bool SupportsReasoning => false;

    /// <inheritdoc />
    public override bool SupportsReasoningEffort => false;

    /// <inheritdoc />
    public override IReadOnlyList<string> StopTokens { get; } = ["<|end|>"];

    /// <inheritdoc />
    public override void RenderSystemMessage(StringBuilder sb, string content)
    {
        sb.Append($"<|system|>\n{content}<|end|>\n");
    }

    /// <inheritdoc />
    public override void RenderUserMessage(StringBuilder sb, string content)
    {
        sb.Append($"<|user|>\n{content}<|end|>\n");
    }

    /// <inheritdoc />
    public override void RenderAssistantMessage(StringBuilder sb, string? text, IReadOnlyList<FunctionCallContent>? toolCalls)
    {
        sb.Append("<|assistant|>");
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

        sb.Append("<|end|>\n");
    }

    /// <inheritdoc />
    public override void RenderToolMessage(StringBuilder sb, string toolContent, bool isFirstInGroup, bool isLastInGroup)
    {
        if (isFirstInGroup)
        {
            sb.Append("<|user|>");
        }

        sb.Append($"\n<tool_response>\n{toolContent}\n</tool_response>");

        if (isLastInGroup)
        {
            sb.Append("<|end|>\n");
        }
    }

    /// <inheritdoc />
    public override void RenderAssistantPrompt(StringBuilder sb)
    {
        sb.Append("<|assistant|>\n");
    }
}
