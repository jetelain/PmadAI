using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pmad.AI.Guardrails;

/// <summary>
/// Type of loop detected by <see cref="ToolLoopCircuitBreaker"/>.
/// </summary>
public enum LoopType
{
    None,
    StreamingRepetition,
    ToolPingPong
}

/// <summary>
/// Result returned by <see cref="ToolLoopCircuitBreaker"/> inspections.
/// </summary>
public record LoopCheckResult(
    bool IsLoopDetected,
    LoopType Type,
    string DiagnosticMessage,
    string? ToolName = null);

/// <summary>
/// Configuration options for <see cref="ToolLoopCircuitBreaker"/>.
/// </summary>
public class ToolLoopCircuitBreakerOptions
{
    /// <summary>
    /// When true (default), parses and sorts JSON argument keys before hashing, preventing key
    /// permutation false-negatives (e.g. {"a":1,"b":2} vs {"b":2,"a":1}).
    /// Set to false to bypass JSON parsing and perform raw string SHA-256 for maximum throughput.
    /// </summary>
    public bool CanonicalizeJsonArguments { get; set; } = true;

    /// <summary>Minimum repeating n-gram length in streaming text. Default: 16.</summary>
    public int NgramMinLength { get; set; } = 16;

    /// <summary>Minimum occurrence count to trip repetition breaker. Default: 3.</summary>
    public int NgramMinOccurrences { get; set; } = 3;

    /// <summary>Maximum tool invocation history entries kept for ping-pong detection. Default: 50.</summary>
    public int MaxToolHistory { get; set; } = 50;

    /// <summary>
    /// Consecutive identical invocations (same name, canonical arguments and state signature)
    /// that trip the ping-pong breaker. Default: 2 (the 2nd identical call is flagged).
    /// </summary>
    public int PingPongThreshold { get; set; } = 2;
}

/// <summary>
/// Circuit breaker protecting against runaway streaming generation loops ($n$-gram repetitions)
/// and duplicate tool invocation ping-pong on unchanged state.
/// Not thread-safe; instantiate one per session/task.
/// </summary>
public sealed class ToolLoopCircuitBreaker
{
    private readonly ToolLoopCircuitBreakerOptions _options;
    private readonly List<(string ToolHash, string? StateSignature)> _history = [];
    private string? _lastToolHash;
    private string? _lastStateSignature;
    private int _consecutiveIdenticalCount;
    private string? _lastObservationHash;
    private string? _lastUserPromptHash;

