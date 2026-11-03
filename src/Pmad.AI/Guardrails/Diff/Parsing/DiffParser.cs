using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Parser for unified git diffs supporting additions, deletions, renames, mode changes, binary markers, and glob exclusions.
/// </summary>
public static partial class DiffParser
{
    [GeneratedRegex(@"^@@\s+-(\d+)(?:,(\d+))?\s+\+(\d+)(?:,(\d+))?\s+@@")]
    private static partial Regex HunkHeaderRegex();

    /// <summary>
    /// Parses a unified git diff into structured <see cref="FileDiff"/> instances.
    /// </summary>
    /// <param name="diffText">The raw unified git diff text.</param>
    /// <param name="options">Options specifying glob exclusions and settings.</param>
    /// <returns>A read-only list of parsed <see cref="FileDiff"/> instances.</returns>
    public static IReadOnlyList<FileDiff> Parse(string diffText, DiffRiskScannerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Parse(diffText, options.ExcludedFileGlobs);
    }

    /// <summary>
    /// Parses a unified git diff into structured <see cref="FileDiff"/> instances, optionally excluding files matching globs.
    /// </summary>
    /// <param name="diffText">The raw unified git diff text.</param>
    /// <param name="excludedGlobs">Optional file glob patterns to exclude from the result.</param>
    /// <returns>A read-only list of parsed <see cref="FileDiff"/> instances.</returns>
    public static IReadOnlyList<FileDiff> Parse(string diffText, IEnumerable<string>? excludedGlobs = null)
    {
        var files = new List<FileDiff>();
        if (string.IsNullOrWhiteSpace(diffText))
        {
            return files;
        }

        var globsList = excludedGlobs as IReadOnlyList<string> ?? excludedGlobs?.ToList();
        var normalizedDiff = diffText.Replace("\r\n", "\n");
        if (normalizedDiff.EndsWith('\n'))
        {
            normalizedDiff = normalizedDiff[..^1];
        }
        var lines = normalizedDiff.Split('\n');

        FileDiff? currentFile = null;
        DiffHunk? currentHunk = null;
        int currentOldLine = 0;
        int currentNewLine = 0;

        void FinalizeHunk()
        {
            if (currentHunk != null && currentFile != null)
            {
                currentFile.Hunks.Add(currentHunk);
                currentHunk = null;
            }
        }

        void FinalizeFile()
        {
            FinalizeHunk();
            if (currentFile != null)
            {
                if (!IsExcluded(currentFile, globsList))
                {
                    files.Add(currentFile);
                }
                currentFile = null;
            }
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (TryParseDiffGitLine(line, out var oldPath, out var newPath))
            {
                FinalizeFile();
                currentFile = new FileDiff
                {
                    FilePath = newPath,
                    OldFilePath = oldPath != newPath ? oldPath : null
                };
                currentFile.HeaderLines.Add(line);
                continue;
            }

            // Detect plain unified diff file header (without 'diff --git')
            if (line.StartsWith("--- ", StringComparison.Ordinal) &&
                i + 1 < lines.Length &&
                lines[i + 1].StartsWith("+++ ", StringComparison.Ordinal) &&
                (currentFile == null || (currentHunk != null && i + 2 < lines.Length && HunkHeaderRegex().IsMatch(lines[i + 2]))))
            {
                FinalizeFile();
                currentFile = new FileDiff { FilePath = string.Empty };
                currentFile.HeaderLines.Add(line);
                ProcessHeaderLine(line, currentFile);
                continue;
            }

            var hunkMatch = HunkHeaderRegex().Match(line);
            if (hunkMatch.Success)
            {
                if (currentFile == null)
                {
                    currentFile = new FileDiff { FilePath = "diff_chunk" };
                }
                else if (string.IsNullOrEmpty(currentFile.FilePath))
                {
                    currentFile.FilePath = !string.IsNullOrEmpty(currentFile.OldFilePath)
                        ? currentFile.OldFilePath
                        : "diff_chunk";
                }

                FinalizeHunk();

                int oldStart = int.Parse(hunkMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                int oldCount = hunkMatch.Groups[2].Success
                    ? int.Parse(hunkMatch.Groups[2].Value, CultureInfo.InvariantCulture)
                    : 1;

                int newStart = int.Parse(hunkMatch.Groups[3].Value, CultureInfo.InvariantCulture);
                int newCount = hunkMatch.Groups[4].Success
                    ? int.Parse(hunkMatch.Groups[4].Value, CultureInfo.InvariantCulture)
                    : 1;

                currentOldLine = oldCount == 0 ? 0 : oldStart;
                currentNewLine = newCount == 0 ? 0 : newStart;

                currentHunk = new DiffHunk
                {
                    Header = line,
                    OldStartingLine = oldStart,
                    OldLineCount = oldCount,
                    NewStartingLine = newStart,
                    NewLineCount = newCount
                };
                continue;
            }

            if (currentHunk != null)
            {
                if (line.StartsWith('+'))
                {
                    var content = line.Length > 1 ? line[1..] : string.Empty;
                    var added = new DiffAddedLine(currentFile!.FilePath, currentNewLine, content);
                    currentHunk.Lines.Add(line);
                    currentHunk.AddedLines.Add(added);
                    currentFile.AddedLines.Add(added);
                    currentNewLine++;
                }
                else if (line.StartsWith('-'))
                {
                    var content = line.Length > 1 ? line[1..] : string.Empty;
                    var deleted = new DiffDeletedLine(currentFile!.FilePath, currentOldLine, content);
                    currentHunk.Lines.Add(line);
                    currentHunk.DeletedLines.Add(deleted);
                    currentFile.DeletedLines.Add(deleted);
                    currentOldLine++;
                }
                else if (line.StartsWith(' '))
                {
                    currentHunk.Lines.Add(line);
                    currentOldLine++;
                    currentNewLine++;
                }
                else if (line.StartsWith('\\'))
                {
                    currentHunk.Lines.Add(line);
                }
                else
                {
                    // Non-standard trailing line inside hunk
                    currentHunk.Lines.Add(line);
                }
            }
            else if (currentFile != null)
            {
                currentFile.HeaderLines.Add(line);
                ProcessHeaderLine(line, currentFile);
            }
            else
            {
                // Diff content before any 'diff --git' header
                if (line.StartsWith("--- ", StringComparison.Ordinal) || line.StartsWith("+++ ", StringComparison.Ordinal))
                {
                    currentFile = new FileDiff { FilePath = string.Empty };
                    currentFile.HeaderLines.Add(line);
                    ProcessHeaderLine(line, currentFile);
                }
            }
        }

        FinalizeFile();

        // Fallback for raw text input without diff markers
        if (files.Count == 0 && lines.Length > 0 && lines.Any(l => !string.IsNullOrWhiteSpace(l)))
        {
            var fallbackFile = new FileDiff { FilePath = "<input>", IsNewFile = true };
            var fallbackHunk = new DiffHunk { Header = "@@ -0,0 +1," + lines.Length + " @@", NewStartingLine = 1, NewLineCount = lines.Length };

            for (int i = 0; i < lines.Length; i++)
            {
                fallbackHunk.Lines.Add("+" + lines[i]);
                var added = new DiffAddedLine("<input>", i + 1, lines[i]);
                fallbackHunk.AddedLines.Add(added);
                fallbackFile.AddedLines.Add(added);
            }

            fallbackFile.Hunks.Add(fallbackHunk);
            if (!IsExcluded(fallbackFile, globsList))
            {
                files.Add(fallbackFile);
            }
        }

        return files;
    }

    /// <summary>
    /// Convenience helper extracting all added lines from a git diff or raw source code input.
    /// </summary>
    /// <param name="text">The raw unified diff or code text.</param>
    /// <param name="excludedGlobs">Optional file glob patterns to exclude.</param>
    /// <returns>A read-only list of all added lines with target line numbers.</returns>
    public static IReadOnlyList<DiffAddedLine> ParseAddedLines(string text, IEnumerable<string>? excludedGlobs = null)
    {
        var files = Parse(text, excludedGlobs);
        var result = new List<DiffAddedLine>();
        foreach (var file in files)
        {
            result.AddRange(file.AddedLines);
        }
        return result;
    }

    private static void ProcessHeaderLine(string line, FileDiff currentFile)
    {
        if (line.StartsWith("old mode ", StringComparison.Ordinal))
        {
            currentFile.OldFileMode = line["old mode ".Length..].Trim();
        }
        else if (line.StartsWith("new mode ", StringComparison.Ordinal))
        {
            currentFile.NewFileMode = line["new mode ".Length..].Trim();
        }
        else if (line.StartsWith("deleted file mode ", StringComparison.Ordinal))
        {
            currentFile.IsDeletedFile = true;
            currentFile.OldFileMode = line["deleted file mode ".Length..].Trim();
        }
        else if (line.StartsWith("new file mode ", StringComparison.Ordinal))
        {
            currentFile.IsNewFile = true;
            currentFile.NewFileMode = line["new file mode ".Length..].Trim();
        }
        else if (line.StartsWith("rename from ", StringComparison.Ordinal))
        {
            currentFile.IsRenamedFile = true;
            currentFile.OldFilePath = NormalizePath(line["rename from ".Length..]);
        }
        else if (line.StartsWith("rename to ", StringComparison.Ordinal))
        {
            currentFile.IsRenamedFile = true;
            currentFile.FilePath = NormalizePath(line["rename to ".Length..]);
        }
        else if (line.StartsWith("copy from ", StringComparison.Ordinal))
        {
            currentFile.OldFilePath = NormalizePath(line["copy from ".Length..]);
        }
        else if (line.StartsWith("copy to ", StringComparison.Ordinal))
        {
            currentFile.FilePath = NormalizePath(line["copy to ".Length..]);
        }
        else if (line.StartsWith("--- ", StringComparison.Ordinal))
        {
            var raw = StripDiffHeaderTimestamp(line["--- ".Length..].Trim());
            if (raw == "/dev/null")
            {
                currentFile.IsNewFile = true;
            }
            else
            {
                var norm = NormalizePrefixedPath(raw, "a/");
                if (string.IsNullOrEmpty(currentFile.OldFilePath) && norm != currentFile.FilePath)
                {
                    currentFile.OldFilePath = norm;
                }
            }
        }
        else if (line.StartsWith("+++ ", StringComparison.Ordinal))
        {
            var raw = StripDiffHeaderTimestamp(line["+++ ".Length..].Trim());
            if (raw == "/dev/null")
            {
                currentFile.IsDeletedFile = true;
                if (string.IsNullOrEmpty(currentFile.FilePath) && !string.IsNullOrEmpty(currentFile.OldFilePath))
                {
                    currentFile.FilePath = currentFile.OldFilePath;
                    currentFile.OldFilePath = null;
                }
            }
            else
            {
                currentFile.FilePath = NormalizePrefixedPath(raw, "b/");
                if (!currentFile.IsRenamedFile && string.Equals(currentFile.OldFilePath, currentFile.FilePath, StringComparison.Ordinal))
                {
                    currentFile.OldFilePath = null;
                }
            }
        }
        else if (line.StartsWith("Binary files ", StringComparison.Ordinal) && line.EndsWith(" differ", StringComparison.Ordinal))
        {
            currentFile.IsBinary = true;
            if (line.Contains("/dev/null and ", StringComparison.Ordinal))
            {
                currentFile.IsNewFile = true;
            }
            if (line.Contains(" and /dev/null differ", StringComparison.Ordinal))
            {
                currentFile.IsDeletedFile = true;
            }
        }
        else if (line.StartsWith("GIT binary patch", StringComparison.Ordinal))
        {
            currentFile.IsBinary = true;
        }
    }

    private static bool TryParseDiffGitLine(string line, out string oldPath, out string newPath)
    {
        oldPath = string.Empty;
        newPath = string.Empty;

        if (!line.StartsWith("diff --git ", StringComparison.Ordinal))
        {
            return false;
        }

        var span = line.AsSpan("diff --git ".Length).Trim();
        if (span.IsEmpty)
        {
            return false;
        }

        if (span[0] == '"')
        {
            if (!TryReadQuotedString(span, out oldPath, out var remaining))
            {
                return false;
            }

            span = remaining.TrimStart();
            if (span.Length > 0 && span[0] == '"')
            {
                if (!TryReadQuotedString(span, out newPath, out _))
                {
                    return false;
                }
            }
            else
            {
                newPath = span.ToString();
            }
        }
        else
        {
            int split = span.IndexOf(" b/", StringComparison.Ordinal);
            if (split >= 0)
            {
                oldPath = span[..split].ToString();
                newPath = span[(split + 1)..].ToString();
            }
            else
            {
                int spaceIdx = span.IndexOf(' ');
                if (spaceIdx >= 0)
                {
                    oldPath = span[..spaceIdx].ToString();
                    newPath = span[(spaceIdx + 1)..].TrimStart().ToString();
                }
                else
                {
                    oldPath = span.ToString();
                    newPath = oldPath;
                }
            }
        }

        oldPath = NormalizePrefixedPath(oldPath, "a/");
        newPath = NormalizePrefixedPath(newPath, "b/");
        return true;
    }

    private static bool TryReadQuotedString(ReadOnlySpan<char> span, out string unescaped, out ReadOnlySpan<char> remaining)
    {
        unescaped = string.Empty;
        remaining = default;

        if (span.IsEmpty || span[0] != '"')
        {
            return false;
        }

        var sb = new StringBuilder(span.Length);
        for (int i = 1; i < span.Length; i++)
        {
            char c = span[i];
            if (c == '\\' && i + 1 < span.Length)
            {
                char next = span[++i];
                sb.Append(next switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '"' => '"',
                    '\\' => '\\',
                    _ => next
                });
            }
            else if (c == '"')
            {
                unescaped = sb.ToString();
                remaining = span[(i + 1)..];
                return true;
            }
            else
            {
                sb.Append(c);
            }
        }

        return false;
    }

