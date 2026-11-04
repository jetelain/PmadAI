namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Represents an individual risk finding detected during a diff security scan.
/// </summary>
/// <param name="RuleId">Identifier of the rule that produced this finding (e.g. NET-IP-001, BD-AUTH-001).</param>
/// <param name="Title">Short human-readable title describing the finding.</param>
/// <param name="Category">Risk category classification.</param>
/// <param name="Severity">Severity level assigned to this finding.</param>
/// <param name="FilePath">Relative path of the affected file in the diff, if applicable.</param>
/// <param name="LineNumber">Target line number in the new file, if applicable.</param>
/// <param name="Column">Column index within the line, if applicable.</param>
/// <param name="Description">Detailed description of the detected risk.</param>
/// <param name="Snippet">Code snippet or matched token that triggered the rule.</param>
public sealed record DiffRiskFinding(
    string RuleId,
    string Title,
    DiffRiskCategory Category,
    DiffRiskSeverity Severity,
    string? FilePath,
    int? LineNumber,
    int? Column,
    string Description,
    string? Snippet = null);

