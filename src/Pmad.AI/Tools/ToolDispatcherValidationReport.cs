using Microsoft.Extensions.AI;

namespace Pmad.AI.Tools;

/// <summary>
/// Severity of a tool dispatcher schema validation issue.
/// </summary>
public enum ToolValidationSeverity
{
    Warning,
    Error
}

/// <summary>
/// Diagnostic issue detected during schema validation.
/// </summary>
public record ToolValidationIssue(
    ToolValidationSeverity Severity,
    string ToolName,
    string? ParameterName,
    string Message);

/// <summary>
/// Exception thrown when tool dispatcher schema validation fails.
/// </summary>
public sealed class ToolDispatcherValidationException(string message) : Exception(message);

/// <summary>
/// Report produced by validating a <see cref="LenientToolDispatcher"/> against registered <see cref="AIFunction"/> instances.
/// </summary>
public class ToolDispatcherValidationReport
{
    /// <summary>Whether the configuration contains zero errors (warnings permitted).</summary>
    public bool IsValid => Issues.All(i => i.Severity != ToolValidationSeverity.Error);

    /// <summary>List of validation issues.</summary>
    public IReadOnlyList<ToolValidationIssue> Issues { get; init; } = Array.Empty<ToolValidationIssue>();

    /// <summary>Throws <see cref="ToolDispatcherValidationException"/> if any error issues exist.</summary>
    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            var errors = string.Join("\n", Issues.Where(i => i.Severity == ToolValidationSeverity.Error).Select(i => $"  - {i.Message}"));
            throw new ToolDispatcherValidationException($"LenientToolDispatcher validation failed:\n{errors}");
        }
    }
}
