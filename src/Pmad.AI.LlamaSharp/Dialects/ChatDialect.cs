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

        return ChatML;
    }

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
    /// Renders a tool response message inside a tool response turn.
    /// </summary>
    public abstract void RenderToolMessage(StringBuilder sb, string toolContent, bool isFirstInGroup, bool isLastInGroup);

    /// <summary>
    /// Renders the assistant generation prefix that prompts the model to generate the next response.
    /// </summary>
    public abstract void RenderAssistantPrompt(StringBuilder sb);

    /// <summary>
    /// Removes any stop tokens associated with this dialect from the raw output string.
    /// </summary>
    public virtual string StripStopTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var stopToken in StopTokens)
        {
            text = text.Replace(stopToken, string.Empty);
        }
        return text.Trim();
    }

    /// <summary>
    /// Formats a conversation into a single prompt string matching this dialect's template,
    /// injecting tool signatures into the system prompt when tools are provided.
    /// </summary>
    public virtual string FormatPrompt(
        IEnumerable<ChatMessage> messages,
        IList<AITool>? tools = null)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var sb = new StringBuilder();
        var msgList = messages as IList<ChatMessage> ?? messages.ToList();

        var functions = tools?.OfType<AIFunction>().ToList();
        var hasTools = functions != null && functions.Count > 0;
        string toolsDefinitionBlock = hasTools ? BuildToolsDefinitionBlock(functions!) : string.Empty;

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
            var combinedSystemText = $"{baseSystemText}{toolsDefinitionBlock}";
            RenderSystemMessage(sb, combinedSystemText);
        }
        else if (firstSystemIndex >= 0)
        {
            var text = GetMessageText(msgList[firstSystemIndex]) ?? string.Empty;
            RenderSystemMessage(sb, text);
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
                RenderAssistantMessage(sb, text, toolCalls);
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
        RenderAssistantPrompt(sb);

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
            object parametersObj = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>()
            };

            if (fn.JsonSchema.ValueKind == JsonValueKind.Object)
            {
                parametersObj = fn.JsonSchema;
            }

            var toolObj = new Dictionary<string, object?>
            {
                ["type"] = "function",
                ["function"] = new Dictionary<string, object?>
                {
                    ["name"] = fn.Name,
                    ["description"] = fn.Description ?? string.Empty,
                    ["parameters"] = parametersObj
                }
            };

            toolsSb.Append('\n').Append(JsonSerializer.Serialize(toolObj));
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

        if (message.Contents is null)
        {
            return null;
        }

        var text = string.Concat(message.Contents.OfType<TextContent>().Select(c => c.Text ?? string.Empty)).Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>
    /// Extracts content from a tool chat message.
    /// </summary>
    protected static string GetToolContent(ChatMessage message)
    {
        var result = message.Contents?.OfType<FunctionResultContent>().FirstOrDefault();
        if (result != null)
        {
            return result.Result switch
            {
                null => string.Empty,
                string s => s,
                JsonElement je => je.GetRawText(),
                _ => JsonSerializer.Serialize(result.Result)
            };
        }

        return GetMessageText(message) ?? string.Empty;
    }
}

