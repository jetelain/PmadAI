using System.Text.Json;

using static Pmad.AI.Common.StringCaseConverter;

namespace Pmad.AI.Tools;

/// <summary>
/// Pure, static helpers for working with tool <c>AIFunction</c> JSON schemas during lenient tool
/// dispatch: resolving <c>$ref</c>/<c>anyOf</c>/<c>oneOf</c> references, matching schema types,
/// extracting property/enum members, and snapping candidate string values to the closest schema
/// enum or const member using deterministic rules. Reflection-free and Native AOT compatible.
/// </summary>
public static class SchemaHelpers
{
    /// <summary>
    /// Extracts the set of top-level property names declared in a JSON schema's <c>properties</c>
    /// bag. Returns <c>null</c> when the schema has no <c>properties</c> object.
    /// </summary>
    public static HashSet<string>? ExtractSchemaProperties(JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in props.EnumerateObject())
            {
                set.Add(prop.Name);
            }
            return set;
        }
        return null;
    }

    /// <summary>
    /// Determines whether two schema <see cref="JsonElement"/> values are structurally equal,
    /// comparing raw text (undefined and null values are always considered equal).
    /// </summary>
    public static bool AreSchemasEqual(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }

        if (a.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return true;
        }

        return string.Equals(a.GetRawText(), b.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns whether a schema's <c>type</c> (a single string or an array of strings) matches
    /// <paramref name="expectedType"/>.
    /// </summary>
    public static bool SchemaTypeMatches(JsonElement schema, string expectedType)
    {
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("type", out var typeProp))
        {
            if (typeProp.ValueKind == JsonValueKind.String)
            {
                return typeProp.ValueEquals(expectedType);
            }
            if (typeProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in typeProp.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.ValueEquals(expectedType))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Resolves a schema fragment against the document root, following <c>$ref</c> pointers and
    /// selecting a non-null branch from <c>anyOf</c>/<c>oneOf</c> nullable unions. Recursion is
    /// bounded to <paramref name="depth"/> 10 to guard against cyclic references.
    /// </summary>
    public static JsonElement ResolveSchema(JsonElement schema, JsonElement rootSchema, int depth = 0)
    {
        if (depth > 10 || schema.ValueKind != JsonValueKind.Object)
        {
            return schema;
        }

        if (schema.TryGetProperty("$ref", out var refProp))
        {
            var refStr = refProp.GetString();
            if (!string.IsNullOrEmpty(refStr) && refStr.StartsWith("#/") && rootSchema.ValueKind == JsonValueKind.Object)
            {
                var parts = refStr.Substring(2).Split('/');
                var current = rootSchema;
                bool found = true;
                foreach (var rawPart in parts)
                {
                    var part = rawPart.Replace("~1", "/").Replace("~0", "~");
                    if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out var next))
                    {
                        current = next;
                    }
                    else
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                {
                    return ResolveSchema(current, rootSchema, depth + 1);
                }
            }
        }

        // Handle anyOf / oneOf for nullable schemas: select non-null schema
        if (schema.TryGetProperty("anyOf", out var anyOfProp) && anyOfProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in anyOfProp.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    if (SchemaTypeMatches(item, "null") && !SchemaTypeMatches(item, "object") && !SchemaTypeMatches(item, "array"))
                    {
                        continue;
                    }
                    var resolved = ResolveSchema(item, rootSchema, depth + 1);
                    if (resolved.ValueKind == JsonValueKind.Object)
                    {
                        return resolved;
                    }
                }
            }
        }

        if (schema.TryGetProperty("oneOf", out var oneOfProp) && oneOfProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in oneOfProp.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    if (SchemaTypeMatches(item, "null") && !SchemaTypeMatches(item, "object") && !SchemaTypeMatches(item, "array"))
                    {
                        continue;
                    }
                    var resolved = ResolveSchema(item, rootSchema, depth + 1);
                    if (resolved.ValueKind == JsonValueKind.Object)
                    {
                        return resolved;
                    }
                }
            }
        }

        return schema;
    }

    /// <summary>
    /// Collects the string members of a schema's <c>enum</c>, <c>const</c>, or a
    /// <c>oneOf</c>/<c>anyOf</c>/<c>allOf</c> compound of those. Returns <c>true</c> when at least
    /// one string member was found.
    /// </summary>
    public static bool TryGetEnumOrConstMembers(JsonElement schema, out List<string> candidates)
    {
        candidates = [];
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (schema.TryGetProperty("enum", out var enumProp) && enumProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in enumProp.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (s != null && !candidates.Contains(s))
                    {
                        candidates.Add(s);
                    }
                }
            }
        }
        else if (schema.TryGetProperty("const", out var constProp) && constProp.ValueKind == JsonValueKind.String)
        {
            var s = constProp.GetString();
            if (s != null)
            {
                candidates.Add(s);
            }
        }
        else if (schema.TryGetProperty("oneOf", out var oneOfProp) && oneOfProp.ValueKind == JsonValueKind.Array)
        {
            ExtractFromCompoundSchema(oneOfProp, candidates);
        }
        else if (schema.TryGetProperty("anyOf", out var anyOfProp) && anyOfProp.ValueKind == JsonValueKind.Array)
        {
            ExtractFromCompoundSchema(anyOfProp, candidates);
        }
        else if (schema.TryGetProperty("allOf", out var allOfProp) && allOfProp.ValueKind == JsonValueKind.Array)
        {
            ExtractFromCompoundSchema(allOfProp, candidates);
        }

        return candidates.Count > 0;
    }

    private static void ExtractFromCompoundSchema(JsonElement arrayProp, List<string> candidates)
    {
        foreach (var item in arrayProp.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                if (item.TryGetProperty("const", out var cp) && cp.ValueKind == JsonValueKind.String)
                {
                    var s = cp.GetString();
                    if (s != null && !candidates.Contains(s)) candidates.Add(s);
                }
                else if (item.TryGetProperty("enum", out var ep) && ep.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in ep.EnumerateArray())
                    {
                        if (el.ValueKind == JsonValueKind.String)
                        {
                            var s = el.GetString();
                            if (s != null && !candidates.Contains(s)) candidates.Add(s);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Snaps a candidate string value to the closest matching schema enum or const member using
    /// deterministic rules. If no unique candidate matches, returns the original value untouched.
    /// </summary>
    public static string SnapEnumValueCore(
        string value,
        IReadOnlyList<string> candidates,
        LenientDispatchTelemetry? telemetry = null,
        string? sessionId = null)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return value;
        }

        // Exact match check (Ordinal) - already a valid member
        for (int i = 0; i < candidates.Count; i++)
        {
            if (string.Equals(candidates[i], value, StringComparison.Ordinal))
            {
                return value;
            }
        }

        // 1. Ordinal case-insensitive match
        List<string>? rule1Matches = null;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (string.Equals(candidates[i], value, StringComparison.OrdinalIgnoreCase))
            {
                rule1Matches ??= new List<string>(2);
                rule1Matches.Add(candidates[i]);
            }
        }

        if (rule1Matches != null)
        {
            if (rule1Matches.Count == 1)
            {
                telemetry?.Record(LenientDispatchEventType.EnumValueSnapped, value, rule1Matches[0], sessionId);
                return rule1Matches[0];
            }
            // Tie among ordinal case-insensitive matches -> do not guess
            telemetry?.Record(LenientDispatchEventType.EnumValueUnmatched, value, value, sessionId);
            return value;
        }

        // 2. Normalization (trim, collapse whitespace/underscore/hyphen, naive plural stripping)
        var normValue = NormalizeEnumString(value);
        var normValueNoPlural = StripPluralSuffix(normValue);
        var normValueNoSep = normValue.Replace("_", "");
        var normValueNoSepNoPlural = StripPluralSuffix(normValueNoSep);

        // 2a. Normalized exact match (collapsed whitespace/underscore/hyphen)
        List<string>? rule2aMatches = null;
        for (int i = 0; i < candidates.Count; i++)
        {
            var normC = NormalizeEnumString(candidates[i]);
            if (string.Equals(normC, normValue, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normC.Replace("_", ""), normValueNoSep, StringComparison.OrdinalIgnoreCase))
            {
                rule2aMatches ??= new List<string>(2);
                rule2aMatches.Add(candidates[i]);
            }
        }

        if (rule2aMatches != null)
        {
            if (rule2aMatches.Count == 1)
            {
                telemetry?.Record(LenientDispatchEventType.EnumValueSnapped, value, rule2aMatches[0], sessionId);
                return rule2aMatches[0];
            }
            // Tie among normalized matches
            telemetry?.Record(LenientDispatchEventType.EnumValueUnmatched, value, value, sessionId);
            return value;
        }

        // 2b. Naive plural stripping
        List<string>? rule2bMatches = null;
        for (int i = 0; i < candidates.Count; i++)
        {
            var normC = NormalizeEnumString(candidates[i]);
            var normCNoPlural = StripPluralSuffix(normC);
            var normCNoSep = normC.Replace("_", "");
            var normCNoSepNoPlural = StripPluralSuffix(normCNoSep);

            if (string.Equals(normCNoPlural, normValueNoPlural, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normCNoSepNoPlural, normValueNoSepNoPlural, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normC, normValueNoPlural, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normCNoPlural, normValue, StringComparison.OrdinalIgnoreCase))
            {
                rule2bMatches ??= new List<string>(2);
                rule2bMatches.Add(candidates[i]);
            }
        }

        if (rule2bMatches != null)
        {
            if (rule2bMatches.Count == 1)
            {
                telemetry?.Record(LenientDispatchEventType.EnumValueSnapped, value, rule2bMatches[0], sessionId);
                return rule2bMatches[0];
            }
            // Tie among plural-stripped matches
            telemetry?.Record(LenientDispatchEventType.EnumValueUnmatched, value, value, sessionId);
            return value;
        }

        // 3. Unique prefix/suffix/substring containment (e.g. Stalingrad in Battle of Stalingrad) — only when exactly one member matches.
        var trimVal = value.Trim();
        if (trimVal.Length >= 2)
        {
            List<string>? rule3Matches = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                var cand = candidates[i];
                var trimCand = cand.Trim();
                var normCand = NormalizeEnumString(cand);

                bool contains = (trimCand.Length >= 2 && (ContainsTokenOrAffix(trimCand, trimVal) || ContainsTokenOrAffix(trimVal, trimCand))) ||
                                (normCand.Length >= 2 && normValue.Length >= 2 &&
                                 (ContainsTokenOrAffix(normCand, normValue) ||
                                  ContainsTokenOrAffix(normValue, normCand)));

                if (contains)
                {
                    rule3Matches ??= new List<string>(2);
                    rule3Matches.Add(cand);
                }
            }

            if (rule3Matches != null)
            {
                if (rule3Matches.Count == 1)
                {
                    telemetry?.Record(LenientDispatchEventType.EnumValueSnapped, value, rule3Matches[0], sessionId);
                    return rule3Matches[0];
                }
                // Ambiguous tie among multiple containing members
                telemetry?.Record(LenientDispatchEventType.EnumValueUnmatched, value, value, sessionId);
                return value;
            }
        }

        // 4. Bounded edit distance (<= 2 and <= 25% of length) with a unique best candidate.
        int minDistance = int.MaxValue;
        List<string>? bestCandidates = null;
        var lowerVal = trimVal.ToLowerInvariant();

        for (int i = 0; i < candidates.Count; i++)
        {
            var cand = candidates[i];
            var lowerCand = cand.Trim().ToLowerInvariant();
            int maxLen = Math.Max(lowerVal.Length, lowerCand.Length);
            if (maxLen == 0) continue;

            int d = EditDistance(lowerVal, lowerCand, maxDistance: 2);
            var normC = NormalizeEnumString(cand);
            if (normC.Length > 0 && normValue.Length > 0)
            {
                int dNorm = EditDistance(normValue, normC, maxDistance: 2);
                d = Math.Min(d, dNorm);
                maxLen = Math.Max(maxLen, Math.Max(normValue.Length, normC.Length));
            }

            // Bounded: <= 2 and <= 25% of max length
            if (d <= 2 && (double)d / maxLen <= 0.25 + 1e-9)
            {
                if (d < minDistance)
                {
                    minDistance = d;
                    bestCandidates ??= new List<string>(2);
                    bestCandidates.Clear();
                    bestCandidates.Add(cand);
                }
                else if (d == minDistance)
                {
                    bestCandidates?.Add(cand);
                }
            }
        }

        if (bestCandidates != null)
        {
            if (bestCandidates.Count == 1)
            {
                telemetry?.Record(LenientDispatchEventType.EnumValueSnapped, value, bestCandidates[0], sessionId);
                return bestCandidates[0];
            }
            // Tie among best candidates at minDistance
            telemetry?.Record(LenientDispatchEventType.EnumValueUnmatched, value, value, sessionId);
            return value;
        }

        // 5. Otherwise leave the value untouched (never guess among ties) and record a telemetry event.
        telemetry?.Record(LenientDispatchEventType.EnumValueUnmatched, value, value, sessionId);
        return value;
    }

    /// <summary>
    /// Determines whether <paramref name="part"/> appears in <paramref name="container"/> as an
    /// exact match, or as a token bounded by separators (<c>_</c>, <c>-</c>, <c>/</c>, whitespace).
    /// Used to avoid snapping short words onto unrelated members via raw substring containment.
    /// </summary>
    public static bool ContainsTokenOrAffix(string container, string part)
    {
        if (string.IsNullOrWhiteSpace(container) || string.IsNullOrWhiteSpace(part)) return false;
        var c = container.Trim();
        var p = part.Trim();
        if (p.Length < 2 || c.Length < p.Length) return false;

        // Exact match
        if (string.Equals(c, p, StringComparison.OrdinalIgnoreCase)) return true;

        // Prefix with separator
        if (c.StartsWith(p, StringComparison.OrdinalIgnoreCase) &&
            c.Length > p.Length && (c[p.Length] is '_' or '-' or '/' or ' ' || char.IsWhiteSpace(c[p.Length])))
        {
            return true;
        }

        // Suffix with separator
        if (c.EndsWith(p, StringComparison.OrdinalIgnoreCase) &&
            c.Length > p.Length && (c[c.Length - p.Length - 1] is '_' or '-' or '/' or ' ' || char.IsWhiteSpace(c[c.Length - p.Length - 1])))
        {
            return true;
        }

        // Substring bounded by separators
        var idx = c.IndexOf(p, StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            bool startBound = idx == 0 || c[idx - 1] is '_' or '-' or '/' or ' ' || char.IsWhiteSpace(c[idx - 1]);
            int endPos = idx + p.Length;
            bool endBound = endPos == c.Length || c[endPos] is '_' or '-' or '/' or ' ' || char.IsWhiteSpace(c[endPos]);

            if (startBound && endBound)
            {
                return true;
            }

            idx = c.IndexOf(p, idx + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string NormalizeEnumString(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        return ToSnakeCase(s.Trim());
    }

    private static string StripPluralSuffix(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.Length > 2 && s.EndsWith("s", StringComparison.OrdinalIgnoreCase) && !s.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
        {
            return s[..^1];
        }
        return s;
    }

    private static int EditDistance(string s, string t, int maxDistance = int.MaxValue)
    {
        int n = s.Length;
        int m = t.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        if (Math.Abs(n - m) > maxDistance)
        {
            return maxDistance + 1;
        }

        var d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++)
            d[i, 0] = i;
        for (int j = 0; j <= m; j++)
            d[0, j] = j;

        for (int i = 1; i <= n; i++)
        {
            int rowMin = int.MaxValue;
            for (int j = 1; j <= m; j++)
            {
                int cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);

                if (i > 1 && j > 1 && s[i - 1] == t[j - 2] && s[i - 2] == t[j - 1])
                {
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }

                if (d[i, j] < rowMin)
                {
                    rowMin = d[i, j];
                }
            }

            if (rowMin > maxDistance)
            {
                return maxDistance + 1;
            }
        }

        return d[n, m];
    }
}
