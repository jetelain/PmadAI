using System.Text;

namespace Pmad.AI.Guardrails.Diff;

/// <summary>
/// Semantic chunking and token optimization engine for unified git diffs.
/// </summary>
public static class DiffSplitter
{
    /// <summary>
    /// Filters out long contiguous blocks of deleted lines to save LLM tokens,
    /// retaining boundary lines and a clear suppression marker.
    /// </summary>
    /// <param name="hunk">The source diff hunk to filter.</param>
    /// <param name="maxConsecutiveDeleted">Maximum consecutive deleted lines allowed before compression triggers. Default: 3.</param>
    /// <returns>A new <see cref="DiffHunk"/> with compressed deleted line sequences.</returns>
    public static DiffHunk FilterDeletedLines(DiffHunk hunk, int maxConsecutiveDeleted = 3)
    {
        ArgumentNullException.ThrowIfNull(hunk);

        var result = new DiffHunk
        {
            Header = hunk.Header,
            OldStartingLine = hunk.OldStartingLine,
            OldLineCount = hunk.OldLineCount,
            NewStartingLine = hunk.NewStartingLine,
            NewLineCount = hunk.NewLineCount,
            AddedLines = new List<DiffAddedLine>(hunk.AddedLines),
            DeletedLines = new List<DiffDeletedLine>(hunk.DeletedLines)
        };

        var deletedBlock = new List<string>();

        void FlushDeleted()
        {
            if (deletedBlock.Count == 0) return;

            if (deletedBlock.Count <= maxConsecutiveDeleted)
            {
                result.Lines.AddRange(deletedBlock);
            }
            else
            {
                // Keep the first line of deletion
                result.Lines.Add(deletedBlock[0]);

                int suppressedCount = deletedBlock.Count - 2;
                if (suppressedCount > 0)
                {
                    result.Lines.Add($"- ... [{suppressedCount} lines of deleted code suppressed to reduce tokens] ...");
                    result.Lines.Add(deletedBlock[^1]);
                }
                else
                {
                    result.Lines.AddRange(deletedBlock.Skip(1));
                }
            }

            deletedBlock.Clear();
        }

        foreach (var line in hunk.Lines)
        {
            // Note: '---' is a file header separator, not code deletion
            if (line.StartsWith('-') && !line.StartsWith("---", StringComparison.Ordinal))
            {
                deletedBlock.Add(line);
            }
            else
            {
                FlushDeleted();
                result.Lines.Add(line);
            }
        }

        FlushDeleted();
        return result;
    }

    /// <summary>
    /// Splits raw unified git diff text into bounded chunks respecting the byte threshold.
    /// </summary>
    /// <param name="diffText">The raw unified diff text.</param>
    /// <param name="maxChunkBytes">Maximum byte size per chunk. Default: 120,000.</param>
    /// <param name="filterDeletedLines">Whether to compress long deleted-line blocks. Default: true.</param>
    /// <param name="excludedGlobs">Optional file globs to exclude before chunking.</param>
    /// <returns>A read-only list of <see cref="DiffChunk"/> instances.</returns>
    public static IReadOnlyList<DiffChunk> SplitIntoChunks(
        string diffText,
        int maxChunkBytes = 120_000,
        bool filterDeletedLines = true,
        IEnumerable<string>? excludedGlobs = null)
    {
        var files = DiffParser.Parse(diffText, excludedGlobs);
        return SplitIntoChunks(files, maxChunkBytes, filterDeletedLines);
    }

