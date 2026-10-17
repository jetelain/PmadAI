using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pmad.AI.Benchmark.Scenarios.SweBench;

/// <summary>
/// Domain model for an authentic or synthetic SWE-bench agent trajectory.
/// </summary>
public sealed class SweBenchTrajectory
{
    [JsonPropertyName("instance_id")]
    public string InstanceId { get; set; } = string.Empty;

    [JsonPropertyName("repository")]
    public string Repository { get; set; } = string.Empty;

    [JsonPropertyName("archetype")]
    public string Archetype { get; set; } = string.Empty;

    [JsonPropertyName("problem_statement")]
    public string ProblemStatement { get; set; } = string.Empty;

    [JsonPropertyName("turns")]
    public List<SweBenchTurnData> Turns { get; set; } = [];

    /// <summary>
    /// Total characters across all user prompts and tool observations in the raw trace.
    /// </summary>
    public int CalculateTotalRawChars()
    {
        int total = 0;
        foreach (var t in Turns)
        {
            if (!string.IsNullOrEmpty(t.UserPrompt)) total += t.UserPrompt.Length;
            if (!string.IsNullOrEmpty(t.AssistantThought)) total += t.AssistantThought.Length;
            if (!string.IsNullOrEmpty(t.Observation)) total += t.Observation.Length;
        }
        return total;
    }

    /// <summary>
    /// Heuristic estimate of raw total tokens (~3.8 chars per token).
    /// </summary>
    public int CalculateEstimatedRawTokens() => Math.Max(1, (int)(CalculateTotalRawChars() / 3.8));
}

/// <summary>
/// A single turn in a SWE-bench trajectory.
/// </summary>
public sealed class SweBenchTurnData
{
    [JsonPropertyName("turn_index")]
    public int TurnIndex { get; set; }

    [JsonPropertyName("user_prompt")]
    public string? UserPrompt { get; set; }

    [JsonPropertyName("assistant_thought")]
    public string? AssistantThought { get; set; }

    [JsonPropertyName("expected_tool_name")]
    public string? ExpectedToolName { get; set; }

    [JsonPropertyName("tool_arguments")]
    public JsonElement? ToolArguments { get; set; }

    [JsonPropertyName("observation")]
    public string? Observation { get; set; }

    [JsonPropertyName("expected_completion_substring")]
    public string? ExpectedCompletionSubstring { get; set; }

    /// <summary>
    /// Returns the tool arguments as a dictionary for Microsoft.Extensions.AI invocation.
    /// </summary>
    public IDictionary<string, object?> GetArgumentsDictionary()
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (ToolArguments.HasValue && ToolArguments.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in ToolArguments.Value.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => prop.Value.GetRawText()
                };
            }
        }
        return dict;
    }
}
