namespace Pmad.AI.Compaction;

/// <summary>
/// Target category of text submitted for speculative background summarization.
/// </summary>
public enum SummarizationTarget
{
    /// <summary>Bulky output produced by tool or function execution.</summary>
    ToolResult,

    /// <summary>Historical chain-of-thought or reasoning monologue produced by an assistant model.</summary>
    ThinkingMonologue
}

/// <summary>
/// Contract for speculative, asynchronous background summarization of conversation history items.
/// Allows idle CPU threads or local SLMs to precompute semantic summaries before compaction occurs.
/// </summary>
public interface ISpeculativeSummarizer : IAsyncDisposable
{
    /// <summary>
    /// Enqueues a raw text block for background summarization if not already computed.
    /// </summary>
    void EnqueueSummarization(string sessionId, string itemKey, SummarizationTarget target, string rawText);

    /// <summary>
    /// Non-blocking lookup for a precomputed summary. Returns true if ready, false on cache miss or while in progress.
    /// </summary>
    bool TryGetSummary(string sessionId, string itemKey, out string? summary);

    /// <summary>
    /// Evicts all cached summaries associated with the given session ID.
    /// </summary>
    void EvictSession(string sessionId);
}

/// <summary>
/// No-op implementation of <see cref="ISpeculativeSummarizer"/> that always returns false on lookup.
/// </summary>
public sealed class NullSpeculativeSummarizer : ISpeculativeSummarizer
{
    /// <summary>Singleton instance of <see cref="NullSpeculativeSummarizer"/>.</summary>
    public static readonly NullSpeculativeSummarizer Instance = new();

    private NullSpeculativeSummarizer() { }

    /// <inheritdoc/>
    public void EnqueueSummarization(string sessionId, string itemKey, SummarizationTarget target, string rawText) { }

    /// <inheritdoc/>
    public bool TryGetSummary(string sessionId, string itemKey, out string? summary)
    {
        summary = null;
        return false;
    }

    /// <inheritdoc/>
    public void EvictSession(string sessionId) { }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
