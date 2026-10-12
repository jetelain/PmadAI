using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Pmad.AI.LlamaSharp;

/// <summary>
/// Base class representing a chat prompt template dialect for local Small Language Models (SLMs).
/// Encapsulates model-specific turn formatting, special tokens, and generation triggers.
/// </summary>
public abstract class ChatDialect
{
    /// <summary>
    /// Gets the canonical name/identifier of this chat dialect (e.g. "chatml", "phi3", "gemma4").
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Gets the stop sequences (anti-prompts) that signal the end of a generation turn for this dialect.
    /// </summary>
    public abstract IReadOnlyList<string> StopTokens { get; }

    /// <summary>
    /// Gets whether this dialect supports reasoning / chain-of-thought controls.
    /// </summary>
    public virtual bool SupportsReasoning => false;

    /// <summary>
    /// Gets whether this dialect supports reasoning effort parameter tuning.
    /// </summary>
    public virtual bool SupportsReasoningEffort => false;

    /// <summary>
    /// Predefined ChatML dialect instance.
    /// </summary>
    public static ChatDialect ChatML => ChatMLDialect.Instance;

    /// <summary>
    /// Predefined Phi-3 dialect instance.
    /// </summary>
    public static ChatDialect Phi3 => Phi3Dialect.Instance;

    /// <summary>
    /// Predefined Gemma 4 dialect instance.
    /// </summary>
    public static ChatDialect Gemma4 => Gemma4Dialect.Instance;

    /// <summary>
    /// Predefined Qwen 3.8 dialect instance (ChatML with reasoning effort tuning enabled).
    /// </summary>
    public static ChatDialect Qwen38 { get; } = new ChatMLDialect(supportsReasoning: true, supportsReasoningEffort: true);

    /// <summary>
    /// Default dialect used when none is specified (ChatML).
    /// </summary>
    public static ChatDialect Default => ChatML;

