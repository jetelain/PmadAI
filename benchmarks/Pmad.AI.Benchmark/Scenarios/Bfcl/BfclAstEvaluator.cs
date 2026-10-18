using System.Collections;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Benchmark.Scenarios.Bfcl;

/// <summary>
/// Evaluates model-generated FunctionCallContent against BFCL ground truth AST definitions.
/// </summary>
public static class BfclAstEvaluator
{
    public static ValidationResult Evaluate(BfclEntry entry, TurnValidationContext ctx)
    {
        // 1. Irrelevance / Negative Control test
        if (entry.GroundTruth.Count == 0)
        {
            if (ctx.ActualToolCalls.Count > 0)
            {
                var toolNames = string.Join(", ", ctx.ActualToolCalls.Select(c => c.Name));
                return ValidationResult.Failure($"Negative test failed: model invoked tool(s) '{toolNames}' when a direct text response was expected.");
            }

            if (string.IsNullOrWhiteSpace(ctx.AssistantFinalText))
            {
                return ValidationResult.Failure("Expected direct conversational text response, but assistant returned empty output.");
            }

            return ValidationResult.Success();
        }

        // 2. Tool calling test
        if (ctx.ActualToolCalls.Count == 0)
        {
            var expectedNames = string.Join(", ", entry.GroundTruth.Select(g => g.Name));
            return ValidationResult.Failure($"Expected tool call(s) [{expectedNames}], but model invoked 0 tools. Assistant text: '{ctx.AssistantFinalText}'");
        }

        if (ctx.ActualToolCalls.Count != entry.GroundTruth.Count)
        {
            return ValidationResult.Failure($"Tool call count mismatch: expected {entry.GroundTruth.Count} call(s), but model generated {ctx.ActualToolCalls.Count}.");
        }

        // Match each expected call to an actual call (unordered match for parallel calls)
        var remainingActual = new List<FunctionCallContent>(ctx.ActualToolCalls);

        foreach (var expected in entry.GroundTruth)
        {
            var matchedIndex = -1;
            string? lastError = null;

            for (int i = 0; i < remainingActual.Count; i++)
            {
                var candidate = remainingActual[i];
                if (!string.Equals(candidate.Name, expected.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (MatchesArguments(expected.Arguments, candidate.Arguments, out lastError))
                {
                    matchedIndex = i;
                    break;
                }
            }

            if (matchedIndex >= 0)
            {
                remainingActual.RemoveAt(matchedIndex);
            }
            else
            {
                var diag = lastError ?? $"No matching call found for '{expected.Name}'.";
                return ValidationResult.Failure($"AST evaluation failed for '{expected.Name}': {diag}");
            }
        }

        return ValidationResult.Success();
    }

    private static bool MatchesArguments(
        IReadOnlyDictionary<string, object?> expected,
        IDictionary<string, object?>? actual,
        out string? error)
    {
        actual ??= new Dictionary<string, object?>();

        foreach (var (expectedKey, expectedVal) in expected)
        {
            // Case-insensitive key lookup
            var matchedKey = actual.Keys.FirstOrDefault(k => string.Equals(k, expectedKey, StringComparison.OrdinalIgnoreCase));
            if (matchedKey == null)
            {
                if (expectedVal == null) continue; // Optional null
                error = $"Missing required argument '{expectedKey}'. Actual arguments: [{string.Join(", ", actual.Keys)}]";
                return false;
            }

            var actualVal = actual[matchedKey];
            if (!ValuesEqual(expectedVal, actualVal))
            {
                error = $"Argument '{expectedKey}' value mismatch: expected '{FormatValueForDisplay(expectedVal)}' ({expectedVal?.GetType().Name}), got '{FormatValueForDisplay(actualVal)}' ({actualVal?.GetType().Name})";
                return false;
            }
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Recursively normalizes JsonElement, dictionaries, lists, and JSON-encoded strings
    /// into standard .NET collections and primitives for consistent AST comparison.
    /// </summary>
    public static object? NormalizeValue(object? val)
    {
        if (val == null) return null;

        if (val is JsonElement je)
        {
            return ConvertJsonElement(je);
        }

        if (val is IDictionary dict)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry de in dict)
            {
                var keyStr = de.Key?.ToString() ?? string.Empty;
                result[keyStr] = NormalizeValue(de.Value);
            }
            return result;
        }

        if (val is IReadOnlyDictionary<string, object?> roDict)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in roDict)
            {
                result[k] = NormalizeValue(v);
            }
            return result;
        }

        if (val is IEnumerable enumVal && val is not string)
        {
            var list = new List<object?>();
            foreach (var item in enumVal)
            {
                list.Add(NormalizeValue(item));
            }
            return list;
        }

        if (val is string str && (str.StartsWith('{') || str.StartsWith('[')))
        {
            try
            {
                using var doc = JsonDocument.Parse(str);
                return ConvertJsonElement(doc.RootElement);
            }
            catch
            {
                // Not valid JSON, keep as raw string
            }
        }

        return val;
    }

