namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Extensible validator contract for validating binary files in git diffs (e.g. images, archives, compiled binaries).
/// </summary>
public interface ISafeBinaryValidator
{
    /// <summary>
    /// Determines whether this validator can validate the specified file path and extension.
    /// </summary>
    /// <param name="filePath">Relative file path in the repository.</param>
    /// <param name="extension">File extension including leading dot (e.g. .png, .jpg).</param>
    /// <returns>True if this validator handles the given file type.</returns>
    bool CanValidate(string filePath, string extension);

    /// <summary>
    /// Validates the binary file content asynchronously.
    /// </summary>
    /// <param name="context">Validation context providing file path, diff information, and access to content streams or headers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result indicating whether the binary is safe, invalid, or unhandled.</returns>
    ValueTask<BinaryValidationResult> ValidateAsync(BinaryValidationContext context, CancellationToken cancellationToken = default);
}

