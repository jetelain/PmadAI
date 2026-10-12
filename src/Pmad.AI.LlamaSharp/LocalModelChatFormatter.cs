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
public static partial class LocalModelChatFormatter
{
    private static readonly string[] _allStopTokens = [
        "<|im_end|>", "<|end|>", "<turn|>", "<end_of_turn>",
        "<|tool_call>", "<tool_call|>", "<|tool_code>", "<tool_code|>",
        "<|tool_response>", "<tool_response|>"
    ];

    [GeneratedRegex(@"<(think|thought)>(.*?)</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkRegex();

    [GeneratedRegex(@"^(.*?)</(think|thought)>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CloseThinkRegex();

    [GeneratedRegex(@"<(think|thought)>(.*)$", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex UnclosedThinkRegex();

    [GeneratedRegex(@"<\|?(?:tool_call|tool_code)>\s*(.*?)\s*(?:</(?:tool_call|tool_code)>|<(?:tool_call|tool_code)\|>|</\|(?:tool_call|tool_code)>|(?=<\|?(?:tool_call|tool_code)>)|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ToolCallRegex();

    [GeneratedRegex(@"```(?:json|tool_call|tool_code)?\s*(\{\s*.*?\})\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex JsonCodeBlockRegex();

    [GeneratedRegex(@"(?:call:)([a-zA-Z0-9_\-]+)\s*(\{.*?\}|\(.*?\))", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex RawCallRegex();

    [GeneratedRegex(@"(?<=[{,]\s*)([a-zA-Z0-9_\-]+)\s*:", RegexOptions.Singleline)]
    private static partial Regex UnquotedKeyRegex();

    [GeneratedRegex(@"(?<=:\s*)(?!true\b|false\b|null\b|[0-9\-])([a-zA-Z_][a-zA-Z0-9_\-]*)(?=\s*[,}])", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex UnquotedValueRegex();

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
        IList<AITool>? tools = null,
        bool? enableReasoning = null,
        string? reasoningEffort = null)
    {
        var dialect = ChatDialect.FromName(format);
        return dialect.FormatPrompt(messages, tools, enableReasoning, reasoningEffort);
    }

    /// <summary>
    /// Formats a conversation into a single prompt string matching the target model's template and options,
    /// extracting tools and reasoning parameters from <paramref name="options"/>.
    /// </summary>
    public static string FormatPrompt(
        IEnumerable<ChatMessage> messages,
        string format,
        ChatOptions? options)
    {
        var dialect = ChatDialect.FromName(format);
        return FormatPrompt(messages, dialect, options);
    }

    /// <summary>
    /// Formats a conversation into a single prompt string matching the specified dialect,
    /// injecting tool signatures into the system prompt when tools are provided.
    /// </summary>
    public static string FormatPrompt(
        IEnumerable<ChatMessage> messages,
        ChatDialect dialect,
        IList<AITool>? tools = null,
        bool? enableReasoning = null,
        string? reasoningEffort = null)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        return dialect.FormatPrompt(messages, tools, enableReasoning, reasoningEffort);
    }

    /// <summary>
    /// Formats a conversation into a single prompt string matching the specified dialect and options,
    /// extracting tools and reasoning parameters from <paramref name="options"/>.
    /// </summary>
    public static string FormatPrompt(
        IEnumerable<ChatMessage> messages,
        ChatDialect dialect,
        ChatOptions? options)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        var (enableReasoning, reasoningEffort) = ResolveReasoningParameters(options);
        return dialect.FormatPrompt(messages, options?.Tools, enableReasoning, reasoningEffort);
    }

    /// <summary>
    /// Resolves reasoning configuration (enable toggle and effort level) from <see cref="ChatOptions"/> and/or <see cref="LlamaSharpOptions"/>.
    /// </summary>
    public static (bool? EnableReasoning, string? ReasoningEffort) ResolveReasoningParameters(
        ChatOptions? chatOptions,
        LlamaSharpOptions? llamaOptions = null)
    {
        bool? enableReasoning = null;
        string? reasoningEffort = null;

        if (chatOptions?.Reasoning is { } reasoning)
        {
            if (reasoning.Effort.HasValue)
            {
                if (reasoning.Effort.Value == ReasoningEffort.None)
                {
                    enableReasoning = false;
                    reasoningEffort = "none";
                }
                else
                {
                    enableReasoning = true;
                    reasoningEffort = reasoning.Effort.Value switch
                    {
                        ReasoningEffort.Low => "low",
                        ReasoningEffort.Medium => "medium",
                        ReasoningEffort.High => "high",
                        ReasoningEffort.ExtraHigh => "xhigh",
                        _ => reasoning.Effort.Value.ToString().ToLowerInvariant()
                    };
                }
            }

            if (reasoning.Output.HasValue && reasoning.Output.Value == ReasoningOutput.None)
            {
                enableReasoning = false;
            }
        }

        if (chatOptions?.AdditionalProperties is { } props)
        {
            if (props.TryGetValue("enable_thinking", out var et) && et is bool b1)
            {
                enableReasoning = b1;
            }
            else if (props.TryGetValue("enable_reasoning", out var er) && er is bool b2)
            {
                enableReasoning = b2;
            }
            else if (props.TryGetValue("reasoning", out var r) && r is bool b3)
            {
                enableReasoning = b3;
            }

            if (props.TryGetValue("reasoning_effort", out var re) && re != null)
            {
                reasoningEffort = re.ToString();
            }
        }

        enableReasoning ??= llamaOptions?.EnableReasoning;
        reasoningEffort ??= llamaOptions?.ReasoningEffort;

        if (!string.IsNullOrWhiteSpace(reasoningEffort))
        {
            var trimmedEffort = reasoningEffort.Trim();
            if (string.Equals(trimmedEffort, "none", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmedEffort, "off", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmedEffort, "false", StringComparison.OrdinalIgnoreCase))
            {
                enableReasoning = false;
            }
            else if (enableReasoning == null)
            {
                enableReasoning = true;
            }
        }

        return (enableReasoning, reasoningEffort);
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
        var thinkMatches = ThinkRegex().Matches(text);
        foreach (Match match in thinkMatches)
        {
            var reasoning = match.Groups[2].Value.Trim();
            if (!string.IsNullOrWhiteSpace(reasoning))
            {
                contents.Add(new TextReasoningContent(reasoning));
            }
        }
        if (thinkMatches.Count > 0)
        {
            text = ThinkRegex().Replace(text, string.Empty).Trim();
        }
        else
        {
            // Fallback for pre-filled thinking prompt (e.g. "<|im_start|>assistant\n<think>\n") where the model only outputs "...</think>"
            var closeMatch = CloseThinkRegex().Match(text);
            if (closeMatch.Success)
            {
                var reasoning = closeMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(reasoning))
                {
                    contents.Add(new TextReasoningContent(reasoning));
                }
                text = text.Substring(closeMatch.Length).Trim();
            }
            else
            {
                // Fallback for unclosed thinking block (e.g. generation aborted or truncated inside <think>)
                var unclosedMatch = UnclosedThinkRegex().Match(text);
                if (unclosedMatch.Success)
                {
                    var reasoning = unclosedMatch.Groups[2].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(reasoning))
                    {
                        contents.Add(new TextReasoningContent(reasoning));
                    }
                    text = text.Substring(0, unclosedMatch.Index).Trim();
                }
            }
        }

        // 2. Extract <tool_call>...</tool_call>
        var toolCallMatches = ToolCallRegex().Matches(text);
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
            text = ToolCallRegex().Replace(text, string.Empty).Trim();
        }

        // 3. Fallback: markdown code blocks ```tool_call or ```json containing JSON object
        if (!contents.OfType<FunctionCallContent>().Any())
        {
            var jsonCodeBlockMatch = JsonCodeBlockRegex().Match(text);
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

        // 4. Fallback: raw call:function_name{args} syntax (e.g. Gemma native output without XML tags)
        if (!contents.OfType<FunctionCallContent>().Any())
        {
            var rawCallMatches = RawCallRegex().Matches(text);
            foreach (Match match in rawCallMatches)
            {
                var parsed = TryParseCallSyntax(match.Value, normalizer);
                if (parsed != null)
                {
                    contents.Add(parsed);
                }
            }
            if (rawCallMatches.Count > 0)
            {
                text = RawCallRegex().Replace(text, string.Empty).Trim();
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
            if (text.Contains(stopToken, StringComparison.Ordinal))
            {
                text = text.Replace(stopToken, string.Empty);
            }
        }
        return text.Trim();
    }

    /// <summary>
    /// Attempts to parse a JSON payload representing a function call and optionally normalizes it via <paramref name="normalizer"/>.
    /// Supports standard JSON as well as Gemma native <c>call:func{arg:&lt;|"|&gt;val&lt;|"|&gt;}</c> syntax.
    /// </summary>
    public static FunctionCallContent? TryParseFunctionCall(string jsonText, FunctionCallNormalizer? normalizer = null)
    {
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return null;
        }

        var trimmed = jsonText.Trim();
        if (trimmed.StartsWith("call:", StringComparison.OrdinalIgnoreCase))
        {
            var callParsed = TryParseCallSyntax(trimmed, normalizer);
            if (callParsed != null)
            {
                return callParsed;
            }
        }

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

            IDictionary<string, object?> arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
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
                    arguments = normalized.Value.Arguments;
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

    private static FunctionCallContent? TryParseCallSyntax(string text, FunctionCallNormalizer? normalizer)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("call:", StringComparison.OrdinalIgnoreCase))
        {
            var openBraceIndex = trimmed.IndexOf('{');
            if (openBraceIndex < 0)
            {
                openBraceIndex = trimmed.IndexOf('(');
            }

            if (openBraceIndex > 5)
            {
                var functionName = trimmed.Substring(5, openBraceIndex - 5).Trim();
                var closeBraceIndex = trimmed.LastIndexOf(trimmed[openBraceIndex] == '{' ? '}' : ')');
                var argsStr = closeBraceIndex > openBraceIndex
                    ? trimmed.Substring(openBraceIndex, closeBraceIndex - openBraceIndex + 1)
                    : trimmed.Substring(openBraceIndex);

                // Replace Gemma's <|"|> with "
                argsStr = argsStr.Replace("<|\"|>", "\"");

                // If argsStr is { ... }, ensure unquoted keys and string values are quoted
                if (argsStr.StartsWith('{') && argsStr.EndsWith('}'))
                {
                    argsStr = UnquotedKeyRegex().Replace(argsStr, "\"$1\":");
                    argsStr = UnquotedValueRegex().Replace(argsStr, "\"$1\"");
                }

                try
                {
                    using var doc = JsonDocument.Parse(argsStr);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        IDictionary<string, object?> arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            arguments[prop.Name] = UnwrapJsonElement(prop.Value);
                        }

                        if (normalizer != null)
                        {
                            var normalized = normalizer(functionName, arguments);
                            if (normalized.HasValue)
                            {
                                functionName = normalized.Value.Name;
                                arguments = normalized.Value.Arguments;
                            }
                        }

                        return new FunctionCallContent(
                            callId: $"call_{Guid.NewGuid():N}",
                            name: functionName,
                            arguments: arguments);
                    }
                }
                catch
                {
                    // Fall back
                }
            }
        }

        return null;
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
}