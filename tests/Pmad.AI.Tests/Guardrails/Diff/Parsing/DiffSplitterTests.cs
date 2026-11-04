using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.Tests.Guardrails.Diff.Parsing;

public class DiffSplitterTests
{
    [Fact]
    public void FilterDeletedLines_LongContiguousBlock_CompressesDeletedLines()
    {
        var hunk = new DiffHunk
        {
            Header = "@@ -1,15 +1,5 @@",
            Lines =
            [
                " context line",
                "- deleted 1",
                "- deleted 2",
                "- deleted 3",
                "- deleted 4",
                "- deleted 5",
                "- deleted 6",
                "- deleted 7",
                "+ added line",
                " trailing context"
            ]
        };

        var filtered = DiffSplitter.FilterDeletedLines(hunk, maxConsecutiveDeleted: 3);

        // Should retain:
        // context line
        // - deleted 1
        // - ... [5 lines of deleted code suppressed to reduce tokens] ...
        // - deleted 7
        // + added line
        // trailing context
        Assert.Contains(filtered.Lines, l => l.Contains("suppressed to reduce tokens"));
        Assert.Contains("- deleted 1", filtered.Lines);
        Assert.Contains("- deleted 7", filtered.Lines);
        Assert.DoesNotContain("- deleted 3", filtered.Lines);
        Assert.DoesNotContain("- deleted 4", filtered.Lines);
        Assert.DoesNotContain("- deleted 5", filtered.Lines);
        Assert.Contains("+ added line", filtered.Lines);
    }

    [Fact]
    public void FilterDeletedLines_ShortBlock_NotCompressed()
    {
        var hunk = new DiffHunk
        {
            Header = "@@ -1,5 +1,4 @@",
            Lines =
            [
                " context",
                "- deleted 1",
                "- deleted 2",
                "+ added 1",
                " context end"
            ]
        };

        var filtered = DiffSplitter.FilterDeletedLines(hunk, maxConsecutiveDeleted: 3);

        Assert.DoesNotContain(filtered.Lines, l => l.Contains("suppressed"));
        Assert.Contains("- deleted 1", filtered.Lines);
        Assert.Contains("- deleted 2", filtered.Lines);
    }

    [Fact]
    public void FilterDeletedLines_ExactThreshold_NotCompressed()
    {
        var hunk = new DiffHunk
        {
            Header = "@@ -1,6 +1,4 @@",
            Lines =
            [
                " context",
                "- deleted 1",
                "- deleted 2",
                "- deleted 3",
                "+ added 1",
                " context end"
            ]
        };

        var filtered = DiffSplitter.FilterDeletedLines(hunk, maxConsecutiveDeleted: 3);

        Assert.DoesNotContain(filtered.Lines, l => l.Contains("suppressed"));
        Assert.Contains("- deleted 1", filtered.Lines);
        Assert.Contains("- deleted 2", filtered.Lines);
        Assert.Contains("- deleted 3", filtered.Lines);
    }

    [Fact]
    public void FilterDeletedLines_MultipleDeletedBlocks_CompressesEachSeparately()
    {
        var hunk = new DiffHunk
        {
            Header = "@@ -1,20 +1,5 @@",
            Lines =
            [
                "- del A1",
                "- del A2",
                "- del A3",
                "- del A4",
                "- del A5",
                " separator context",
                "- del B1",
                "- del B2",
                "- del B3",
                "- del B4",
                "+ added"
            ]
        };

        var filtered = DiffSplitter.FilterDeletedLines(hunk, maxConsecutiveDeleted: 3);

        int suppressedCount = filtered.Lines.Count(l => l.Contains("suppressed to reduce tokens"));
        Assert.Equal(2, suppressedCount);
    }

