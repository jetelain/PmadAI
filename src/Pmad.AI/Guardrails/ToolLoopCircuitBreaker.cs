using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pmad.AI.Guardrails;

/// <summary>
/// Type of loop detected by <see cref="ToolLoopCircuitBreaker"/>.
/// </summary>
public enum LoopType
{
    None,
    StreamingRepetition,
    ToolPingPong,
    NoProgress
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
    /// that trip the ping-pong breaker. Default: 3 (the 3rd identical call is flagged).
    /// </summary>
    public int PingPongThreshold { get; set; } = 3;

    /// <summary>
    /// Maximum consecutive identical tool observation streak across turns before tripping the circuit breaker with
    /// <see cref="LoopType.NoProgress"/>, regardless of argument variations. Default: 0 (disabled).
    /// Recommended presets configure this to 3.
    /// </summary>
    public int MaxIdenticalObservationStreak { get; set; } = 0;

    /// <summary>
    /// Maximum consecutive tool errors from the same tool before tripping the circuit breaker with
    /// <see cref="LoopType.NoProgress"/>, regardless of argument variations. Default: 0 (disabled).
    /// An observation is treated as an error when it represents an exception or begins with 'Error:' or 'File ... not found'.
    /// Recommended presets configure this to 5.
    /// </summary>
    public int MaxConsecutiveToolErrors { get; set; } = 0;

    /// <summary>
    /// When true, resetting ping-pong tracking occurs when a distinct user prompt is observed.
    /// Default: true (distinct user messages allow fresh tool exploration).
    /// </summary>
    public bool ResetOnUserPrompt { get; set; } = true;

    /// <summary>
    /// Optional telemetry collector to record circuit breaker trip events.
    /// </summary>
    public CircuitBreakerTelemetry? Telemetry { get; set; }

    /// <summary>
    /// Optional session identifier propagated to telemetry events.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Optional callback invoked whenever a loop is detected.
    /// </summary>
    public Action<LoopCheckResult>? OnLoopDetected { get; set; }
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
    private int _identicalObservationStreak;
    private readonly Dictionary<string, int> _consecutiveToolErrors = new(StringComparer.OrdinalIgnoreCase);
    private LoopCheckResult? _trippedStreakResult;

    /// <summary>
    /// Event fired when the circuit breaker is reset.
    /// </summary>
    public event Action? OnReset;

    public ToolLoopCircuitBreakerOptions Options => _options;

    /// <summary>
    /// Gets a value indicating whether a semantic observation or error streak has tripped the circuit breaker.
    /// </summary>
    public bool IsTripped => _trippedStreakResult != null;