    private static string StripDiffHeaderTimestamp(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        if (raw[0] == '"' && TryReadQuotedString(raw.AsSpan(), out var unquoted, out _))
        {
            return unquoted;
        }

        int tabIdx = raw.IndexOf('\t');
        return tabIdx >= 0 ? raw[..tabIdx].Trim() : raw;
    }

    private static string NormalizePrefixedPath(string raw, string prefix)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = UnquoteAndNormalizeSlashes(raw);
        if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[prefix.Length..];
        }
        if (trimmed.StartsWith("a/", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("b/", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[2..];
        }
        return trimmed;
    }

    private static string NormalizePath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return UnquoteAndNormalizeSlashes(raw);
    }

    private static string UnquoteAndNormalizeSlashes(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && TryReadQuotedString(trimmed.AsSpan(), out var unquoted, out _))
        {
            trimmed = unquoted;
        }
        else
        {
            trimmed = trimmed.Trim('\"');
        }
        return trimmed.Replace('\\', '/');
    }

    private static bool IsExcluded(FileDiff file, IReadOnlyList<string>? globs)
    {
        if (globs == null || globs.Count == 0) return false;

        foreach (var glob in globs)
        {
            if (!string.IsNullOrEmpty(file.FilePath) && GlobMatcher.IsMatch(file.FilePath, glob))
            {
                return true;
            }
            if (!string.IsNullOrEmpty(file.OldFilePath) && GlobMatcher.IsMatch(file.OldFilePath, glob))
            {
                return true;
            }
        }

        return false;
    }
}