    [Fact]
    public void SplitIntoChunks_SmallFiles_PackedTogether()
    {
        var diff = """
        diff --git a/file1.cs b/file1.cs
        index 1111111..2222222 100644
        --- a/file1.cs
        +++ b/file1.cs
        @@ -1,3 +1,3 @@
         line 1
        -old line 2
        +new line 2
         line 3
        diff --git a/file2.cs b/file2.cs
        index 3333333..4444444 100644
        --- a/file2.cs
        +++ b/file2.cs
        @@ -1,3 +1,3 @@
         file 2 line 1
        -file 2 old line 2
        +file 2 new line 2
         file 2 line 3
        """;

        var files = DiffParser.Parse(diff);
        var chunks = DiffSplitter.SplitIntoChunks(files, maxChunkBytes: 50_000);

        Assert.Single(chunks);
        var chunk = chunks[0];
        Assert.Equal(1, chunk.Index);
        Assert.Equal(1, chunk.TotalChunks);
        Assert.Equal(2, chunk.FilePaths.Count);
        Assert.Contains("file1.cs", chunk.FilePaths);
        Assert.Contains("file2.cs", chunk.FilePaths);
        Assert.Contains("new line 2", chunk.Content);
        Assert.Contains("file 2 new line 2", chunk.Content);
    }

    [Fact]
    public void SplitIntoChunks_ExceedingThreshold_SplitsAcrossChunks()
    {
        // Build 3 distinct files that exceed maxChunkBytes when grouped
        var files = new List<FileDiff>();
        for (int i = 1; i <= 3; i++)
        {
            var hunk = new DiffHunk
            {
                Header = "@@ -1,10 +1,10 @@",
                Lines = Enumerable.Range(1, 10).Select(n => $"+ File {i} added line {n} with padding text").ToList()
            };
            files.Add(new FileDiff
            {
                FilePath = $"src/File{i}.cs",
                HeaderLines = [$"diff --git a/src/File{i}.cs b/src/File{i}.cs"],
                Hunks = [hunk],
                AddedLines = hunk.Lines.Select((l, idx) => new DiffAddedLine($"src/File{i}.cs", idx + 1, l)).ToList()
            });
        }

        // Set maxChunkBytes small enough that not all 3 files fit in one chunk (~300 bytes per file)
        int maxChunkBytes = 400;
        var chunks = DiffSplitter.SplitIntoChunks(files, maxChunkBytes: maxChunkBytes);

        Assert.True(chunks.Count > 1, $"Expected > 1 chunks, got {chunks.Count}");
        Assert.All(chunks, c => Assert.True(c.ByteSize <= maxChunkBytes + 300));
        Assert.Equal(chunks.Count, chunks[^1].TotalChunks);
    }

    [Fact]
    public void SplitIntoChunks_SingleFileExceedingThreshold_SplitsByHunks()
    {
        var hunks = new List<DiffHunk>();
        for (int h = 1; h <= 4; h++)
        {
            hunks.Add(new DiffHunk
            {
                Header = $"@@ -{h * 10},5 +{h * 10},5 @@",
                Lines = Enumerable.Range(1, 5).Select(n => $"+ Hunk {h} line {n}").ToList()
            });
        }

        var file = new FileDiff
        {
            FilePath = "LargeFile.cs",
            HeaderLines = ["diff --git a/LargeFile.cs b/LargeFile.cs"],
            Hunks = hunks
        };

        // Budget fits approximately 1 hunk per chunk
        int maxChunkBytes = 120;
        var chunks = DiffSplitter.SplitIntoChunks([file], maxChunkBytes: maxChunkBytes);

        Assert.True(chunks.Count > 1, $"Expected > 1 chunks, got {chunks.Count}");
        Assert.All(chunks, c =>
        {
            Assert.Contains("diff --git a/LargeFile.cs b/LargeFile.cs", c.Content);
            Assert.Contains("LargeFile.cs", c.FilePaths);
        });
    }

    [Fact]
    public void SplitIntoChunks_SingleLineExceedingThreshold_SplitsBySlices()
    {
        // 1 very long line of 5,000 bytes
        string veryLongLine = "+ var payload = \"" + new string('A', 5_000) + "\";";

        var hunk = new DiffHunk
        {
            Header = "@@ -1,1 +1,1 @@",
            Lines = [veryLongLine]
        };

        var file = new FileDiff
        {
            FilePath = "HugeLine.cs",
            HeaderLines = ["diff --git a/HugeLine.cs b/HugeLine.cs"],
            Hunks = [hunk]
        };

        int maxChunkBytes = 1_000;
        var chunks = DiffSplitter.SplitIntoChunks([file], maxChunkBytes: maxChunkBytes);

        Assert.True(chunks.Count >= 5, $"Expected >= 5 chunks for 5k line with 1k limit, got {chunks.Count}");
        Assert.All(chunks, c => Assert.Contains("HugeLine.cs", c.FilePaths));
    }

