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
            Header = "@@ -1,5 +1,5 @@",
            Lines =
            [
                " context line",
                "- deleted 1",
                "- deleted 2",
                "+ added line"
            ]
        };

        var filtered = DiffSplitter.FilterDeletedLines(hunk, maxConsecutiveDeleted: 3);

        Assert.DoesNotContain(filtered.Lines, l => l.Contains("suppressed to reduce tokens"));
        Assert.Contains("- deleted 1", filtered.Lines);
        Assert.Contains("- deleted 2", filtered.Lines);
        Assert.Contains("+ added line", filtered.Lines);
    }

    [Fact]
    public void SplitIntoChunks_RespectsMaxBytes_PartitionsMultipleFiles()
    {
        // Generate 5 files, each ~1000 bytes
        var files = new List<FileDiff>();
        for (int i = 1; i <= 5; i++)
        {
            var f = new FileDiff
            {
                FilePath = $"file{i}.cs",
                HeaderLines = [$"diff --git a/file{i}.cs b/file{i}.cs", "--- a", "+++ b"],
                Hunks =
                [
                    new DiffHunk
                    {
                        Header = "@@ -1,10 +1,10 @@",
                        Lines = Enumerable.Range(1, 30).Select(n => $"+ line {n} in file {i} with some extra padding text").ToList()
                    }
                ]
            };
            files.Add(f);
        }

        // Total bytes ~5000 bytes. With maxChunkBytes = 2500, we should get at least 2 chunks
        int maxChunkBytes = 2500;
        var chunks = DiffSplitter.SplitIntoChunks(files, maxChunkBytes);

        Assert.True(chunks.Count >= 2);
        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            Assert.Equal(i + 1, chunk.Index);
            Assert.Equal(chunks.Count, chunk.TotalChunks);
            Assert.True(chunk.ByteSize <= maxChunkBytes);
            Assert.NotEmpty(chunk.Content);
            Assert.NotEmpty(chunk.FilePaths);
            Assert.True(chunk.EstimatedTokens > 0);
        }
    }

    [Fact]
    public void SplitIntoChunks_LargeFile_SplitsByHunks()
    {
        var largeFile = new FileDiff
        {
            FilePath = "LargeFile.cs",
            HeaderLines = ["diff --git a/LargeFile.cs b/LargeFile.cs", "--- a/LargeFile.cs", "+++ b/LargeFile.cs"]
        };

        // Add 5 hunks, each ~1500 bytes
        for (int h = 1; h <= 5; h++)
        {
            largeFile.Hunks.Add(new DiffHunk
            {
                Header = $"@@ -{h * 100},10 +{h * 100},10 @@",
                Lines = Enumerable.Range(1, 40).Select(n => $"+ hunk {h} line {n} padding padding padding padding padding").ToList()
            });
        }

        int maxBytes = 3000;
        var chunks = DiffSplitter.SplitIntoChunks([largeFile], maxBytes);

        Assert.True(chunks.Count > 1);
        foreach (var chunk in chunks)
        {
            Assert.Contains("LargeFile.cs", chunk.FilePaths);
            Assert.True(chunk.ByteSize <= maxBytes);
            Assert.StartsWith("diff --git a/LargeFile.cs", chunk.Content);
        }
    }

    [Fact]
    public void SplitIntoChunks_OversizedSingleLine_SlicesLineWithoutExceedingLimit()
    {
        var hunk = new DiffHunk
        {
            Header = "@@ -1,1 +1,1 @@",
            // Single huge line of 5,000 characters
            Lines = ["+" + new string('X', 5000)]
        };

        var file = new FileDiff
        {
            FilePath = "Minified.js",
            HeaderLines = ["diff --git a/Minified.js b/Minified.js"],
            Hunks = [hunk]
        };

        int maxChunkBytes = 1500;
        var chunks = DiffSplitter.SplitIntoChunks([file], maxChunkBytes);

        Assert.True(chunks.Count >= 3);
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.ByteSize <= maxChunkBytes);
            Assert.Contains("Minified.js", chunk.FilePaths);
        }
    }

    [Fact]
    public void SplitIntoChunks_EmptyList_ReturnsEmpty()
    {
        var chunks = DiffSplitter.SplitIntoChunks([]);
        Assert.Empty(chunks);
    }
}

