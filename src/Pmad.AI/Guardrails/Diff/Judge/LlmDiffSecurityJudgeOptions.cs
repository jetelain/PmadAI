namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Configuration options for <see cref="LlmDiffSecurityJudge"/>.
/// </summary>
public sealed class LlmDiffSecurityJudgeOptions
{
    /// <summary>
    /// Maximum bytes per diff chunk passed to the LLM judge. Default: 120,000 (~30,000 tokens).
    /// </summary>
    public int MaxChunkBytes { get; set; } = 120_000;

    /// <summary>
    /// Whether to compress large contiguous deletions to conserve prompt tokens. Default: false.
    /// </summary>
    public bool FilterDeletedLines { get; set; } = false;

    /// <summary>
    /// Maximum consecutive deleted lines retained before compression triggers when <see cref="FilterDeletedLines"/> is true. Default: 3.
    /// </summary>
    public int MaxConsecutiveDeletedLines { get; set; } = 3;

    /// <summary>
    /// Sampling temperature for the LLM judge request. Default: 0.0f (deterministic).
    /// </summary>
    public float Temperature { get; set; } = 0.0f;

    /// <summary>
    /// Optional model identifier override for the underlying chat client.
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// Whether to instruct the LLM judge to detect prompt injection attempts in diff comments/strings. Default: false.
    /// </summary>
    public bool IncludePromptInjectionCheck { get; set; } = false;

    /// <summary>
    /// Whether to return a high-severity finding if evaluation fails (e.g. timeout, malformed JSON, network error). Default: true.
    /// </summary>
    public bool FailClosedOnError { get; set; } = true;

    /// <summary>
    /// Optional custom system prompt overriding the default judge instructions.
    /// </summary>
    public string? CustomSystemPrompt { get; set; }
}
