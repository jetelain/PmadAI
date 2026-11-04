namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Context passed to <see cref="ISafeBinaryValidator"/> instances during binary diff validation.
/// </summary>
public sealed class BinaryValidationContext
{
    /// <summary>
    /// Target file path in the repository.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// File extension including leading dot (e.g. .png).
    /// </summary>
    public required string Extension { get; init; }

    /// <summary>
    /// Associated <see cref="FileDiff"/> metadata if available.
    /// </summary>
    public FileDiff? FileDiff { get; init; }

    /// <summary>
    /// Delegate providing an opened read stream to the binary file or blob.
    /// </summary>
    public Func<Stream?> StreamOpener { get; init; } = () => null;

    /// <summary>
    /// Optional asynchronous delegate providing an opened read stream to the binary file or Git blob.
    /// </summary>
    public Func<CancellationToken, ValueTask<Stream?>>? AsyncStreamOpener { get; init; }

    /// <summary>
    /// Pre-buffered header bytes (typically first 512 bytes) if resolved.
    /// </summary>
    public ReadOnlyMemory<byte>? HeaderBytes { get; init; }

    /// <summary>
    /// Opens a read stream for the target file synchronously. Returns null if stream access is not configured.
    /// </summary>
    public Stream? OpenRead() => StreamOpener?.Invoke();

    /// <summary>
    /// Opens a read stream for the target file or Git blob asynchronously.
    /// Uses <see cref="AsyncStreamOpener"/> when configured, falling back to <see cref="StreamOpener"/>.
    /// </summary>
    public ValueTask<Stream?> OpenReadAsync(CancellationToken cancellationToken = default)
    {
        if (AsyncStreamOpener != null)
        {
            return AsyncStreamOpener(cancellationToken);
        }

        return ValueTask.FromResult(StreamOpener?.Invoke());
    }
}