    public ToolLoopCircuitBreaker(ToolLoopCircuitBreakerOptions? options = null)
    {
        _options = options ?? new ToolLoopCircuitBreakerOptions();
        if (_options.NgramMinOccurrences < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "NgramMinOccurrences must be at least 2.");
        }
        if (_options.NgramMinLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "NgramMinLength must be at least 1.");
        }
        if (_options.PingPongThreshold < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "PingPongThreshold must be at least 1.");
        }
        if (_options.MaxToolHistory < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxToolHistory must be at least 1.");
        }
    }

    /// <summary>
    /// Resets invocation history and consecutive count. Call this between independent sessions or benchmark scenarios.
    /// </summary>
    public void Reset()
    {
        _history.Clear();
        _lastToolHash = null;
        _lastStateSignature = null;
        _consecutiveIdenticalCount = 0;
        _lastObservationHash = null;
        _lastUserPromptHash = null;
    }

    /// <summary>
    /// Resets the ping-pong counter and consecutive identical call tracking without clearing invocation history.
    /// Call this when forward step progress is observed to ensure valid multi-step exploration is not prematurely terminated.
    /// </summary>
    public void ResetPingPong()
    {
        _consecutiveIdenticalCount = 0;
        _lastToolHash = null;
        _lastStateSignature = null;
    }

    /// <summary>
    /// Records a tool execution observation or result. If the observation is distinct from the prior observation,
    /// resets the ping-pong counter to signal that the agent has received new information and is making forward progress.
    /// </summary>
    public void RecordObservation(string? observation)
    {
        if (observation == null) return;
        var obsHash = ComputeHash(observation);
        if (_lastObservationHash == null || !string.Equals(_lastObservationHash, obsHash, StringComparison.Ordinal))
        {
            ResetPingPong();
        }
        _lastObservationHash = obsHash;
    }

    /// <summary>
    /// Records a tool execution observation or result object.
    /// </summary>
    public void RecordObservation(object? observation)
    {
        if (observation == null) return;
        RecordObservation(observation.ToString());
    }

    /// <summary>
    /// Determines whether the prompt is an automated step continuation instruction rather than a new guiding user prompt.
    /// </summary>
    public static bool IsContinuationPrompt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var trimmed = text.Trim();
        return int.TryParse(trimmed, out _)
            || trimmed.StartsWith("Proceed with step", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Step ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Step", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Turn ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Turn", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "continue", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "next", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Records a user prompt. If the prompt represents a new guiding instruction from the user (different from
    /// the previous prompt), resets the ping-pong counter to allow fresh exploration.
    /// </summary>
    public void RecordUserPrompt(string? userPrompt)
    {
        if (string.IsNullOrWhiteSpace(userPrompt) || IsContinuationPrompt(userPrompt)) return;
        var promptHash = ComputeHash(userPrompt.Trim());
        if (_lastUserPromptHash != null && !string.Equals(_lastUserPromptHash, promptHash, StringComparison.Ordinal))
        {
            ResetPingPong();
        }
        _lastUserPromptHash = promptHash;
    }

    /// <summary>
    /// Scans a text buffer for repeating character sequences ($\ge \text{NgramMinLength}$ repeated $\ge \text{NgramMinOccurrences}$ times).
    /// Only scans a bounded tail window for $O(1)$ per-chunk performance.
    /// </summary>
    public LoopCheckResult DetectStreamingRepetition(ReadOnlySpan<char> accumulatedText)
    {
        int minLen = _options.NgramMinLength;
        int minOccurrences = _options.NgramMinOccurrences;

        if (accumulatedText.Length < minLen * minOccurrences)
        {
            return new LoopCheckResult(false, LoopType.None, string.Empty);
        }

        // Bounded tail scan: at most 128 chars pattern length or text.Length / minOccurrences
        int maxLen = Math.Min(accumulatedText.Length / minOccurrences, 128);

        for (int len = minLen; len <= maxLen; len++)
        {
            var pattern = accumulatedText.Slice(accumulatedText.Length - len, len);
            int count = 1;
            int checkIndex = accumulatedText.Length - 2 * len;

            while (checkIndex >= 0 && accumulatedText.Slice(checkIndex, len).SequenceEqual(pattern))
            {
                count++;
                checkIndex -= len;
                if (count >= minOccurrences)
                {
                    var sample = pattern.Length > 32 ? string.Concat(pattern[..32].ToString(), "...") : pattern.ToString();
                    return new LoopCheckResult(
                        true,
                        LoopType.StreamingRepetition,
                        $"Streaming repetition loop detected: sequence of length {len} repeated {count} times ('{sample}').");
                }
            }
        }

        return new LoopCheckResult(false, LoopType.None, string.Empty);
    }

    /// <summary>
    /// Records and evaluates a tool invocation with arguments supplied as a dictionary.
    /// </summary>
    public LoopCheckResult EvaluateToolInvocation(string toolName, IDictionary<string, object?>? arguments, string? stateSignature = null)
    {
        return EvaluateToolInvocation(toolName, SerializeArguments(arguments), stateSignature);
    }

    /// <summary>
    /// Records and evaluates a tool invocation with arguments supplied as a dictionary and an optional observation from the step.
    /// </summary>
    public LoopCheckResult EvaluateToolInvocation(string toolName, IDictionary<string, object?>? arguments, string? stateSignature, string? observation)
    {
        if (observation != null)
        {
            RecordObservation(observation);
        }
        return EvaluateToolInvocation(toolName, arguments, stateSignature);
    }

    /// <summary>
    /// Records and evaluates a tool invocation with an optional observation from the step.
    /// </summary>
    public LoopCheckResult EvaluateToolInvocation(string toolName, string argumentsJson, string? stateSignature, string? observation)
    {
        if (observation != null)
        {
            RecordObservation(observation);
        }
        return EvaluateToolInvocation(toolName, argumentsJson, stateSignature);
    }

    /// <summary>
    /// Records and evaluates a tool invocation.
    /// <paramref name="stateSignature"/> is an opaque caller-supplied token (e.g. hash of workspace/file state).
    /// When null, state is considered unknown and consecutive identical calls are evaluated directly.
    /// </summary>
    public LoopCheckResult EvaluateToolInvocation(string toolName, string argumentsJson, string? stateSignature = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        var argsToHash = _options.CanonicalizeJsonArguments
            ? CanonicalizeJson(argumentsJson)
            : (argumentsJson ?? string.Empty);

        var toolHash = ComputeHash($"{toolName}:{argsToHash}");

        bool stateMatches = string.Equals(_lastStateSignature, stateSignature, StringComparison.Ordinal);
        bool callMatches = string.Equals(_lastToolHash, toolHash, StringComparison.Ordinal);

        if (callMatches && (stateMatches || (stateSignature == null && _lastStateSignature == null)))
        {
            _consecutiveIdenticalCount++;
            if (_consecutiveIdenticalCount >= _options.PingPongThreshold)
            {
                var diag = $"Duplicate tool invocation detected: '{toolName}' was called {_consecutiveIdenticalCount} consecutive times with identical arguments on unchanged state (threshold: {_options.PingPongThreshold}). Cease repetitive tool invocation.";
                return new LoopCheckResult(true, LoopType.ToolPingPong, diag, toolName);
            }
        }
        else
        {
            _consecutiveIdenticalCount = 1;
        }

        _lastToolHash = toolHash;
        _lastStateSignature = stateSignature;

        _history.Add((toolHash, stateSignature));
        if (_history.Count > _options.MaxToolHistory)
        {
            _history.RemoveAt(0);
        }

        // Check for multi-tool ping-pong cycles (L >= 2) across history on unchanged state
        int maxCycleLen = Math.Min(10, _history.Count / 2);
        for (int L = 2; L <= maxCycleLen; L++)
        {
            bool cycleMatches = true;
            for (int k = 0; k < L; k++)
            {
                var cur = _history[_history.Count - 1 - k];
                var prev = _history[_history.Count - 1 - k - L];

                if (!string.Equals(cur.ToolHash, prev.ToolHash, StringComparison.Ordinal) ||
                    !string.Equals(cur.StateSignature, prev.StateSignature, StringComparison.Ordinal))
                {
                    cycleMatches = false;
                    break;
                }
            }

            if (cycleMatches)
            {
                var diag = $"Tool ping-pong loop detected: cycle of {L} tool invocations repeated with identical arguments on unchanged state. Cease repetitive tool invocation.";
                return new LoopCheckResult(true, LoopType.ToolPingPong, diag, toolName);
            }
        }

        return new LoopCheckResult(false, LoopType.None, string.Empty, toolName);
    }


    private static string CanonicalizeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "{}";
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder();
            WriteCanonicalElement(doc.RootElement, sb);
            return sb.ToString();
        }
        catch
        {
            return json.Trim();
        }
    }

    private static void WriteCanonicalElement(JsonElement element, StringBuilder sb)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                sb.Append('{');
                var props = element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
                for (int i = 0; i < props.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(props[i].Name).Append("\":");
                    WriteCanonicalElement(props[i].Value, sb);
                }
                sb.Append('}');
                break;

            case JsonValueKind.Array:
                sb.Append('[');
                int idx = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (idx > 0) sb.Append(',');
                    WriteCanonicalElement(item, sb);
                    idx++;
                }
                sb.Append(']');
                break;

            case JsonValueKind.String:
                sb.Append('"').Append(element.GetString()).Append('"');
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                sb.Append(element.GetRawText());
                break;

            default:
                sb.Append(element.GetRawText());
                break;
        }
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Serializes an arguments dictionary to a canonical JSON string without reflection.
    /// </summary>
    public static string SerializeArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments == null || arguments.Count == 0)
        {
            return "{}";
        }

        var sb = new StringBuilder("{");
        bool first = true;
        foreach (var kvp in arguments.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(kvp.Key).Append("\":");
            WriteValue(sb, kvp.Value);
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static void WriteValue(StringBuilder sb, object? value)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                break;
            case string s:
                sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"');
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                sb.Append(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case JsonElement je:
                sb.Append(je.GetRawText());
                break;
            default:
                sb.Append('"').Append(value.ToString()?.Replace("\"", "\\\"")).Append('"');
                break;
        }
    }
}
