using System.Text.Json.Serialization;

namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Root response schema returned by the LLM security judge.
/// </summary>
/// <param name="Status">Evaluation status (e.g. "OK", "SECURITY_ISSUE", "PROMPT_INJECTION").</param>
/// <param name="Summary">High-level summary of the analysis.</param>
/// <param name="Findings">List of security findings detected by the judge.</param>
public sealed record JudgeScanResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("summary")] string? Summary = null,
    [property: JsonPropertyName("findings")] IReadOnlyList<JudgeFindingDto>? Findings = null);

/// <summary>
/// Individual finding DTO emitted by the LLM security judge.
/// </summary>
/// <param name="Severity">Risk severity (e.g. "Low", "Medium", "High", "Critical").</param>
/// <param name="Category">Risk category classification (e.g. "BackdoorPattern", "PromptInjection").</param>
/// <param name="FilePath">Relative path of the affected file.</param>
/// <param name="LineNumber">Target line number in the diff/file.</param>
/// <param name="Title">Short title describing the finding.</param>
/// <param name="Description">Detailed description of the detected risk.</param>
/// <param name="Snippet">Relevant code snippet exhibiting the vulnerability.</param>
public sealed record JudgeFindingDto(
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("file")] string? FilePath,
    [property: JsonPropertyName("line"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] int? LineNumber,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("snippet")] string? Snippet = null);

