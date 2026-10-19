using System.Collections.Concurrent;
using System.Globalization;

namespace Pmad.AI.Compaction;

/// <summary>
/// Canonical compaction method identifiers.
/// </summary>
public static class CompactionMethods
{
    /// <summary>Deterministic truncation stub for chain-of-thought / reasoning monologue.</summary>
    public const string DeterministicStub = "DeterministicStub";

    /// <summary>Semantic summary produced by speculative SLM or background summarizer.</summary>
    public const string SemanticSummary = "SemanticSummary";

    /// <summary>Prefix and suffix preserving truncation for bulky tool execution results.</summary>
    public const string PrefixSuffixTruncation = "PrefixSuffixTruncation";
}

/// <summary>
/// Telemetry record emitted when a conversation item is pruned or summarized.
/// </summary>
/// <param name="Timestamp">Point in time when the compaction occurred.</param>
/// <param name="SessionId">Identifier of the conversation session.</param>
/// <param name="Target">Target category of the compacted item (tool result or reasoning trace).</param>
/// <param name="ItemKey">Unique key or CallId of the compacted item.</param>
/// <param name="OriginalChars">Character count before compaction.</param>
/// <param name="CompactedChars">Character count after compaction.</param>
/// <param name="WasSpeculativeHit">True if a background speculative summary was hit; false if fallback was applied.</param>
/// <param name="Method">Compaction method applied (e.g. DeterministicStub, SemanticSummary, PrefixSuffixTruncation).</param>
public sealed record CompactionEvent(
    DateTimeOffset Timestamp,
    string SessionId,
    CompactionTarget Target,
    string ItemKey,
    int OriginalChars,
    int CompactedChars,
    bool WasSpeculativeHit,
    string Method
)
{
    /// <summary>
    /// Characters saved by this compaction event.
    /// </summary>
    public int PrunedChars => Math.Max(0, OriginalChars - CompactedChars);

    /// <summary>
    /// Estimated prompt tokens saved using standard ~4 characters per token heuristic.
    /// </summary>
    public int EstimatedPrunedTokens => (int)Math.Round(PrunedChars / 4.0);

    /// <summary>
    /// Human-readable display label for the compaction method.
    /// </summary>
    public string MethodDisplayName => Method switch
    {
        CompactionMethods.SemanticSummary => "Semantic Summary",
        CompactionMethods.DeterministicStub => "Deterministic Stub",
        CompactionMethods.PrefixSuffixTruncation => "Prefix-Suffix Truncation",
        _ => Method
    };

    /// <summary>
    /// Formats an audit-grade markdown compaction banner suitable for transcripts.
    /// </summary>
    public string ToMarkdownBanner()
    {
        var origFormatted = OriginalChars.ToString("N0", CultureInfo.InvariantCulture);
        var compFormatted = CompactedChars.ToString("N0", CultureInfo.InvariantCulture);

        if (Target == CompactionTarget.ToolResult)
        {
            return $"> ✂️ **[Compaction Event: Tool Result for `{ItemKey}` pruned: {origFormatted} chars -> {compFormatted} chars ({MethodDisplayName})]**";
        }
        else
        {
            return $"> ✂️ **[Compaction Event: Thinking Trace pruned: {origFormatted} chars -> {compFormatted} chars ({MethodDisplayName})]**";
        }
    }
}

/// <summary>
/// Immutable snapshot of aggregate compaction telemetry metrics.
/// </summary>
public sealed record CompactionTelemetrySnapshot(
    int TotalEvents,
    int TotalOriginalChars,
    int TotalCompactedChars,
    int TotalPrunedChars,
    int EstimatedPrunedTokens,
    int SpeculativeHits,
    double SpeculativeHitRatePercentage,
    int ThinkingTracesCollapsed,
    int ToolResultsTruncated,
    int ToolResultsSummarized,
    int ToolResultsCompacted
);

/// <summary>
/// Thread-safe collector and publisher of conversation history compaction telemetry.
/// </summary>
public sealed class CompactionTelemetry
{
    private readonly ConcurrentBag<CompactionEvent> _events = [];

    /// <summary>
    /// Event fired whenever a new compaction event is recorded.
    /// </summary>
    public event Action<CompactionEvent>? EventRecorded;

    /// <summary>
    /// Collection of all compaction events recorded.
    /// </summary>
    public IReadOnlyCollection<CompactionEvent> Events => _events;

    /// <summary>
    /// Records a compaction event and notifies subscribers.
    /// </summary>
    public void Record(CompactionEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _events.Add(@event);
        EventRecorded?.Invoke(@event);
    }