    [Fact]
    public void FilterDeletedLines_DeletedSqlCommentsStartingWithTripleDash_CompressesCorrectly()
    {
        var hunk = new DiffHunk
        {
            Header = "@@ -1,6 +1,1 @@",
            Lines =
            [
                "--- comment 1",
                "--- comment 2",
                "--- comment 3",
                "--- comment 4",
                "--- comment 5",
                "+SELECT 1;"
            ]
        };

        var filtered = DiffSplitter.FilterDeletedLines(hunk, maxConsecutiveDeleted: 3);

        Assert.Contains(filtered.Lines, l => l.Contains("suppressed to reduce tokens"));
        Assert.Contains("--- comment 1", filtered.Lines);
        Assert.Contains("--- comment 5", filtered.Lines);
        Assert.DoesNotContain("--- comment 3", filtered.Lines);
    }

    [Fact]
    public void SplitIntoChunks_EmptyList_ReturnsEmpty()
    {
        var chunks = DiffSplitter.SplitIntoChunks([]);
        Assert.Empty(chunks);
    }

    [Fact]
    public void SplitIntoChunks_OversizedFileWithoutHunks_DoesNotDropFile()
    {
        // Diff with large binary patch or header data but 0 hunks exceeding maxChunkBytes
        var largeHeaderLines = new List<string> { "diff --git a/big.bin b/big.bin", "GIT binary patch" };
        largeHeaderLines.AddRange(Enumerable.Range(1, 100).Select(n => "literal " + new string('A', 80)));

        var file = new FileDiff
        {
            FilePath = "big.bin",
            IsBinary = true,
            HeaderLines = largeHeaderLines,
            Hunks = []
        };

        int maxChunkBytes = 1000;
        var chunks = DiffSplitter.SplitIntoChunks([file], maxChunkBytes);

        var chunk = Assert.Single(chunks);
        Assert.Equal("big.bin", Assert.Single(chunk.FilePaths));
        Assert.Contains("GIT binary patch", chunk.Content);
    }

    [Fact]
    public void SplitIntoChunks_ConfigurableMaxConsecutiveDeletedLines_CompressesProperly()
    {
        var hunk = new DiffHunk
        {
            Header = "@@ -1,7 +1,1 @@",
            Lines =
            [
                "- line 1",
                "- line 2",
                "- line 3",
                "- line 4",
                "- line 5",
                "+ replacement"
            ]
        };

        var file = new FileDiff
        {
            FilePath = "Test.cs",
            HeaderLines = ["diff --git a/Test.cs b/Test.cs"],
            Hunks = [hunk]
        };

        // When maxConsecutiveDeletedLines is 10, 5 lines should NOT be compressed
        var uncompressedChunks = DiffSplitter.SplitIntoChunks([file], filterDeletedLines: true, maxConsecutiveDeletedLines: 10);
        var uncompressed = Assert.Single(uncompressedChunks);
        Assert.DoesNotContain("suppressed to reduce tokens", uncompressed.Content);
        Assert.Contains("- line 3", uncompressed.Content);

        // When maxConsecutiveDeletedLines is 2, 5 lines should be compressed
        var compressedChunks = DiffSplitter.SplitIntoChunks([file], filterDeletedLines: true, maxConsecutiveDeletedLines: 2);
        var compressed = Assert.Single(compressedChunks);
        Assert.Contains("suppressed to reduce tokens", compressed.Content);
        Assert.DoesNotContain("- line 3", compressed.Content);
    }

