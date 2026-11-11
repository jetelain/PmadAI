using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Pmad.AI.Common;
using Pmad.AI.Tools;

namespace Pmad.AI.Healing;

/// <summary>
/// Protocol-level sanitizer for tool calling responses from local/open-weights models.
/// Strips markdown fences inside function arguments, promotes pseudo &lt;tool_call&gt; tags from content,
/// injects missing tool call IDs, and repairs trailing commas or unescaped control characters.
/// Uses source-generated regex for Native AOT compatibility.
/// </summary>
public static partial class ToolCallHealingTransform
{
    [GeneratedRegex(@"<(?:tool_call|function_call)>\s*(?:```(?:json)?\s*)?(?<payload>\{[\s\S]*?\})(?:\s*```)?\s*</(?:tool_call|function_call)>")]
    private static partial Regex ToolCallRegex();

    [GeneratedRegex(@"<(?:tool_call|function_call)>\s*(?:```(?:json)?\s*)?")]
    private static partial Regex UnclosedToolCallRegex();

    [GeneratedRegex(@"Action:\s*(?<name>[a-zA-Z0-9_\-\.]+)\s*\r?\nAction Input:\s*(?:```(?:json)?\s*)?")]
    private static partial Regex ActionHeaderRegex();




    [GeneratedRegex(@"^<(?:think|thought)>\s*([\s\S]*)$")]
    private static partial Regex UnclosedThinkRegex();

    /// <summary>
    /// Evaluates whether a candidate pseudo-tool-call extracted from text passes available tools guardrails.
    /// When tools are declared in <paramref name="chatOptions"/>, candidate tool names must match a declared tool,
    /// accounting for <see cref="LenientToolDispatcher"/> alias resolution, namespace prefix stripping, and translations.
    /// </summary>
    public static bool ShouldPromoteToolCall(
        string toolName,
        ToolCallHealingOptions? options = null,
        ChatOptions? chatOptions = null)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        options ??= new ToolCallHealingOptions();

        if (!options.EnforceAvailableToolsGuardrail)
        {
            return true;
        }

        var dispatcher = options.Dispatcher ?? chatOptions.GetToolDispatcher();

