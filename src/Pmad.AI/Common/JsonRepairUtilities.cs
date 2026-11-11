using System.Text;
using System.Text.Json;

namespace Pmad.AI.Common;

/// <summary>
/// Utilities for leniently parsing and repairing tool-call argument JSON that local/open-weights
/// models frequently emit in a truncated or malformed state (e.g. a missing closing bracket, an
/// unterminated string, or a dangling key). All repairs are conservative: the repaired payload is
/// only accepted when it re-parses as valid JSON, and already-balanced input is never modified.
/// Reflection-free and Native AOT compatible.
/// </summary>
public static class JsonRepairUtilities
{
    /// <summary>
    /// Converts a <see cref="JsonElement"/> into plain CLR objects: dictionaries, lists, strings,
    /// numbers (int/long/double), booleans, or null.
    /// </summary>
    public static object? ConvertJsonElementToClr(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var prop in element.EnumerateObject())
                {
                    dict[prop.Name] = ConvertJsonElementToClr(prop.Value);
                }
                return dict;
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(ConvertJsonElementToClr(item));
                }
                return list;
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l))
                {
                    if (l >= int.MinValue && l <= int.MaxValue)
                    {
                        return (int)l;
                    }
                    return l;
                }
                return element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
                return null;
            default:
                return element.Clone();
        }
    }

    /// <summary>
    /// Parses a stringified JSON value (object or array). If the direct parse fails, attempts a
    /// conservative repair of a truncated payload (e.g. a missing closing bracket or unterminated
    /// string) before re-parsing. Returns null when the value is not (repairable) JSON.
    /// </summary>
    public static object? TryParseStringifiedJson(string trimmed, out bool wasRepaired)
    {
        wasRepaired = false;
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            return ConvertJsonElementToClr(doc.RootElement);
        }
        catch (JsonException)
        {
            // Fall through to repair below.
        }

        var repaired = TryRepairTruncatedJsonPrefix(trimmed);
        if (repaired == null)
        {
            try
            {
                using var lenientDoc = JsonDocument.Parse(trimmed, LenientDocumentOptions);
                wasRepaired = true;
                return ConvertJsonElementToClr(lenientDoc.RootElement);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(repaired, LenientDocumentOptions);
            wasRepaired = true;
            return ConvertJsonElementToClr(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Attempts to repair a truncated JSON payload (object or array) by dropping any dangling key,
    /// closing any unterminated string, and appending the closing brackets/braces implied by the
    /// still-open structure. Returns null when the input was not a repairable JSON prefix (e.g.
    /// already balanced or not JSON-shaped at all).
    /// </summary>
    public static string? TryRepairTruncatedJson(string json)
    {
        if (string.IsNullOrEmpty(json) || (json[0] != '[' && json[0] != '{'))
        {
            return null;
        }

        return TryRepairTruncatedJsonPrefix(json);
    }

    /// <summary>
    /// Shared truncated-JSON repair. Given a valid JSON *prefix* (a string cut off mid-stream), produces a
    /// complete, parseable JSON document by dropping any dangling key, closing an unterminated string, and
    /// appending the closers implied by the still-open containers. Returns null when no repair is applicable
    /// (i.e. the input is already a balanced/complete JSON document).
    /// </summary>
    public static string? TryRepairTruncatedJsonPrefix(string json)
    {
        const int R_KEY = 0;    // object: expecting a key (right after '{' or ',')
        const int R_COLON = 1;  // object: key complete, expecting ':'
        const int R_VALUE = 2;  // object (after ':') or array: expecting a value / element
        const int R_AFTER = 3;  // a value / element just completed; expecting ',' or a closer

        var stack = new Stack<char>(8);
        var roles = new Stack<int>(8);
        var openCuts = new Stack<int>(8); // safeCut before each container was opened
        int safeCut = 0;               // highest index that is a valid truncation point (end of last complete entry)
        bool inString = false;
        bool escaped = false;
        bool pendingStringIsKey = false;
        bool inScalar = false;
        int scalarStart = -1;

        int TopRole() => roles.Count > 0 ? roles.Peek() : -1;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];

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
                    if (pendingStringIsKey)
                    {
                        // The key is complete; the object now expects a ':' (stays R_KEY while the key string was open).
                        roles.Pop(); roles.Push(R_COLON);
                    }
                    else
                    {
                        roles.Pop(); roles.Push(R_AFTER);
                        safeCut = i + 1;
                    }
                    pendingStringIsKey = false;
                }
                continue;
            }

            if (inScalar)
            {
                // A scalar (number / true / false / null) ends at the next structural character.
                if (c == ',' || c == '}' || c == ']')
                {
                    safeCut = i;         // cutting just before this char keeps the whole scalar
                    inScalar = false;
                    if (roles.Count > 0)
                    {
                        roles.Pop();
                        roles.Push(R_AFTER);
                    }
                }
                else
                {
                    continue; // digit, '.', '-' or whitespace inside/after the scalar
                }
            }

            if (c is ' ' or '\t' or '\r' or '\n') continue;

            switch (c)
            {
                case '"':
                    inString = true;
                    pendingStringIsKey = TopRole() == R_KEY;
                    break;
                case '{':
                    openCuts.Push(safeCut);
                    stack.Push('{'); roles.Push(R_KEY); safeCut = i + 1;
                    break;
                case '[':
                    openCuts.Push(safeCut);
                    stack.Push('['); roles.Push(R_VALUE); safeCut = i + 1;
                    break;
                case ':':
                    if (TopRole() == R_COLON) { roles.Pop(); roles.Push(R_VALUE); }
                    break;
                case ',':
                    if (roles.Count > 0)
                    {
                        roles.Pop();
                        roles.Push(stack.Peek() == '{' ? R_KEY : R_VALUE);
                    }
                    break;
                case '}':
                    if (stack.Count > 0 && stack.Peek() == '{') { stack.Pop(); roles.Pop(); openCuts.Pop(); }
                    if (roles.Count > 0) { roles.Pop(); roles.Push(R_AFTER); }
                    safeCut = i + 1;
                    break;
                case ']':
                    if (stack.Count > 0 && stack.Peek() == '[') { stack.Pop(); roles.Pop(); openCuts.Pop(); }
                    if (roles.Count > 0) { roles.Pop(); roles.Push(R_AFTER); }
                    safeCut = i + 1;
                    break;
                default:
                    // First character of a scalar value (digit, sign, or t/f/n of true/false/null).
                    if (TopRole() == R_VALUE)
                    {
                        inScalar = true;
                        scalarStart = i;
                    }
                    break;
            }
        }

        if (stack.Count == 0 && !inString)
        {
            return null; // Already a complete, balanced document.
        }

        int cut = json.Length;
        string? scalarReplacement = null;
        bool dropEmptyOpenContainers = false;

        if (inString)
        {
            // An unterminated string: drop it if it is a dangling key, otherwise keep and close it.
            if (pendingStringIsKey)
            {
                cut = safeCut;
                dropEmptyOpenContainers = true;
            }
            else
            {
                cut = TrimIncompleteStringEscape(json, cut, escaped);
            }
        }
        else if (inScalar)
        {
            // Truncated inside a scalar: complete partial true/false/null or repair partial number;
            // if the scalar cannot be salvaged (e.g. a lone '-' or key-value without digits), drop back to safeCut.
            if (TryRepairPartialScalar(json.AsSpan(scalarStart, json.Length - scalarStart), out var repairedScalar))
            {
                cut = scalarStart;
                scalarReplacement = repairedScalar;
            }
            else
            {
                cut = safeCut;
                dropEmptyOpenContainers = true;
            }
        }
        else
        {
            int tr = TopRole();
            if (tr == R_KEY || tr == R_COLON || tr == R_VALUE)
            {
                cut = safeCut; // dangling entry (missing key, missing value, or missing element)
                dropEmptyOpenContainers = true;
            }
        }

        // If dropping a dangling key left an inner object inside an array completely empty (e.g. `[{"prom` -> `[`),
        // or left a `key: {` object property with no completed fields, pop that inner `{` so we don't emit a bogus `{}` element.
        if (dropEmptyOpenContainers)
        {
            while (stack.Count > 1 && stack.Peek() == '{' && IsOnlyOpenObjectInArraySince(json, openCuts.Peek(), cut))
            {
                stack.Pop();
                roles.Pop();
                cut = openCuts.Pop();
            }
        }

        var sb = new StringBuilder(json.Length + 8);
        sb.Append(json, 0, cut);
        if (scalarReplacement != null)
        {
            sb.Append(scalarReplacement);
        }
        else if (inString && !pendingStringIsKey)
        {
            sb.Append('"');
        }

        int end = sb.Length;
        while (end > 0 && sb[end - 1] is ',' or ' ' or '\t' or '\r' or '\n')
        {
            end--;
        }
        sb.Length = end;

        while (stack.Count > 0)
        {
            char open = stack.Pop();
            sb.Append(open == '[' ? ']' : '}');
        }
        return sb.ToString();
    }

    private static bool IsOnlyOpenObjectInArraySince(string json, int prevSafeCut, int currentCut)
    {
        // Checks whether the region between prevSafeCut and currentCut only opened an object `{` as an array element
        // (i.e. only whitespace/comma precedes `{` up to prevSafeCut, with no `:` from a parent object property).
        bool sawOpenBrace = false;
        for (int i = currentCut - 1; i >= prevSafeCut; i--)
        {
            char c = json[i];
            if (c is ' ' or '\t' or '\r' or '\n') continue;
            if (!sawOpenBrace && c == '{')
            {
                sawOpenBrace = true;
                continue;
            }
            if (c == ',') continue;
            return false;
        }
        return sawOpenBrace;
    }

    private static int TrimIncompleteStringEscape(string json, int cut, bool endsWithDanglingBackslash)
    {
        if (endsWithDanglingBackslash && cut > 0 && json[cut - 1] == '\\')
        {
            return cut - 1;
        }

        // Check for an incomplete \uXXXX escape at the tail of the string (1 to 3 hex digits after \u).
        int maxLookback = Math.Min(5, cut);
        for (int back = 2; back <= maxLookback; back++)
        {
            int idx = cut - back;
            if (json[idx] == '\\' && idx + 1 < cut && json[idx + 1] == 'u')
            {
                // Count preceding consecutive backslashes to ensure this '\' is not itself escaped.
                int slashes = 0;
                for (int k = idx - 1; k >= 0 && json[k] == '\\'; k--)
                {
                    slashes++;
                }
                if ((slashes & 1) == 0)
                {
                    return idx;
                }
            }
        }

        return cut;
    }

    private static bool TryRepairPartialScalar(ReadOnlySpan<char> rawScalar, out string repaired)
    {
        var trimmed = rawScalar.Trim();
        if (trimmed.IsEmpty)
        {
            repaired = string.Empty;
            return false;
        }

        // Prefix of "true", "false", or "null"
        if ("true".AsSpan().StartsWith(trimmed, StringComparison.Ordinal))
        {
            repaired = "true";
            return true;
        }
        if ("false".AsSpan().StartsWith(trimmed, StringComparison.Ordinal))
        {
            repaired = "false";
            return true;
        }
        if ("null".AsSpan().StartsWith(trimmed, StringComparison.Ordinal))
        {
            repaired = "null";
            return true;
        }

        // Numeric scalar: strip trailing incomplete exponent ('e', 'E', 'e+', 'e-') or trailing decimal point '.'
        var num = trimmed;
        if (num[^1] is '+' or '-')
        {
            num = num[..^1];
        }
        if (!num.IsEmpty && num[^1] is 'e' or 'E')
        {
            num = num[..^1];
        }
        if (!num.IsEmpty && num[^1] == '.')
        {
            num = num[..^1];
        }

        // Must have at least one digit and parse as a valid JSON number
        if (num.IsEmpty || (num.Length == 1 && num[0] == '-'))
        {
            repaired = string.Empty;
            return false;
        }

        for (int i = 0; i < num.Length; i++)
        {
            char c = num[i];
            if (!((c >= '0' && c <= '9') || c is '-' or '+' or '.' or 'e' or 'E'))
            {
                repaired = string.Empty;
                return false;
            }
        }

        repaired = num.ToString();
        return true;
    }


    /// <summary>
    /// Extracts a balanced JSON object or array starting from or after <paramref name="startIndex"/>.
    /// Handles nested braces/brackets, string literals, and escape sequences, repairing truncated JSON if necessary.
    /// </summary>
    public static string? ExtractBalancedJson(string text, int startIndex, out int consumedLength, out bool isTruncated)
    {
        consumedLength = 0;
        isTruncated = false;
        if (string.IsNullOrEmpty(text) || startIndex < 0 || startIndex >= text.Length)
        {
            return null;
        }

        int startBrace = text.IndexOf('{', startIndex);
        int startBracket = text.IndexOf('[', startIndex);
        int startPos;
        if (startBrace >= 0 && startBracket >= 0)
        {
            startPos = Math.Min(startBrace, startBracket);
        }
        else if (startBrace >= 0)
        {
            startPos = startBrace;
        }
        else if (startBracket >= 0)
        {
            startPos = startBracket;
        }
        else
        {
            return null;
        }

        var openStack = new Stack<char>();
        bool inString = false;
        bool escaped = false;

        for (int i = startPos; i < text.Length; i++)
        {
            char c = text[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (!inString)
            {
                if (c == '{' || c == '[')
                {
                    openStack.Push(c);
                }
                else if (c == '}' || c == ']')
                {
                    if (openStack.Count > 0 && ((c == '}' && openStack.Peek() == '{') || (c == ']' && openStack.Peek() == '[')))
                    {
                        openStack.Pop();
                    }
                    if (openStack.Count == 0)
                    {
                        consumedLength = (i - startIndex) + 1;
                        isTruncated = false;
                        return text.Substring(startPos, i - startPos + 1);
                    }
                }
            }
        }

        if (openStack.Count > 0)
        {
            consumedLength = text.Length - startIndex;
            isTruncated = true;
            var raw = text.Substring(startPos);
            var repaired = JsonRepairUtilities.TryRepairTruncatedJson(raw);
            if (repaired != null)
            {
                return repaired;
            }

            var sb = new StringBuilder(raw);
            if (escaped)
            {
                sb.Append('\\');
            }
            if (inString)
            {
                sb.Append('"');
            }
            while (openStack.Count > 0)
            {
                sb.Append(openStack.Pop() == '[' ? ']' : '}');
            }
            return sb.ToString();
        }

        return null;
    }

    private static readonly JsonDocumentOptions LenientDocumentOptions = new()
    {
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Extracts individual balanced JSON elements (objects or arrays) from an array container string,
    /// skipping any rogue delimiters or tokens between elements.
    /// Handles truncated trailing elements using <see cref="TryRepairTruncatedJson"/>.
    /// Reflection-free and Native AOT compatible.
    /// </summary>
    /// <param name="text">The raw or corrupted JSON array string.</param>
    /// <param name="openDelimiter">Optional open delimiter ('{' for objects, '[' for arrays, or null for either).</param>
    /// <returns>A list of raw JSON element strings.</returns>
    public static List<string> ExtractBalancedJsonElements(string text, char? openDelimiter = null)
    {
        var elements = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return elements;
        }

        var trimmed = text.Trim();
        int searchStart = 0;
        int containerEnd = text.Length;

        int firstBracket = text.IndexOf('[');
        bool hasOuterArrayBracket = firstBracket >= 0 && trimmed.StartsWith('[') &&
            IsOuterArrayContainer(text, firstBracket, openDelimiter);
        int lastOuterBracket = hasOuterArrayBracket && trimmed.EndsWith(']') ? text.LastIndexOf(']') : -1;
        if (hasOuterArrayBracket)
        {
            searchStart = firstBracket + 1;
        }

        int currentIndex = searchStart;
        while (currentIndex < containerEnd)
        {
            int nextOpen = FindNextOpenDelimiter(text, currentIndex, containerEnd, openDelimiter);
            if (nextOpen < 0 || nextOpen >= containerEnd)
            {
                break;
            }

            string? block = ExtractBalancedJson(text, nextOpen, out int consumedLength, out bool isTruncated);
            if (!string.IsNullOrEmpty(block) && IsValidExtractedElement(block, openDelimiter))
            {
                elements.Add(block);
                currentIndex = nextOpen + Math.Max(1, consumedLength);
                continue;
            }

            // If the trailing element was unclosed before the outer array's closing ']', retry without the outer ']'.
            if (isTruncated && lastOuterBracket > nextOpen)
            {
                string? trimmedBlock = ExtractBalancedJson(text[..lastOuterBracket], nextOpen, out int trimmedConsumed, out _);
                if (!string.IsNullOrEmpty(trimmedBlock) && IsValidExtractedElement(trimmedBlock, openDelimiter))
                {
                    elements.Add(trimmedBlock);
                    currentIndex = nextOpen + Math.Max(1, trimmedConsumed);
                    continue;
                }
            }

            // If an earlier element was missing its closing brace/bracket before the next element or had an extra closer,
            // attempt to repair the prefix up to the corruption boundary.
            if (TryRepairElementBeforeNextSibling(text, nextOpen, containerEnd, openDelimiter, out var repairedSiblingBlock, out int nextSiblingIndex))
            {
                elements.Add(repairedSiblingBlock);
                currentIndex = nextSiblingIndex;
                continue;
            }

            currentIndex = nextOpen + 1;
        }

        return elements;
    }

    private static bool IsOuterArrayContainer(string text, int firstBracket, char? openDelimiter)
    {
        if (openDelimiter == '{')
        {
            return true;
        }

        int i = firstBracket + 1;
        while (i < text.Length && text[i] is ' ' or '\t' or '\r' or '\n')
        {
            i++;
        }

        if (i < text.Length && text[i] is '{' or '[' or ']')
        {
            return true;
        }

        // If the leading '[' starts a self-contained 1D array (e.g. "[1, 2], [3, 4]") followed by another '[' or '{' element,
        // treat the leading '[' as the first element rather than an outer container.
        var firstBlock = ExtractBalancedJson(text, firstBracket, out int consumedLength, out bool isTruncated);
        if (!isTruncated && !string.IsNullOrEmpty(firstBlock) && consumedLength > 0)
        {
            int nextOpen = FindNextOpenDelimiter(text, firstBracket + consumedLength, text.Length, openDelimiter);
            if (nextOpen >= 0)
            {
                return false;
            }
        }

        return true;
    }

    private static int FindNextOpenDelimiter(string text, int startIndex, int endIndex, char? openDelimiter)
    {
        bool inString = false;
        bool escaped = false;

        for (int i = startIndex; i < endIndex; i++)
        {
            char c = text[i];
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

            if (openDelimiter.HasValue)
            {
                if (c == openDelimiter.Value)
                {
                    return i;
                }
            }
            else if (c == '{' || c == '[')
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsValidExtractedElement(string block, char? openDelimiter, bool rejectEmptyContainer = false)
    {
        try
        {
            using var doc = JsonDocument.Parse(block, LenientDocumentOptions);
            var root = doc.RootElement;
            bool kindMatches = openDelimiter switch
            {
                '{' => root.ValueKind == JsonValueKind.Object,
                '[' => root.ValueKind == JsonValueKind.Array,
                _ => root.ValueKind is JsonValueKind.Object or JsonValueKind.Array
            };

            if (!kindMatches)
            {
                return false;
            }

            if (rejectEmptyContainer)
            {
                if (root.ValueKind == JsonValueKind.Object && !root.EnumerateObject().MoveNext())
                {
                    return false;
                }
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 0)
                {
                    return false;
                }
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryRepairElementBeforeNextSibling(
        string text,
        int startIndex,
        int endIndex,
        char? openDelimiter,
        out string repairedBlock,
        out int nextIndex)
    {
        repairedBlock = string.Empty;
        nextIndex = startIndex + 1;

        if (startIndex < 0 || startIndex >= endIndex || text[startIndex] != '{')
        {
            return false;
        }

        var containerStack = new Stack<char>(8);
        containerStack.Push('{');
        bool inString = false;
        bool escaped = false;
        char lastSigChar = '{';

        for (int i = startIndex + 1; i < endIndex; i++)
        {
            char c = text[i];
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
                    lastSigChar = '"';
                }
                continue;
            }

            if (c is ' ' or '\t' or '\r' or '\n')
            {
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '[')
            {
                containerStack.Push('[');
                lastSigChar = '[';
                continue;
            }

            if (c == ']')
            {
                if (containerStack.Count > 0 && containerStack.Peek() == '[')
                {
                    containerStack.Pop();
                    lastSigChar = ']';
                    continue;
                }

                // Mismatched ']' while inside '{' (e.g. model wrote ']]' instead of ']}').
                var slice = text.Substring(startIndex, i - startIndex);
                var candidate = TryRepairTruncatedJson(slice);
                if (!string.IsNullOrEmpty(candidate) && IsValidExtractedElement(candidate, openDelimiter, rejectEmptyContainer: true))
                {
                    repairedBlock = candidate;
                    nextIndex = i + 1;
                    return true;
                }
                return false;
            }

            if (c == '{')
            {
                // Inside an object when '{' is on top of the stack, '{' is only valid as a property value after ':'.
                // If it appears after ',' (where a property key string is required), the current object was missing its closing '}'.
                if (containerStack.Count > 0 && containerStack.Peek() == '{' && lastSigChar != ':')
                {
                    var slice = text.Substring(startIndex, i - startIndex);
                    var candidate = TryRepairTruncatedJson(slice);
                    if (!string.IsNullOrEmpty(candidate) && IsValidExtractedElement(candidate, openDelimiter, rejectEmptyContainer: true))
                    {
                        repairedBlock = candidate;
                        nextIndex = i;
                        return true;
                    }
                    return false;
                }

                containerStack.Push('{');
                lastSigChar = '{';
                continue;
            }

            if (c == '}')
            {
                if (containerStack.Count > 0 && containerStack.Peek() == '{')
                {
                    containerStack.Pop();
                    lastSigChar = '}';
                    if (containerStack.Count == 0)
                    {
                        break;
                    }
                    continue;
                }

                // Mismatched '}' while inside '[' (e.g. model wrote '["A", "B"}' instead of '["A", "B"]}').
                var slice = text.Substring(startIndex, i - startIndex);
                var candidate = TryRepairTruncatedJson(slice);
                if (!string.IsNullOrEmpty(candidate) && IsValidExtractedElement(candidate, openDelimiter, rejectEmptyContainer: true))
                {
                    repairedBlock = candidate;
                    nextIndex = i + 1;
                    return true;
                }
                return false;
            }

            lastSigChar = c;
        }

        return false;
    }

    /// <summary>
    /// Leniently parses an array container string into CLR objects (typically a list of dictionaries),
    /// extracting balanced elements even if the container syntax has middle corruption or rogue delimiters.
    /// Returns null if no valid elements could be extracted.
    /// Reflection-free and Native AOT compatible.
    /// </summary>
    /// <param name="text">The raw or corrupted JSON array string.</param>
    /// <returns>A list of parsed CLR objects, or null if no valid elements could be extracted.</returns>
    public static List<object?>? TryParseStringifiedJsonArrayElements(string text)
    {
        var elements = ExtractBalancedJsonElements(text);
        if (elements.Count == 0)
        {
            return null;
        }

        var result = new List<object?>(elements.Count);
        foreach (var el in elements)
        {
            try
            {
                using var doc = JsonDocument.Parse(el, LenientDocumentOptions);
                result.Add(ConvertJsonElementToClr(doc.RootElement));
            }
            catch (JsonException)
            {
                // Skip unparseable element
            }
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Extracts top-level properties from a JSON object string whose array/container property values
    /// contain middle syntax corruption (e.g. rogue delimiters between array elements).
    /// Valid property values are converted to CLR objects, while corrupted array/object container slices
    /// are preserved as raw strings so downstream schema-aware normalization can recover valid elements.
    /// </summary>
    internal static bool TryExtractTopLevelPropertiesFromCorruptedObject(string json, IDictionary<string, object?> target)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        int firstBrace = json.IndexOf('{');
        if (firstBrace < 0)
        {
            return false;
        }

        int i = firstBrace + 1;
        int len = json.Length;
        bool extractedAny = false;

        while (i < len)
        {
            while (i < len && json[i] is ' ' or '\t' or '\r' or '\n' or ',')
            {
                i++;
            }

            if (i >= len || json[i] == '}')
            {
                break;
            }

            if (json[i] != '"')
            {
                break;
            }

            if (!TryReadJsonString(json, i, out var propName, out int afterKey))
            {
                break;
            }

            i = afterKey;
            while (i < len && json[i] is ' ' or '\t' or '\r' or '\n')
            {
                i++;
            }

            if (i >= len || json[i] != ':')
            {
                break;
            }

            i++; // skip ':'
            while (i < len && json[i] is ' ' or '\t' or '\r' or '\n')
            {
                i++;
            }

            if (i >= len)
            {
                break;
            }

            char valChar = json[i];
            if (valChar == '"')
            {
                if (!TryReadJsonString(json, i, out var strVal, out int afterStr))
                {
                    break;
                }
                target[propName] = strVal;
                extractedAny = true;
                i = afterStr;
            }
            else if (valChar is '[' or '{')
            {
                int endPos;
                string rawContainer;

                if (valChar == '[')
                {
                    endPos = FindTopLevelArrayEnd(json, i);
                    if (endPos <= i)
                    {
                        break;
                    }
                    rawContainer = json.Substring(i, endPos - i);
                }
                else
                {
                    var extracted = ExtractBalancedJson(json, i, out int consumed, out _);
                    if (string.IsNullOrEmpty(extracted) || consumed <= 0)
                    {
                        break;
                    }
                    endPos = i + consumed;
                    rawContainer = extracted;
                }

                try
                {
                    using var doc = JsonDocument.Parse(rawContainer, LenientDocumentOptions);
                    target[propName] = ConvertJsonElementToClr(doc.RootElement);
                }
                catch (JsonException)
                {
                    // Keep the corrupted container as a raw string so LenientToolDispatcher can apply schema-aware element extraction.
                    target[propName] = rawContainer;
                }

                extractedAny = true;
                i = endPos;
            }
            else
            {
                int scalarStart = i;
                while (i < len && json[i] is not (',' or '}' or ']' or ' ' or '\t' or '\r' or '\n'))
                {
                    i++;
                }
                var rawScalar = json.Substring(scalarStart, i - scalarStart);
                try
                {
                    using var doc = JsonDocument.Parse(rawScalar);
                    target[propName] = ConvertJsonElementToClr(doc.RootElement);
                    extractedAny = true;
                }
                catch (JsonException)
                {
                    break;
                }
            }
        }

        return extractedAny;
    }

    private static int FindTopLevelArrayEnd(string json, int arrayStartIndex)
    {
        int len = json.Length;
        var elementStack = new Stack<char>(8);
        bool inString = false;
        bool escaped = false;
        char lastSigChar = '[';

        for (int i = arrayStartIndex + 1; i < len; i++)
        {
            char c = json[i];
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
                    lastSigChar = '"';
                }
                continue;
            }

            if (c is ' ' or '\t' or '\r' or '\n')
            {
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '[')
            {
                elementStack.Push('[');
                lastSigChar = '[';
                continue;
            }

            if (c == '{')
            {
                // If an object element inside the array was missing its closing '}' before the next sibling '{',
                // keep the top-level element '{' rather than nesting deeper.
                if (elementStack.Count == 1 && elementStack.Peek() == '{' && lastSigChar != ':')
                {
                    lastSigChar = '{';
                    continue;
                }

                elementStack.Push('{');
                lastSigChar = '{';
                continue;
            }

            if (c == '}')
            {
                if (elementStack.Count > 0)
                {
                    if (elementStack.Peek() == '{')
                    {
                        elementStack.Pop();
                    }
                    else
                    {
                        // Model omitted ']' before '}' (e.g. `["A", "B"}`). Pop open '['s and the enclosing '{'.
                        while (elementStack.Count > 0 && elementStack.Peek() == '[')
                        {
                            elementStack.Pop();
                        }
                        if (elementStack.Count > 0 && elementStack.Peek() == '{')
                        {
                            elementStack.Pop();
                        }
                    }
                    lastSigChar = '}';
                    continue;
                }

                // elementStack.Count == 0: if only whitespace follows this '}', it is the root object's closing brace.
                int afterBrace = i + 1;
                while (afterBrace < len && json[afterBrace] is ' ' or '\t' or '\r' or '\n')
                {
                    afterBrace++;
                }
                if (afterBrace >= len)
                {
                    return i;
                }

                // Otherwise it is a rogue '}' between array elements (e.g. `}, "options": null}, {`); skip it.
                lastSigChar = '}';
                continue;
            }

            if (c == ']')
            {
                if (elementStack.Count > 0 && elementStack.Peek() == '[')
                {
                    elementStack.Pop();
                    lastSigChar = ']';
                    continue;
                }

                // Either elementStack is empty (normal array close) or has an unclosed '{' (e.g. `]]` instead of `]}` or `{"a": 1]`).
                // Look ahead to see if another '{' or '[' array element follows.
                int probe = i + 1;
                while (probe < len && json[probe] is ' ' or '\t' or '\r' or '\n' or ',' or ']' or '}')
                {
                    if (json[probe] == ']')
                    {
                        i = probe;
                    }
                    else if (json[probe] == '}')
                    {
                        int afterBrace = probe + 1;
                        while (afterBrace < len && json[afterBrace] is ' ' or '\t' or '\r' or '\n')
                        {
                            afterBrace++;
                        }
                        if (afterBrace >= len)
                        {
                            break;
                        }
                    }
                    probe++;
                }

                if (probe < len && json[probe] is '{' or '[')
                {
                    elementStack.Clear();
                    lastSigChar = ']';
                    continue;
                }

                return i + 1;
            }

            lastSigChar = c;
        }

        return len;
    }

    private static bool TryReadJsonString(string json, int startQuoteIndex, out string value, out int nextIndex)
    {
        value = string.Empty;
        nextIndex = startQuoteIndex;

        bool escaped = false;
        for (int i = startQuoteIndex + 1; i < json.Length; i++)
        {
            char c = json[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }
            if (c == '\\')
            {
                escaped = true;
                continue;
            }
            if (c == '"')
            {
                var rawLiteral = json.Substring(startQuoteIndex, i - startQuoteIndex + 1);
                try
                {
                    using var doc = JsonDocument.Parse(rawLiteral);
                    value = doc.RootElement.GetString() ?? string.Empty;
                    nextIndex = i + 1;
                    return true;
                }
                catch (JsonException)
                {
                    return false;
                }
            }
        }

        return false;
    }
}
