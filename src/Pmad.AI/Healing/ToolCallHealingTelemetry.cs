using System.Collections.Concurrent;

namespace Pmad.AI.Healing;

/// <summary>
/// Categories of protocol repair and syntax healing operations performed by <see cref="ToolCallHealingChatClient"/>.
/// </summary>
public enum ToolCallHealingEventType
{
    /// <summary>Extracted embedded pseudo-tags such as &lt;tool_call&gt; or &lt;function_call&gt; from text content.</summary>
    PseudoTagExtracted,

    /// <summary>Extracted ReAct action blocks such as Action: / Action Input: from text content.</summary>
    ActionBlockExtracted,

    /// <summary>Repaired malformed JSON syntax (trailing commas, unescaped quotes/controls) in argument payloads.</summary>
    JsonRepaired,

    /// <summary>Stripped markdown code fences (```json ... ```) wrapping argument payloads.</summary>
    FenceStripped,

    /// <summary>Generated a missing unique tool call ID when none was emitted by the model.</summary>
    CallIdGenerated,

    /// <summary>Unwrapped a synthetic single-field input object containing stringified JSON arguments.</summary>
    ArgumentUnwrapped
}

/// <summary>
/// Telemetry record emitted when a tool call healing intervention occurs.
/// </summary>
public sealed record ToolCallHealingEvent(
    DateTimeOffset Timestamp,
    ToolCallHealingEventType Type,
    string? ToolName,
    string? SessionId,
    string? Details = null
);

/// <summary>
/// Immutable snapshot of aggregate healing interventions.
/// </summary>
public sealed record ToolCallHealingTelemetrySnapshot(
    int TotalInterventions,
    int PseudoTagsExtracted,
    int ActionBlocksExtracted,
    int JsonRepairs,
    int FencesStripped,
    int CallIdsGenerated,
    int ArgumentsUnwrapped
);

/// <summary>
/// Thread-safe collector and publisher of tool call healing telemetry.
/// </summary>
public sealed class ToolCallHealingTelemetry
{
    private readonly ConcurrentBag<ToolCallHealingEvent> _events = [];

    /// <summary>
    /// Event fired whenever a new healing intervention is recorded.
    /// </summary>
    public event Action<ToolCallHealingEvent>? EventRecorded;

    /// <summary>
    /// Collection of all healing events recorded.
    /// </summary>
    public IReadOnlyCollection<ToolCallHealingEvent> Events => _events;

    /// <summary>
    /// Records a healing intervention.
    /// </summary>
    public void Record(ToolCallHealingEventType type, string? toolName, string? sessionId = null, string? details = null)
    {
        var evt = new ToolCallHealingEvent(DateTimeOffset.UtcNow, type, toolName, sessionId, details);
        _events.Add(evt);
        EventRecorded?.Invoke(evt);
    }

    /// <summary>
    /// Creates an aggregated snapshot of all recorded healing interventions, optionally filtered by session ID.
    /// </summary>
    public ToolCallHealingTelemetrySnapshot CreateSnapshot(string? sessionId = null)
    {
        var filtered = string.IsNullOrWhiteSpace(sessionId)
            ? _events
            : _events.Where(e => string.Equals(e.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));

        int tags = 0, actions = 0, json = 0, fences = 0, ids = 0, unwrapped = 0;
        foreach (var e in filtered)
        {
            switch (e.Type)
            {
                case ToolCallHealingEventType.PseudoTagExtracted: tags++; break;
                case ToolCallHealingEventType.ActionBlockExtracted: actions++; break;
                case ToolCallHealingEventType.JsonRepaired: json++; break;
                case ToolCallHealingEventType.FenceStripped: fences++; break;
                case ToolCallHealingEventType.CallIdGenerated: ids++; break;
                case ToolCallHealingEventType.ArgumentUnwrapped: unwrapped++; break;
            }
        }

        var total = tags + actions + json + fences + ids + unwrapped;
        return new ToolCallHealingTelemetrySnapshot(total, tags, actions, json, fences, ids, unwrapped);
    }
}