    /// <summary>
    /// Checks whether the circuit breaker has tripped from an observation streak or consecutive error streak.
    /// Optionally checks for a specific tool.
    /// </summary>
    public bool TryGetTrippedResult([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out LoopCheckResult? result, string? toolName = null)
    {
        if (_trippedStreakResult != null)
        {
            if (toolName == null || _trippedStreakResult.ToolName == null || string.Equals(_trippedStreakResult.ToolName, toolName, StringComparison.OrdinalIgnoreCase))
            {
                result = _trippedStreakResult;
                return true;
            }
        }

        if (toolName != null && _options.MaxConsecutiveToolErrors > 0 &&
            _consecutiveToolErrors.TryGetValue(toolName, out var errCount) && errCount >= _options.MaxConsecutiveToolErrors)
        {
            var diag = $"Tool failure streak detected: tool '{toolName}' failed with {errCount} consecutive errors (threshold: {_options.MaxConsecutiveToolErrors}). Cease repetitive failing tool calls.";
            result = new LoopCheckResult(true, LoopType.NoProgress, diag, toolName);
            return true;
        }

        result = null;
        return false;
    }

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
        _identicalObservationStreak = 0;
        _consecutiveToolErrors.Clear();
        _trippedStreakResult = null;
        OnReset?.Invoke();
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

    private static readonly Regex JsonErrorRegex = new(@"^\{\s*""(?:error|errors)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Determines whether an observation text represents an error or exception result from a tool.
    /// Recognizes standard patterns including 'Error:', '[ERROR]', 'Exception:', 'Fatal:', JSON error objects,
    /// 'File ... not found', and 'Hunk context mismatch'.
    /// </summary>
    public static bool IsErrorObservation(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.TrimStart();
        return trimmed.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Exception:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Fatal:", StringComparison.OrdinalIgnoreCase)
            || JsonErrorRegex.IsMatch(trimmed)
            || (trimmed.StartsWith("File ", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("not found", StringComparison.OrdinalIgnoreCase))
            || trimmed.StartsWith("Hunk context mismatch", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Records a tool execution observation or result. Tracks observation hashes and error streaks across tools.
    /// Resets the exact-hash ping-pong counter upon receiving a distinct observation, and resets streak counters
    /// upon receiving a successful, non-error, non-repeated observation.
    /// </summary>
    public void RecordObservation(string? observation, string? toolName = null, bool? isError = null)
    {
        if (observation == null) return;
        var obsHash = ComputeHash(observation);
        bool error = isError ?? IsErrorObservation(observation);

        if (error)
        {
            if (!string.IsNullOrWhiteSpace(toolName))
            {
                int errCount = _consecutiveToolErrors.GetValueOrDefault(toolName, 0) + 1;
                _consecutiveToolErrors[toolName] = errCount;
                if (_options.MaxConsecutiveToolErrors > 0 && errCount >= _options.MaxConsecutiveToolErrors)
                {
                    var diag = $"Tool failure streak detected: tool '{toolName}' failed with {errCount} consecutive errors (threshold: {_options.MaxConsecutiveToolErrors}). Cease repetitive failing tool calls.";
                    _trippedStreakResult = new LoopCheckResult(true, LoopType.NoProgress, diag, toolName);
                }
            }

            if (_lastObservationHash != null && string.Equals(_lastObservationHash, obsHash, StringComparison.Ordinal))
            {
                _identicalObservationStreak++;
            }
            else
            {
                _identicalObservationStreak = 1;
            }

            if (_options.MaxIdenticalObservationStreak > 0 && _identicalObservationStreak >= _options.MaxIdenticalObservationStreak)
            {
                var diag = $"Identical tool observation streak detected: tool execution produced {_identicalObservationStreak} consecutive identical observations (threshold: {_options.MaxIdenticalObservationStreak}). Cease repetitive tool invocation.";
                _trippedStreakResult = new LoopCheckResult(true, LoopType.NoProgress, diag, null);
            }

            // Per Recommendation 3 Point 3:
            // A changed observation hash only resets the exact-hash ping-pong counter.
            if (_lastObservationHash == null || !string.Equals(_lastObservationHash, obsHash, StringComparison.Ordinal))
            {
                ResetPingPong();
            }
            _lastObservationHash = obsHash;
        }
        else
        {
            // Observation is NOT an error.
            if (_lastObservationHash != null && string.Equals(_lastObservationHash, obsHash, StringComparison.Ordinal))
            {
                // Repeated identical observation
                _identicalObservationStreak++;
                if (_options.MaxIdenticalObservationStreak > 0 && _identicalObservationStreak >= _options.MaxIdenticalObservationStreak)
                {
                    var diag = $"Identical tool observation streak detected: tool execution produced {_identicalObservationStreak} consecutive identical observations (threshold: {_options.MaxIdenticalObservationStreak}). Cease repetitive tool invocation.";
                    _trippedStreakResult = new LoopCheckResult(true, LoopType.NoProgress, diag, null);
                }
            }
            else
            {
                // Successful, non-error, non-repeated observation!
                // Streak counters reset ONLY upon receiving a successful, non-error, non-repeated observation.
                ResetPingPong();
                _identicalObservationStreak = 1;
                _consecutiveToolErrors.Clear();
                _trippedStreakResult = null;
            }

            _lastObservationHash = obsHash;
        }
    }

    /// <summary>
    /// Records a tool execution observation or result object.
    /// </summary>
    public void RecordObservation(object? observation, string? toolName = null, bool? isError = null)
    {
        if (observation == null) return;
        RecordObservation(observation.ToString(), toolName, isError);
    }

    /// <summary>
    /// Records a tool execution observation or result. If the observation is distinct from the prior observation,
    /// resets the ping-pong counter to signal that the agent has received new information and is making forward progress.
    /// </summary>
    public void RecordObservation(string? observation)
    {
        RecordObservation(observation, null, null);
    }

    /// <summary>
    /// Records a tool execution observation or result object.
    /// </summary>
    public void RecordObservation(object? observation)
    {
        RecordObservation(observation, null, null);
    }

    private static readonly Regex ContinuationPrefixRegex = new(@"^(?:proceed\s+with\s+)?(?:step|turn)\s*[:#]?\s*\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RetryKeywordRegex = new(@"\b(?:again|one\s+more\s+time|retry|retries|try\s+again|repeat)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Determines whether the prompt is an automated step continuation or retry instruction rather than a new guiding user prompt.
    /// </summary>
    public static bool IsContinuationPrompt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var trimmed = text.Trim();
        return int.TryParse(trimmed, out _)
            || string.Equals(trimmed, "continue", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "next", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "step", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "turn", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "again", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "retry", StringComparison.OrdinalIgnoreCase)
            || ContinuationPrefixRegex.IsMatch(trimmed)
            || RetryKeywordRegex.IsMatch(trimmed);
    }

    /// <summary>
    /// Records a user prompt. When <see cref="ToolLoopCircuitBreakerOptions.ResetOnUserPrompt"/> is enabled (the default) and the prompt represents
    /// a new guiding instruction from the user (different from the previous prompt and not a continuation/retry instruction),
    /// resets the ping-pong counter, observation streak, and per-tool error counters to allow fresh exploration.
    /// When disabled (<see cref="ToolLoopCircuitBreakerOptions.ResetOnUserPrompt"/> = false), resets are state-driven
    /// (triggering only when tool signatures or observation hashes change).
    /// </summary>
    public void RecordUserPrompt(string? userPrompt)
    {
        if (string.IsNullOrWhiteSpace(userPrompt) || IsContinuationPrompt(userPrompt)) return;
        var promptHash = ComputeHash(userPrompt.Trim());
        if (_options.ResetOnUserPrompt && _lastUserPromptHash != null && !string.Equals(_lastUserPromptHash, promptHash, StringComparison.Ordinal))
        {
            ResetPingPong();
            _identicalObservationStreak = 0;
            _consecutiveToolErrors.Clear();
            _trippedStreakResult = null;
            _lastObservationHash = null;
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
                    return RecordTrip(new LoopCheckResult(
                        true,
                        LoopType.StreamingRepetition,
                        $"Streaming repetition loop detected: sequence of length {len} repeated {count} times ('{sample}')."));
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
            RecordObservation(observation, toolName);
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
            RecordObservation(observation, toolName);
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

        if (_trippedStreakResult != null)
        {
            if (_trippedStreakResult.ToolName == null || string.Equals(_trippedStreakResult.ToolName, toolName, StringComparison.OrdinalIgnoreCase))
            {
                return RecordTrip(_trippedStreakResult, toolName);
            }
        }

        if (_options.MaxConsecutiveToolErrors > 0 && _consecutiveToolErrors.TryGetValue(toolName, out var errCount) && errCount >= _options.MaxConsecutiveToolErrors)
        {
            var diag = $"Tool failure streak detected: tool '{toolName}' failed with {errCount} consecutive errors (threshold: {_options.MaxConsecutiveToolErrors}). Cease repetitive failing tool calls.";
            return RecordTrip(new LoopCheckResult(true, LoopType.NoProgress, diag, toolName));
        }

        if (_options.MaxIdenticalObservationStreak > 0 && _identicalObservationStreak >= _options.MaxIdenticalObservationStreak)
        {
            var diag = $"Identical tool observation streak detected: tool execution produced {_identicalObservationStreak} consecutive identical observations (threshold: {_options.MaxIdenticalObservationStreak}). Cease repetitive tool invocation.";
            return RecordTrip(new LoopCheckResult(true, LoopType.NoProgress, diag, toolName));
        }

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
                return RecordTrip(new LoopCheckResult(true, LoopType.ToolPingPong, diag, toolName));
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
                return RecordTrip(new LoopCheckResult(true, LoopType.ToolPingPong, diag, toolName));
            }
        }

        return new LoopCheckResult(false, LoopType.None, string.Empty, toolName);
    }

    private LoopCheckResult RecordTrip(LoopCheckResult result, string? fallbackToolName = null)
    {
        if (result.IsLoopDetected)
        {
            var effectiveTool = result.ToolName ?? fallbackToolName;
            var eventType = result.Type switch
            {
                LoopType.StreamingRepetition => CircuitBreakerEventType.StreamingRepetition,
                LoopType.NoProgress => CircuitBreakerEventType.NoProgress,
                _ => CircuitBreakerEventType.ToolPingPong
            };
            _options.Telemetry?.Record(eventType, effectiveTool, result.DiagnosticMessage, _options.SessionId);
            _options.OnLoopDetected?.Invoke(result.ToolName == null && effectiveTool != null ? result with { ToolName = effectiveTool } : result);
        }
        return result;
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
                int propCount = 0;
                foreach (var _ in element.EnumerateObject()) propCount++;

                if (propCount <= 32)
                {
                    var propArray = System.Buffers.ArrayPool<JsonProperty>.Shared.Rent(propCount);
                    try
                    {
                        int pIdx = 0;
                        foreach (var prop in element.EnumerateObject())
                        {
                            propArray[pIdx++] = prop;
                        }
                        for (int i = 1; i < propCount; i++)
                        {
                            var keyProp = propArray[i];
                            int j = i - 1;
                            while (j >= 0 && string.CompareOrdinal(propArray[j].Name, keyProp.Name) > 0)
                            {
                                propArray[j + 1] = propArray[j];
                                j--;
                            }
                            propArray[j + 1] = keyProp;
                        }
                        for (int i = 0; i < propCount; i++)
                        {
                            if (i > 0) sb.Append(',');
                            sb.Append('"').Append(propArray[i].Name).Append("\":");
                            WriteCanonicalElement(propArray[i].Value, sb);
                        }
                    }
                    finally
                    {
                        System.Buffers.ArrayPool<JsonProperty>.Shared.Return(propArray);
                    }
                }
                else
                {
                    var props = element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
                    for (int i = 0; i < props.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append('"').Append(props[i].Name).Append("\":");
                        WriteCanonicalElement(props[i].Value, sb);
                    }
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
        int maxBytes = Encoding.UTF8.GetMaxByteCount(input.Length);
        if (maxBytes <= 512)
        {
            Span<byte> utf8Bytes = stackalloc byte[maxBytes];
            int written = Encoding.UTF8.GetBytes(input, utf8Bytes);
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(utf8Bytes[..written], hash);
            return Convert.ToHexString(hash);
        }
        else
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(bytes, hash);
            return Convert.ToHexString(hash);
        }
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
