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
    /// <summary>
    /// Formats a conversation into a single prompt string matching the target model's template,
    /// injecting tool signatures into the system prompt when tools are provided.
    /// </summary>
    public static string FormatPrompt(
        IEnumerable<ChatMessage> messages,
        string format = "chatml",
        IList<AITool>? tools = null)
    {
        var sb = new StringBuilder();
        var msgList = messages.ToList();

        var functions = tools?.OfType<AIFunction>().ToList();
        var hasTools = functions != null && functions.Count > 0;
        string toolsDefinitionBlock = string.Empty;

        if (hasTools)
        {
            var toolsSb = new StringBuilder();
            foreach (var fn in functions!)
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

            toolsDefinitionBlock = "\n\n# Tools\n\nYou may call one or more functions to assist with the user query.\n\nYou are provided with function signatures within <tools></tools> XML tags:\n<tools>" +
                toolsSb.ToString() +
                "\n</tools>\n\nFor each function call, return a json object with function name and arguments within <tool_call></tool_call> XML tags:\n<tool_call>\n{\"name\": <function-name>, \"arguments\": <args-json-object>}\n</tool_call>";
        }

        var isPhi3 = format.Equals("phi3", StringComparison.OrdinalIgnoreCase);
        var isGemma4 = format.Equals("gemma4", StringComparison.OrdinalIgnoreCase);

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

            if (isPhi3)
            {
                sb.Append($"<|system|>\n{combinedSystemText}<|end|>\n");
            }
            else if (isGemma4)
            {
                sb.Append($"<|turn|>system\n{combinedSystemText}<turn|>\n");
            }
            else
            {
                sb.Append($"<|im_start|>system\n{combinedSystemText}<|im_end|>\n");
            }
        }
        else if (firstSystemIndex >= 0)
        {
            var text = GetMessageText(msgList[firstSystemIndex]);
            if (isPhi3)
            {
                sb.Append($"<|system|>\n{text}<|end|>\n");
            }
            else if (isGemma4)
            {
                sb.Append($"<|turn|>system\n{text}<turn|>\n");
            }
            else
            {
                sb.Append($"<|im_start|>system\n{text}<|im_end|>\n");
            }
        }

        // Render conversation history
        for (int i = 0; i < msgList.Count; i++)
        {
            var m = msgList[i];

            if (m.Role == ChatRole.System)
            {
                if (i == firstSystemIndex) continue;

                var text = GetMessageText(m);
                if (isPhi3)
                {
                    sb.Append($"<|system|>\n{text}<|end|>\n");
                }
                else if (isGemma4)
                {
                    sb.Append($"<|turn|>system\n{text}<turn|>\n");
                }
                else
                {
                    sb.Append($"<|im_start|>system\n{text}<|im_end|>\n");
                }
            }
            else if (m.Role == ChatRole.User)
            {
                var text = GetMessageText(m);
                if (isPhi3)
                {
                    sb.Append($"<|user|>\n{text}<|end|>\n");
                }
                else if (isGemma4)
                {
                    sb.Append($"<|turn|>user\n{text}<turn|>\n");
                }
                else
                {
                    sb.Append($"<|im_start|>user\n{text}<|im_end|>\n");
                }
            }
            else if (m.Role == ChatRole.Assistant)
            {
                var text = GetMessageText(m);
                var toolCalls = m.Contents?.OfType<FunctionCallContent>().ToList();

                if (isPhi3)
                {
                    sb.Append("<|assistant|>");
                    if (!string.IsNullOrWhiteSpace(text)) sb.Append('\n').Append(text);
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
                else if (isGemma4)
                {
                    sb.Append("<|turn|>model");
                    if (!string.IsNullOrWhiteSpace(text)) sb.Append('\n').Append(text);
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
                else
                {
                    sb.Append("<|im_start|>assistant");
                    if (!string.IsNullOrWhiteSpace(text)) sb.Append('\n').Append(text);
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
            }
            else if (m.Role == ChatRole.Tool)
            {
                var isFirstToolInGroup = i == 0 || msgList[i - 1].Role != ChatRole.Tool;
                var isLastToolInGroup = i == msgList.Count - 1 || msgList[i + 1].Role != ChatRole.Tool;

                var result = m.Contents?.OfType<FunctionResultContent>().FirstOrDefault();
                string toolContent;
                if (result != null)
                {
                    toolContent = result.Result switch
                    {
                        null => string.Empty,
                        string s => s,
                        JsonElement je => je.GetRawText(),
                        _ => JsonSerializer.Serialize(result.Result)
                    };
                }
                else
                {
                    toolContent = GetMessageText(m) ?? string.Empty;
                }

                if (isPhi3)
                {
                    if (isFirstToolInGroup) sb.Append("<|user|>");
                    sb.Append($"\n<tool_response>\n{toolContent}\n</tool_response>");
                    if (isLastToolInGroup) sb.Append("<|end|>\n");
                }
                else if (isGemma4)
                {
                    if (isFirstToolInGroup) sb.Append("<|turn|>user");
                    sb.Append($"\n<tool_response>\n{toolContent}\n</tool_response>");
                    if (isLastToolInGroup) sb.Append("<turn|>\n");
                }
                else
                {
                    if (isFirstToolInGroup) sb.Append("<|im_start|>user");
                    sb.Append($"\n<tool_response>\n{toolContent}\n</tool_response>");
                    if (isLastToolInGroup) sb.Append("<|im_end|>\n");
                }
            }
        }

        // Add generation prompt
        if (isPhi3)
        {
            sb.Append("<|assistant|>\n");
        }
        else if (isGemma4)
        {
            sb.Append("<|turn|>model\n");
        }
        else
        {
            sb.Append("<|im_start|>assistant\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parses raw assistant output into structured contents: reasoning traces, tool calls, and text.
    /// </summary>
    public static ChatMessage ParseAssistantResponse(string rawText, FunctionCallNormalizer? normalizer = null)
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

            callBlock = callBlock.Replace("<|im_end|>", "").Replace("<|end|>", "").Replace("<turn|>", "").Trim();
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
        text = text.Replace("<|im_end|>", "").Replace("<|end|>", "").Replace("<turn|>", "").Trim();

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

    private static string? GetMessageText(ChatMessage message)
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