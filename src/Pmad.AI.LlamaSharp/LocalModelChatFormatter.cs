using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Pmad.AI.LlamaSharp;

/// <summary>
/// Provider-agnostic prompt formatter and assistant output parser for local Small Language Models (SLMs).
/// Handles template rendering (ChatML, Gemma4, Phi-3), function signature injection, reasoning trace extraction,
/// and function call normalization.
/// </summary>
public static class LocalModelChatFormatter
{
    private static readonly string[] _allStopTokens = ["<|im_end|>", "<|end|>", "<turn|>"];

    /// <summary>
    /// Resolves a <see cref="ChatDialect"/> by format name, falling back to <see cref="ChatDialect.Default"/> if null or unknown.
    /// </summary>
    public static ChatDialect GetDialect(string? format) => ChatDialect.FromName(format);

    /// <summary>
    /// Formats a conversation into a single prompt string matching the target model's template,
    /// injecting tool signatures into the system prompt when tools are provided.
    /// </summary>
    public static string FormatPrompt(
        IEnumerable<ChatMessage> messages,
        string format = "chatml",
        IList<AITool>? tools = null)
    {
        var dialect = ChatDialect.FromName(format);
        return dialect.FormatPrompt(messages, tools);
    }

    /// <summary>
    /// Formats a conversation into a single prompt string matching the specified dialect,
    /// injecting tool signatures into the system prompt when tools are provided.
    /// </summary>
    public static string FormatPrompt(
        IEnumerable<ChatMessage> messages,
        ChatDialect dialect,
        IList<AITool>? tools = null)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        return dialect.FormatPrompt(messages, tools);
    }

    /// <summary>
    /// Parses raw assistant output into structured contents: reasoning traces, tool calls, and text.
    /// </summary>
    public static ChatMessage ParseAssistantResponse(string rawText, FunctionCallNormalizer? normalizer = null)
    {
        return ParseAssistantResponse(rawText, dialect: null, normalizer: normalizer);
    }

    /// <summary>
    /// Parses raw assistant output into structured contents: reasoning traces, tool calls, and text,
    /// using the specified dialect to strip dialect-specific stop tokens.
    /// </summary>
    public static ChatMessage ParseAssistantResponse(string rawText, ChatDialect? dialect, FunctionCallNormalizer? normalizer = null)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new ChatMessage(ChatRole.Assistant, new List<AIContent> { new TextContent(string.Empty) });
        }

        var contents = new List<AIContent>();
        var text = rawText;

        // 1. Extract reasoning: <think>...</think> or <thought>...</thought>
        var thinkMatch = Regex.Match(text, @"<(?:think|thought)>(.*?)</(?:think|thought)>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (thinkMatch.Success)
        {
            var reasoning = thinkMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(reasoning))
            {
                contents.Add(new TextReasoningContent(reasoning));
            }
            text = text.Remove(thinkMatch.Index, thinkMatch.Length).Trim();
        }

        // 2. Extract <tool_call>...</tool_call>
        var toolCallMatches = Regex.Matches(text, @"<tool_call>\s*(.*?)\s*(?:</tool_call>|(?=<tool_call>)|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        foreach (Match match in toolCallMatches)
        {
            var callBlock = match.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(callBlock)) continue;

            callBlock = StripStrayTokens(callBlock, dialect);
            var parsed = TryParseFunctionCall(callBlock, normalizer);
            if (parsed != null)
            {
                contents.Add(parsed);
            }
        }

        // Remove matched <tool_call> blocks from text
        if (toolCallMatches.Count > 0)
        {
            text = Regex.Replace(text, @"<tool_call>\s*.*?\s*(?:</tool_call>|(?=<tool_call>)|$)", "", RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
        }

        // 3. Fallback: markdown code blocks ```tool_call or ```json containing JSON object
        if (!contents.OfType<FunctionCallContent>().Any())
        {
            var jsonCodeBlockMatch = Regex.Match(text, @"```(?:json|tool_call)?\s*(\{\s*.*?\})\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (jsonCodeBlockMatch.Success)
            {
                var blockContent = jsonCodeBlockMatch.Groups[1].Value.Trim();
                var parsed = TryParseFunctionCall(blockContent, normalizer);
                if (parsed != null)
                {
                    contents.Add(parsed);
                    text = text.Remove(jsonCodeBlockMatch.Index, jsonCodeBlockMatch.Length).Trim();
                }
            }
        }

        // 4. Strip stray chatml / template end tokens
        text = StripStrayTokens(text, dialect);

        // 5. Add remaining text if non-empty
        if (!string.IsNullOrWhiteSpace(text))
        {
            contents.Add(new TextContent(text));
        }

        if (contents.Count == 0)
        {
            contents.Add(new TextContent(string.Empty));
        }

        return new ChatMessage(ChatRole.Assistant, contents);
    }

    private static string StripStrayTokens(string text, ChatDialect? dialect)
    {
        if (dialect != null)
        {
            text = dialect.StripStopTokens(text);
        }

        foreach (var stopToken in _allStopTokens)
        {
            text = text.Replace(stopToken, string.Empty);
        }
        return text.Trim();
    }

    /// <summary>
    /// Attempts to parse a JSON payload representing a function call and optionally normalizes it via <paramref name="normalizer"/>.
    /// </summary>
    public static FunctionCallContent? TryParseFunctionCall(string jsonText, FunctionCallNormalizer? normalizer = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            string? functionName = null;
            if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
            {
                functionName = nameProp.GetString();
            }
            else if (root.TryGetProperty("function", out var fnProp))
            {
                if (fnProp.ValueKind == JsonValueKind.String)
                {
                    functionName = fnProp.GetString();
                }
                else if (fnProp.ValueKind == JsonValueKind.Object &&
                         fnProp.TryGetProperty("name", out var innerName) &&
                         innerName.ValueKind == JsonValueKind.String)
                {
                    functionName = innerName.GetString();
                }
            }

            if (string.IsNullOrWhiteSpace(functionName))
            {
                return null;
            }

            var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
            JsonElement argsElement = default;
            var hasArgs = false;

            if (root.TryGetProperty("arguments", out var aProp))
            {
                argsElement = aProp;
                hasArgs = true;
            }
            else if (root.TryGetProperty("parameters", out var pProp))
            {
                argsElement = pProp;
                hasArgs = true;
            }
            else if (root.TryGetProperty("function", out var fnObj) && fnObj.TryGetProperty("arguments", out var fnArgs))
            {
                argsElement = fnArgs;
                hasArgs = true;
            }

            if (hasArgs)
            {
                if (argsElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in argsElement.EnumerateObject())
                    {
                        arguments[prop.Name] = UnwrapJsonElement(prop.Value);
                    }
                }
                else if (argsElement.ValueKind == JsonValueKind.String)
                {
                    var innerJson = argsElement.GetString();
                    if (!string.IsNullOrWhiteSpace(innerJson))
                    {
                        try
                        {
                            using var innerDoc = JsonDocument.Parse(innerJson);
                            if (innerDoc.RootElement.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var prop in innerDoc.RootElement.EnumerateObject())
                                {
                                    arguments[prop.Name] = UnwrapJsonElement(prop.Value);
                                }
                            }
                        }
                        catch
                        {
                            arguments["command"] = innerJson;
                        }
                    }
                }
            }

            if (normalizer != null)
            {
                var normalized = normalizer(functionName, arguments);
                if (normalized.HasValue)
                {
                    functionName = normalized.Value.Name;
                    arguments = (Dictionary<string, object?>)normalized.Value.Arguments;
                }
            }

            return new FunctionCallContent(
                callId: $"call_{Guid.NewGuid():N}",
                name: functionName,
                arguments: arguments);
        }
        catch
        {
            return null;
        }
    }

    private static object? UnwrapJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.Clone()
        };
    }

    /// <summary>
    /// Helper to extract text from a chat message.
    /// </summary>
    public static string? GetMessageText(ChatMessage message)
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
}