    private static object? ConvertJsonElement(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? (object)l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => NormalizeValue(p.Value), StringComparer.OrdinalIgnoreCase),
        JsonValueKind.Array => el.EnumerateArray().Select(ConvertJsonElement).ToList<object?>(),
        _ => el.Clone()
    };

    private static bool ValuesEqual(object? expected, object? actual)
    {
        expected = NormalizeValue(expected);
        actual = NormalizeValue(actual);

        if (expected == null && actual == null) return true;
        if (expected == null || actual == null) return false;

        // Dictionaries / nested objects
        if (expected is IDictionary<string, object?> expDict)
        {
            if (actual is not IDictionary<string, object?> actDict)
            {
                return false;
            }

            if (expDict.Count != actDict.Count)
            {
                return false;
            }

            foreach (var (k, expVal) in expDict)
            {
                var matchedK = actDict.Keys.FirstOrDefault(ak => string.Equals(ak, k, StringComparison.OrdinalIgnoreCase));
                if (matchedK == null)
                {
                    return false;
                }

                if (!ValuesEqual(expVal, actDict[matchedK]))
                {
                    return false;
                }
            }

            return true;
        }

        if (actual is IDictionary<string, object?>)
        {
            return false;
        }

        // Arrays / Lists
        if (expected is IList<object?> expList)
        {
            if (actual is IList<object?> actList)
            {
                if (expList.Count != actList.Count)
                {
                    return false;
                }

                for (int i = 0; i < expList.Count; i++)
                {
                    if (!ValuesEqual(expList[i], actList[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            // If expected is a list of acceptable alternatives, check if actual matches any
            foreach (var alt in expList)
            {
                if (ValuesEqual(alt, actual))
                {
                    return true;
                }
            }

            return false;
        }

        if (actual is IList<object?>)
        {
            return false;
        }

        // Boolean comparison
        if (expected is bool expBool)
        {
            return TryExtractBool(actual, out var actBool) && expBool == actBool;
        }

        if (actual is bool actBool2)
        {
            return TryExtractBool(expected, out var expBool2) && expBool2 == actBool2;
        }

        // Numeric comparison (handle int vs double vs string representation)
        if (TryExtractDouble(expected, out var expNum) && TryExtractDouble(actual, out var actNum))
        {
            return Math.Abs(expNum - actNum) < 0.001;
        }

        // String comparison
        var strExpected = expected.ToString()?.Trim();
        var strActual = actual.ToString()?.Trim();

        if (string.Equals(strExpected, strActual, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Mathematical power operator equivalence (e.g. "x^2" vs "x**2")
        if (strExpected != null && strActual != null &&
            string.Equals(strExpected.Replace("**", "^"), strActual.Replace("**", "^"), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string FormatValueForDisplay(object? val)
    {
        if (val == null) return "null";
        if (val is JsonElement je) return je.GetRawText();
        if (val is string s) return s;
        if (val is IDictionary or IEnumerable)
        {
            try
            {
                return JsonSerializer.Serialize(val);
            }
            catch
            {
                return val.ToString() ?? "";
            }
        }
        return val.ToString() ?? "";
    }

    private static bool TryExtractDouble(object? val, out double result)
    {
        if (val is JsonElement je && je.ValueKind == JsonValueKind.Number)
        {
            result = je.GetDouble();
            return true;
        }
        if (val is double d) { result = d; return true; }
        if (val is float f) { result = f; return true; }
        if (val is int i) { result = i; return true; }
        if (val is long l) { result = l; return true; }
        if (val is decimal m) { result = (double)m; return true; }
        if (val is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            result = parsed;
            return true;
        }
        result = 0;
        return false;
    }

    private static bool TryExtractBool(object? val, out bool result)
    {
        if (val is JsonElement je && (je.ValueKind == JsonValueKind.True || je.ValueKind == JsonValueKind.False))
        {
            result = je.GetBoolean();
            return true;
        }
        if (val is bool b) { result = b; return true; }
        if (val is string s && bool.TryParse(s, out var parsed))
        {
            result = parsed;
            return true;
        }
        result = false;
        return false;
    }

    /// <summary>
    /// Evaluates function calls under strict schema validation rules:
    /// - Distractor rejection: calling any tool in distractorTools fails.
    /// - Tool name validation: must match expectedToolName.
    /// - Strict type enforcement: integer arguments cannot have fractional values or non-integral string representations.
    /// - Strict enum validation: string arguments must match declared enum choices.
    /// - Nested object validation: all nested dictionary / object properties are verified recursively.
    /// </summary>
    public static ValidationResult EvaluateStrict(
        BfclEntry entry,
        TurnValidationContext ctx,
        IReadOnlySet<string>? distractorTools = null)
    {
        if (entry.GroundTruth.Count == 0)
        {
            return ValidationResult.Success();
        }

        var expected = entry.GroundTruth[0];
        var distractors = distractorTools;
        if (distractors == null && entry.Tools != null)
        {
            var expectedToolNames = entry.GroundTruth.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            distractors = entry.Tools
                .Select(t => t.Name)
                .Where(n => !expectedToolNames.Contains(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return EvaluateStrict(expected.Name, expected.Arguments, distractors, ctx);
    }

    /// <summary>
    /// Evaluates a function call with strict schema validation:
    /// - Distractor rejection: calling any tool in distractorTools fails.
    /// - Tool name validation: must match expectedToolName.
    /// - Strict type enforcement: integer arguments cannot have fractional values or non-integral string representations.
    /// - Strict enum validation: string arguments must match declared enum choices.
    /// - Nested object validation: all nested dictionary / object properties are verified recursively.
    /// </summary>
    public static ValidationResult EvaluateStrict(
        string expectedToolName,
        IReadOnlyDictionary<string, object?> expectedArgs,
        IReadOnlySet<string>? distractorTools,
        TurnValidationContext ctx)
    {
        if (ctx.ActualToolCalls.Count == 0)
        {
            return ValidationResult.Failure($"Expected tool call '{expectedToolName}', but model invoked 0 tools. Assistant text: '{ctx.AssistantFinalText}'");
        }

        // Check for distractor invocation
        if (distractorTools != null)
        {
            foreach (var call in ctx.ActualToolCalls)
            {
                if (distractorTools.Contains(call.Name))
                {
                    return ValidationResult.Failure($"Model invoked distractor tool '{call.Name}' instead of target tool '{expectedToolName}'.");
                }
            }
        }

        var matchingCall = ctx.ActualToolCalls.FirstOrDefault(c => string.Equals(c.Name, expectedToolName, StringComparison.OrdinalIgnoreCase));
        if (matchingCall == null)
        {
            var actualNames = string.Join(", ", ctx.ActualToolCalls.Select(c => c.Name));
            return ValidationResult.Failure($"Expected tool '{expectedToolName}', but model invoked [{actualNames}].");
        }

        var actualArgs = matchingCall.Arguments ?? new Dictionary<string, object?>();

        foreach (var (expectedKey, expectedVal) in expectedArgs)
        {
            var matchedKey = actualArgs.Keys.FirstOrDefault(k => string.Equals(k, expectedKey, StringComparison.OrdinalIgnoreCase));
            if (matchedKey == null)
            {
                return ValidationResult.Failure($"Missing required argument '{expectedKey}'. Actual: [{string.Join(", ", actualArgs.Keys)}]");
            }

            var actualVal = actualArgs[matchedKey];
            if (!ValidateStrictValue(expectedVal, actualVal, out var error))
            {
                return ValidationResult.Failure($"Strict schema validation failed for '{expectedKey}': {error}");
            }
        }

        return ValidationResult.Success();
    }

    private static bool ValidateStrictValue(object? expected, object? actual, out string? error)
    {
        expected = NormalizeValue(expected);
        actual = NormalizeValue(actual);

        if (expected == null && actual == null)
        {
            error = null;
            return true;
        }
        if (expected == null || actual == null)
        {
            error = $"Expected '{FormatValueForDisplay(expected)}', got '{FormatValueForDisplay(actual)}'.";
            return false;
        }

        // Strict integer check
        if (expected is int or long or short or byte)
        {
            var expLong = Convert.ToInt64(expected);
            if (actual is int or long or short or byte)
            {
                var actLong = Convert.ToInt64(actual);
                if (actLong != expLong)
                {
                    error = $"Integer value mismatch: expected {expLong}, got {actLong}";
                    return false;
                }
                error = null;
                return true;
            }
            if (actual is double actD)
            {
                if (Math.Abs(actD - expLong) < 0.0001 && actD % 1 == 0) { error = null; return true; }
                error = $"Expected integer {expLong}, got float {actD}";
                return false;
            }
            error = $"Expected integer {expLong}, got {FormatValueForDisplay(actual)} ({actual.GetType().Name})";
            return false;
        }

        // Strict float check
        if (expected is float or double or decimal)
        {
            if (TryExtractDouble(expected, out var expD) && TryExtractDouble(actual, out var actD))
            {
                if (Math.Abs(expD - actD) < 0.001)
                {
                    error = null;
                    return true;
                }
                error = $"Float value mismatch: expected {expD}, got {actD}";
                return false;
            }
            error = $"Expected float {expected}, got {FormatValueForDisplay(actual)}";
            return false;
        }

        // Strict boolean check
        if (expected is bool expBool)
        {
            if (actual is bool actBool)
            {
                if (expBool == actBool)
                {
                    error = null;
                    return true;
                }
                error = $"Boolean value mismatch: expected {expBool}, got {actBool}";
                return false;
            }
            error = $"Expected boolean {expBool}, got {FormatValueForDisplay(actual)} ({actual.GetType().Name})";
            return false;
        }

        // Strict nested dictionary / object check
        if (expected is IDictionary<string, object?> expDict)
        {
            if (actual is not IDictionary<string, object?> actDict)
            {
                error = $"Expected nested object, got {FormatValueForDisplay(actual)} ({actual.GetType().Name})";
                return false;
            }

            foreach (var (k, v) in expDict)
            {
                var matchedK = actDict.Keys.FirstOrDefault(ak => string.Equals(ak, k, StringComparison.OrdinalIgnoreCase));
                if (matchedK == null)
                {
                    error = $"Missing required nested property '{k}'";
                    return false;
                }
                if (!ValidateStrictValue(v, actDict[matchedK], out error))
                {
                    return false;
                }
            }

            error = null;
            return true;
        }

        // Strict array / list check
        if (expected is IList<object?> expList)
        {
            if (actual is not IList<object?> actList)
            {
                error = $"Expected array/list, got {FormatValueForDisplay(actual)} ({actual.GetType().Name})";
                return false;
            }

            if (expList.Count != actList.Count)
            {
                error = $"Array length mismatch: expected {expList.Count} elements, got {actList.Count}";
                return false;
            }

            for (int i = 0; i < expList.Count; i++)
            {
                if (!ValidateStrictValue(expList[i], actList[i], out error))
                {
                    return false;
                }
            }

            error = null;
            return true;
        }

        // String / enum check
        var expStr = expected.ToString()?.Trim();
        var actStr = actual.ToString()?.Trim();

        if (string.Equals(expStr, actStr, StringComparison.Ordinal))
        {
            error = null;
            return true;
        }

        error = $"String/enum mismatch: expected '{expStr}', got '{actStr}'";
        return false;
    }

}
