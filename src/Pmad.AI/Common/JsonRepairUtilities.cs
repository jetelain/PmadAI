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
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(repaired);
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
        int safeCut = 0;               // highest index that is a valid truncation point (end of last complete entry)
        bool inString = false;
        bool escaped = false;
        bool pendingStringIsKey = false;
        bool inScalar = false;

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
                    stack.Push('{'); roles.Push(R_KEY); safeCut = i + 1;
                    break;
                case '[':
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
                    if (stack.Count > 0 && stack.Peek() == '{') { stack.Pop(); roles.Pop(); }
                    if (roles.Count > 0) { roles.Pop(); roles.Push(R_AFTER); }
                    safeCut = i + 1;
                    break;
                case ']':
                    if (stack.Count > 0 && stack.Peek() == '[') { stack.Pop(); roles.Pop(); }
                    if (roles.Count > 0) { roles.Pop(); roles.Push(R_AFTER); }
                    safeCut = i + 1;
                    break;
                default:
                    // First character of a scalar value (digit, sign, or t/f/n of true/false/null).
                    if (TopRole() == R_VALUE)
                    {
                        inScalar = true;
                    }
                    break;
            }
        }

        if (stack.Count == 0 && !inString)
        {
            return null; // Already a complete, balanced document.
        }

        int cut = json.Length;
        if (inString)
        {
            // An unterminated string: drop it if it is a dangling key, otherwise keep and close it.
            if (pendingStringIsKey)
            {
                cut = safeCut;
            }
        }
        else if (inScalar)
        {
            // Truncated inside a scalar: the digits seen so far form a valid value; keep as-is.
        }
        else
        {
            int tr = TopRole();
            if (tr == R_KEY || tr == R_COLON || tr == R_VALUE)
            {
                cut = safeCut; // dangling entry (missing key, missing value, or missing element)
            }
        }

        var sb = new StringBuilder(json.Length + 8);
        sb.Append(json, 0, cut);
        if (inString && !pendingStringIsKey)
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
}
