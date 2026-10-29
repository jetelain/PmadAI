using System.Text.Json;
using Microsoft.Extensions.AI;
using Pmad.AI.Tools;

namespace Pmad.AI.Tests;

public class LenientToolDispatcherEnhancementsTests
{
    public record TestEditItem(string OldText, string NewText, bool ReplaceAll = false);
    public record TestQuestionItem(string Prompt, string? Rationale = null, List<string>? Options = null);

    [Fact]
    public void CustomToolMapping_ComputesLimitFromEndLineAndStartLine()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["read_file"] = new ToolMapping(
                    aliases: ["read", "view_file"],
                    parameterAliases: new Dictionary<string, IEnumerable<string>>
                    {
                        ["path"] = ["file_path", "filePath"],
                        ["offset"] = ["start_line", "startLine"]
                    },
                    computedParameters: new Dictionary<string, ParameterComputer>
                    {
                        ["limit"] = raw =>
                        {
                            if (raw.TryGetValue("end_line", out var end) && int.TryParse(end?.ToString(), out var e))
                            {
                                int start = raw.TryGetValue("offset", out var off) && int.TryParse(off?.ToString(), out var o) ? o : 1;
                                return Math.Max(1, e - start + 1);
                            }
                            if (raw.TryGetValue("to_line", out var to) && int.TryParse(to?.ToString(), out var t))
                            {
                                int start = raw.TryGetValue("start_line", out var sl) && int.TryParse(sl?.ToString(), out var s) ? s : 1;
                                return Math.Max(1, t - start + 1);
                            }
                            return null;
                        }
                    })
            }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string path, int offset, int limit) => $"read {path} offset {offset} limit {limit}",
            "read_file"));

        // Case 1: offset and end_line
        var d1 = dispatcher.NormalizeCall("read_file", new Dictionary<string, object?>
        {
            ["path"] = "app.cs",
            ["offset"] = 10,
            ["end_line"] = 25
        });

        Assert.Equal("read_file", d1.CanonicalToolName);
        Assert.Equal(10, d1.Arguments["offset"]);
        Assert.Equal(16, d1.Arguments["limit"]); // 25 - 10 + 1 = 16

        // Case 2: start_line and to_line via alias "read"
        var d2 = dispatcher.NormalizeCall("read", new Dictionary<string, object?>
        {
            ["file_path"] = "test.txt",
            ["start_line"] = 5,
            ["to_line"] = 15
        });

        Assert.Equal("read_file", d2.CanonicalToolName);
        Assert.Equal("test.txt", d2.Arguments["path"]);
        Assert.Equal(5, d2.Arguments["offset"]);
        Assert.Equal(11, d2.Arguments["limit"]); // 15 - 5 + 1 = 11
    }

    [Fact]
    public async Task AutoPromote_FlattenedParametersToEditsArray()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (string path, List<TestEditItem> edits) => $"Edited {path} with {edits.Count} chunk: {edits[0].OldText}->{edits[0].NewText}",
            "edit");

        dispatcher.RegisterTool(tool);

        // Model emits flattened root properties: path, oldText, newText
        var dispatched = dispatcher.NormalizeCall("edit", new Dictionary<string, object?>
        {
            ["path"] = "Service.cs",
            ["oldText"] = "class Old",
            ["newText"] = "class New"
        });

        Assert.Equal("edit", dispatched.CanonicalToolName);
        Assert.Equal("Service.cs", dispatched.Arguments["path"]);
        Assert.False(dispatched.Arguments.ContainsKey("oldText"));
        Assert.False(dispatched.Arguments.ContainsKey("newText"));

        var editsList = Assert.IsAssignableFrom<IEnumerable<object?>>(dispatched.Arguments["edits"]);
        var chunk = editsList.Cast<IDictionary<string, object?>>().Single();
        Assert.Equal("class Old", chunk["oldText"]);
        Assert.Equal("class New", chunk["newText"]);

        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal("Edited Service.cs with 1 chunk: class Old->class New", result?.ToString());
    }

    [Fact]
    public async Task AutoPromote_BlankChunkFallback_SynthesizesFromRootArgs()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (string path, List<TestEditItem> edits) => $"Edited {path} with {edits.Count} chunk: {edits[0].OldText}->{edits[0].NewText}",
            "edit");

        dispatcher.RegisterTool(tool);

        // Model emits empty [{}] array in edits, but leaves old_text and new_text at root
        var dispatched = dispatcher.NormalizeCall("edit", new Dictionary<string, object?>
        {
            ["path"] = "Service.cs",
            ["edits"] = new List<object?> { new Dictionary<string, object?>() },
            ["old_text"] = "before",
            ["new_text"] = "after"
        });

        Assert.Equal("edit", dispatched.CanonicalToolName);
        var editsList = Assert.IsAssignableFrom<IEnumerable<object?>>(dispatched.Arguments["edits"]);
        var chunk = editsList.Cast<IDictionary<string, object?>>().Single();
        Assert.Equal("before", chunk["oldText"]);
        Assert.Equal("after", chunk["newText"]);

        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal("Edited Service.cs with 1 chunk: before->after", result?.ToString());
    }

    [Fact]
    public async Task AutoPromote_UnpacksSingleItemArrayToFlatParameters()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (string path, string old_text, string new_text) => $"Single edit on {path}: {old_text}->{new_text}",
            "replace_text");

        dispatcher.RegisterTool(tool);

        // Model passes edits array of 1 element to a tool that expects flat parameters
        var dispatched = dispatcher.NormalizeCall("replace_text", new Dictionary<string, object?>
        {
            ["path"] = "README.md",
            ["edits"] = new List<object?>
            {
                new Dictionary<string, object?> { ["old_text"] = "foo", ["new_text"] = "bar" }
            }
        });

        Assert.Equal("replace_text", dispatched.CanonicalToolName);
        Assert.False(dispatched.Arguments.ContainsKey("edits"));
        Assert.Equal("foo", dispatched.Arguments["old_text"]);
        Assert.Equal("bar", dispatched.Arguments["new_text"]);

        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal("Single edit on README.md: foo->bar", result?.ToString());
    }

    [Fact]
    public async Task PropagateReplaceAll_ToMultipleChunks()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (string path, List<TestEditItem> edits) => $"Edited {path} count {edits.Count} replaceAll1={edits[0].ReplaceAll} replaceAll2={edits[1].ReplaceAll}",
            "edit");

        dispatcher.RegisterTool(tool);

        // Root replace_all = "true" (string boolean) should bubble into all chunk dictionaries
        var dispatched = dispatcher.NormalizeCall("edit", new Dictionary<string, object?>
        {
            ["path"] = "main.py",
            ["replace_all"] = "true",
            ["edits"] = new List<object?>
            {
                new Dictionary<string, object?> { ["oldText"] = "x", ["newText"] = "y" },
                new Dictionary<string, object?> { ["oldText"] = "1", ["newText"] = "2" }
            }
        });

        Assert.Equal("edit", dispatched.CanonicalToolName);
        Assert.False(dispatched.Arguments.ContainsKey("replace_all")); // Cleaned from root

        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal("Edited main.py count 2 replaceAll1=True replaceAll2=True", result?.ToString());
    }

    [Fact]
    public async Task PrimitiveToObject_AutoWrapping_ForQuestionArrays()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (List<TestQuestionItem> questions) => $"Asked {questions.Count} questions: {string.Join("; ", questions.Select(q => q.Prompt))}",
            "ask_questions");

        dispatcher.RegisterTool(tool);

        // Case 1: Model passes array of primitive strings instead of array of QuestionItem objects
        var dispatched1 = dispatcher.NormalizeCall("ask_questions", new Dictionary<string, object?>
        {
            ["questions"] = new List<object?> { "Should we use PostgreSQL or SQLite?", "Enable Redis cache?" }
        });

        var result1 = await tool.InvokeAsync(dispatched1.Arguments);
        Assert.Equal("Asked 2 questions: Should we use PostgreSQL or SQLite?; Enable Redis cache?", result1?.ToString());

        // Case 2: Model passes a single string for questions parameter
        var dispatched2 = dispatcher.NormalizeCall("ask_questions", new Dictionary<string, object?>
        {
            ["questions"] = "Do you want to proceed?"
        });

        var result2 = await tool.InvokeAsync(dispatched2.Arguments);
        Assert.Equal("Asked 1 questions: Do you want to proceed?", result2?.ToString());
    }

    [Fact]
    public async Task StringifiedJson_NestedInQuestionItem_ParsedCorrectly()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (List<TestQuestionItem> questions) => $"Prompt: {questions[0].Prompt}, Options: {string.Join(", ", questions[0].Options!)}",
            "ask");

        dispatcher.RegisterTool(tool);

        // Model passes options as stringified JSON: "[\"Option A\", \"Option B\"]"
        var dispatched = dispatcher.NormalizeCall("ask", new Dictionary<string, object?>
        {
            ["questions"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["prompt"] = "Select deployment target",
                    ["options"] = "[\"AWS\", \"Azure\", \"GCP\"]"
                }
            }
        });

        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal("Prompt: Select deployment target, Options: AWS, Azure, GCP", result?.ToString());
    }

    [Fact]
    public void ToolMapping_MutualAliasResolution_WorksInBothDirections()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["run_command"] = new ToolMapping(
                    aliases: ["bash"],
                    parameterAliases: new Dictionary<string, IEnumerable<string>> { ["command"] = ["cmd"] }),
                ["read_file"] = new ToolMapping(
                    aliases: ["read", "view_file"],
                    parameterAliases: new Dictionary<string, IEnumerable<string>> { ["path"] = ["filePath"] })
            }
        });

        // Developer registers tool as "bash"
        dispatcher.RegisterTool(AIFunctionFactory.Create((string command) => $"Ran: {command}", "bash"));

        // Model invokes "run_command" with parameter alias "cmd"
        var d1 = dispatcher.NormalizeCall("run_command", new Dictionary<string, object?> { ["cmd"] = "ls" });
        Assert.Equal("bash", d1.CanonicalToolName);
        Assert.Equal("ls", d1.Arguments["command"]);

        // Developer registers tool as "read"
        dispatcher.RegisterTool(AIFunctionFactory.Create((string path) => $"Read: {path}", "read"));

        // Model invokes "view_file" with parameter alias "filePath"
        var d2 = dispatcher.NormalizeCall("view_file", new Dictionary<string, object?> { ["filePath"] = "test.txt" });
        Assert.Equal("read", d2.CanonicalToolName);
        Assert.Equal("test.txt", d2.Arguments["path"]);
    }

    public record TestBatchTaskItem(string Action, string? Author = null, bool DryRun = false);
    public record TestCustomNoteItem(string Content, int Priority = 0);
    public record TestMultiFieldItem(string Title, string Summary, string Details);

    [Fact]
    public async Task PropagateUnmappedParameters_AutoDiscoversGenericParametersIntoCollectionItems()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (List<TestBatchTaskItem> tasks) => $"Tasks: {tasks.Count}, Author: {tasks[0].Author}/{tasks[1].Author}, DryRun: {tasks[0].DryRun}/{tasks[1].DryRun}",
            "batch_run");

        dispatcher.RegisterTool(tool);

        // Model passes author and dry_run outside of collection items
        var dispatched = dispatcher.NormalizeCall("batch_run", new Dictionary<string, object?>
        {
            ["author"] = "antigravity",
            ["dry_run"] = "true",
            ["tasks"] = new List<object?>
            {
                new Dictionary<string, object?> { ["action"] = "build" },
                new Dictionary<string, object?> { ["action"] = "deploy", ["author"] = "custom_override" } // should not be overwritten
            }
        });

        Assert.False(dispatched.Arguments.ContainsKey("author"));
        Assert.False(dispatched.Arguments.ContainsKey("dry_run"));

        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal("Tasks: 2, Author: antigravity/custom_override, DryRun: True/True", result?.ToString());
    }

    [Fact]
    public async Task PropagateUnmappedParameters_CanBeDisabledViaOptions()
    {
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            AutoPropagateCollectionParameters = false
        });
        var tool = AIFunctionFactory.Create(
            (List<TestBatchTaskItem> tasks) => $"Tasks: {tasks.Count}, Author: {tasks[0].Author}",
            "batch_run");

        dispatcher.RegisterTool(tool);

        var dispatched = dispatcher.NormalizeCall("batch_run", new Dictionary<string, object?>
        {
            ["author"] = "antigravity",
            ["tasks"] = new List<object?>
            {
                new Dictionary<string, object?> { ["action"] = "build" }
            }
        });

        // Still at root because auto-propagation is disabled
        Assert.True(dispatched.Arguments.ContainsKey("author"));
        var tasksList = Assert.IsAssignableFrom<IEnumerable<object?>>(dispatched.Arguments["tasks"]);
        var firstItem = (IDictionary<string, object?>)tasksList.Cast<object>().First();
        Assert.False(firstItem.ContainsKey("author"));
    }

    [Fact]
    public async Task FindPrimaryStringProperty_AutoDiscoversSingleStringProperty_WithoutWellKnownNames()
    {
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            // Clear default well-known names to prove pure schema auto-discovery
            DefaultPrimaryStringProperties = new List<string>()
        });

        var tool = AIFunctionFactory.Create(
            (List<TestCustomNoteItem> notes) => $"Notes: {string.Join(", ", notes.Select(n => $"{n.Content}(P{n.Priority})"))}",
            "add_notes");

        dispatcher.RegisterTool(tool);

        // Model passes raw strings for notes, where TestCustomNoteItem has a single string property 'Content'
        var dispatched = dispatcher.NormalizeCall("add_notes", new Dictionary<string, object?>
        {
            ["notes"] = new List<object?> { "Buy milk", "Walk dog" }
        });

        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal("Notes: Buy milk(P0), Walk dog(P0)", result?.ToString());
    }

    [Fact]
    public async Task FindPrimaryStringProperty_UsesExplicitToolMappingConfiguration()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["record_items"] = new ToolMapping()
                    .SetPrimaryCollectionProperty("items[]", "summary")
            }
        });

        var tool = AIFunctionFactory.Create(
            (List<TestMultiFieldItem> items) => $"Item summary: {items[0].Summary}",
            "record_items");

        dispatcher.RegisterTool(tool);

        // Model passes a single string for items
        var dispatched = dispatcher.NormalizeCall("record_items", new Dictionary<string, object?>
        {
            ["items"] = "Important summary"
        });

        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal("Item summary: Important summary", result?.ToString());
    }
}
