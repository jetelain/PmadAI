using System.Collections.Concurrent;

namespace Pmad.AI.Tools;

/// <summary>
/// Categories of lenient dispatch interventions and remappings.
/// </summary>
public enum LenientDispatchEventType
{
    /// <summary>Tool name alias remapped to canonical tool name (e.g. get_current_weather -> get_weather).</summary>
    ToolAliasResolved,

    /// <summary>Parameter name alias remapped to canonical parameter (e.g. city -> location).</summary>
    ParameterAliasResolved,

    /// <summary>Namespace prefix stripped from tool name (e.g. functions.get_weather -> get_weather).</summary>
    NamespaceStripped,

    /// <summary>Case-folding or casing normalization applied to match tool or parameter.</summary>
    CasingNormalized,

    /// <summary>String argument value snapped to schema enum or const member.</summary>
    EnumValueSnapped,

    /// <summary>String argument value did not match any schema enum or const member and could not be unambiguously snapped.</summary>
    EnumValueUnmatched
}

/// <summary>
/// Telemetry record emitted when lenient dispatch resolves an alias, namespace, or casing drift.
/// </summary>
public sealed record LenientDispatchEvent(
    DateTimeOffset Timestamp,
    LenientDispatchEventType Type,
    string RequestedName,
    string ResolvedName,
    string? SessionId = null
);

/// <summary>
/// Immutable snapshot of aggregate lenient dispatch interventions.
/// </summary>
public sealed record LenientDispatchTelemetrySnapshot(
    int TotalHits,
    int ToolAliasesResolved,
    int ParameterAliasesResolved,
    int NamespacesStripped,
    int CasingNormalized,
    int EnumValuesSnapped = 0,
    int EnumValuesUnmatched = 0
);

/// <summary>
/// Thread-safe collector and publisher of lenient tool dispatch telemetry.
/// </summary>
public sealed class LenientDispatchTelemetry
{
    private readonly ConcurrentBag<LenientDispatchEvent> _events = [];

    /// <summary>
    /// Event fired whenever lenient dispatch resolves an alias or remapping.
    /// </summary>
    public event Action<LenientDispatchEvent>? EventRecorded;

    /// <summary>
    /// Collection of all dispatch events recorded.
    /// </summary>
    public IReadOnlyCollection<LenientDispatchEvent> Events => _events;

    /// <summary>
    /// Records a dispatch remapping event.
    /// </summary>
    public void Record(LenientDispatchEventType type, string requested, string resolved, string? sessionId = null)
    {
        var evt = new LenientDispatchEvent(DateTimeOffset.UtcNow, type, requested, resolved, sessionId);
        _events.Add(evt);
        EventRecorded?.Invoke(evt);
    }

    /// <summary>
    /// Creates an aggregated snapshot of all recorded dispatch events, optionally filtered by session ID.
    /// </summary>
    public LenientDispatchTelemetrySnapshot CreateSnapshot(string? sessionId = null)
    {
        var filtered = string.IsNullOrWhiteSpace(sessionId)
            ? _events
            : _events.Where(e => string.Equals(e.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));

        int toolAliases = 0, paramAliases = 0, namespaces = 0, casing = 0, enumSnapped = 0, enumUnmatched = 0;
        foreach (var e in filtered)
        {
            switch (e.Type)
            {
                case LenientDispatchEventType.ToolAliasResolved: toolAliases++; break;
                case LenientDispatchEventType.ParameterAliasResolved: paramAliases++; break;
                case LenientDispatchEventType.NamespaceStripped: namespaces++; break;
                case LenientDispatchEventType.CasingNormalized: casing++; break;
                case LenientDispatchEventType.EnumValueSnapped: enumSnapped++; break;
                case LenientDispatchEventType.EnumValueUnmatched: enumUnmatched++; break;
            }
        }

        var total = toolAliases + paramAliases + namespaces + casing + enumSnapped;
        return new LenientDispatchTelemetrySnapshot(total, toolAliases, paramAliases, namespaces, casing, enumSnapped, enumUnmatched);
    }
}