    /// <summary>
    /// Splits parsed file diffs into isolated chunks respecting the maximum byte threshold.
    /// Files are kept together when possible. Large files are split by hunk.
    /// </summary>
    /// <param name="files">The list of file diffs to partition.</param>
    /// <param name="maxChunkBytes">Maximum byte size per chunk. Default: 120,000.</param>
    /// <param name="filterDeletedLines">Whether to compress long deleted-line blocks. Default: true.</param>
    /// <returns>A read-only list of <see cref="DiffChunk"/> instances.</returns>
    public static IReadOnlyList<DiffChunk> SplitIntoChunks(
        IReadOnlyList<FileDiff> files,
        int maxChunkBytes = 120_000,
        bool filterDeletedLines = true)
    {
        ArgumentNullException.ThrowIfNull(files);

        var chunks = new List<DiffChunk>();
        if (files.Count == 0)
        {
            return chunks;
        }

        var currentBatch = new List<FileDiff>();
        int currentBytes = 0;

        void FlushCurrentBatch()
        {
            if (currentBatch.Count == 0) return;

            var chunkContent = new StringBuilder();
            var chunkFiles = new List<string>();

            foreach (var f in currentBatch)
            {
                if (!string.IsNullOrEmpty(f.FilePath))
                {
                    chunkFiles.Add(f.FilePath);
                }
                chunkContent.Append(f.ToText());
            }

            var chunkText = chunkContent.ToString();
            chunks.Add(new DiffChunk
            {
                Index = chunks.Count + 1,
                FilePaths = chunkFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Content = chunkText,
                ByteSize = Encoding.UTF8.GetByteCount(chunkText)
            });

            currentBatch.Clear();
            currentBytes = 0;
        }

        foreach (var originalFile in files)
        {
            var file = PrepareFileForChunking(originalFile, filterDeletedLines);
            int fileBytes = file.GetByteCount(Encoding.UTF8);

            // Case 1: The file by itself exceeds maxChunkBytes -> Split by hunk
            if (fileBytes > maxChunkBytes)
            {
                FlushCurrentBatch();

                var fileHeaderLines = file.HeaderLines;
                int headerBytes = fileHeaderLines.Sum(h => Encoding.UTF8.GetByteCount(h) + 1);

                var currentHunkGroup = new List<DiffHunk>();
                int currentHunkGroupBytes = headerBytes;

                foreach (var hunk in file.Hunks)
                {
                    int hunkBytes = hunk.GetByteCount(Encoding.UTF8);

                    // If a single hunk itself is larger than maxChunkBytes, split lines within it
                    if (hunkBytes + headerBytes > maxChunkBytes)
                    {
                        if (currentHunkGroup.Count > 0)
                        {
                            var partFile = new FileDiff
                            {
                                FilePath = file.FilePath,
                                HeaderLines = new List<string>(fileHeaderLines),
                                Hunks = new List<DiffHunk>(currentHunkGroup)
                            };
                            currentBatch.Add(partFile);
                            FlushCurrentBatch();
                            currentHunkGroup.Clear();
                            currentHunkGroupBytes = headerBytes;
                        }

                        var subHunks = SplitLargeHunk(hunk, maxChunkBytes - headerBytes - 100);
                        foreach (var subHunk in subHunks)
                        {
                            var partFile = new FileDiff
                            {
                                FilePath = file.FilePath,
                                HeaderLines = new List<string>(fileHeaderLines),
                                Hunks = [subHunk]
                            };
                            currentBatch.Add(partFile);
                            FlushCurrentBatch();
                        }
                        continue;
                    }

                    if (currentHunkGroupBytes + hunkBytes > maxChunkBytes && currentHunkGroup.Count > 0)
                    {
                        var partFile = new FileDiff
                        {
                            FilePath = file.FilePath,
                            HeaderLines = new List<string>(fileHeaderLines),
                            Hunks = new List<DiffHunk>(currentHunkGroup)
                        };
                        currentBatch.Add(partFile);
                        FlushCurrentBatch();
                        currentHunkGroup.Clear();
                        currentHunkGroupBytes = headerBytes;
                    }

                    currentHunkGroup.Add(hunk);
                    currentHunkGroupBytes += hunkBytes;
                }

                if (currentHunkGroup.Count > 0)
                {
                    var partFile = new FileDiff
                    {
                        FilePath = file.FilePath,
                        HeaderLines = new List<string>(fileHeaderLines),
                        Hunks = new List<DiffHunk>(currentHunkGroup)
                    };
                    currentBatch.Add(partFile);
                    FlushCurrentBatch();
                }

                continue;
            }

            // Case 2: File fits in a normal chunk batch
            if (currentBytes + fileBytes > maxChunkBytes && currentBatch.Count > 0)
            {
                FlushCurrentBatch();
            }

            currentBatch.Add(file);
            currentBytes += fileBytes;
        }

        FlushCurrentBatch();

        // Update TotalChunks
        for (int i = 0; i < chunks.Count; i++)
        {
            chunks[i].Index = i + 1;
            chunks[i].TotalChunks = chunks.Count;
        }

        return chunks;
    }