    /// <summary>
    /// Total count of compaction events.
    /// </summary>
    public int TotalEvents => _events.Count;

    /// <summary>
    /// Total characters before compaction across all recorded events.
    /// </summary>
    public int TotalOriginalChars => _events.Sum(e => e.OriginalChars);

    /// <summary>
    /// Total characters after compaction across all recorded events.
    /// </summary>
    public int TotalCompactedChars => _events.Sum(e => e.CompactedChars);

    /// <summary>
    /// Total characters pruned across all recorded events.
    /// </summary>
    public int TotalPrunedChars => Math.Max(0, TotalOriginalChars - TotalCompactedChars);

    /// <summary>
    /// Estimated prompt tokens saved using standard ~4 characters per token heuristic.
    /// </summary>
    public int EstimatedPrunedTokens => (int)Math.Round(TotalPrunedChars / 4.0);

    /// <summary>
    /// Total count of speculative summary hits.
    /// </summary>
    public int SpeculativeHits => _events.Count(e => e.WasSpeculativeHit);

    /// <summary>
    /// Speculative cache hit rate percentage (hits / total events).
    /// </summary>
    public double SpeculativeHitRatePercentage => _events.Count > 0
        ? Math.Round((double)SpeculativeHits * 100.0 / _events.Count, 1)
        : 0.0;

    /// <summary>
    /// Count of assistant reasoning / thinking traces collapsed.
    /// </summary>
    public int ThinkingTracesCollapsed => _events.Count(e => e.Target == CompactionTarget.ThinkingMonologue);

    /// <summary>
    /// Count of tool results compacted using prefix-suffix fallback truncation.
    /// </summary>
    public int ToolResultsTruncated => _events.Count(e => e.Target == CompactionTarget.ToolResult && e.Method == CompactionMethods.PrefixSuffixTruncation);

    /// <summary>
    /// Count of tool results compacted using semantic SLM summaries.
    /// </summary>
    public int ToolResultsSummarized => _events.Count(e => e.Target == CompactionTarget.ToolResult && e.Method == CompactionMethods.SemanticSummary);

    /// <summary>
    /// Total count of tool result compaction events.
    /// </summary>
    public int ToolResultsCompacted => _events.Count(e => e.Target == CompactionTarget.ToolResult);

    /// <summary>
    /// Creates an immutable summary snapshot of current compaction telemetry, optionally filtered by session ID.
    /// </summary>
    public CompactionTelemetrySnapshot CreateSnapshot(string? sessionId = null)
    {
        var relevant = string.IsNullOrWhiteSpace(sessionId)
            ? _events.ToList()
            : _events.Where(e => string.Equals(e.SessionId, sessionId, StringComparison.OrdinalIgnoreCase)).ToList();

        var totalEvents = relevant.Count;
        var totalOrig = relevant.Sum(e => e.OriginalChars);
        var totalComp = relevant.Sum(e => e.CompactedChars);
        var totalPruned = Math.Max(0, totalOrig - totalComp);
        var estTokens = (int)Math.Round(totalPruned / 4.0);
        var specHits = relevant.Count(e => e.WasSpeculativeHit);
        var hitRate = totalEvents > 0 ? Math.Round((double)specHits * 100.0 / totalEvents, 1) : 0.0;
        var thinking = relevant.Count(e => e.Target == CompactionTarget.ThinkingMonologue);
        var toolTrunc = relevant.Count(e => e.Target == CompactionTarget.ToolResult && e.Method == CompactionMethods.PrefixSuffixTruncation);
        var toolSumm = relevant.Count(e => e.Target == CompactionTarget.ToolResult && e.Method == CompactionMethods.SemanticSummary);
        var toolComp = relevant.Count(e => e.Target == CompactionTarget.ToolResult);

        return new CompactionTelemetrySnapshot(
            TotalEvents: totalEvents,
            TotalOriginalChars: totalOrig,
            TotalCompactedChars: totalComp,
            TotalPrunedChars: totalPruned,
            EstimatedPrunedTokens: estTokens,
            SpeculativeHits: specHits,
            SpeculativeHitRatePercentage: hitRate,
            ThinkingTracesCollapsed: thinking,
            ToolResultsTruncated: toolTrunc,
            ToolResultsSummarized: toolSumm,
            ToolResultsCompacted: toolComp
        );
    }

    /// <summary>
    /// Clears all recorded compaction events.
    /// </summary>
    public void Clear() => _events.Clear();
}
