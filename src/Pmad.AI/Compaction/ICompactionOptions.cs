using Microsoft.Extensions.AI;

namespace Pmad.AI.Compaction;

/// <summary>
/// Common configuration options for conversation history compactors.
/// </summary>
public interface ICompactionOptions
{
    /// <summary>Number of recent turns to retain verbatim.</summary>
    int KeepVerbatimCount { get; set; }

    /// <summary>
    /// Maximum character limit before deterministic fallback truncation or compaction occurs.
    /// Items shorter than or equal to this limit are retained verbatim to preserve KV-cache reuse.
    /// Must be &gt;= FallbackPrefixLength + FallbackSuffixLength.
    /// </summary>
    int FallbackCharacterLimit { get; set; }

    /// <summary>Number of initial characters preserved during fallback truncation.</summary>
    int FallbackPrefixLength { get; set; }

    /// <summary>Number of trailing characters preserved during fallback truncation (e.g. error summaries or conclusions).</summary>
    int FallbackSuffixLength { get; set; }

    /// <summary>Format string for the truncation marker inserted between prefix and suffix.</summary>
    string FallbackTruncationMarker { get; set; }

    /// <summary>
    /// When true, modifies the passed ChatMessage list and instances in-place (zero-allocations).
    /// When false (default), returns a shallow copy with modified messages, ensuring caller's
    /// conversation history remains pristine and immutable.
    /// </summary>
    bool MutateInPlace { get; set; }

    /// <summary>Resolves the session id used to scope summaries (default: ChatOptions.ConversationId, then "default").</summary>
    Func<ChatOptions?, string>? SessionIdResolver { get; set; }

    /// <summary>Optional compaction telemetry instance to collect compaction events.</summary>
    CompactionTelemetry? Telemetry { get; set; }

    /// <summary>Optional callback invoked whenever a compaction event occurs.</summary>
    Action<CompactionEvent>? OnCompaction { get; set; }
}

/// <summary>
/// Common base class implementing <see cref="ICompactionOptions"/>.
/// </summary>
public abstract class CompactionOptionsBase : ICompactionOptions
{
    /// <summary>Number of recent turns to retain verbatim. Default: 5.</summary>
    public int KeepVerbatimCount { get; set; } = 5;

    /// <summary>
    /// Maximum character limit before deterministic fallback truncation occurs. Default: 1,500.
    /// Must be &gt;= FallbackPrefixLength + FallbackSuffixLength.
    /// </summary>
    public int FallbackCharacterLimit { get; set; } = 1500;

    /// <summary>Number of initial characters preserved during fallback truncation. Default: 1,000.</summary>
    public int FallbackPrefixLength { get; set; } = 1000;

    /// <summary>Number of trailing characters preserved during fallback truncation. Default: 400.</summary>
    public int FallbackSuffixLength { get; set; } = 400;

    /// <summary>Format string for the truncation marker inserted between prefix and suffix. Default: "\n... [Truncated {0} characters] ...\n"</summary>
    public string FallbackTruncationMarker { get; set; } = "\n... [Truncated {0} characters] ...\n";

    /// <summary>
    /// When true, modifies the passed ChatMessage list and instances in-place (zero-allocations).
    /// When false (default), returns a shallow copy with modified messages, ensuring caller's
    /// conversation history remains pristine and immutable.
    /// </summary>
    public bool MutateInPlace { get; set; } = false;

    /// <summary>Resolves the session id used to scope summaries (default: ChatOptions.ConversationId, then "default").</summary>
    public Func<ChatOptions?, string>? SessionIdResolver { get; set; }

    /// <summary>Optional compaction telemetry instance to collect compaction events.</summary>
    public CompactionTelemetry? Telemetry { get; set; }

    /// <summary>Optional callback invoked whenever a compaction event occurs.</summary>
    public Action<CompactionEvent>? OnCompaction { get; set; }
}