    private static FileDiff PrepareFileForChunking(FileDiff file, bool filterDeletedLines, int maxConsecutiveDeleted = 3)
    {
        if (!filterDeletedLines || file.Hunks.Count == 0)
        {
            return file;
        }

        var filteredHunks = new List<DiffHunk>(file.Hunks.Count);
        foreach (var hunk in file.Hunks)
        {
            filteredHunks.Add(FilterDeletedLines(hunk, maxConsecutiveDeleted));
        }

        return new FileDiff
        {
            FilePath = file.FilePath,
            OldFilePath = file.OldFilePath,
            NewFileMode = file.NewFileMode,
            OldFileMode = file.OldFileMode,
            IsNewFile = file.IsNewFile,
            IsDeletedFile = file.IsDeletedFile,
            IsRenamedFile = file.IsRenamedFile,
            IsBinary = file.IsBinary,
            HeaderLines = new List<string>(file.HeaderLines),
            Hunks = filteredHunks,
            AddedLines = file.AddedLines,
            DeletedLines = file.DeletedLines
        };
    }

    private static List<DiffHunk> SplitLargeHunk(DiffHunk hunk, int maxBytes)
    {
        maxBytes = Math.Max(maxBytes, 1000);
        var subHunks = new List<DiffHunk>();
        var currentLines = new List<string>();
        int currentBytes = Encoding.UTF8.GetByteCount(hunk.Header) + 1;
        int part = 1;

        foreach (var line in hunk.Lines)
        {
            int lineBytes = Encoding.UTF8.GetByteCount(line) + 1;

            if (lineBytes > maxBytes)
            {
                if (currentLines.Count > 0)
                {
                    subHunks.Add(new DiffHunk
                    {
                        Header = $"{hunk.Header} [Part {part++}]",
                        Lines = new List<string>(currentLines)
                    });
                    currentLines.Clear();
                    currentBytes = Encoding.UTF8.GetByteCount(hunk.Header) + 1;
                }

                int maxLineChars = maxBytes / 2;
                if (maxLineChars < 1) maxLineChars = 1;

                for (int start = 0; start < line.Length; start += maxLineChars)
                {
                    int length = Math.Min(maxLineChars, line.Length - start);
                    var slice = line.Substring(start, length);
                    subHunks.Add(new DiffHunk
                    {
                        Header = $"{hunk.Header} [Part {part++} - Segment {start + 1}..{start + length}]",
                        Lines = [slice]
                    });
                }
                continue;
            }

            if (currentBytes + lineBytes > maxBytes && currentLines.Count > 0)
            {
                subHunks.Add(new DiffHunk
                {
                    Header = $"{hunk.Header} [Part {part++}]",
                    Lines = new List<string>(currentLines)
                });
                currentLines.Clear();
                currentBytes = Encoding.UTF8.GetByteCount(hunk.Header) + 1;
            }

            currentLines.Add(line);
            currentBytes += lineBytes;
        }

        if (currentLines.Count > 0)
        {
            subHunks.Add(new DiffHunk
            {
                Header = $"{hunk.Header} [Part {part}]",
                Lines = new List<string>(currentLines)
            });
        }

        return subHunks;
    }
}

