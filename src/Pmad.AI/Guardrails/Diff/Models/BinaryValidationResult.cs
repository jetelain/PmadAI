namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Result returned by an <see cref="ISafeBinaryValidator"/>.
/// </summary>
public sealed class BinaryValidationResult
{
    /// <summary>
    /// True if the binary was verified as safe.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// True if this validator handled the file. If false, subsequent validators will be queried.
    /// </summary>
    public bool Handled { get; init; }

    /// <summary>
    /// Detailed failure explanation when <see cref="IsValid"/> is false.
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// Severity to assign when validation fails. Default: Critical.
    /// </summary>
    public DiffRiskSeverity Severity { get; init; } = DiffRiskSeverity.Critical;

    /// <summary>
    /// Optional rule ID override to report when validation fails. Defaults to PAY-BIN-002 if null.
    /// </summary>
    public string? RuleId { get; init; }

    /// <summary>
    /// Creates a successful validation result indicating the file is valid and safe.
    /// </summary>
    public static BinaryValidationResult Valid() => new() { IsValid = true, Handled = true };

    /// <summary>
    /// Creates a failure validation result indicating the file is suspicious, corrupted, or dangerous.
    /// </summary>
    public static BinaryValidationResult Invalid(string reason, DiffRiskSeverity severity = DiffRiskSeverity.Critical, string? ruleId = null) =>
        new() { IsValid = false, Handled = true, FailureReason = reason, Severity = severity, RuleId = ruleId };

    /// <summary>
    /// Creates a result indicating the validator did not handle the candidate file.
    /// </summary>
    public static BinaryValidationResult NotHandled() => new() { Handled = false };
}