        // 1. Available tools guardrail: if tools are declared in chat options
        if (chatOptions?.Tools != null && chatOptions.Tools.Count > 0)
        {
            // Direct match against declared tool names
            if (chatOptions.Tools.Any(t =>
                string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase) ||
                (t is AIFunction fn && string.Equals(fn.Name, toolName, StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }

            // Dispatcher-aware matching (aliases, translations, and namespaces)
            if (dispatcher != null)
            {
                if (dispatcher.IsToolAvailable(toolName, chatOptions.Tools))
                {
                    return true;
                }
            }
            else
            {
                // Fallback namespace stripping if no dispatcher instance was provided
                var stripped = LenientToolDispatcher.StripNamespace(toolName);
                if (!string.Equals(stripped, toolName, StringComparison.OrdinalIgnoreCase) &&
                    chatOptions.Tools.Any(t =>
                        string.Equals(t.Name, stripped, StringComparison.OrdinalIgnoreCase) ||
                        (t is AIFunction fn && string.Equals(fn.Name, stripped, StringComparison.OrdinalIgnoreCase))))
                {
                    return true;
                }
            }

            return false;
        }

        // 2. If no chatOptions.Tools declared, but dispatcher has registered tools
        if (dispatcher != null && dispatcher.HasRegisteredTools)
        {
            return dispatcher.IsToolRegistered(toolName);
        }

        return true;
    }

    /// <summary>
    /// Transforms the given JSON node representing a chat completion response or chunk.
    /// Supports both OpenAI format (choices[].message / choices[].delta) and Ollama native format (message).
    /// </summary>
    public static JsonNode HealChatCompletionResponse(
        JsonNode rootNode,
        ILogger? logger = null,
        ToolCallHealingOptions? options = null,
        ChatOptions? chatOptions = null)
    {
        // Support OpenAI format: choices[].message or choices[].delta
        if (rootNode["choices"] is JsonArray choices)
        {
            foreach (var choice in choices)
            {
                if (choice?["message"] is JsonObject message)
                {
                    HealMessage(message, logger, options, chatOptions);
                }
                else if (choice?["delta"] is JsonObject delta)
                {
                    HealMessage(delta, logger, options, chatOptions);
                }
            }
        }
        // Support Ollama native format: message
        else if (rootNode["message"] is JsonObject ollamaMessage)
        {
            HealMessage(ollamaMessage, logger, options, chatOptions);
        }

        return rootNode;
    }

    /// <summary>
    /// Transforms the given JSON node representing a chat completion response or chunk.
    /// </summary>
    public static JsonNode HealChatCompletionResponse(JsonNode rootNode, ILogger? logger)
        => HealChatCompletionResponse(rootNode, logger, null, null);

    /// <summary>
    /// Parses a JSON response string, heals tool calling payloads, and returns the healed JSON string.
    /// Falls back to the original string if parsing fails.
    /// </summary>
    public static string HealResponseText(
        string json,
        ILogger? logger = null,
        ToolCallHealingOptions? options = null,
        ChatOptions? chatOptions = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        try
        {
            var node = JsonNode.Parse(json);
            if (node != null)
            {
                HealChatCompletionResponse(node, logger, options, chatOptions);
                return node.ToJsonString();
            }
        }
        catch
        {
            // Retain original string on parse error.
        }

        return json;
    }

    /// <summary>
    /// Parses a JSON response string with default options, heals tool calling payloads, and returns the healed JSON string.
    /// </summary>
    public static string HealResponseText(string json, ILogger? logger)
        => HealResponseText(json, logger, null, null);

    private static void HealMessage(
        JsonObject message,
        ILogger? logger,
        ToolCallHealingOptions? options = null,
        ChatOptions? chatOptions = null)
    {
        // 1. Check for pseudo-tool calls in content if tool_calls is empty
        var toolCalls = message["tool_calls"] as JsonArray;

        string? content = null;
        if (message.TryGetPropertyValue("content", out var contentNode) &&
            contentNode is JsonValue cVal &&
            cVal.TryGetValue<string>(out var strVal))
        {
            content = strVal;
        }

        if ((toolCalls == null || toolCalls.Count == 0) && !string.IsNullOrWhiteSpace(content))
        {
            var contentCopy = content;
            if (TryExtractToolCalls(ref contentCopy, out var extractedCalls, out _, out _, logger, options, chatOptions) && extractedCalls.Count > 0)
            {
                var newToolCalls = new JsonArray();
                foreach (var call in extractedCalls)
                {
                    var generatedId = call.CallId ?? $"call_{Guid.NewGuid():N}"[..12];
                    newToolCalls.Add((JsonNode)new JsonObject
                    {
                        ["id"] = generatedId,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = call.Name,
                            ["arguments"] = SerializeArgumentsToJson(call.Arguments)
                        }
                    });
                }
                toolCalls = newToolCalls;
                message["tool_calls"] = toolCalls;
                message["content"] = contentCopy.Trim();
            }
        }

        // 2. Sanitize existing tool_calls
        if (toolCalls != null)
        {
            foreach (var tc in toolCalls)
            {
                if (tc is JsonObject tcObj)
                {
                    var toolName = tcObj["function"]?["name"]?.GetValue<string>() ?? "unknown";

                    // Missing ID fix
                    if (string.IsNullOrWhiteSpace(tcObj["id"]?.GetValue<string>()))
                    {
                        var generatedId = $"call_{Guid.NewGuid():N}"[..12];
                        tcObj["id"] = generatedId;
                        logger?.LogInformation("Injected missing tool call ID '{ToolCallId}' for tool '{ToolName}'", generatedId, toolName);
                        options?.Telemetry?.Record(ToolCallHealingEventType.CallIdGenerated, toolName, options.SessionId);
                    }

                    if (string.IsNullOrWhiteSpace(tcObj["type"]?.GetValue<string>()))
                    {
                        tcObj["type"] = "function";
                    }

                    // Arguments fence stripping & repair
                    if (tcObj["function"] is JsonObject funcObj && funcObj["arguments"] != null)
                    {
                        if (funcObj["arguments"] is JsonValue jVal && jVal.TryGetValue<string>(out var strArgVal))
                        {
                            var cleanedArgs = SanitizeArgumentsJson(strArgVal, logger, toolName, options);
                            if (!IsValidJson(cleanedArgs))
                            {
                                var recovered = new Dictionary<string, object?>(StringComparer.Ordinal);
                                if (cleanedArgs.StartsWith('[') && JsonRepairUtilities.ExtractBalancedJsonElements(cleanedArgs).Count > 0)
                                {
                                    recovered[RootArraySentinelKey] = cleanedArgs;
                                    options?.Telemetry?.Record(ToolCallHealingEventType.JsonRepaired, toolName, options.SessionId, "Corrupted array container");
                                }
                                else if (cleanedArgs.StartsWith('{') && JsonRepairUtilities.TryExtractTopLevelPropertiesFromCorruptedObject(cleanedArgs, recovered))
                                {
                                    options?.Telemetry?.Record(ToolCallHealingEventType.JsonRepaired, toolName, options.SessionId, "Corrupted object container");
                                }

                                if (recovered.Count > 0)
                                {
                                    cleanedArgs = SerializeArgumentsToJson(recovered);
                                }
                            }
                            if (IsValidJson(cleanedArgs))
                            {
                                funcObj["arguments"] = cleanedArgs;
                            }
                        }
                        else
                        {
                            var rawArgs = funcObj["arguments"]!.ToJsonString();
                            var cleanedArgs = SanitizeArgumentsJson(rawArgs, logger, toolName, options);
                            if (cleanedArgs != rawArgs)
                            {
                                try
                                {
                                    funcObj["arguments"] = JsonNode.Parse(cleanedArgs);
                                }
                                catch
                                {
                                    funcObj["arguments"] = cleanedArgs;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Sanitizes the raw function arguments JSON string by stripping leading and trailing
    /// markdown fences (```json ... ``` or ``` ... ```), trimming whitespace, removing trailing
    /// commas, and repairing unescaped control characters in string values.
    /// </summary>
    public static string SanitizeArgumentsJson(string raw, ILogger? logger = null, string? toolName = null, ToolCallHealingOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "{}";
        }

        var trimmedRaw = raw.Trim();
        var s = trimmedRaw;
        bool fenceStripped = false;

        // Strip ```json ... ``` or ``` ... ```
        if (s.StartsWith("```"))
        {
            var unFenced = StripMarkdownFences(s);
            if (!IsObjectPayload(unFenced) && !IsValidJson(unFenced))
            {
                return trimmedRaw;
            }

            s = unFenced;
            fenceStripped = true;
        }

        if (IsValidJson(s))
        {
            if (fenceStripped)
            {
                logger?.LogInformation("Stripped markdown code fences from arguments of tool '{ToolName}'", toolName ?? "unknown");
                options?.Telemetry?.Record(ToolCallHealingEventType.FenceStripped, toolName, options.SessionId);
            }
            return s;
        }

        if (!IsObjectPayload(s) && !s.StartsWith('"'))
        {
            return trimmedRaw;
        }

        var candidate = s;
        bool commasRepaired = false;

        // Remove trailing commas before } or ] outside of string literals only if commas and closing brackets exist
        if (candidate.IndexOf(',') >= 0 && (candidate.IndexOf('}') >= 0 || candidate.IndexOf(']') >= 0))
        {
            var afterCommas = RemoveTrailingCommasOutsideStrings(candidate);
            if (afterCommas != candidate)
            {
                candidate = afterCommas;
                commasRepaired = true;
            }
        }

        // Validate JSON and attempt to escape unescaped control chars if parsing fails
        bool isValidJson = false;
        bool controlCharsRepaired = false;
        try
        {
            using var _ = JsonDocument.Parse(candidate);
            isValidJson = true;
        }
        catch (JsonException)
        {
            var escaped = EscapeControlCharsInStrings(candidate);
            if (escaped != candidate)
            {
                candidate = escaped;
                controlCharsRepaired = true;
                try
                {
                    using var _ = JsonDocument.Parse(escaped);
                    isValidJson = true;
                }
                catch (JsonException)
                {
                    // Escaped control chars helped, but payload may still be truncated.
                }
            }
        }

        // Last resort: if the payload is still not valid JSON, it is likely truncated (e.g. the model
        // stopped emitting before the closing ']' or '}', or left a dangling key). Close the open
        // structure so the payload can be parsed and the tool invoked with the data it did emit.
        bool truncatedRepaired = false;
        if (!isValidJson && IsObjectPayload(candidate))
        {
            var repaired = JsonRepairUtilities.TryRepairTruncatedJson(candidate);
            if (repaired != null)
            {
                try
                {
                    using var _ = JsonDocument.Parse(repaired);
                    isValidJson = true;
                    truncatedRepaired = true;
                    candidate = repaired;
                }
                catch (JsonException)
                {
                    // Not repairable; leave as-is so ParseArgumentsJson logs its usual warning.
                }
            }
        }

        if (!isValidJson && !IsRecoverableCorruptedContainer(candidate))
        {
            return trimmedRaw;
        }

        if (fenceStripped)
        {
            logger?.LogInformation("Stripped markdown code fences from arguments of tool '{ToolName}'", toolName ?? "unknown");
            options?.Telemetry?.Record(ToolCallHealingEventType.FenceStripped, toolName, options.SessionId);
        }

        if (commasRepaired)
        {
            logger?.LogDebug("Repaired trailing commas in arguments of tool '{ToolName}'", toolName ?? "unknown");
            options?.Telemetry?.Record(ToolCallHealingEventType.JsonRepaired, toolName, options.SessionId, "Trailing comma");
        }

        if (controlCharsRepaired)
        {
            logger?.LogInformation("Repaired unescaped control characters in arguments of tool '{ToolName}'", toolName ?? "unknown");
            options?.Telemetry?.Record(ToolCallHealingEventType.JsonRepaired, toolName, options.SessionId, "Control characters");
        }

        if (truncatedRepaired)
        {
            logger?.LogInformation("Repaired truncated JSON arguments of tool '{ToolName}'", toolName ?? "unknown");
            options?.Telemetry?.Record(ToolCallHealingEventType.JsonRepaired, toolName, options.SessionId, "Truncated payload");
        }

        return candidate;
    }

    private static string StripMarkdownFences(string s)
    {
        var firstNewline = s.IndexOf('\n');
        if (firstNewline >= 0)
        {
            s = s.Substring(firstNewline + 1);
        }
        else if (s.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring(7);
        }
        else
        {
            s = s.Substring(3);
        }

        if (s.EndsWith("```"))
        {
            s = s.Substring(0, s.Length - 3);
        }

        return s.Trim();
    }

    private static bool IsRecoverableCorruptedContainer(string candidate)
    {
        if (candidate.StartsWith('['))
        {
            return JsonRepairUtilities.ExtractBalancedJsonElements(candidate).Count > 0;
        }

        if (candidate.StartsWith('{'))
        {
            return JsonRepairUtilities.TryExtractTopLevelPropertiesFromCorruptedObject(
                candidate,
                new Dictionary<string, object?>(StringComparer.Ordinal));
        }

        return false;
    }

    private static bool IsObjectPayload(string s)
    {
        if (s.Length == 0) return false;
        var first = s[0];
        return first == '{' || first == '[';
    }

    private static string RemoveTrailingCommasOutsideStrings(string s)
    {
        StringBuilder? sb = null;
        bool inString = false;
        bool escaped = false;
        int lastCopiedIndex = 0;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == ',')
            {
                int j = i + 1;
                while (j < s.Length && (char.IsWhiteSpace(s[j]) || s[j] == ','))
                {
                    j++;
                }

                if (j < s.Length && (s[j] == '}' || s[j] == ']'))
                {
                    sb ??= new StringBuilder(s.Length);
                    sb.Append(s, lastCopiedIndex, i - lastCopiedIndex);
                    lastCopiedIndex = j;
                    i = j - 1;
                }
            }
        }

        if (sb == null)
        {
            return s;
        }

        sb.Append(s, lastCopiedIndex, s.Length - lastCopiedIndex);
        return sb.ToString();
    }

    private static string EscapeControlCharsInStrings(string s)
    {
        var sb = new StringBuilder(s.Length + 16);
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (escaped)
            {
                sb.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                sb.Append(c);
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                sb.Append(c);
                continue;
            }

            if (inString)
            {
                switch (c)
                {
                    case '\n':
                        sb.Append("\\n");
                        continue;
                    case '\r':
                        sb.Append("\\r");
                        continue;
                    case '\t':
                        sb.Append("\\t");
                        continue;
                    case < ' ':
                        sb.Append($"\\u{(int)c:x4}");
                        continue;
                }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Sanitizes any raw JSON string (e.g. tool call payload, full function call JSON, or arguments)
    /// by stripping leading and trailing markdown code fences, trimming whitespace, removing trailing
    /// commas, and repairing unescaped control characters in string values.
    /// </summary>
    public static string SanitizeJson(string raw, ILogger? logger = null, string? toolName = null, ToolCallHealingOptions? options = null)
    {
        return SanitizeArgumentsJson(raw, logger, toolName, options);
    }

    /// <summary>
    /// Sentinel key used by <see cref="ParseArgumentsJson"/> when the root JSON payload is a bare array
    /// rather than an object, allowing <see cref="LenientToolDispatcher"/> to map it to the tool's
    /// array parameter discovered from <c>AIFunction.JsonSchema</c> (or <c>"items"</c> via <c>ParameterAliases</c>).
    /// </summary>
    internal const string RootArraySentinelKey = "$rootArray";

    /// <summary>
    /// Sanitizes and parses a JSON arguments string into a strongly-typed dictionary,
    /// safe for Native AOT without reflection.
    /// </summary>
    public static IDictionary<string, object?> ParseArgumentsJson(
        string rawJson,
        ILogger? logger = null,
        string? toolName = null,
        ToolCallHealingOptions? options = null)
        => ParseArgumentsJsonCore(rawJson, logger, toolName, options, logWarningOnFailure: true);

    private static IDictionary<string, object?> ParseArgumentsJsonCore(
        string rawJson,
        ILogger? logger,
        string? toolName,
        ToolCallHealingOptions? options,
        bool logWarningOnFailure)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        var trimmed = rawJson.Trim();

        // Fast path: if clean JSON without markdown code fences, attempt direct single-pass parse first
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            try
            {
                using var quickDoc = JsonDocument.Parse(trimmed);
                if (quickDoc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var quickDict = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var prop in quickDoc.RootElement.EnumerateObject())
                    {
                        quickDict[prop.Name] = UnwrapJsonElement(prop.Value);
                    }
                    return quickDict;
                }
                if (quickDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    return new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [RootArraySentinelKey] = JsonRepairUtilities.ConvertJsonElementToClr(quickDoc.RootElement)
                    };
                }
            }
            catch (JsonException)
            {
                // Fall through to full sanitization on parse failure
            }
        }

        var sanitized = SanitizeArgumentsJson(rawJson, logger, toolName, options);
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(sanitized);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    dict[prop.Name] = UnwrapJsonElement(prop.Value);
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                dict[RootArraySentinelKey] = JsonRepairUtilities.ConvertJsonElementToClr(doc.RootElement);
            }
        }
        catch (Exception ex)
        {
            if (sanitized.StartsWith('[') && JsonRepairUtilities.ExtractBalancedJsonElements(sanitized).Count > 0)
            {
                dict[RootArraySentinelKey] = sanitized;
                options?.Telemetry?.Record(ToolCallHealingEventType.JsonRepaired, toolName, options.SessionId, "Corrupted array container");
            }
            else if (sanitized.StartsWith('{') && JsonRepairUtilities.TryExtractTopLevelPropertiesFromCorruptedObject(sanitized, dict))
            {
                options?.Telemetry?.Record(ToolCallHealingEventType.JsonRepaired, toolName, options.SessionId, "Corrupted object container");
            }
            else if (logWarningOnFailure)
            {
                logger?.LogWarning(ex, "Failed to parse sanitized JSON arguments for tool '{ToolName}'", toolName ?? "unknown");
            }
        }
        return dict;
    }

    /// <summary>
    /// Attempts to extract an unclosed pseudo-tool call (&lt;tool_call&gt; or &lt;function_call&gt;) lacking a closing tag.
    /// Extracts balanced JSON, repairs truncated JSON payloads, and promotes to <see cref="FunctionCallContent"/>.
    /// </summary>
    public static bool TryExtractUnclosedToolCall(
        ref string content,
        out FunctionCallContent? toolCall,
        out string? proseBefore,
        out string? proseAfter,
        ILogger? logger = null,
        ToolCallHealingOptions? options = null,
        ChatOptions? chatOptions = null)
    {
        toolCall = null;
        proseBefore = null;
        proseAfter = null;

        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var match = UnclosedToolCallRegex().Match(content);
        if (!match.Success)
        {
            return false;
        }

        int jsonStart = match.Index + match.Length;
        var balancedJson = JsonRepairUtilities.ExtractBalancedJson(content, jsonStart, out int consumedLen, out _);
        if (string.IsNullOrEmpty(balancedJson))
        {
            return false;
        }

        try
        {
            var sanitizedPayload = SanitizeJson(balancedJson, logger);
            if (TryExtractToolCallEnvelopeFields(sanitizedPayload, out var toolName, out var rawArgsJson, out var fallbackArgsDict, out var wasCorruptedEnvelope) &&
                !string.IsNullOrEmpty(toolName))
            {
                if (!ShouldPromoteToolCall(toolName, options, chatOptions))
                {
                    logger?.LogInformation("Rejected unclosed pseudo-tool-call candidate '{ToolName}' because it does not match declared tools.", toolName);
                    return false;
                }

                var generatedId = $"call_{Guid.NewGuid():N}"[..12];
                var parsedArgs = ResolveEnvelopeArguments(toolName, rawArgsJson, fallbackArgsDict, wasCorruptedEnvelope, logger, options);
                toolCall = new FunctionCallContent(generatedId, toolName, parsedArgs);

                proseBefore = content[..match.Index].Trim();

                int matchEnd = jsonStart + consumedLen;
                if (matchEnd < content.Length)
                {
                    var rem = content.Substring(matchEnd);
                    var fenceMatch = Regex.Match(rem, @"^\s*```(?:\s*</(?:tool_call|function_call)>)?");
                    if (fenceMatch.Success)
                    {
                        matchEnd += fenceMatch.Length;
                    }
                    else
                    {
                        var closeMatch = Regex.Match(rem, @"^\s*</(?:tool_call|function_call)>");
                        if (closeMatch.Success)
                        {
                            matchEnd += closeMatch.Length;
                        }
                    }
                }

                proseAfter = matchEnd < content.Length ? content[matchEnd..].Trim() : string.Empty;
                content = (proseBefore + (string.IsNullOrEmpty(proseAfter) ? "" : " " + proseAfter)).Trim();
                logger?.LogInformation("Extracted unclosed pseudo-tool-call from text for '{ToolName}' (id: {ToolCallId})", toolName, generatedId);
                options?.Telemetry?.Record(ToolCallHealingEventType.PseudoTagExtracted, toolName, options.SessionId);
                return true;
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to parse unclosed pseudo-tool-call payload: {Payload}", balancedJson);
        }

        return false;
    }

    /// <summary>
    /// Extracts pseudo-tool calls (<c>&lt;tool_call&gt;...&lt;/tool_call&gt;</c>, unclosed pseudo-tags, or ReAct <c>Action: ...\nAction Input: {...}</c>)
    /// from <paramref name="content"/>, converts them into strongly-typed <see cref="FunctionCallContent"/> instances,
    /// returns the preceding prose, extracted tool calls, and succeeding prose, and strips the matched tags from <paramref name="content"/>.
    /// </summary>
    public static bool TryExtractToolCalls(
        ref string content,
        out List<FunctionCallContent> toolCalls,
        out string? proseBefore,
        out string? proseAfter,
        ILogger? logger = null,
        ToolCallHealingOptions? options = null,
        ChatOptions? chatOptions = null)
    {
        toolCalls = new List<FunctionCallContent>();
        proseBefore = null;
        proseAfter = null;

        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        // 1. Try closed <tool_call> / <function_call> tags
        var matches = ToolCallRegex().Matches(content);
        if (matches.Count > 0)
        {
            var promotedMatches = new List<Match>();
            foreach (Match match in matches)
            {
                try
                {
                    var sanitizedPayload = SanitizeJson(match.Groups["payload"].Value, logger);
                    if (TryExtractToolCallEnvelopeFields(sanitizedPayload, out var toolName, out var rawArgsJson, out var fallbackArgsDict, out var wasCorruptedEnvelope) &&
                        !string.IsNullOrEmpty(toolName))
                    {
                        if (!ShouldPromoteToolCall(toolName, options, chatOptions))
                        {
                            logger?.LogInformation("Rejected pseudo-tool-call candidate '{ToolName}' because it does not match declared tools.", toolName);
                            continue;
                        }

                        var generatedId = $"call_{Guid.NewGuid():N}"[..12];
                        var parsedArgs = ResolveEnvelopeArguments(toolName, rawArgsJson, fallbackArgsDict, wasCorruptedEnvelope, logger, options);
                        toolCalls.Add(new FunctionCallContent(generatedId, toolName, parsedArgs));
                        promotedMatches.Add(match);
                        logger?.LogInformation("Extracted pseudo-tool-call from text for '{ToolName}' (id: {ToolCallId})", toolName, generatedId);
                        options?.Telemetry?.Record(ToolCallHealingEventType.PseudoTagExtracted, toolName, options.SessionId);
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to parse pseudo-tool-call payload in content: {Payload}", match.Groups["payload"].Value);
                }
            }

            if (toolCalls.Count > 0)
            {
                int firstIdx = promotedMatches[0].Index;
                int lastEnd = promotedMatches[^1].Index + promotedMatches[^1].Length;
                proseBefore = content[..firstIdx].Trim();
                proseAfter = content[lastEnd..].Trim();

                foreach (var match in promotedMatches)
                {
                    content = content.Replace(match.Value, string.Empty);
                }
                content = content.Trim();
                return true;
            }
        }

        // 2. Try unclosed <tool_call> tags lacking closing tag
        if (TryExtractUnclosedToolCall(ref content, out var unclosedCall, out proseBefore, out proseAfter, logger, options, chatOptions) && unclosedCall != null)
        {
            toolCalls.Add(unclosedCall);
            return true;
        }

        // 3. Try ReAct Action blocks (with or without markdown code fences in Action Input)
        var actionMatch = ActionHeaderRegex().Match(content);
        if (actionMatch.Success)
        {
            try
            {
                var toolName = actionMatch.Groups["name"].Value.Trim();
                int jsonStart = actionMatch.Index + actionMatch.Length;
                var balancedJson = JsonRepairUtilities.ExtractBalancedJson(content, jsonStart, out int consumedLen, out _);

                if (!string.IsNullOrEmpty(toolName) && !string.IsNullOrEmpty(balancedJson))
                {
                    if (ShouldPromoteToolCall(toolName, options, chatOptions))
                    {
                        var generatedId = $"call_{Guid.NewGuid():N}"[..12];
                        var parsedArgs = ParseArgumentsJson(balancedJson, logger, toolName, options);
                        toolCalls.Add(new FunctionCallContent(generatedId, toolName, parsedArgs));

                        proseBefore = content[..actionMatch.Index].Trim();
                        int matchEnd = jsonStart + consumedLen;
                        if (matchEnd < content.Length)
                        {
                            var rem = content.Substring(matchEnd);
                            var fenceMatch = Regex.Match(rem, @"^\s*```");
                            if (fenceMatch.Success)
                            {
                                matchEnd += fenceMatch.Length;
                            }
                        }

                        proseAfter = matchEnd < content.Length ? content[matchEnd..].Trim() : string.Empty;
                        content = (proseBefore + (string.IsNullOrEmpty(proseAfter) ? "" : " " + proseAfter)).Trim();
                        logger?.LogInformation("Extracted ReAct pseudo-tool-call from text for '{ToolName}' (id: {ToolCallId})", toolName, generatedId);
                        options?.Telemetry?.Record(ToolCallHealingEventType.ActionBlockExtracted, toolName, options.SessionId);
                        return true;
                    }
                    else
                    {
                        logger?.LogInformation("Rejected ReAct Action pseudo-tool-call candidate '{ToolName}' because it does not match declared tools.", toolName);
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to parse Action tool call payload in content.");
            }
        }

        return false;
    }

    /// <summary>
    /// Extracts pseudo-tool calls (<c>&lt;tool_call&gt;...&lt;/tool_call&gt;</c> or ReAct <c>Action: ...\nAction Input: {...}</c>)
    /// from <paramref name="content"/>, converts them into strongly-typed <see cref="FunctionCallContent"/> instances,
    /// and strips the matched tags from <paramref name="content"/>.
    /// </summary>
    public static bool TryExtractToolCalls(
        ref string content,
        out List<FunctionCallContent> toolCalls,
        ILogger? logger = null,
        ToolCallHealingOptions? options = null,
        ChatOptions? chatOptions = null)
        => TryExtractToolCalls(ref content, out toolCalls, out _, out _, logger, options, chatOptions);

    /// <summary>
    /// Extracts pseudo-tool calls from content with default healing options.
    /// </summary>
    public static bool TryExtractToolCalls(ref string content, out List<FunctionCallContent> toolCalls, ILogger? logger)
        => TryExtractToolCalls(ref content, out toolCalls, out _, out _, logger, null, null);

    /// <summary>
    /// Heals a <see cref="ChatMessage"/> by extracting any pseudo-tool calls (<c>&lt;tool_call&gt;</c> or <c>Action:</c>)
    /// embedded in <see cref="TextContent"/> into structured <see cref="FunctionCallContent"/> items,
    /// generating missing tool call IDs, sanitizing function argument payloads, and splitting preceding prose,
    /// tool calls, and succeeding prose into sequential <see cref="ChatMessage"/> instances for lossless preservation.
    /// </summary>
    public static List<ChatMessage> HealChatMessage(
        ChatMessage message,
        ILogger? logger = null,
        ToolCallHealingOptions? options = null,
        ChatOptions? chatOptions = null)
    {
        if (message == null)
        {
            return [];
        }

        if (message.Contents == null || message.Contents.Count == 0)
        {
            return [message];
        }

        // 0. Thinking trace runaway protection:
        // If content is an unclosed <think> / <thought> block that exhausted token limit, preserve thought content as text.
        for (int i = 0; i < message.Contents.Count; i++)
        {
            if (message.Contents[i] is TextContent tc && !string.IsNullOrWhiteSpace(tc.Text))
            {
                var text = tc.Text.Trim();
                if ((text.StartsWith("<think>", StringComparison.OrdinalIgnoreCase) || text.StartsWith("<thought>", StringComparison.OrdinalIgnoreCase)) &&
                    !text.Contains("</think>", StringComparison.OrdinalIgnoreCase) && !text.Contains("</thought>", StringComparison.OrdinalIgnoreCase))
                {
                    var match = UnclosedThinkRegex().Match(text);
                    if (match.Success)
                    {
                        var thoughtText = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(thoughtText))
                        {
                            message.Contents[i] = new TextContent(thoughtText);
                        }
                    }
                }
            }
        }

        // 1. Sanitize existing FunctionCallContent items before extracting pseudo-tool calls from text
        for (int i = 0; i < message.Contents.Count; i++)
        {
            if (message.Contents[i] is FunctionCallContent fcc)
            {
                message.Contents[i] = SanitizeFunctionCallContent(fcc, logger, options, chatOptions);
            }
        }

        // 2. Check for pseudo-tool calls in TextContent
        for (int i = 0; i < message.Contents.Count; i++)
        {
            if (message.Contents[i] is TextContent textContent && !string.IsNullOrWhiteSpace(textContent.Text))
            {
                var originalText = textContent.Text;
                var text = textContent.Text;
                if (TryExtractToolCalls(ref text, out var extractedCalls, out var proseBefore, out var proseAfter, logger, options, chatOptions) && extractedCalls.Count > 0)
                {
                    message.Contents.RemoveAt(i);
                    int insertIdx = i;

                    if (!string.IsNullOrWhiteSpace(proseBefore))
                    {
                        message.Contents.Insert(insertIdx++, new TextContent(proseBefore));
                    }

                    foreach (var call in extractedCalls)
                    {
                        message.Contents.Insert(insertIdx++, call);
                    }

                    if (!string.IsNullOrWhiteSpace(proseAfter))
                    {
                        message.Contents.Insert(insertIdx++, new TextContent(proseAfter));
                    }

                    i = insertIdx - 1;
                }
                else if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(originalText))
                {
                    // Non-empty text safeguard: if stripping reduced text to whitespace with no tool calls, retain original text
                    message.Contents[i] = new TextContent(originalText.Trim());
                }
            }
        }

        // 3. Fallback: if no tool calls and no non-empty text content, but reasoning content exists, surface reasoning as text
        bool hasToolCalls = message.Contents.OfType<FunctionCallContent>().Any();
        bool hasNonEmptyText = message.Contents.OfType<TextContent>().Any(tc => !string.IsNullOrWhiteSpace(tc.Text));
        if (!hasToolCalls && !hasNonEmptyText)
        {
            var reasoning = message.Contents.OfType<TextReasoningContent>().FirstOrDefault(rc => !string.IsNullOrWhiteSpace(rc.Text));
            if (reasoning != null)
            {
                message.Contents.Add(new TextContent(reasoning.Text));
            }
        }

        return SplitChatMessage(message);
    }

    /// <summary>
    /// Heals a <see cref="ChatMessage"/> with default options, returning sequential split messages.
    /// </summary>
    public static List<ChatMessage> HealChatMessage(ChatMessage message, ILogger? logger)
        => HealChatMessage(message, logger, null, null);

    /// <summary>
    /// Splits a <see cref="ChatMessage"/> containing both prose and tool calls into multiple sequential
    /// <see cref="ChatMessage"/> instances (e.g. preceding prose, tool calls, and succeeding prose)
    /// to ensure that downstream consumers and formatters preserve both losslessly.
    /// </summary>
    public static List<ChatMessage> SplitChatMessage(ChatMessage message)
    {
        if (message == null)
        {
            return [];
        }

        if (message.Contents == null || message.Contents.Count == 0)
        {
            return [message];
        }

        bool hasToolCalls = message.Contents.OfType<FunctionCallContent>().Any();
        bool hasProse = message.Contents.OfType<TextContent>().Any(tc => !string.IsNullOrWhiteSpace(tc.Text));

        if (!hasToolCalls || !hasProse)
        {
            return [message];
        }

        var result = new List<ChatMessage>();
        var currentRun = new List<AIContent>();
        bool? currentIsFunctionCall = null;

        foreach (var content in message.Contents)
        {
            if (content is TextContent tc && string.IsNullOrWhiteSpace(tc.Text))
            {
                continue;
            }

            bool isFuncCall = content is FunctionCallContent;

            if (currentIsFunctionCall.HasValue && currentIsFunctionCall.Value != isFuncCall)
            {
                if (currentRun.Count > 0)
                {
                    result.Add(CreateSplitMessage(message, currentRun));
                    currentRun = new List<AIContent>();
                }
            }

            currentIsFunctionCall = isFuncCall;
            currentRun.Add(content);
        }

        if (currentRun.Count > 0)
        {
            result.Add(CreateSplitMessage(message, currentRun));
        }

        return result.Count > 0 ? result : [message];
    }

    private static ChatMessage CreateSplitMessage(ChatMessage template, IList<AIContent> contents)
    {
        var msg = new ChatMessage(template.Role, contents)
        {
            AuthorName = template.AuthorName,
            RawRepresentation = template.RawRepresentation
        };

        if (template.AdditionalProperties != null)
        {
            msg.AdditionalProperties = new AdditionalPropertiesDictionary(template.AdditionalProperties);
        }

        return msg;
    }

    /// <summary>
    /// Heals a <see cref="ChatResponse"/> by healing all contained messages, splitting prose and tool calls
    /// into separate messages for lossless preservation, and ensuring <see cref="ChatResponse.FinishReason"/>
    /// is set to <see cref="ChatFinishReason.ToolCalls"/> if any function calls are present.
    /// </summary>
    public static ChatResponse HealChatResponse(
        ChatResponse response,
        ILogger? logger = null,
        ToolCallHealingOptions? options = null,
        ChatOptions? chatOptions = null)
    {
        if (response == null || response.Messages == null)
        {
            return response!;
        }

        options ??= new ToolCallHealingOptions();
        var newMessages = new List<ChatMessage>();
        bool hasToolCalls = false;

        foreach (var msg in response.Messages)
        {
            var healedList = HealChatMessage(msg, logger, options, chatOptions);
            foreach (var hm in healedList)
            {
                if (hm.Contents?.OfType<FunctionCallContent>().Any() == true)
                {
                    hasToolCalls = true;
                }
                newMessages.Add(hm);
            }
        }

        response.Messages = newMessages;

        if (hasToolCalls && response.FinishReason != ChatFinishReason.ToolCalls)
        {
            response.FinishReason = ChatFinishReason.ToolCalls;
        }

        return response;
    }

    /// <summary>
    /// Heals a <see cref="ChatResponse"/> with default options.
    /// </summary>
    public static ChatResponse HealChatResponse(ChatResponse response, ILogger? logger)
        => HealChatResponse(response, logger, null, null);

    internal static FunctionCallContent SanitizeFunctionCallContent(
        FunctionCallContent fcc,
        ILogger? logger,
        ToolCallHealingOptions? options,
        ChatOptions? chatOptions = null)
    {
        var isCallIdGenerated = string.IsNullOrWhiteSpace(fcc.CallId);
        var callId = isCallIdGenerated ? $"call_{Guid.NewGuid():N}"[..12] : fcc.CallId;
        if (isCallIdGenerated)
        {
            options?.Telemetry?.Record(ToolCallHealingEventType.CallIdGenerated, fcc.Name, options.SessionId);
        }

        var arguments = fcc.Arguments;

        // If arguments contains a single "input" property with a string that looks like JSON,
        // unwrap it only when synthetic input unwrapping is enabled and the target tool does not legitimately declare an "input" parameter.
        if ((options?.UnwrapSyntheticInputArgument ?? true) &&
            arguments is { Count: 1 } &&
            arguments.TryGetValue("input", out var inputVal) &&
            inputVal is string strInput &&
            LooksLikeJsonOrFencedJsonContainer(strInput) &&
            !ToolDeclaresInputParameter(fcc.Name, options, chatOptions))
        {
            var parsed = ParseArgumentsJsonCore(strInput, logger, fcc.Name, options, logWarningOnFailure: false);
            if (parsed.Count > 0)
            {
                arguments = parsed;
                options?.Telemetry?.Record(ToolCallHealingEventType.ArgumentUnwrapped, fcc.Name, options.SessionId);
            }
        }

        if (callId != fcc.CallId || !ReferenceEquals(arguments, fcc.Arguments))
        {
            return new FunctionCallContent(callId, fcc.Name, arguments);
        }

        return fcc;
    }

    private static bool LooksLikeJsonOrFencedJsonContainer(string strInput)
    {
        var trimmed = strInput.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            return true;
        }

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var unFenced = StripMarkdownFences(trimmed);
            return IsObjectPayload(unFenced);
        }

        return false;
    }

    private static bool ToolDeclaresInputParameter(
        string toolName,
        ToolCallHealingOptions? options,
        ChatOptions? chatOptions)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        var dispatcher = options?.Dispatcher ?? chatOptions.GetToolDispatcher();
        if (dispatcher != null)
        {
            if (dispatcher.DeclaresParameter(toolName, "input"))
            {
                return true;
            }

            if (dispatcher.IsTranslation(toolName))
            {
                return false;
            }
        }

        if (chatOptions?.Tools is { Count: > 0 })
        {
            var stripped = LenientToolDispatcher.StripNamespace(toolName.Trim());
            var normToolName = ToolHelpers.NormalizeToolIdentifier(toolName);
            var normStripped = ToolHelpers.NormalizeToolIdentifier(stripped);
            var canonical = dispatcher != null ? dispatcher.ResolveCanonicalToolName(toolName) : stripped;
            var normCanonical = ToolHelpers.NormalizeToolIdentifier(canonical);

            foreach (var tool in chatOptions.Tools)
            {
                if (tool is not AIFunctionDeclaration fnDecl || string.IsNullOrWhiteSpace(fnDecl.Name))
                {
                    continue;
                }

                var declaredName = fnDecl.Name;
                var declaredStripped = LenientToolDispatcher.StripNamespace(declaredName.Trim());
                var normDeclared = ToolHelpers.NormalizeToolIdentifier(declaredName);
                var normDeclaredStripped = ToolHelpers.NormalizeToolIdentifier(declaredStripped);

                bool matchesTool =
                    string.Equals(declaredName, toolName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(declaredName, stripped, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(declaredName, canonical, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(declaredStripped, stripped, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(normDeclared) &&
                     (string.Equals(normDeclared, normToolName, StringComparison.Ordinal) ||
                      string.Equals(normDeclared, normStripped, StringComparison.Ordinal) ||
                      string.Equals(normDeclared, normCanonical, StringComparison.Ordinal) ||
                      string.Equals(normDeclaredStripped, normStripped, StringComparison.Ordinal))) ||
                    (dispatcher != null && dispatcher.IsToolAvailable(toolName, [tool]));

                if (matchesTool)
                {
                    var props = SchemaHelpers.ExtractSchemaProperties(fnDecl.JsonSchema);
                    if (props != null &&
                        (props.Contains("input") ||
                         props.Any(p => string.Equals(ToolHelpers.NormalizeToolIdentifier(p), "input", StringComparison.Ordinal))))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    internal static object? UnwrapJsonElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            if (element.TryGetInt64(out var l))
            {
                if (l >= int.MinValue && l <= int.MaxValue)
                {
                    return (int)l;
                }
                return l;
            }
            return element.GetDouble();
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.Clone()
        };
    }

    internal static string SerializeArgumentsToJson(IDictionary<string, object?>? args)
    {
        if (args == null || args.Count == 0)
        {
            return "{}";
        }
        var obj = new JsonObject();
        foreach (var (k, v) in args)
        {
            obj[k] = ConvertObjectToJsonNode(v);
        }
        return obj.ToJsonString();
    }

    private static JsonNode? ConvertObjectToJsonNode(object? value)
    {
        if (value == null) return null;
        if (value is JsonNode jn) return jn.DeepClone();
        if (value is JsonElement je) return JsonNode.Parse(je.GetRawText());
        if (value is string s) return JsonValue.Create(s);
        if (value is bool b) return JsonValue.Create(b);
        if (value is int i) return JsonValue.Create(i);
        if (value is long l) return JsonValue.Create(l);
        if (value is double d) return JsonValue.Create(d);
        if (value is float f) return JsonValue.Create(f);
        if (value is decimal dec) return JsonValue.Create(dec);
        if (value is IDictionary<string, object?> dict)
        {
            var jo = new JsonObject();
            foreach (var (k, v) in dict)
            {
                jo[k] = ConvertObjectToJsonNode(v);
            }
            return jo;
        }
        if (value is System.Collections.IEnumerable list)
        {
            var ja = new JsonArray();
            foreach (var item in list)
            {
                ja.Add(ConvertObjectToJsonNode(item));
            }
            return ja;
        }
        return JsonValue.Create(value.ToString());
    }

    private static bool IsValidJson(string json)
    {
        try
        {
            using var _ = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryExtractToolCallEnvelopeFields(
        string sanitizedPayload,
        out string? toolName,
        out string? rawArgsJson,
        out IDictionary<string, object?>? fallbackArgsDict,
        out bool wasCorruptedEnvelope)
    {
        toolName = null;
        rawArgsJson = null;
        fallbackArgsDict = null;
        wasCorruptedEnvelope = false;

        try
        {
            var payloadNode = JsonNode.Parse(sanitizedPayload);
            toolName = payloadNode?["name"]?.GetValue<string>();
            var argsNode = payloadNode?["arguments"] ?? payloadNode?["parameters"];
            rawArgsJson = argsNode is JsonValue argVal && argVal.TryGetValue<string>(out var strArg)
                ? strArg
                : argsNode?.ToJsonString() ?? "{}";
            return !string.IsNullOrEmpty(toolName);
        }
        catch (JsonException)
        {
            var envelopeDict = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (!JsonRepairUtilities.TryExtractTopLevelPropertiesFromCorruptedObject(sanitizedPayload, envelopeDict))
            {
                return false;
            }

            if (!envelopeDict.TryGetValue("name", out var nameObj) || nameObj is not string recoveredName || string.IsNullOrEmpty(recoveredName))
            {
                return false;
            }

            toolName = recoveredName;
            wasCorruptedEnvelope = true;

            if (envelopeDict.TryGetValue("arguments", out var argsObj) || envelopeDict.TryGetValue("parameters", out argsObj))
            {
                if (argsObj is IDictionary<string, object?> argsDict)
                {
                    fallbackArgsDict = argsDict;
                }
                else if (argsObj is string argsStr)
                {
                    rawArgsJson = argsStr;
                }
                else
                {
                    fallbackArgsDict = new Dictionary<string, object?>();
                }
            }
            else
            {
                fallbackArgsDict = new Dictionary<string, object?>();
            }

            return true;
        }
    }

    private static IDictionary<string, object?> ResolveEnvelopeArguments(
        string toolName,
        string? rawArgsJson,
        IDictionary<string, object?>? fallbackArgsDict,
        bool wasCorruptedEnvelope,
        ILogger? logger,
        ToolCallHealingOptions? options)
    {
        if (fallbackArgsDict != null)
        {
            logger?.LogInformation("Recovered arguments dictionary from corrupted tool call envelope for '{ToolName}'.", toolName);
            options?.Telemetry?.Record(ToolCallHealingEventType.JsonRepaired, toolName, options.SessionId);
            return fallbackArgsDict;
        }

        if (wasCorruptedEnvelope)
        {
            options?.Telemetry?.Record(ToolCallHealingEventType.JsonRepaired, toolName, options.SessionId);
        }

        return ParseArgumentsJson(rawArgsJson ?? "{}", logger, toolName, options);
    }
}
