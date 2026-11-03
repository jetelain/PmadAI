using Pmad.AI.Guardrails.Diff;

namespace Pmad.AI.Tests.Guardrails.Diff.Parsing;

public class DiffParserTests
{
    [Fact]
    public void Parse_StandardDiff_ParsesAddedAndDeletedLinesAndHunks()
    {
        var diff = """
            diff --git a/src/Program.cs b/src/Program.cs
            index 1234567..89abcdef 100644
            --- a/src/Program.cs
            +++ b/src/Program.cs
            @@ -10,4 +10,5 @@ namespace Demo;
             using System;
            -using System.Text;
            +using System.IO;
            +using System.Threading;
             public class Program
            """;

        var files = DiffParser.Parse(diff);

        Assert.Single(files);
        var file = files[0];
        Assert.Equal("src/Program.cs", file.FilePath);
        Assert.Null(file.OldFilePath);
        Assert.False(file.IsNewFile);
        Assert.False(file.IsDeletedFile);
        Assert.False(file.IsRenamedFile);
        Assert.False(file.IsBinary);

        Assert.Single(file.Hunks);
        var hunk = file.Hunks[0];
        Assert.Equal(10, hunk.OldStartingLine);
        Assert.Equal(4, hunk.OldLineCount);
        Assert.Equal(10, hunk.NewStartingLine);
        Assert.Equal(5, hunk.NewLineCount);

        Assert.Single(file.DeletedLines);
        Assert.Equal("using System.Text;", file.DeletedLines[0].Content);
        Assert.Equal(11, file.DeletedLines[0].LineNumber);
        Assert.Equal("src/Program.cs", file.DeletedLines[0].FilePath);

        Assert.Equal(2, file.AddedLines.Count);
        Assert.Equal("using System.IO;", file.AddedLines[0].Content);
        Assert.Equal(11, file.AddedLines[0].LineNumber);
        Assert.Equal("using System.Threading;", file.AddedLines[1].Content);
        Assert.Equal(12, file.AddedLines[1].LineNumber);
    }

    [Fact]
    public void Parse_MultipleFiles_ParsesAllFiles()
    {
        var diff = """
            diff --git a/file1.cs b/file1.cs
            index 111..222 100644
            --- a/file1.cs
            +++ b/file1.cs
            @@ -1,2 +1,3 @@
             Console.WriteLine();
            +Console.WriteLine("Added");
            diff --git a/file2.cs b/file2.cs
            index 333..444 100644
            --- a/file2.cs
            +++ b/file2.cs
            @@ -5,2 +5,2 @@
            -int a = 1;
            +int a = 2;
            """;

        var files = DiffParser.Parse(diff);

        Assert.Equal(2, files.Count);
        Assert.Equal("file1.cs", files[0].FilePath);
        Assert.Single(files[0].AddedLines);
        Assert.Empty(files[0].DeletedLines);

        Assert.Equal("file2.cs", files[1].FilePath);
        Assert.Single(files[1].AddedLines);
        Assert.Single(files[1].DeletedLines);
    }

    [Fact]
    public void Parse_NewFile_SetsIsNewFileAndMode()
    {
        var diff = """
            diff --git a/NewFile.cs b/NewFile.cs
            new file mode 100644
            index 0000000..abcdef1
            --- /dev/null
            +++ b/NewFile.cs
            @@ -0,0 +1,3 @@
            +public class NewClass
            +{
            +}
            """;

        var files = DiffParser.Parse(diff);

        Assert.Single(files);
        var file = files[0];
        Assert.Equal("NewFile.cs", file.FilePath);
        Assert.True(file.IsNewFile);
        Assert.False(file.IsDeletedFile);
        Assert.Equal("100644", file.NewFileMode);
        Assert.Equal(3, file.AddedLines.Count);
        Assert.Empty(file.DeletedLines);
        Assert.Equal(1, file.AddedLines[0].LineNumber);
        Assert.Equal("public class NewClass", file.AddedLines[0].Content);
    }

