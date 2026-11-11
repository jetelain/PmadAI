using System.Text.Json;
using Microsoft.Extensions.AI;
using Pmad.AI.Common;
using Pmad.AI.Healing;
using Pmad.AI.Tools;

namespace Pmad.AI.Tests;

public class LenientToolDispatcherEnhancementsTests
{
    public record TestEditItem(string OldText, string NewText, bool ReplaceAll = false);
    public record TestQuestionItem(string Prompt, string? Rationale = null, List<string>? Options = null);

    [Fact]
    public void CustomToolMapping_ComputesLimitFromEndLineAndStartLine()
    {
        var dispatcher = new LenientToolDispatcher(
        new ToolDispatcherConfiguration
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
    public async Task StringifiedJson_TruncatedArray_RepairMissingClosingBracket()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (List<TestQuestionItem> questions) => $"Asked {questions.Count}: {string.Join("; ", questions.Select(q => q.Prompt))}",
            "ask");

        dispatcher.RegisterTool(tool);

        // Model emits `questions` as a stringified JSON array but truncates it, dropping the final `]`.
        // Before the repair this value would be treated as a lone string and wrapped into a single bogus QuestionItem.
        var dispatched = dispatcher.NormalizeCall("ask", new Dictionary<string, object?>
        {
            ["questions"] = "[{\"prompt\": \"Q1: Use SQLite or Postgres?\", \"options\": [\"SQLite\", \"Postgres\"]}, {\"prompt\": \"Q2: Enable caching?\"}"
        });

        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal("Asked 2: Q1: Use SQLite or Postgres?; Q2: Enable caching?", result?.ToString());
    }

