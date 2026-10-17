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
                error = $"Argument '{expectedKey}' value mismatch: expected '{expectedVal}' ({expectedVal?.GetType().Name}), got '{actualVal}' ({actualVal?.GetType().Name})";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static bool ValuesEqual(object? expected, object? actual)
    {
        if (expected == null && actual == null) return true;
        if (expected == null || actual == null) return false;

        // If one is string representing JSON and other is JsonElement, parse string as JsonDocument
        if (expected is string expS && (expS.StartsWith('{') || expS.StartsWith('[')))
        {
            try
            {
                using var expDoc = JsonDocument.Parse(expS);
                return ValuesEqual(expDoc.RootElement, actual);
            }
            catch { }
        }

        if (actual is string actS && (actS.StartsWith('{') || actS.StartsWith('[')))
        {
            try
            {
                using var actDoc = JsonDocument.Parse(actS);
                return ValuesEqual(expected, actDoc.RootElement);
            }
            catch { }
        }

        // Unpack JsonElement if needed
        if (expected is JsonElement jeExp)
        {
            return CompareJsonElement(jeExp, actual);
        }

        if (actual is JsonElement jeAct)
        {
            return CompareJsonElement(jeAct, expected);
        }

        // Numeric comparison (handle int vs double vs string representation)
        if (TryExtractDouble(expected, out var expNum) && TryExtractDouble(actual, out var actNum))
        {
            return Math.Abs(expNum - actNum) < 0.001;
        }

        // Boolean comparison
        if (TryExtractBool(expected, out var expBool) && TryExtractBool(actual, out var actBool))
        {
            return expBool == actBool;
        }

        // Array / List comparison
        if (expected is IEnumerable<object?> expList && expected is not string)
        {
            var expArray = expList.ToList();
            if (actual is IEnumerable<object?> actList && actual is not string)
            {
                var actArray = actList.ToList();
                if (expArray.Count != actArray.Count) return false;
                for (int i = 0; i < expArray.Count; i++)
                {
                    if (!ValuesEqual(expArray[i], actArray[i])) return false;
                }
                return true;
            }

            // If expected is a list of acceptable alternatives, check if actual matches any
            foreach (var alt in expArray)
            {
                if (ValuesEqual(alt, actual)) return true;
            }
        }

        // String comparison
        var strExpected = expected.ToString()?.Trim();
        var strActual = actual.ToString()?.Trim();

        return string.Equals(strExpected, strActual, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CompareJsonElement(JsonElement el, object? target)
    {
        if (target is JsonElement targetEl)
        {
            if (el.ValueKind != targetEl.ValueKind)
            {
                // Number vs string coercion check
                if (TryExtractDouble(el, out var d1) && TryExtractDouble(targetEl, out var d2))
                {
                    return Math.Abs(d1 - d2) < 0.001;
                }
                return false;
            }

            switch (el.ValueKind)
            {
                case JsonValueKind.String:
                    return string.Equals(el.GetString(), targetEl.GetString(), StringComparison.OrdinalIgnoreCase);
                case JsonValueKind.Number:
                    return Math.Abs(el.GetDouble() - targetEl.GetDouble()) < 0.001;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return el.GetBoolean() == targetEl.GetBoolean();
                case JsonValueKind.Null:
                    return true;
                case JsonValueKind.Array:
                    if (el.GetArrayLength() != targetEl.GetArrayLength()) return false;
                    var arr1 = el.EnumerateArray().ToList();
                    var arr2 = targetEl.EnumerateArray().ToList();
                    for (int i = 0; i < arr1.Count; i++)
                    {
                        if (!CompareJsonElement(arr1[i], arr2[i])) return false;
                    }
                    return true;
                case JsonValueKind.Object:
                    var obj1 = el.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
                    var obj2 = targetEl.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
                    if (obj1.Count != obj2.Count) return false;
                    foreach (var (k, v1) in obj1)
                    {
                        if (!obj2.TryGetValue(k, out var v2) || !CompareJsonElement(v1, v2)) return false;
                    }
                    return true;
            }
        }

        return el.ValueKind switch
        {
            JsonValueKind.String => string.Equals(el.GetString()?.Trim(), target?.ToString()?.Trim(), StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number => TryExtractDouble(target, out var d) && Math.Abs(el.GetDouble() - d) < 0.001,
            JsonValueKind.True => TryExtractBool(target, out var b) && b,
            JsonValueKind.False => TryExtractBool(target, out var b) && !b,
            JsonValueKind.Null => target == null,
            _ => string.Equals(el.GetRawText().Trim(), target?.ToString()?.Trim(), StringComparison.OrdinalIgnoreCase)
        };
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
        if (expected == null && actual == null)
        {
            error = null;
            return true;
        }
        if (expected == null || actual == null)
        {
            error = $"Expected '{expected}', got '{actual}'.";
            return false;
        }

        // Strict integer check
        if (expected is int or long or short or byte)
        {
            var expLong = Convert.ToInt64(expected);
            if (actual is JsonElement jeInt && jeInt.ValueKind == JsonValueKind.Number)
            {
                if (jeInt.TryGetInt64(out var actLong))
                {
                    if (actLong != expLong)
                    {
                        error = $"Integer value mismatch: expected {expLong}, got {actLong}";
                        return false;
                    }
                    error = null;
                    return true;
                }
                error = $"Expected integer {expLong}, got fractional or out-of-range number {jeInt.GetRawText()}";
                return false;
            }
            if (actual is int actI && actI == expLong) { error = null; return true; }
            if (actual is long actL && actL == expLong) { error = null; return true; }
            if (actual is double actD)
            {
                if (Math.Abs(actD - expLong) < 0.0001 && actD % 1 == 0) { error = null; return true; }
                error = $"Expected integer {expLong}, got float {actD}";
                return false;
            }
            error = $"Expected integer {expLong}, got {actual} ({actual.GetType().Name})";
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
            error = $"Expected float {expected}, got {actual}";
            return false;
        }

        // Strict nested dictionary / object check
        if (expected is IReadOnlyDictionary<string, object?> expDict)
        {
            IDictionary<string, object?>? actDict = null;
            if (actual is IDictionary<string, object?> d)
            {
                actDict = d;
            }
            else if (actual is JsonElement je && je.ValueKind == JsonValueKind.Object)
            {
                actDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in je.EnumerateObject())
                {
                    actDict[prop.Name] = prop.Value;
                }
            }

            if (actDict == null)
            {
                error = $"Expected nested object, got {actual.GetType().Name}";
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

        // String / enum check
        var expStr = expected.ToString()?.Trim();
        var actStr = (actual is JsonElement jeStr && jeStr.ValueKind == JsonValueKind.String ? jeStr.GetString() : actual.ToString())?.Trim();

        if (string.Equals(expStr, actStr, StringComparison.Ordinal))
        {
            error = null;
            return true;
        }

        error = $"String/enum mismatch: expected '{expStr}', got '{actStr}'";
        return false;
    }
}