    [Fact]
    public void Parse_DeletedFile_SetsIsDeletedFileAndMode()
    {
        var diff = """
            diff --git a/OldFile.cs b/OldFile.cs
            deleted file mode 100644
            index abcdef1..0000000
            --- a/OldFile.cs
            +++ /dev/null
            @@ -1,2 +0,0 @@
            -class Old
            -{
            """;

        var files = DiffParser.Parse(diff);

        Assert.Single(files);
        var file = files[0];
        Assert.Equal("OldFile.cs", file.FilePath);
        Assert.True(file.IsDeletedFile);
        Assert.False(file.IsNewFile);
        Assert.Equal("100644", file.OldFileMode);
        Assert.Equal(2, file.DeletedLines.Count);
        Assert.Empty(file.AddedLines);
    }

    [Fact]
    public void Parse_FileRenameWithoutEdits_SetsOldFilePathAndIsRenamed()
    {
        var diff = """
            diff --git a/src/OldName.cs b/src/NewName.cs
            similarity index 100%
            rename from src/OldName.cs
            rename to src/NewName.cs
            """;

        var files = DiffParser.Parse(diff);

        Assert.Single(files);
        var file = files[0];
        Assert.Equal("src/NewName.cs", file.FilePath);
        Assert.Equal("src/OldName.cs", file.OldFilePath);
        Assert.True(file.IsRenamedFile);
        Assert.Empty(file.Hunks);
        Assert.Empty(file.AddedLines);
        Assert.Empty(file.DeletedLines);
    }

    [Fact]
    public void Parse_FileRenameWithEdits_SetsOldFilePathAndParsesHunk()
    {
        var diff = """
            diff --git a/src/OldName.cs b/src/NewName.cs
            similarity index 80%
            rename from src/OldName.cs
            rename to src/NewName.cs
            index 1234567..89abcdef 100644
            --- a/src/OldName.cs
            +++ b/src/NewName.cs
            @@ -1,2 +1,2 @@
            -string name = "old";
            +string name = "new";
            """;

        var files = DiffParser.Parse(diff);

        Assert.Single(files);
        var file = files[0];
        Assert.Equal("src/NewName.cs", file.FilePath);
        Assert.Equal("src/OldName.cs", file.OldFilePath);
        Assert.True(file.IsRenamedFile);
        Assert.Single(file.Hunks);
        Assert.Single(file.AddedLines);
        Assert.Single(file.DeletedLines);
        Assert.Equal("string name = \"new\";", file.AddedLines[0].Content);
    }

    [Fact]
    public void Parse_FileModeChangeOnly_ParsesOldAndNewModes()
    {
        var diff = """
            diff --git a/scripts/run.sh b/scripts/run.sh
            old mode 100644
            new mode 100755
            """;

        var files = DiffParser.Parse(diff);

        Assert.Single(files);
        var file = files[0];
        Assert.Equal("scripts/run.sh", file.FilePath);
        Assert.Equal("100644", file.OldFileMode);
        Assert.Equal("100755", file.NewFileMode);
        Assert.Empty(file.Hunks);
    }

    [Fact]
    public void Parse_SubmoduleMode_Parses160000Mode()
    {
        var diff = """
            diff --git a/external/submod b/external/submod
            new file mode 160000
            index 0000000..abcdef1
            --- /dev/null
            +++ b/external/submod
            @@ -0,0 +1 @@
            +Subproject commit abcdef1234567890
            """;

        var files = DiffParser.Parse(diff);

        Assert.Single(files);
        var file = files[0];
        Assert.Equal("external/submod", file.FilePath);
        Assert.True(file.IsNewFile);
        Assert.Equal("160000", file.NewFileMode);
        Assert.Single(file.AddedLines);
    }

    [Fact]
    public void Parse_BinaryFilesDiffer_SetsIsBinary()
    {
        var diff = """
            diff --git a/assets/logo.png b/assets/logo.png
            index 1111111..2222222 100644
            Binary files a/assets/logo.png and b/assets/logo.png differ
            """;

        var files = DiffParser.Parse(diff);

        Assert.Single(files);
        var file = files[0];
        Assert.Equal("assets/logo.png", file.FilePath);
        Assert.True(file.IsBinary);
    }