    [Fact]
    public void SplitIntoChunks_DefaultFilterDeletedLinesIsFalse_DoesNotCompressDeletedLines()
    {
        var hunk = new DiffHunk
        {
            Header = "@@ -1,7 +1,1 @@",
            Lines =
            [
                "- line 1",
                "- line 2",
                "- line 3",
                "- line 4",
                "- line 5",
                "+ replacement"
            ]
        };

        var file = new FileDiff
        {
            FilePath = "Test.cs",
            HeaderLines = ["diff --git a/Test.cs b/Test.cs"],
            Hunks = [hunk]
        };

        // Calling SplitIntoChunks with default settings (filterDeletedLines = false) must preserve all deleted lines
        var defaultChunks = DiffSplitter.SplitIntoChunks([file]);
        var chunk = Assert.Single(defaultChunks);
        Assert.DoesNotContain("suppressed to reduce tokens", chunk.Content);
        Assert.Contains("- line 1", chunk.Content);
        Assert.Contains("- line 2", chunk.Content);
        Assert.Contains("- line 3", chunk.Content);
        Assert.Contains("- line 4", chunk.Content);
        Assert.Contains("- line 5", chunk.Content);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(-1, 3)]
    [InlineData(1000, 0)]
    [InlineData(1000, -1)]
    public void SplitIntoChunks_InvalidArguments_ThrowsArgumentOutOfRangeException(int maxChunkBytes, int maxConsecutiveDeletedLines)
    {
        var file = new FileDiff
        {
            FilePath = "test.cs",
            HeaderLines = ["diff --git a/test.cs b/test.cs"],
            Hunks = [new DiffHunk { Header = "@@ -1,1 +1,1 @@", Lines = ["+ line"] }]
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DiffSplitter.SplitIntoChunks([file], maxChunkBytes: maxChunkBytes, maxConsecutiveDeletedLines: maxConsecutiveDeletedLines));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DiffSplitter.SplitIntoChunks("diff text", maxChunkBytes: maxChunkBytes, maxConsecutiveDeletedLines: maxConsecutiveDeletedLines));
    }

    [Fact]
    public void SplitIntoChunks_OversizedMultiByteUtf8LineAndSmallBudget_PreservesSurrogatePairsWithoutReplacementChar()
    {
        // 4-byte UTF-8 emojis (surrogate pairs in UTF-16) and 3-byte characters
        var multiBytePayload = string.Concat(Enumerable.Repeat("🎉あ", 300));
        var hunk = new DiffHunk
        {
            Header = "@@ -1,1 +1,1 @@",
            Lines = ["+" + multiBytePayload]
        };

        var file = new FileDiff
        {
            FilePath = "Unicode.txt",
            HeaderLines = ["diff --git a/Unicode.txt b/Unicode.txt"],
            Hunks = [hunk]
        };

        int maxChunkBytes = 500;
        var chunks = DiffSplitter.SplitIntoChunks([file], maxChunkBytes: maxChunkBytes);

        Assert.True(chunks.Count >= 4, $"Expected >= 4 chunks, got {chunks.Count}");
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.ByteSize <= maxChunkBytes, $"Chunk byte size {chunk.ByteSize} exceeded {maxChunkBytes}");
            Assert.DoesNotContain('\uFFFD', chunk.Content);
            Assert.Contains("Unicode.txt", chunk.FilePaths);
        }
    }

    [Fact]
    public void SplitIntoChunks_NullArguments_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DiffSplitter.SplitIntoChunks((string)null!));
        Assert.Throws<ArgumentNullException>(() => DiffSplitter.SplitIntoChunks((IReadOnlyList<FileDiff>)null!));
    }

    [Fact]
    public void SplitIntoChunks_SmallHunkPartBudgetBelow64Bytes_RespectsMaxChunkBytes()
    {
        var hunk = new DiffHunk
        {
            Header = "@@ -1,3 +1,3 @@",
            Lines =
            [
                "+ line_one_12345678901234567890",
                "+ line_two_12345678901234567890",
                "+ line_three_123456789012345678"
            ]
        };

        var file = new FileDiff
        {
            FilePath = "Small.cs",
            HeaderLines = ["diff --git a/Small.cs b/Small.cs"],
            Hunks = [hunk]
        };

        // File header is 33 bytes, hunk part header is ~26 bytes, leaving ~41 bytes (< 64) for content
        int maxChunkBytes = 100;
        var chunks = DiffSplitter.SplitIntoChunks([file], maxChunkBytes: maxChunkBytes);

        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, c => Assert.True(c.ByteSize <= maxChunkBytes, $"Chunk byte size {c.ByteSize} exceeded {maxChunkBytes}"));
    }
}