    /// <summary>
    /// Resolves a dialect instance by name, falling back to <see cref="ChatML"/> if unknown or null.
    /// </summary>
    public static ChatDialect FromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Default;
        }

        var trimmed = name.Trim();
        if (trimmed.Equals("phi3", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("phi-3", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("phi3.5", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("phi-3.5", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("phi", StringComparison.OrdinalIgnoreCase))
        {
            return Phi3;
        }

        if (trimmed.Equals("gemma4", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("gemma-4", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("gemma", StringComparison.OrdinalIgnoreCase))
        {
            return Gemma4;
        }

        if (trimmed.Equals("qwen3.8", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("qwen-3.8", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("qwen38", StringComparison.OrdinalIgnoreCase))
        {
            return Qwen38;
        }

        return ChatML;
    }

    /// <summary>
    /// Gets the reasoning instruction directive to inject for the specified effort level, or null if not applicable.
    /// </summary>
    public virtual string? GetReasoningInstructions(string? reasoningEffort) => null;

    /// <summary>
    /// Renders a system message turn.
    /// </summary>
    public abstract void RenderSystemMessage(StringBuilder sb, string content);

    /// <summary>
    /// Renders a user message turn.
    /// </summary>
    public abstract void RenderUserMessage(StringBuilder sb, string content);

    /// <summary>
    /// Renders an assistant message turn including text and function calls.
    /// </summary>
    public abstract void RenderAssistantMessage(StringBuilder sb, string? text, IReadOnlyList<FunctionCallContent>? toolCalls);

    /// <summary>
    /// Renders an assistant message turn including text, function calls, and optional reasoning content.
    /// </summary>
    public virtual void RenderAssistantMessage(StringBuilder sb, string? text, IReadOnlyList<FunctionCallContent>? toolCalls, string? reasoning)
    {
        RenderAssistantMessage(sb, text, toolCalls);
    }

    /// <summary>
    /// Renders a tool response message inside a tool response turn.
    /// </summary>
    public abstract void RenderToolMessage(StringBuilder sb, string toolContent, bool isFirstInGroup, bool isLastInGroup);

    /// <summary>
    /// Renders the assistant generation prefix that prompts the model to generate the next response.
    /// </summary>
    public abstract void RenderAssistantPrompt(StringBuilder sb);

    /// <summary>
    /// Renders the assistant generation prefix that prompts the model to generate the next response,
    /// optionally configuring reasoning / thinking generation.
    /// </summary>
    public virtual void RenderAssistantPrompt(StringBuilder sb, bool? enableReasoning)
    {
        RenderAssistantPrompt(sb);
    }

    private List<string>? _cachedStopTokensList;

    /// <summary>
    /// Gets the stop sequences as a cached <see cref="List{String}"/> to avoid per-turn allocations.
    /// </summary>
    internal List<string> CachedStopTokensList => _cachedStopTokensList ??= (StopTokens as List<string> ?? StopTokens.ToList());

    /// <summary>
    /// Removes any stop tokens associated with this dialect from the raw output string.
    /// </summary>
    public virtual string StripStopTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var stopToken in StopTokens)
        {
            if (text.Contains(stopToken, StringComparison.Ordinal))
            {
                text = text.Replace(stopToken, string.Empty);
            }
        }
        return text.Trim();
    }

    /// <summary>
    /// Formats a conversation into a single prompt string matching this dialect's template,
    /// injecting tool signatures into the system prompt when tools are provided,
    /// and optionally configuring reasoning generation and effort.
    /// </summary>
    public virtual string FormatPrompt(
        IEnumerable<ChatMessage> messages,
        IList<AITool>? tools = null,
        bool? enableReasoning = null,
        string? reasoningEffort = null)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var sb = new StringBuilder();
        var msgList = messages as IList<ChatMessage> ?? messages.ToList();

        var functions = tools?.OfType<AIFunction>().ToList();
        var hasTools = functions != null && functions.Count > 0;
        string toolsDefinitionBlock = hasTools ? BuildToolsDefinitionBlock(functions!) : string.Empty;

        string? reasoningInstructions = null;
        if (SupportsReasoning && SupportsReasoningEffort && enableReasoning != false)
        {
            reasoningInstructions = GetReasoningInstructions(reasoningEffort);
        }

        int firstSystemIndex = -1;
        for (int i = 0; i < msgList.Count; i++)
        {
            if (msgList[i].Role == ChatRole.System)
            {
                firstSystemIndex = i;
                break;
            }
        }

        // Render system prompt block
        if (hasTools)
        {
            var baseSystemText = firstSystemIndex >= 0 ? GetMessageText(msgList[firstSystemIndex]) : "You are a helpful assistant.";
            var combinedSystemText = !string.IsNullOrWhiteSpace(reasoningInstructions)
                ? $"{reasoningInstructions}\n\n{baseSystemText}{toolsDefinitionBlock}"
                : $"{baseSystemText}{toolsDefinitionBlock}";
            RenderSystemMessage(sb, combinedSystemText);
        }
        else if (firstSystemIndex >= 0)
        {
            var text = GetMessageText(msgList[firstSystemIndex]) ?? string.Empty;
            var combinedSystemText = !string.IsNullOrWhiteSpace(reasoningInstructions)
                ? (string.IsNullOrWhiteSpace(text) ? reasoningInstructions : $"{reasoningInstructions}\n\n{text}")
                : text;
            RenderSystemMessage(sb, combinedSystemText);
        }
        else if (!string.IsNullOrWhiteSpace(reasoningInstructions))
        {
            RenderSystemMessage(sb, reasoningInstructions);
        }

        // Render conversation history
        for (int i = 0; i < msgList.Count; i++)
        {
            var m = msgList[i];

            if (m.Role == ChatRole.System)
            {
                if (i == firstSystemIndex) continue;
                RenderSystemMessage(sb, GetMessageText(m) ?? string.Empty);
            }
            else if (m.Role == ChatRole.User)
            {
                RenderUserMessage(sb, GetMessageText(m) ?? string.Empty);
            }
            else if (m.Role == ChatRole.Assistant)
            {
                var text = GetMessageText(m);
                var toolCalls = m.Contents?.OfType<FunctionCallContent>().ToList();
                var reasoning = GetMessageReasoning(m);
                RenderAssistantMessage(sb, text, toolCalls, reasoning);
            }
            else if (m.Role == ChatRole.Tool)
            {
                var isFirstToolInGroup = i == 0 || msgList[i - 1].Role != ChatRole.Tool;
                var isLastToolInGroup = i == msgList.Count - 1 || msgList[i + 1].Role != ChatRole.Tool;
                var toolContent = GetToolContent(m);
                RenderToolMessage(sb, toolContent, isFirstToolInGroup, isLastToolInGroup);
            }
        }

        // Add generation prompt
        RenderAssistantPrompt(sb, enableReasoning);

        return sb.ToString();
    }

    /// <summary>
    /// Builds the tool definition and calling instructions block to inject into the system prompt.
    /// </summary>
    protected virtual string BuildToolsDefinitionBlock(IReadOnlyList<AIFunction> functions)
    {
        var toolsSb = new StringBuilder();
        foreach (var fn in functions)
        {
            toolsSb.Append('\n').Append(AotJsonUtilities.SerializeFunctionDefinition(fn));
        }

        return "\n\n# Tools\n\nYou may call one or more functions to assist with the user query.\n\nYou are provided with function signatures within <tools></tools> XML tags:\n<tools>" +
            toolsSb.ToString() +
            "\n</tools>\n\nFor each function call, return a json object with function name and arguments within <tool_call></tool_call> XML tags:\n<tool_call>\n{\"name\": <function-name>, \"arguments\": <args-json-object>}\n</tool_call>";
    }

    /// <summary>
    /// Extracts text from a chat message.
    /// </summary>
    protected static string? GetMessageText(ChatMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            return message.Text;
        }

        if (message.Contents is null || message.Contents.Count == 0)
        {
            return null;
        }

        string? singleText = null;
        StringBuilder? sb = null;

        foreach (var content in message.Contents)
        {
            if (content is TextContent tc && !string.IsNullOrEmpty(tc.Text))
            {
                if (singleText == null)
                {
                    singleText = tc.Text;
                }
                else
                {
                    sb ??= new StringBuilder(singleText);
                    sb.Append(tc.Text);
                }
            }
        }

        if (sb != null)
        {
            var trimmed = sb.ToString().Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        if (singleText != null)
        {
            var trimmed = singleText.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        return null;
    }

    /// <summary>
    /// Extracts reasoning content from a chat message if available.
    /// </summary>
    protected static string? GetMessageReasoning(ChatMessage message)
    {
        if (message.Contents is null || message.Contents.Count == 0)
        {
            return null;
        }

        string? single = null;
        StringBuilder? sb = null;

        foreach (var content in message.Contents)
        {
            if (content is TextReasoningContent trc && !string.IsNullOrWhiteSpace(trc.Text))
            {
                if (single == null)
                {
                    single = trc.Text;
                }
                else
                {
                    sb ??= new StringBuilder(single);
                    sb.Append("\n\n").Append(trc.Text);
                }
            }
        }

        return sb?.ToString() ?? single;
    }

    /// <summary>
    /// Extracts content from a tool chat message.
    /// </summary>
    protected static string GetToolContent(ChatMessage message)
    {
        var results = message.Contents?.OfType<FunctionResultContent>().ToList();
        if (results is { Count: > 0 })
        {
            if (results.Count == 1)
            {
                return FormatFunctionResult(results[0].Result);
            }

            return string.Join("\n", results.Select(r => FormatFunctionResult(r.Result)));
        }

        return GetMessageText(message) ?? string.Empty;
    }

    private static string FormatFunctionResult(object? result)
    {
        return AotJsonUtilities.FormatFunctionResult(result);
    }
}