    [Fact]
    public async Task StringifiedJson_TruncatedArray_UnderminatedString_Repaired()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (List<TestQuestionItem> questions) => $"Asked {questions.Count}: {string.Join("; ", questions.Select(q => q.Prompt))}",
            "ask");

        dispatcher.RegisterTool(tool);

        // Truncated mid-string: the last prompt is cut off and both the string and array closers are missing.
        var dispatched = dispatcher.NormalizeCall("ask", new Dictionary<string, object?>
        {
            ["questions"] = "[{\"prompt\": \"Q1: Pick a DB\"}, {\"prompt\": \"Q2: Enable caching"
        });

        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal("Asked 2: Q1: Pick a DB; Q2: Enable caching", result?.ToString());
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
        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
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

    [Fact]
    public async Task PropagateUnmappedParameters_ToStringifiedJsonArray_ParsesAndPropagates()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (List<TestBatchTaskItem> tasks) => $"Tasks: {tasks.Count}, Author: {tasks[0].Author}/{tasks[1].Author}",
            "batch_run");

        dispatcher.RegisterTool(tool);

        // Model passes tasks as a stringified JSON array and author at root
        var dispatched = dispatcher.NormalizeCall("batch_run", new Dictionary<string, object?>
        {
            ["author"] = "antigravity",
            ["tasks"] = "[{\"action\": \"build\"}, {\"action\": \"test\"}]"
        });

        Assert.False(dispatched.Arguments.ContainsKey("author"));
        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal("Tasks: 2, Author: antigravity/antigravity", result?.ToString());
    }

    public record TestPromptItem(string Prompt, string? Category = null);

    [Fact]
    public async Task PropagateUnmappedParameters_ToRawStringArray_WrapsAndPropagates()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (List<TestPromptItem> prompts) => $"Prompts: {prompts.Count}, P0: {prompts[0].Prompt}({prompts[0].Category}), P1: {prompts[1].Prompt}({prompts[1].Category})",
            "ask_all");

        dispatcher.RegisterTool(tool);

        // Model passes raw strings in prompts array, and category at root
        var dispatched = dispatcher.NormalizeCall("ask_all", new Dictionary<string, object?>
        {
            ["category"] = "general",
            ["prompts"] = new List<object?> { "Question 1?", "Question 2?" }
        });

        Assert.False(dispatched.Arguments.ContainsKey("category"));
        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal("Prompts: 2, P0: Question 1?(general), P1: Question 2?(general)", result?.ToString());
    }

    [Fact]
    public async Task PropagateUnmappedParameters_ReadOnlyDictionary_DoesNotThrow()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (List<TestBatchTaskItem> tasks) => $"Tasks: {tasks.Count}, Author: {tasks[0].Author}",
            "batch_run");

        dispatcher.RegisterTool(tool);

        var innerDict = new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(
            new Dictionary<string, object?> { ["action"] = "compile" });

        var dispatched = dispatcher.NormalizeCall("batch_run", new Dictionary<string, object?>
        {
            ["author"] = "antigravity",
            ["tasks"] = new List<object?> { innerDict }
        });

        Assert.False(dispatched.Arguments.ContainsKey("author"));
        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal("Tasks: 1, Author: antigravity", result?.ToString());
    }

    public record TestNullablePromptItem(string? Text = null, int Priority = 1);

    [Fact]
    public async Task FindPrimaryStringProperty_NullableStringProperty_AutoDiscovered()
    {
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            DefaultPrimaryStringProperties = new List<string>() // no hardcoded fallbacks
        });

        var tool = AIFunctionFactory.Create(
            (List<TestNullablePromptItem> items) => $"Items: {items.Count}, Text: {items[0].Text}",
            "process_items");

        dispatcher.RegisterTool(tool);

        // TestNullablePromptItem has a single string property 'Text' which is nullable (string?)
        var dispatched = dispatcher.NormalizeCall("process_items", new Dictionary<string, object?>
        {
            ["items"] = new List<object?> { "Hello nullable world" }
        });

        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal("Items: 1, Text: Hello nullable world", result?.ToString());
    }

    [Fact]
    public void UnpackSingleItemArrays_MultiItemArray_NotUnpacked()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (string path, string old_text, string new_text) => $"Single edit on {path}: {old_text}->{new_text}",
            "replace_text");

        dispatcher.RegisterTool(tool);

        // Model passes edits array of 2 elements to a tool that expects flat parameters
        var dispatched = dispatcher.NormalizeCall("replace_text", new Dictionary<string, object?>
        {
            ["path"] = "README.md",
            ["edits"] = new List<object?>
            {
                new Dictionary<string, object?> { ["old_text"] = "foo", ["new_text"] = "bar" },
                new Dictionary<string, object?> { ["old_text"] = "alpha", ["new_text"] = "beta" }
            }
        });

        // edits array should NOT be unpacked because count > 1
        Assert.True(dispatched.Arguments.ContainsKey("edits"));
        Assert.False(dispatched.Arguments.ContainsKey("old_text"));
    }

    [Fact]
    public async Task UnpackSingleItemArrays_StringifiedJsonArray_UnpacksSuccessfully()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (string path, string old_text, string new_text) => $"Single edit on {path}: {old_text}->{new_text}",
            "replace_text");

        dispatcher.RegisterTool(tool);

        var dispatched = dispatcher.NormalizeCall("replace_text", new Dictionary<string, object?>
        {
            ["path"] = "README.md",
            ["edits"] = "[{\"old_text\": \"hello\", \"new_text\": \"world\"}]"
        });

        Assert.False(dispatched.Arguments.ContainsKey("edits"));
        Assert.Equal("hello", dispatched.Arguments["old_text"]);
        Assert.Equal("world", dispatched.Arguments["new_text"]);

        var result = await tool.InvokeAsync(dispatched.Arguments);
        Assert.Equal("Single edit on README.md: hello->world", result?.ToString());
    }

    // ---------------------------------------------------------------------
    // NightCodeForge parity: cases ported from NightCodeForge.Agents.Tests/
    // LenientToolDispatcherTests.cs and NightCodeForge.Core.Tests/
    // QuestionItemParserTests.cs to prove the PmadAI dispatcher supports the
    // same malformed / non-canonical tool-call shapes.
    // ---------------------------------------------------------------------
    private LenientToolDispatcher CreateAskDispatcher(out AIFunction askTool)
    {
        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["ask"] = new ToolMapping()
                    .SetPrimaryCollectionProperty("questions[]", "prompt")
                    .AddNestedParameterAlias("questions[]", "options", "optionsJson", "choices")
            }
        }); 
        var ask = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) => $"Asked {questions.Count}: {string.Join("; ", questions.Select(q => q.Prompt))}",
            "ask");
        askTool = ask;
        dispatcher.RegisterTool(ask);
        return dispatcher;
    }

    [Fact]
    public async Task NCF_AskQuestionItems_ParameterAlias_RenamesToQuestions()
    {
        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["ask"] = new ToolMapping(parameterAliases: new Dictionary<string, IEnumerable<string>>
                    { ["questions"] = ["question_items", "questionItems"] })
            }
        });
        var tool = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) => $"Asked {questions.Count}: {string.Join("; ", questions.Select(q => q.Prompt))}",
            "ask");
        dispatcher.RegisterTool(tool);

        // NightCodeForge: { "question_items": [...] } -> questions
        var d1 = dispatcher.NormalizeCall("ask", "{\"question_items\": [{\"prompt\": \"Question 1\"}]}");
        Assert.True(d1.Arguments.ContainsKey("questions"));
        Assert.False(d1.Arguments.ContainsKey("question_items"));

        // NightCodeForge: { "items": [...] } -> questions
        dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["ask"] = new ToolMapping(parameterAliases: new Dictionary<string, IEnumerable<string>>
                    { ["questions"] = ["question_items", "items"] })
            }
        });
        dispatcher.RegisterTool(tool);
        var d2 = dispatcher.NormalizeCall("ask", "{\"items\": [{\"prompt\": \"Question 2\"}]}");
        Assert.True(d2.Arguments.ContainsKey("questions"));
        Assert.False(d2.Arguments.ContainsKey("items"));

        var result = await tool.InvokeAsync(d1.Arguments);
        Assert.Equal("Asked 1: Question 1", result?.ToString());
    }

    [Fact]
    public async Task NCF_AskWithOptionsJson_NestedAlias_ParsesOptions()
    {
        var dispatcher = CreateAskDispatcher(out var tool);

        // NightCodeForge: nested "optionsJson" alias -> "options" (stringified JSON array)
        var d = dispatcher.NormalizeCall("ask",
            "{\"questions\": [{\"prompt\": \"Select caching layer\", \"optionsJson\": \"[\\\"Redis\\\", \\\"Memory\\\"]\"}]}");

        var q = Assert.IsAssignableFrom<IEnumerable<object?>>(d.Arguments["questions"]).Cast<IDictionary<string, object?>>().Single();
        var options = Assert.IsAssignableFrom<IEnumerable<object?>>(q["options"]).Cast<object?>().Select(o => o?.ToString()).ToList();
        Assert.Equal("Redis", options[0]);
        Assert.Equal("Memory", options[1]);
        Assert.False(q.ContainsKey("optionsJson"));

        var result = await tool.InvokeAsync(d.Arguments);
        Assert.Equal("Asked 1: Select caching layer", result?.ToString());
    }

    [Fact]
    public async Task NCF_AskWithStringifiedSingleQuestionObject_ParsesAsList()
    {
        var dispatcher = CreateAskDispatcher(out var tool);

        // NightCodeForge: "questions" is a stringified SINGLE object (not array) -> wrapped into 1 item
        var d = dispatcher.NormalizeCall("ask",
            "{\"questions\": \"{\\\"prompt\\\": \\\"Confirm deploy to staging?\\\", \\\"options\\\": [\\\"Yes\\\", \\\"No\\\"]}\"}");

        var questions = Assert.IsAssignableFrom<IEnumerable<object?>>(d.Arguments["questions"]).Cast<object?>().ToList();
        Assert.Single(questions);
        var result = await tool.InvokeAsync(d.Arguments);
        Assert.Equal("Asked 1: Confirm deploy to staging?", result?.ToString());
    }

    [Fact]
    public async Task NCF_AskWithTruncatedRawArgumentsJson_RepairedAtTopLevel()
    {
        var dispatcher = CreateAskDispatcher(out var tool);

        // NightCodeForge: raw arguments JSON is itself truncated (missing "]}" at the end).
        var d = dispatcher.NormalizeCall("ask", "{\"questions\": [{\"prompt\": \"Proceed?\"}");

        var result = await tool.InvokeAsync(d.Arguments);
        Assert.Equal("Asked 1: Proceed?", result?.ToString());
    }

    [Fact]
    public async Task NCF_AskWithTruncatedDanglingKey_RepairedAtTopLevel()
    {
        var dispatcher = CreateAskDispatcher(out var tool);

        // NightCodeForge: truncated mid-key ("ration") with no value -> dangling key dropped on repair.
        var d = dispatcher.NormalizeCall("ask",
            "{\"questions\": [{\"prompt\": \"Which DB?\", \"options\": [\"SQLite\", \"Postgres\"], \"ration");

        var result = await tool.InvokeAsync(d.Arguments);
        Assert.Equal("Asked 1: Which DB?", result?.ToString());
    }

    [Fact]
    public async Task NCF_AskWithTruncatedNestedOptions_Repaired()
    {
        var dispatcher = CreateAskDispatcher(out var tool);

        // NightCodeForge: inner "options" array truncated (missing "]") -> repaired and parsed.
        var d = dispatcher.NormalizeCall("ask",
            "{\"questions\": [{\"prompt\": \"Select target environment\", \"options\": \"[\\\"Staging\\\", \\\"Prod\\\"\"}]}");

        var q = Assert.IsAssignableFrom<IEnumerable<object?>>(d.Arguments["questions"]).Cast<IDictionary<string, object?>>().Single();
        var options = Assert.IsAssignableFrom<IEnumerable<object?>>(q["options"]).Cast<object?>().Select(o => o?.ToString()).ToList();
        Assert.Equal("Staging", options[0]);
        Assert.Equal("Prod", options[1]);

        var result = await tool.InvokeAsync(d.Arguments);
        Assert.Equal("Asked 1: Select target environment", result?.ToString());
    }

    [Fact]
    public async Task NCF_AskWithBoxedJsonElementString_ParsesAndInvokes()
    {
        var dispatcher = CreateAskDispatcher(out var tool);

        // NightCodeForge: "questions" arrives as a boxed JsonElement holding a JSON string.
        using var doc = JsonDocument.Parse("[{\"prompt\": \"Choose environment\", \"options\": [\"Dev\", \"Prod\"]}]");
        var d = dispatcher.NormalizeCall("ask", new Dictionary<string, object?>
        {
            ["questions"] = doc.RootElement.Clone()
        });

        var result = await tool.InvokeAsync(d.Arguments);
        Assert.Equal("Asked 1: Choose environment", result?.ToString());
    }

    [Fact]
    public void NCF_TryRepairTruncatedJson_SharedHelper_ClosesStructuresAndDropsDanglingKeys()
    {
        // Nested array missing its closing ']' (the original NighCodeForge repro).
        Assert.Equal(
            "[{\"prompt\":\"Q1\",\"options\":[\"A\",\"B\"]},{\"prompt\":\"Q2\",\"options\":[\"C\",\"D\"]}]",
            JsonRepairUtilities.TryRepairTruncatedJson(
                "[{\"prompt\":\"Q1\",\"options\":[\"A\",\"B\"]},{\"prompt\":\"Q2\",\"options\":[\"C\",\"D\"]}"));

        // Full payload truncated before the closing "]}" -> both closed.
        Assert.Equal(
            "{\"questions\":[{\"prompt\":\"Proceed?\"}]}",
            JsonRepairUtilities.TryRepairTruncatedJson(
                "{\"questions\":[{\"prompt\":\"Proceed?\"}"));

        // Unterminated value string is closed in place.
        Assert.Equal(
            "[{\"prompt\":\"Q1\"},{\"prompt\":\"Q2: Enable caching\"}]",
            JsonRepairUtilities.TryRepairTruncatedJson(
                "[{\"prompt\":\"Q1\"},{\"prompt\":\"Q2: Enable caching"));

        // Dangling key (no value) is dropped.
        Assert.Equal(
            "{\"questions\":[{\"prompt\":\"Which DB?\",\"options\":[\"SQLite\",\"Postgres\"]}]}",
            JsonRepairUtilities.TryRepairTruncatedJson(
                "{\"questions\":[{\"prompt\":\"Which DB?\",\"options\":[\"SQLite\",\"Postgres\"],\"ration"));

        // A key truncated mid-string is dropped (safe cut back to the previous entry).
        Assert.Equal(
            "{\"questions\":[{\"prompt\":\"Which DB?\"}]}",
            JsonRepairUtilities.TryRepairTruncatedJson(
                "{\"questions\":[{\"prompt\":\"Which DB?\",\"rat"));

        // Already-balanced documents are left untouched (returns null).
        Assert.Null(JsonRepairUtilities.TryRepairTruncatedJson("{\"a\":[1,2,3]}"));
        Assert.Null(JsonRepairUtilities.TryRepairTruncatedJson("[1,2,3]"));
    }

    [Fact]
    public void NormalizeCall_NullValuedCanonicalKey_OverwrittenByNonNullAlias()
    {
        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["merge_task"] = new ToolMapping(
                    parameterAliases: new Dictionary<string, IEnumerable<string>>
                    {
                        ["merge_mode"] = ["mergeMode", "mode"]
                    })
            }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (int task_id, string? merge_mode = null) => $"{task_id}:{merge_mode}",
            "merge_task"));

        // Case 1: JSON string with canonical key = null and alias = "Automatic"
        var d1 = dispatcher.NormalizeCall("merge_task", "{\"task_id\": 5, \"merge_mode\": null, \"mergeMode\": \"Automatic\"}");
        Assert.Equal(5, d1.Arguments["task_id"]);
        Assert.Equal("Automatic", d1.Arguments["merge_mode"]);

        // Case 2: Dictionary with JsonElement(Null) on canonical key and non-null alias
        using var nullDoc = JsonDocument.Parse("null");
        var d2 = dispatcher.NormalizeCall("merge_task", new Dictionary<string, object?>
        {
            ["task_id"] = 5,
            ["merge_mode"] = nullDoc.RootElement.Clone(),
            ["mergeMode"] = "Automatic"
        });
        Assert.Equal("Automatic", d2.Arguments["merge_mode"]);

        // Case 3: When both are null, JsonValueKind.Null is unwrapped to null
        var d3 = dispatcher.NormalizeCall("merge_task", new Dictionary<string, object?>
        {
            ["task_id"] = 5,
            ["merge_mode"] = nullDoc.RootElement.Clone()
        });
        Assert.Null(d3.Arguments["merge_mode"]);

        // Case 4: Schema-only casing difference (without explicit ParameterAliases) where canonical is null and variant is non-null
        var schemaOnlyDispatcher = new LenientToolDispatcher();
        schemaOnlyDispatcher.RegisterTool(AIFunctionFactory.Create(
            (int task_id, string? merge_mode = null) => $"{task_id}:{merge_mode}",
            "merge_task"));

        var d4 = schemaOnlyDispatcher.NormalizeCall("merge_task", new Dictionary<string, object?>
        {
            ["task_id"] = 5,
            ["merge_mode"] = nullDoc.RootElement.Clone(),
            ["mergeMode"] = "Automatic"
        });
        Assert.Equal("Automatic", d4.Arguments["merge_mode"]);
        Assert.False(d4.Arguments.ContainsKey("mergeMode"));
    }

    public record TestPriorityUpdate(int TaskId, int Priority);

    [Fact]
    public async Task NormalizeCall_TopLevelJsonArrayPayload_MapsToSchemaArrayParameter()
    {
        // 1. Direct schema array discovery (ask -> questions)
        var askDispatcher = new LenientToolDispatcher();
        var askTool = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) => $"Asked {questions.Count}: {string.Join("; ", questions.Select(q => q.Prompt))}",
            "ask");
        askDispatcher.RegisterTool(askTool);

        var d1 = askDispatcher.NormalizeCall("ask", "[{\"prompt\": \"Q1\"}, {\"prompt\": \"Q2\"}]");
        var r1 = await askTool.InvokeAsync(d1.Arguments);
        Assert.Equal("Asked 2: Q1; Q2", r1?.ToString());

        // 2. Truncated top-level JSON array repaired by SanitizeArgumentsJson
        var d2 = askDispatcher.NormalizeCall("ask", "[{\"prompt\": \"Q1\"}, {\"prompt\": \"Q2\"");
        var r2 = await askTool.InvokeAsync(d2.Arguments);
        Assert.Equal("Asked 2: Q1; Q2", r2?.ToString());

        // 3. Top-level array mapped via "items" alias in ParameterAliases + nested snake_case -> camelCase normalization
        var reworkDispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["rework_priorities"] = new ToolMapping(
                    parameterAliases: new Dictionary<string, IEnumerable<string>>
                    {
                        ["updates"] = ["items", "priorities"]
                    })
            }
        });
        var reworkTool = AIFunctionFactory.Create(
            (IReadOnlyList<TestPriorityUpdate> updates) => string.Join(",", updates.Select(u => $"{u.TaskId}={u.Priority}")),
            "rework_priorities");
        reworkDispatcher.RegisterTool(reworkTool);

        var d3 = reworkDispatcher.NormalizeCall("rework_priorities", "[{\"task_id\": 1, \"priority\": 2}, {\"task_id\": \"#3\", \"priority\": \"4\"}]");
        var r3 = await reworkTool.InvokeAsync(d3.Arguments);
        Assert.Equal("1=2,3=4", r3?.ToString());
    }

    [Fact]
    public async Task NormalizeCall_RecursiveScalarArrayElementCoercion_AndHashPrefixedIntegers()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (int task_id, int[]? depends_on_ids = null) => $"{task_id}:[{string.Join(",", depends_on_ids ?? [])}]",
            "create_task");
        dispatcher.RegisterTool(tool);

        // Top-level "#42" integer and array elements [1, "#3", "7"]
        var d = dispatcher.NormalizeCall("create_task", "{\"task_id\": \"#42\", \"depends_on_ids\": [1, \"#3\", \"7\"]}");
        Assert.Equal(42, d.Arguments["task_id"]);

        var result = await tool.InvokeAsync(d.Arguments);
        Assert.Equal("42:[1,3,7]", result?.ToString());
    }

    [Fact]
    public async Task NormalizeCall_NestedObjectAndStringifiedArrayViaJsonSchema_WithoutExplicitAlias()
    {
        // Pure schema-driven: no explicit NestedParameterAliases configured on dispatcher!
        var dispatcher = new LenientToolDispatcher();
        var askTool = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) =>
                $"Prompt: {questions[0].Prompt}, Rationale: {questions[0].Rationale}, Options: {string.Join("|", questions[0].Options ?? [])}",
            "ask");
        dispatcher.RegisterTool(askTool);

        // Model emits snake_case / options_json / optionsJson inside questions[]
        var d1 = dispatcher.NormalizeCall("ask",
            "{\"questions\": [{\"prompt\": \"Choose DB\", \"rationale\": \"Storage\", \"options_json\": \"[\\\"A\\\", \\\"B\\\"]\"}]}");
        var r1 = await askTool.InvokeAsync(d1.Arguments);
        Assert.Equal("Prompt: Choose DB, Rationale: Storage, Options: A|B", r1?.ToString());

        var d2 = dispatcher.NormalizeCall("ask",
            "{\"questions\": [{\"Prompt\": \"Choose DB\", \"Rationale\": \"Storage\", \"optionsJson\": \"[\\\"X\\\", \\\"Y\\\"]\"}]}");
        var r2 = await askTool.InvokeAsync(d2.Arguments);
        Assert.Equal("Prompt: Choose DB, Rationale: Storage, Options: X|Y", r2?.ToString());
    }

    [Fact]
    public async Task AutoPromote_BlankCollectionPlaceholders_WithNullPropertiesOrJsonElement_PromotesFlatShortcuts()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (string path, List<TestEditItem> edits) => $"Edited {path} with {edits.Count} chunk: {edits[0].OldText}->{edits[0].NewText} (all={edits[0].ReplaceAll})",
            "edit");
        dispatcher.RegisterTool(tool);

        // Case 1: Dictionary element with only null/blank properties: [{"oldText": null, "newText": ""}]
        var d1 = dispatcher.NormalizeCall("edit", new Dictionary<string, object?>
        {
            ["path"] = "Service.cs",
            ["edits"] = new List<object?>
            {
                new Dictionary<string, object?> { ["oldText"] = null, ["newText"] = "   " }
            },
            ["old_text"] = "before1",
            ["new_text"] = "after1",
            ["replace_all"] = true
        });
        var r1 = await tool.InvokeAsync(d1.Arguments);
        Assert.Equal("Edited Service.cs with 1 chunk: before1->after1 (all=True)", r1?.ToString());

        // Case 2: JsonElement array "[{}]" or "[{\"oldText\": null}]"
        using var doc = JsonDocument.Parse("[{\"oldText\": null, \"newText\": null}]");
        var d2 = dispatcher.NormalizeCall("edit", new Dictionary<string, object?>
        {
            ["path"] = "Service.cs",
            ["edits"] = doc.RootElement.Clone(),
            ["old_text"] = "before2",
            ["new_text"] = "after2"
        });
        var r2 = await tool.InvokeAsync(d2.Arguments);
        Assert.Equal("Edited Service.cs with 1 chunk: before2->after2 (all=False)", r2?.ToString());
    }

    [Fact]
    public async Task LenientToolDispatchChatClient_NonStreaming_HasSameFallbackSafetyAsStreaming()
    {
        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Translations =
            [
                new ToolTranslation
                {
                    SourceTool = "grep_search",
                    TargetTool = "bash",
                    CommandTemplate = "rg -- {query}",
                    EscapeMethod = ParameterEscapeMethod.Bash
                }
            ]
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create((string command) => command, "bash"));

        // 1. Missing required translation parameter (throws KeyNotFoundException in NormalizeCall)
        // 2. Option injection starting with '-' (throws ArgumentException in NormalizeCall)
        var innerClient = new FakeSimpleChatClient((_, _, _) =>
        {
            var msg = new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent("call_1", "grep_search", new Dictionary<string, object?>()),
                new FunctionCallContent("call_2", "grep_search", new Dictionary<string, object?> { ["query"] = "--evil" })
            ]);
            return Task.FromResult(new ChatResponse(msg));
        });

        var client = new LenientToolDispatchChatClient(innerClient, dispatcher);
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "search")]);

        // Should not throw; raw tool calls should remain intact
        var calls = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();
        Assert.Equal(2, calls.Count);
        Assert.Equal("grep_search", calls[0].Name);
        Assert.Equal("grep_search", calls[1].Name);
    }

    [Fact]
    public async Task NormalizeCall_TopLevelJsonSuffixParameter_MapsToSchemaPropertyWithoutExplicitAlias()
    {
        var dispatcher = new LenientToolDispatcher();
        var askTool = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) => $"Asked {questions.Count}: {string.Join("; ", questions.Select(q => q.Prompt))}",
            "ask");
        dispatcher.RegisterTool(askTool);

        // Top-level "questions_json" and "questionsJson" mapped automatically to "questions"
        var d1 = dispatcher.NormalizeCall("ask", "{\"questions_json\": \"[{\\\"prompt\\\": \\\"Q1\\\"}, {\\\"prompt\\\": \\\"Q2\\\"}]\"}");
        var r1 = await askTool.InvokeAsync(d1.Arguments);
        Assert.Equal("Asked 2: Q1; Q2", r1?.ToString());

        var d2 = dispatcher.NormalizeCall("ask", "{\"questionsJson\": \"[{\\\"prompt\\\": \\\"Q3\\\"}]\"}");
        var r2 = await askTool.InvokeAsync(d2.Arguments);
        Assert.Equal("Asked 1: Q3", r2?.ToString());
    }

    [Fact]
    public async Task ToolCallHealing_DoesNotPrematurelyTruncateToolCallTag_WhenProseBeforeJsonContainsArrayBrackets()
    {
        var askTool = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) => $"Asked {questions.Count}: {questions[0].Prompt}",
            "ask");

        var content = "Let me check element [0] first.\n<tool_call>{\"name\": \"ask\", \"arguments\": [{\"prompt\": \"Which DB?\"}]}</tool_call>";
        var extracted = ToolCallHealingTransform.TryExtractToolCalls(
            ref content,
            out var calls,
            out var proseBefore,
            out _,
            options: new ToolCallHealingOptions { EnforceAvailableToolsGuardrail = false });

        Assert.True(extracted);
        Assert.Single(calls);
        Assert.Equal("ask", calls[0].Name);
        Assert.Equal("Let me check element [0] first.", proseBefore);

        var dispatcher = new LenientToolDispatcher();
        dispatcher.RegisterTool(askTool);
        var normalized = dispatcher.NormalizeCall(calls[0]);
        var res = await askTool.InvokeAsync(normalized.Arguments);
        Assert.Equal("Asked 1: Which DB?", res?.ToString());
    }

    [Fact]
    public async Task NormalizeCall_CommaSeparatedLongIntegerArray_Supports64BitAndHashPrefixes()
    {
        var dispatcher = new LenientToolDispatcher();
        var tool = AIFunctionFactory.Create(
            (long[] ids) => string.Join(",", ids),
            "process_ids");
        dispatcher.RegisterTool(tool);

        var d = dispatcher.NormalizeCall("process_ids", new Dictionary<string, object?>
        {
            ["ids"] = "#10, 5000000000, #9000000000"
        });
        var res = await tool.InvokeAsync(d.Arguments);
        Assert.Equal("10,5000000000,9000000000", res?.ToString());
    }

    [Fact]
    public async Task NormalizeCall_CorruptedArrayElementsWithRogueDelimiters_ExtractsElementsCorrectly()
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions { Telemetry = telemetry });
        var askTool = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) => $"Count: {questions.Count}, Q1: {questions[0].Prompt} ({string.Join(",", questions[0].Options ?? [])}), Q2: {questions[1].Prompt}",
            "ask");
        dispatcher.RegisterTool(askTool);

        var corruptedJsonArray = "[{\"prompt\": \"Q1\", \"options\": [\"A\", \"B\"]}, \"options\": null}, {\"prompt\": \"Q2\"}]";
        var d = dispatcher.NormalizeCall("ask", new Dictionary<string, object?>
        {
            ["questions"] = corruptedJsonArray
        });

        var res = await askTool.InvokeAsync(d.Arguments);
        Assert.Equal("Count: 2, Q1: Q1 (A,B), Q2: Q2", res?.ToString());

        var qList = Assert.IsAssignableFrom<IList<object?>>(d.Arguments["questions"]);
        Assert.Equal(2, qList.Count);
        var item1 = Assert.IsAssignableFrom<IDictionary<string, object?>>(qList[0]);
        Assert.Equal("Q1", item1["prompt"]);
        var item2 = Assert.IsAssignableFrom<IDictionary<string, object?>>(qList[1]);
        Assert.Equal("Q2", item2["prompt"]);

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(1, snapshot.StringifiedJsonRepaired);
    }

    [Fact]
    public void NormalizeCall_ArrayShapedString_WithUnparseableGarbage_DoesNotWrapRawStringIntoScalarProperty()
    {
        var dispatcher = new LenientToolDispatcher();
        var askTool = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) => questions.Count.ToString(),
            "ask");
        dispatcher.RegisterTool(askTool);

        // Input starts with '[' but contains no valid matching question items
        var invalidArray = "[invalid: syntax, middle {garbage}]";
        var d = dispatcher.NormalizeCall("ask", new Dictionary<string, object?>
        {
            ["questions"] = invalidArray
        });

        // Verify it was NOT wrapped into [ { prompt: "[invalid...]" } ]
        var argVal = d.Arguments["questions"];
        Assert.True(argVal is string, "Array-shaped corrupted string should not be wrapped into scalar primary property dictionary");
        Assert.Equal(invalidArray, argVal);
    }

    [Fact]
    public void NormalizeCall_DoubleNestedJsonStringInArrayParameter_TrailingOrMiddleGarbage_DoesNotWrapRawStringAsScalar()
    {
        var dispatcher = new LenientToolDispatcher();
        var askTool = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) => questions.Count.ToString(),
            "ask");
        dispatcher.RegisterTool(askTool);

        // A stringified array containing unmatched items with middle/trailing garbage
        var corruptedArrayString = "[{\"unrecognized\": 123}, \"options\": null}] trailing garbage";
        var d = dispatcher.NormalizeCall("ask", new Dictionary<string, object?>
        {
            ["questions"] = corruptedArrayString
        });

        // Verify it was NOT wrapped into [ { prompt: "[{...}] trailing garbage" } ]
        var argVal = d.Arguments["questions"];
        if (argVal is IList<object?> list && list.Count > 0 && list[0] is IDictionary<string, object?> dict)
        {
            Assert.False(dict.TryGetValue("prompt", out var pVal) && pVal?.ToString() == corruptedArrayString,
                "Dispatcher should not wrap raw malformed array string into prompt property");
        }
        else
        {
            Assert.Equal(corruptedArrayString, argVal);
        }
    }

    [Fact]
    public async Task NormalizeCall_DoubleNestedJsonStringInArrayParameter_ExtractsValidItems()
    {
        var dispatcher = new LenientToolDispatcher();
        var askTool = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) => $"Count: {questions.Count}, Q1: {questions[0].Prompt}, Q2: {questions[1].Prompt}",
            "ask");
        dispatcher.RegisterTool(askTool);

        // Stringified tool-call JSON containing stringified array with middle corruption
        var rawJsonCall = "{\"questions\": \"[{\\\"prompt\\\": \\\"Q1\\\"}, \\\"options\\\": null}, {\\\"prompt\\\": \\\"Q2\\\"}]\"}";
        var d = dispatcher.NormalizeCall("ask", rawJsonCall);

        var res = await askTool.InvokeAsync(d.Arguments);
        Assert.Equal("Count: 2, Q1: Q1, Q2: Q2", res?.ToString());
    }

    [Fact]
    public async Task NormalizeCall_CleanScalarString_WrapsIntoPrimaryProperty()
    {
        var dispatcher = new LenientToolDispatcher();
        var askTool = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) => $"Count: {questions.Count}, Prompt: {questions[0].Prompt}",
            "ask");
        dispatcher.RegisterTool(askTool);

        var d = dispatcher.NormalizeCall("ask", new Dictionary<string, object?>
        {
            ["questions"] = "Just a single question prompt"
        });

        var res = await askTool.InvokeAsync(d.Arguments);
        Assert.Equal("Count: 1, Prompt: Just a single question prompt", res?.ToString());

        var qList = Assert.IsAssignableFrom<IList<object?>>(d.Arguments["questions"]);
        Assert.Single(qList);
        var item = Assert.IsAssignableFrom<IDictionary<string, object?>>(qList[0]);
        Assert.Equal("Just a single question prompt", item["prompt"]);
    }

    [Fact]
    public async Task NormalizeCall_HallucinatedMiddleDelimiterAndOrphanProperties_ExtractsBothItems()
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions { Telemetry = telemetry });
        var askTool = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) => $"Count: {questions.Count}, Q1: {questions[0].Prompt}, Q2: {questions[1].Prompt}",
            "ask");
        dispatcher.RegisterTool(askTool);

        var corrupted = "[{\"prompt\": \"The spec asks the GUI to EDIT fields...\", \"rationale\": \"some reason\", \"options\": [\"opt1\", \"opt2\"]}, \"options\": null}, {\"rationale\": \"another reason\", \"options\": null, \"prompt\": \"Second question\"}]";
        var d = dispatcher.NormalizeCall("ask", new Dictionary<string, object?>
        {
            ["questions"] = corrupted
        });

        var res = await askTool.InvokeAsync(d.Arguments);
        Assert.Equal("Count: 2, Q1: The spec asks the GUI to EDIT fields..., Q2: Second question", res?.ToString());
    }

    [Fact]
    public async Task NormalizeCall_CorruptedStringifiedArray_WithRootParameterPropagationAndPromotionEnabled_PreservesItemsAndPropagates()
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions { Telemetry = telemetry });
        var askTool = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) =>
                $"Count: {questions.Count}, Q1: {questions[0].Prompt} ({questions[0].Rationale}), Q2: {questions[1].Prompt} ({questions[1].Rationale})",
            "ask");
        dispatcher.RegisterTool(askTool);

        // Model passes both a middle-corrupted stringified array AND a shared root "rationale" property.
        // PromoteFlattenedParametersToArrays must not overwrite "questions", and PropagateUnmappedParametersToCollections
        // must propagate "rationale" into the extracted items.
        var d = dispatcher.NormalizeCall("ask", new Dictionary<string, object?>
        {
            ["questions"] = "[{\"prompt\": \"Q1\"}, \"options\": null}, {\"prompt\": \"Q2\"}]",
            ["rationale"] = "Shared context"
        });

        Assert.False(d.Arguments.ContainsKey("rationale"));
        var res = await askTool.InvokeAsync(d.Arguments);
        Assert.Equal("Count: 2, Q1: Q1 (Shared context), Q2: Q2 (Shared context)", res?.ToString());
        Assert.Equal(1, telemetry.CreateSnapshot().StringifiedJsonRepaired);
    }

    [Fact]
    public async Task NormalizeCall_CommaSeparatedJsonObjectsWithoutOuterBrackets_ExtractsAllItems()
    {
        var dispatcher = new LenientToolDispatcher();
        var askTool = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) => $"Count: {questions.Count}, Q1: {questions[0].Prompt}, Q2: {questions[1].Prompt}",
            "ask");
        dispatcher.RegisterTool(askTool);

        // Multiple objects emitted without outer '[' and ']' brackets
        var d = dispatcher.NormalizeCall("ask", new Dictionary<string, object?>
        {
            ["questions"] = "{\"prompt\": \"First question?\"}, {\"prompt\": \"Second question?\"}"
        });

        var res = await askTool.InvokeAsync(d.Arguments);
        Assert.Equal("Count: 2, Q1: First question?, Q2: Second question?", res?.ToString());
    }

    [Fact]
    public void NormalizeCall_ObjectShapedString_WithUnparseableGarbage_DoesNotWrapRawStringIntoScalarProperty()
    {
        var dispatcher = new LenientToolDispatcher();
        var askTool = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) => questions.Count.ToString(),
            "ask");
        dispatcher.RegisterTool(askTool);

        var invalidObject = "{invalid: syntax, middle}";
        var d = dispatcher.NormalizeCall("ask", new Dictionary<string, object?>
        {
            ["questions"] = invalidObject
        });

        Assert.Equal(invalidObject, d.Arguments["questions"]);
    }

    [Fact]
    public async Task NormalizeCall_ArrayContainingStringifiedJsonObjects_ParsesObjectsInsteadOfWrappingIntoPrompt()
    {
        var dispatcher = new LenientToolDispatcher();
        var askTool = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) =>
                $"Count: {questions.Count}, Q1: {questions[0].Prompt} ({string.Join(",", questions[0].Options ?? [])}), Q2: {questions[1].Prompt}",
            "ask");
        dispatcher.RegisterTool(askTool);

        var d = dispatcher.NormalizeCall("ask", new Dictionary<string, object?>
        {
            ["questions"] = new List<object?>
            {
                "{\"prompt\": \"Q1\", \"options\": [\"A\", \"B\"]}",
                "{\"prompt\": \"Q2\"}"
            }
        });

        var res = await askTool.InvokeAsync(d.Arguments);
        Assert.Equal("Count: 2, Q1: Q1 (A,B), Q2: Q2", res?.ToString());
    }

    [Fact]
    public async Task NormalizeCall_CorruptedArrayWithJsonSuffixedOrKebabCaseItemKeys_MatchesSchemaAndNormalizes()
    {
        var dispatcher = new LenientToolDispatcher();
        var editTool = AIFunctionFactory.Create(
            (string path, IReadOnlyList<TestEditItem> edits) =>
                $"Count: {edits.Count}, Edit1: {edits[0].OldText}->{edits[0].NewText}",
            "edit");
        dispatcher.RegisterTool(editTool);

        var d = dispatcher.NormalizeCall("edit", new Dictionary<string, object?>
        {
            ["path"] = "Program.cs",
            ["edits"] = "[{\"old-text\": \"foo\", \"new-text\": \"bar\"}, \"rogue\": null}]"
        });

        var res = await editTool.InvokeAsync(d.Arguments);
        Assert.Equal("Count: 1, Edit1: foo->bar", res?.ToString());
    }

    [Fact]
    public async Task NormalizeCall_RawArgumentsJsonWithDirectCorruptedArray_ExtractsValidItems()
    {
        var dispatcher = new LenientToolDispatcher();
        var askTool = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) =>
                $"Count: {questions.Count}, Q1: {questions[0].Prompt} ({string.Join(",", questions[0].Options ?? [])}), Q2: {questions[1].Prompt}",
            "ask");
        dispatcher.RegisterTool(askTool);

        // Case 1: Non-stringified array property inside raw tool call JSON object with middle corruption
        var rawObjectJson = "{\"questions\": [{\"prompt\": \"Q1\", \"options\": [\"A\", \"B\"]}, \"options\": null}, {\"prompt\": \"Q2\"}]}";
        var d1 = dispatcher.NormalizeCall("ask", rawObjectJson);
        var r1 = await askTool.InvokeAsync(d1.Arguments);
        Assert.Equal("Count: 2, Q1: Q1 (A,B), Q2: Q2", r1?.ToString());

        // Case 2: Bare top-level array in rawArgumentsJson with middle corruption
        var rawRootArrayJson = "[{\"prompt\": \"Q1\", \"options\": [\"A\", \"B\"]}, \"options\": null}, {\"prompt\": \"Q2\"}]";
        var d2 = dispatcher.NormalizeCall("ask", rawRootArrayJson);
        var r2 = await askTool.InvokeAsync(d2.Arguments);
        Assert.Equal("Count: 2, Q1: Q1 (A,B), Q2: Q2", r2?.ToString());
    }

    [Fact]
    public async Task NormalizeCall_RawArgumentsJsonWithCorruptedArrayBeforeOtherTopLevelProperties_PreservesSubsequentTopLevelProperties()
    {
        var dispatcher = new LenientToolDispatcher();
        var editTool = AIFunctionFactory.Create(
            (string path, IReadOnlyList<TestEditItem> edits) =>
                $"Path: {path}, Count: {edits.Count}, E1: {edits[0].OldText}->{edits[0].NewText}, E2: {edits[1].OldText}->{edits[1].NewText}",
            "edit");
        dispatcher.RegisterTool(editTool);

        // First element in "edits" is missing its closing '}' before the second element, and "path" follows "edits" at top level
        var rawJsonMissingBrace = "{\"edits\": [{\"oldText\": \"a\", \"newText\": \"b\", {\"oldText\": \"c\", \"newText\": \"d\"}], \"path\": \"Program.cs\"}";
        var d1 = dispatcher.NormalizeCall("edit", rawJsonMissingBrace);
        var r1 = await editTool.InvokeAsync(d1.Arguments);
        Assert.Equal("Path: Program.cs, Count: 2, E1: a->b, E2: c->d", r1?.ToString());

        // First element has mistyped ']]' and "rationale" follows at top level for propagation
        var askTool = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) =>
                $"Count: {questions.Count}, Q1: {questions[0].Prompt} ({questions[0].Rationale}), Q2: {questions[1].Prompt} ({questions[1].Rationale})",
            "ask");
        dispatcher.RegisterTool(askTool);

        var rawJsonMistypedBracket = "{\"questions\": [{\"prompt\": \"Q1\", \"options\": [\"A\", \"B\"]]], {\"prompt\": \"Q2\"}], \"rationale\": \"Shared\"}";
        var d2 = dispatcher.NormalizeCall("ask", rawJsonMistypedBracket);
        var r2 = await askTool.InvokeAsync(d2.Arguments);
        Assert.Equal("Count: 2, Q1: Q1 (Shared), Q2: Q2 (Shared)", r2?.ToString());
    }

    [Fact]
    public async Task NormalizeCall_ArrayContainingStringifiedJsonObjects_WithRootParameterPropagation_PropagatesIntoParsedItems()
    {
        var dispatcher = new LenientToolDispatcher();
        var askTool = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) =>
                $"Count: {questions.Count}, Q1: {questions[0].Prompt} ({questions[0].Rationale}), Q2: {questions[1].Prompt} ({questions[1].Rationale})",
            "ask");
        dispatcher.RegisterTool(askTool);

        var d = dispatcher.NormalizeCall("ask", new Dictionary<string, object?>
        {
            ["questions"] = new List<object?>
            {
                "{\"prompt\": \"Q1\"}",
                "{\"prompt\": \"Q2\"}"
            },
            ["rationale"] = "Shared rationale"
        });

        Assert.False(d.Arguments.ContainsKey("rationale"));
        var res = await askTool.InvokeAsync(d.Arguments);
        Assert.Equal("Count: 2, Q1: Q1 (Shared rationale), Q2: Q2 (Shared rationale)", res?.ToString());
    }

    [Fact]
    public void NormalizeCall_UnregisteredToolWithMalformedJsonContainerString_DoesNotCommaSplitContainerString()
    {
        var dispatcher = new LenientToolDispatcher();
        var malformed = "[invalid: syntax, middle]";
        var d = dispatcher.NormalizeCall("unregistered_tool", new Dictionary<string, object?>
        {
            ["items"] = malformed
        });

        Assert.Equal(malformed, d.Arguments["items"]);
    }

    [Fact]
    public void NormalizeCall_WildcardNestedAliasForUnrelatedCollection_DoesNotMatchUnrelatedCorruptedObject()
    {
        var mapping = new ToolMapping();
        mapping.AddNestedParameterAlias("*[]", "unrelated_target", "unrelated_alias");
        var config = new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["ask"] = mapping
            }
        };
        var dispatcher = new LenientToolDispatcher(config);
        var askTool = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) => questions.Count.ToString(),
            "ask");
        dispatcher.RegisterTool(askTool);

        var corrupted = "[{\"unrelated_alias\": \"val\"}, \"rogue\": null}]";
        var d = dispatcher.NormalizeCall("ask", new Dictionary<string, object?>
        {
            ["questions"] = corrupted
        });

        // Should not extract the unrelated object as a valid TestQuestionItem
        Assert.Equal(corrupted, d.Arguments["questions"]);
    }

    [Fact]
    public async Task NormalizeCall_HealthyStructuredAndStringifiedCalls_PreservesValuesVerbatimWithoutRepairTelemetry()
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions { Telemetry = telemetry });

        var editTool = AIFunctionFactory.Create(
            (string path, IReadOnlyList<TestEditItem> edits) =>
                $"Path: {path}, Count: {edits.Count}, Old: {edits[0].OldText}, New: {edits[0].NewText}, ReplaceAll: {edits[0].ReplaceAll}",
            "edit");
        var askTool = AIFunctionFactory.Create(
            (IReadOnlyList<TestQuestionItem> questions) =>
                $"Count: {questions.Count}, Q1: {questions[0].Prompt} ({string.Join("|", questions[0].Options ?? [])}), Q2: {questions[1].Prompt}",
            "ask");
        var writeTool = AIFunctionFactory.Create(
            (string path, string content) => $"{path}:{content}",
            "write_file");
        var linesTool = AIFunctionFactory.Create(
            (IReadOnlyList<string> lines) => string.Join("\n", lines),
            "add_lines");

        dispatcher.RegisterTool(editTool);
        dispatcher.RegisterTool(askTool);
        dispatcher.RegisterTool(writeTool);
        dispatcher.RegisterTool(linesTool);

        // 1. Healthy edit call containing C# code with braces, brackets, and rogue-looking substrings inside strings
        var oldCode = "if (items[0] == \"}\") {\n    Log(\"}, \\\"options\\\": null}, {\");\n}";
        var newCode = "{\n    var matrix = new[] { new[] { 1, 2 }, new[] { 3, 4 } };\n}";
        var rawEditJson = JsonSerializer.Serialize(new
        {
            path = "src/Parser.cs",
            edits = new[]
            {
                new { oldText = oldCode, newText = newCode, replaceAll = true }
            }
        });

        var dEdit = dispatcher.NormalizeCall("edit", rawEditJson);
        var resEdit = await editTool.InvokeAsync(dEdit.Arguments);
        Assert.Equal($"Path: src/Parser.cs, Count: 1, Old: {oldCode}, New: {newCode}, ReplaceAll: True", resEdit?.ToString());

        // 2. Healthy ask call with valid stringified JSON array (no truncation or corruption)
        var validStringifiedQuestions = "[{\"prompt\": \"Keep {brace} and [bracket]?\", \"options\": [\"[Yes]\", \"{No}\"]}, {\"prompt\": \"Second, question?\"}]";
        var dAsk = dispatcher.NormalizeCall("ask", new Dictionary<string, object?>
        {
            ["questions"] = validStringifiedQuestions
        });
        var resAsk = await askTool.InvokeAsync(dAsk.Arguments);
        Assert.Equal("Count: 2, Q1: Keep {brace} and [bracket]? ([Yes]|{No}), Q2: Second, question?", resAsk?.ToString());

        // 3. Healthy write_file call whose string content happens to be a corrupted JSON test snippet: must not be altered
        var corruptedJsonSnippet = "[{\"prompt\": \"Q1\"}, \"options\": null}, {\"prompt\": \"Q2\"]";
        var dWrite = dispatcher.NormalizeCall("write_file", new Dictionary<string, object?>
        {
            ["path"] = "test.json",
            ["content"] = corruptedJsonSnippet
        });
        var resWrite = await writeTool.InvokeAsync(dWrite.Arguments);
        Assert.Equal($"test.json:{corruptedJsonSnippet}", resWrite?.ToString());

        // 4. Healthy string-array call where individual string elements are JSON objects/arrays or bracketed tags
        var rawLinesJson = JsonSerializer.Serialize(new
        {
            lines = new[] { "[Fact]", "{\"a\": 1, \"b\": 2}", "[1, 2, 3]", "[{\"prompt\": \"Q1\"}, \"options\": null}]" }
        });
        var healingTelemetry = new ToolCallHealingTelemetry();
        var parsedLinesArgs = ToolCallHealingTransform.ParseArgumentsJson(
            rawLinesJson,
            toolName: "add_lines",
            options: new ToolCallHealingOptions { Telemetry = healingTelemetry });
        var dLines = dispatcher.NormalizeCall("add_lines", parsedLinesArgs);
        var resLines = await linesTool.InvokeAsync(dLines.Arguments);
        Assert.Equal("[Fact]\n{\"a\": 1, \"b\": 2}\n[1, 2, 3]\n[{\"prompt\": \"Q1\"}, \"options\": null}]", resLines?.ToString());

        // Zero repair telemetry should have been recorded across all healthy calls
        Assert.Equal(0, healingTelemetry.CreateSnapshot().TotalInterventions);
        var snap = telemetry.CreateSnapshot();
        Assert.Equal(0, snap.TotalHits);
        Assert.Equal(0, snap.StringifiedJsonRepaired);
        Assert.Equal(0, snap.ToolAliasesResolved);
        Assert.Equal(0, snap.ParameterAliasesResolved);
    }

    private sealed class FakeSimpleChatClient(Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> handler) : IChatClient
    {
        public ChatClientMetadata Metadata => new("FakeSimple");
        public void Dispose() { }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => handler(chatMessages, options, cancellationToken);
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }
}
