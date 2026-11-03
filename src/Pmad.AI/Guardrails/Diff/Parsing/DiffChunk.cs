namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Represents a bounded chunk of unified git diff text intended for processing or LLM evaluation.
/// </summary>
public sealed class DiffChunk
{
    /// <summary>
    /// 1-based index of this chunk.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Total count of chunks in the partition.
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// File paths included in this chunk.
    /// </summary>
    public IReadOnlyList<string> FilePaths { get; set; } = [];

    /// <summary>
    /// Reconstructed unified diff text for this chunk.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Size of Content in UTF-8 bytes.
    /// </summary>
    public int ByteSize { get; set; }

    /// <summary>
    /// Estimated token count (ByteSize / 4).
    /// </summary>
    public int EstimatedTokens => (int)Math.Ceiling(ByteSize / 4.0);
}

