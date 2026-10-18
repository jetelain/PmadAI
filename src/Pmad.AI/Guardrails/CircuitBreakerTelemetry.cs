using System.Collections.Concurrent;

namespace Pmad.AI.Guardrails;

/// <summary>
/// Categories of circuit breaker trip events.
/// </summary>
public enum CircuitBreakerEventType
{
    /// <summary>Infinite repeating character/n-gram loop detected in streaming output.</summary>
    StreamingRepetition,

    /// <summary>Repetitive identical tool invocation on unchanged state detected.</summary>
    ToolPingPong
}

/// <summary>
/// Telemetry record emitted when a circuit breaker trips.
/// </summary>
public sealed record CircuitBreakerEvent(
    DateTimeOffset Timestamp,
    CircuitBreakerEventType Type,
    string? ToolName,
    string? SessionId,
    string? Diagnostic = null
);

/// <summary>
/// Immutable snapshot of aggregate circuit breaker trips.
/// </summary>
public sealed record CircuitBreakerTelemetrySnapshot(
    int TotalTrips,
    int StreamingRepetitionTrips,
    int ToolPingPongTrips
);

/// <summary>
/// Thread-safe collector and publisher of circuit breaker telemetry.
/// </summary>
public sealed class CircuitBreakerTelemetry
{
    private readonly ConcurrentBag<CircuitBreakerEvent> _events = [];

    /// <summary>
    /// Event fired whenever the circuit breaker trips.
    /// </summary>
    public event Action<CircuitBreakerEvent>? EventRecorded;

    /// <summary>
    /// Collection of all circuit breaker events recorded.
    /// </summary>
    public IReadOnlyCollection<CircuitBreakerEvent> Events => _events;

    /// <summary>
    /// Records a circuit breaker trip event.
    /// </summary>
    public void Record(CircuitBreakerEventType type, string? toolName, string? diagnostic, string? sessionId = null)
    {
        var evt = new CircuitBreakerEvent(DateTimeOffset.UtcNow, type, toolName, sessionId, diagnostic);
        _events.Add(evt);
        EventRecorded?.Invoke(evt);
    }

    /// <summary>
    /// Creates an aggregated snapshot of all recorded breaker events, optionally filtered by session ID.
    /// </summary>
    public CircuitBreakerTelemetrySnapshot CreateSnapshot(string? sessionId = null)
    {
        var filtered = string.IsNullOrWhiteSpace(sessionId)
            ? _events
            : _events.Where(e => string.Equals(e.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));

        int streaming = 0, pingPong = 0;
        foreach (var e in filtered)
        {
            switch (e.Type)
            {
                case CircuitBreakerEventType.StreamingRepetition: streaming++; break;
                case CircuitBreakerEventType.ToolPingPong: pingPong++; break;
            }
        }

        return new CircuitBreakerTelemetrySnapshot(streaming + pingPong, streaming, pingPong);
    }
}
