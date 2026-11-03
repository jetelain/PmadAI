using System.Text;

namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Represents a single diff hunk within a file diff.
/// </summary>
public sealed class DiffHunk
{
    /// <summary>
    /// The hunk header line (e.g. @@ -1,5 +1,6 @@).
    /// </summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>
    /// Raw lines contained within the hunk, including diff marker prefixes (+, -, space, \).
    /// </summary>
    public List<string> Lines { get; set; } = [];

    /// <summary>
    /// Added lines within this hunk.
    /// </summary>
    public List<DiffAddedLine> AddedLines { get; set; } = [];

    /// <summary>
    /// Deleted lines within this hunk.
    /// </summary>
    public List<DiffDeletedLine> DeletedLines { get; set; } = [];

    /// <summary>
    /// Starting line in the original file.
    /// </summary>
    public int OldStartingLine { get; set; }

    /// <summary>
    /// Number of lines in the original file covered by this hunk.
    /// </summary>
    public int OldLineCount { get; set; }

    /// <summary>
    /// Starting line in the new file.
    /// </summary>
    public int NewStartingLine { get; set; }

    /// <summary>
    /// Number of lines in the new file covered by this hunk.
    /// </summary>
    public int NewLineCount { get; set; }

    /// <summary>
    /// Computes the UTF-8 byte count of the hunk including trailing newlines.
    /// </summary>
    public int GetByteCount(Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;
        int bytes = encoding.GetByteCount(Header) + 1;
        foreach (var line in Lines)
        {
            bytes += encoding.GetByteCount(line) + 1;
        }
        return bytes;
    }

    /// <summary>
    /// Reconstructs the raw diff text representation of this hunk.
    /// </summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');
        foreach (var line in Lines)
        {
            sb.Append(line).Append('\n');
        }
        return sb.ToString();
    }
}

