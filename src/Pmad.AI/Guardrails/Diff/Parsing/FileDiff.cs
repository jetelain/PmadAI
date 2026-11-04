using System.Text;

namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Represents an added or modified line in a diff hunk.
/// </summary>
/// <param name="FilePath">Relative path to the target file.</param>
/// <param name="LineNumber">1-based line number in the target (new) file.</param>
/// <param name="Content">The text of the line without the leading '+' diff marker.</param>
public sealed record DiffAddedLine(
    string FilePath,
    int LineNumber,
    string Content);

/// <summary>
/// Represents a deleted line in a diff hunk.
/// </summary>
/// <param name="FilePath">Relative path to the source file.</param>
/// <param name="LineNumber">1-based line number in the source (old) file.</param>
/// <param name="Content">The text of the line without the leading '-' diff marker.</param>
public sealed record DiffDeletedLine(
    string FilePath,
    int LineNumber,
    string Content);

/// <summary>
/// Represents changes to a single file within a unified git diff.
/// </summary>
public sealed class FileDiff
{
    /// <summary>
    /// Target file path in the repository (normalized to forward slashes).
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Previous file path if the file was renamed or copied.
    /// </summary>
    public string? OldFilePath { get; set; }

    /// <summary>
    /// New file mode (e.g. "100755", "160000").
    /// </summary>
    public string? NewFileMode { get; set; }

    /// <summary>
    /// Old file mode (e.g. "100644").
    /// </summary>
    public string? OldFileMode { get; set; }

    /// <summary>
    /// True if the file is newly added in this diff.
    /// </summary>
    public bool IsNewFile { get; set; }

    /// <summary>
    /// True if the file is deleted in this diff.
    /// </summary>
    public bool IsDeletedFile { get; set; }

    /// <summary>
    /// True if the file is renamed or copied in this diff.
    /// </summary>
    public bool IsRenamedFile { get; set; }

    /// <summary>
    /// True if git binary markers were present (e.g. Binary files ... differ or GIT binary patch).
    /// </summary>
    public bool IsBinary { get; set; }

    /// <summary>
    /// File-level header lines prior to the first hunk.
    /// </summary>
    public List<string> HeaderLines { get; set; } = [];

    /// <summary>
    /// Hunks contained within this file diff.
    /// </summary>
    public List<DiffHunk> Hunks { get; set; } = [];

    /// <summary>
    /// Aggregated added lines across all hunks.
    /// </summary>
    public List<DiffAddedLine> AddedLines { get; set; } = [];

    /// <summary>
    /// Aggregated deleted lines across all hunks.
    /// </summary>
    public List<DiffDeletedLine> DeletedLines { get; set; } = [];

    /// <summary>
    /// Computes the UTF-8 byte count of the file diff (headers plus hunks).
    /// </summary>
    public int GetByteCount(Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;
        int bytes = 0;
        foreach (var hl in HeaderLines)
        {
            bytes += encoding.GetByteCount(hl) + 1;
        }
        foreach (var hunk in Hunks)
        {
            bytes += hunk.GetByteCount(encoding);
        }
        return bytes;
    }

    /// <summary>
    /// Reconstructs the raw unified diff text representation of this file.
    /// </summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        foreach (var hl in HeaderLines)
        {
            sb.Append(hl).Append('\n');
        }
        foreach (var hunk in Hunks)
        {
            sb.Append(hunk.ToText());
        }
        return sb.ToString();
    }
}