    [Fact]
    public void Parse_NewBinaryFile_SetsIsBinaryAndIsNewFile()
    {
        var diff = """
            diff --git a/assets/new.png b/assets/new.png
            new file mode 100644
            index 0000000..2222222
            Binary files /dev/null and b/assets/new.png differ
            """;

        var files = DiffParser.Parse(diff);

        Assert.Single(files);
        var file = files[0];
        Assert.Equal("assets/new.png", file.FilePath);
        Assert.True(file.IsBinary);
        Assert.True(file.IsNewFile);
    }

    [Fact]
    public void Parse_GitBinaryPatch_SetsIsBinary()
    {
        var diff = """
            diff --git a/data/archive.bin b/data/archive.bin
            index 1111111..2222222 100644
            GIT binary patch
            literal 128
            zcmd1?U|<3|NkV{
            """;

        var files = DiffParser.Parse(diff);

        Assert.Single(files);
        var file = files[0];
        Assert.Equal("data/archive.bin", file.FilePath);
        Assert.True(file.IsBinary);
    }

    [Fact]
    public void Parse_ExcludedGlobs_FiltersMatchingFiles()
    {
        var diff = """
            diff --git a/.scratch/temp.cs b/.scratch/temp.cs
            --- a/.scratch/temp.cs
            +++ b/.scratch/temp.cs
            @@ -1,1 +1,1 @@
            +temp code
            diff --git a/docs/tasks/todo.md b/docs/tasks/todo.md
            --- a/docs/tasks/todo.md
            +++ b/docs/tasks/todo.md
            @@ -1,1 +1,1 @@
            +tasks
            diff --git a/src/App.cs b/src/App.cs
            --- a/src/App.cs
            +++ b/src/App.cs
            @@ -1,1 +1,1 @@
            +clean code
            """;

        var options = new DiffRiskScannerOptions
        {
            ExcludedFileGlobs = [".scratch/**", "docs/tasks/**"]
        };

        var files = DiffParser.Parse(diff, options);

        Assert.Single(files);
        Assert.Equal("src/App.cs", files[0].FilePath);
    }

    [Fact]
    public void Parse_QuotedPathWithSpaces_ParsesCorrectly()
    {
        var diff = """
            diff --git "a/my directory/file name.cs" "b/my directory/file name.cs"
            index 111..222 100644
            --- "a/my directory/file name.cs"
            +++ "b/my directory/file name.cs"
            @@ -1,1 +1,2 @@
             int a;
            +int b;
            """;

        var files = DiffParser.Parse(diff);

        Assert.Single(files);
        Assert.Equal("my directory/file name.cs", files[0].FilePath);
        Assert.Single(files[0].AddedLines);
    }

    [Fact]
    public void Parse_EmptyOrWhitespaceDiff_ReturnsEmptyList()
    {
        Assert.Empty(DiffParser.Parse(string.Empty));
        Assert.Empty(DiffParser.Parse("   \r\n\t   "));
    }

    [Fact]
    public void ParseAddedLines_ReturnsAllAddedLines()
    {
        var diff = """
            diff --git a/a.cs b/a.cs
            --- a/a.cs
            +++ b/a.cs
            @@ -1,1 +1,2 @@
             context
            +line 1
            diff --git a/b.cs b/b.cs
            --- a/b.cs
            +++ b/b.cs
            @@ -1,1 +1,2 @@
             context
            +line 2
            """;

        var addedLines = DiffParser.ParseAddedLines(diff);

        Assert.Equal(2, addedLines.Count);
        Assert.Equal("a.cs", addedLines[0].FilePath);
        Assert.Equal("line 1", addedLines[0].Content);
        Assert.Equal("b.cs", addedLines[1].FilePath);
        Assert.Equal("line 2", addedLines[1].Content);
    }

    [Fact]
    public void Parse_DiffEndingWithNewline_DoesNotAppendPhantomEmptyHunkLine()
    {
        var diff = "diff --git a/src/App.cs b/src/App.cs\n--- a/src/App.cs\n+++ b/src/App.cs\n@@ -1,1 +1,2 @@\n context\n+added\n";

        var files = DiffParser.Parse(diff);

        Assert.Single(files);
        var hunk = Assert.Single(files[0].Hunks);
        Assert.Equal([" context", "+added"], hunk.Lines);
    }

    [Fact]
    public void Parse_PlainUnifiedDiffMultipleFilesWithTimestamps_ParsesCorrectlyWithoutSettingOldFilePath()
    {
        var diff =
            "--- a/src/First.cs\t2026-11-03 21:00:00 +0100\n" +
            "+++ b/src/First.cs\t2026-11-03 21:01:00 +0100\n" +
            "@@ -1,1 +1,2 @@\n" +
            " context\n" +
            "+first added\n" +
            "--- a/src/Second.cs\t2026-11-03 21:00:00 +0100\n" +
            "+++ b/src/Second.cs\t2026-11-03 21:01:00 +0100\n" +
            "@@ -1,1 +1,1 @@\n" +
            "-second old\n" +
            "+second new\n" +
            "--- a/src/Deleted.cs\t2026-11-03 21:00:00 +0100\n" +
            "+++ /dev/null\t2026-11-03 21:01:00 +0100\n" +
            "@@ -1,1 +0,0 @@\n" +
            "-deleted line\n";

        var files = DiffParser.Parse(diff);

        Assert.Equal(3, files.Count);

        Assert.Equal("src/First.cs", files[0].FilePath);
        Assert.Null(files[0].OldFilePath);
        Assert.Single(files[0].AddedLines);
        Assert.Equal("first added", files[0].AddedLines[0].Content);

        Assert.Equal("src/Second.cs", files[1].FilePath);
        Assert.Null(files[1].OldFilePath);
        Assert.Single(files[1].AddedLines);
        Assert.Equal("second new", files[1].AddedLines[0].Content);
        Assert.Single(files[1].DeletedLines);
        Assert.Equal("second old", files[1].DeletedLines[0].Content);

        Assert.Equal("src/Deleted.cs", files[2].FilePath);
        Assert.Null(files[2].OldFilePath);
        Assert.True(files[2].IsDeletedFile);
        Assert.Single(files[2].DeletedLines);
    }

    [Fact]
    public void Parse_QuotedPathWithEscapedQuotes_UnescapesCorrectly()
    {
        var diff = """
            diff --git "a/dir/file \"quoted\" name.cs" "b/dir/file \"quoted\" name.cs"
            --- "a/dir/file \"quoted\" name.cs"
            +++ "b/dir/file \"quoted\" name.cs"
            @@ -1,1 +1,2 @@
             int a;
            +int b;
            """;

        var files = DiffParser.Parse(diff);

        Assert.Single(files);
        Assert.Equal("dir/file \"quoted\" name.cs", files[0].FilePath);
        Assert.Null(files[0].OldFilePath);
        Assert.Single(files[0].AddedLines);
    }

    [Fact]
    public void Parse_HunkLinesStartingWithDoublePlusOrDoubleMinus_AreParsedAsCodeChanges()
    {
        var diff = """
            diff --git a/query.sql b/query.sql
            --- a/query.sql
            +++ b/query.sql
            @@ -1,2 +1,2 @@
            --- old sql comment
            -SELECT 1;
            +++i;
            +-- new sql comment
            """;

        var files = DiffParser.Parse(diff);

        Assert.Single(files);
        Assert.Equal(2, files[0].DeletedLines.Count);
        Assert.Equal("-- old sql comment", files[0].DeletedLines[0].Content);
        Assert.Equal("SELECT 1;", files[0].DeletedLines[1].Content);

        Assert.Equal(2, files[0].AddedLines.Count);
        Assert.Equal("++i;", files[0].AddedLines[0].Content);
        Assert.Equal("-- new sql comment", files[0].AddedLines[1].Content);
    }
}

