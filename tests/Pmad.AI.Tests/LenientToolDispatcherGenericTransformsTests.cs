using System.Text.Json;
using Microsoft.Extensions.AI;
using Pmad.AI.Common;
using Pmad.AI.Tools;

namespace Pmad.AI.Tests;

public class LenientToolDispatcherGenericTransformsTests
{
    public record EditChunk(string OldText, string? NewText = null, bool ReplaceAll = false);

    #region 2.1 Deterministic Precedence, Post-Alias ComputedParameters, Blank Collection Handling

    [Fact]
    public void NormalizeCall_DeterministicTieBreaking_PreservesAppearanceOrder_WhenMultipleAliasesPresent()
    {
        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["find"] = new ToolMapping(parameterAliases: new Dictionary<string, IEnumerable<string>>
                {
                    ["query"] = ["pattern", "searchTerm", "search"]
                })
            }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string query) => query,
            "find"));

        // Case 1: "pattern" appears before "searchTerm"
        var call1 = dispatcher.NormalizeCall("find", new Dictionary<string, object?>
        {
            ["pattern"] = "first_pattern",
            ["searchTerm"] = "second_pattern"
        });
        Assert.Equal("first_pattern", call1.Arguments["query"]);

        // Case 2: "searchTerm" appears before "pattern"
        var call2 = dispatcher.NormalizeCall("find", new Dictionary<string, object?>
        {
            ["searchTerm"] = "second_pattern",
            ["pattern"] = "first_pattern"
        });
        Assert.Equal("second_pattern", call2.Arguments["query"]);

        // Case 3: exact canonical "query" always wins even if appearing later
        var call3 = dispatcher.NormalizeCall("find", new Dictionary<string, object?>
        {
            ["pattern"] = "alias_val",
            ["query"] = "canonical_val"
        });
        Assert.Equal("canonical_val", call3.Arguments["query"]);

        // Case 4: null canonical key lets non-null alias win
        var call4 = dispatcher.NormalizeCall("find", new Dictionary<string, object?>
        {
            ["query"] = null,
            ["pattern"] = "alias_val"
        });
        Assert.Equal("alias_val", call4.Arguments["query"]);
    }

    [Fact]
    public void ComputedParameters_ReceivesToolParameterComputationContext_WithUnwrappedValuesAndAliases()
    {
        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["read"] = new ToolMapping(
                    parameterAliases: new Dictionary<string, IEnumerable<string>>
                    {
                        ["offset"] = ["start_line", "startLine"],
                        ["limit"] = ["count"]
                    },
                    computedParameters: new Dictionary<string, ParameterComputer>
                    {
                        ["limit"] = raw =>
                        {
                            // Verify typed extension method GetInt automatically resolving alias "offset" -> "start_line"
                            int start = raw.GetInt("offset", 1);
                            int end = raw.GetInt("end_line", 0);
                            return end >= start ? end - start + 1 : 1;
                        }
                    })
            }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string path, int offset, int limit) => $"{path}:{offset}:{limit}",
            "read"));

        using var jsonDoc = JsonDocument.Parse(@"{""path"": ""test.cs"", ""start_line"": ""#10"", ""end_line"": 25}");
        var rawDict = new Dictionary<string, object?>();
        foreach (var p in jsonDoc.RootElement.EnumerateObject())
        {
            rawDict[p.Name] = p.Value;
        }

        var call = dispatcher.NormalizeCall("read", rawDict);
        Assert.Equal(10, Convert.ToInt32(call.Arguments["offset"]));
        Assert.Equal(16, Convert.ToInt32(call.Arguments["limit"]));
    }

    [Fact]
    public void ComputedParameters_ReplacesBlankCollectionPlaceholder()
    {
        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["edit"] = new ToolMapping(
                    computedParameters: new Dictionary<string, ParameterComputer>
                    {
                        ["edits"] = raw =>
                        {
                            var oldText = raw.GetString("old_text");
                            var newText = raw.GetString("new_text") ?? string.Empty;
                            if (oldText != null)
                            {
                                return new List<object?>
                                {
                                    new Dictionary<string, object?>
                                    {
                                        ["oldText"] = oldText,
                                        ["newText"] = newText
                                    }
                                };
                            }
                            return null;
                        }
                    })
            }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string path, List<EditChunk>? edits = null) => edits?.Count ?? 0,
            "edit"));

        // When LLM emits blank collection edits: [{}] alongside shortcut parameters
        using var jsonDoc = JsonDocument.Parse(@"{""path"": ""a.cs"", ""edits"": [{}], ""old_text"": ""foo"", ""new_text"": ""bar""}");
        var rawDict = new Dictionary<string, object?>();
        foreach (var p in jsonDoc.RootElement.EnumerateObject())
        {
            rawDict[p.Name] = p.Value;
        }

        var call = dispatcher.NormalizeCall("edit", rawDict);
        Assert.True(call.Arguments.ContainsKey("edits"));
        var edits = Assert.IsAssignableFrom<System.Collections.IEnumerable>(call.Arguments["edits"]);
        var list = edits.Cast<object?>().ToList();
        Assert.Single(list);
    }

    #endregion

    #region 2.2 Declarative Range-to-Limit Transform

    [Fact]
    public void DeclarativeRangeToLimit_ComputesLimit_AndRemovesConsumedEndLineKey()
    {
        var mapping = new ToolMapping();
        mapping.ParameterAliases["offset"] = "offset";
        mapping.ParameterAliases["start_line"] = "offset";
        mapping.ParameterAliases["startLine"] = "offset";
        mapping.MapEndLineToLimit("offset", "limit", ["end_line", "endLine", "to_line", "to", "end"]);

        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools = { ["read_file"] = mapping }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string path, int offset = 1, int limit = 100) => $"{path}:{offset}:{limit}",
            "read_file"));

        // Case 1: start_line and end_line provided
        var call1 = dispatcher.NormalizeCall("read_file", new Dictionary<string, object?>
        {
            ["path"] = "main.cs",
            ["start_line"] = 15,
            ["end_line"] = 30
        });

        Assert.Equal(15, Convert.ToInt32(call1.Arguments["offset"]));
        Assert.Equal(16, Convert.ToInt32(call1.Arguments["limit"]));
        Assert.False(call1.Arguments.ContainsKey("end_line"));

        // Case 2: only end_line provided, defaults start to 1
        var call2 = dispatcher.NormalizeCall("read_file", new Dictionary<string, object?>
        {
            ["path"] = "main.cs",
            ["to_line"] = 50
        });

        Assert.Equal(1, Convert.ToInt32(call2.Arguments["offset"]));
        Assert.Equal(50, Convert.ToInt32(call2.Arguments["limit"]));
        Assert.False(call2.Arguments.ContainsKey("to_line"));

        // Case 3: limit already provided; end_line alias is cleaned up
        var call3 = dispatcher.NormalizeCall("read_file", new Dictionary<string, object?>
        {
            ["path"] = "main.cs",
            ["offset"] = 1,
            ["limit"] = 20,
            ["end_line"] = 50
        });

        Assert.Equal(1, Convert.ToInt32(call3.Arguments["offset"]));
        Assert.Equal(20, Convert.ToInt32(call3.Arguments["limit"]));
        Assert.False(call3.Arguments.ContainsKey("end_line"));
    }

    [Fact]
    public void DeclarativeRangeToLimit_SupportsExclusiveMode()
    {
        var mapping = new ToolMapping();
        mapping.MapEndLineToLimit("offset", "limit", ["end"], defaultStart: 0, inclusive: false);

        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools = { ["slice"] = mapping }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (int offset, int limit) => $"{offset}:{limit}",
            "slice"));

        var call = dispatcher.NormalizeCall("slice", new Dictionary<string, object?>
        {
            ["offset"] = 10,
            ["end"] = 25
        });

        Assert.Equal(10, Convert.ToInt32(call.Arguments["offset"]));
        Assert.Equal(15, Convert.ToInt32(call.Arguments["limit"])); // 25 - 10 = 15
    }

    #endregion

    #region 2.3 Enhanced Flattened-to-Collection Promotion & Null-to-Empty-String

    [Fact]
    public void PromoteFlattenedParameters_PromotesTopLevelParametersMatchingCollectionItem()
    {
        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration());

        // Function signature has path (required) and optional edits/old_text/new_text/replace_all
        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string path, IReadOnlyList<EditChunk>? edits = null, string? old_text = null, string? new_text = null, bool replace_all = false) =>
                edits?.Count ?? 0,
            "edit_file"));

        var call = dispatcher.NormalizeCall("edit_file", new Dictionary<string, object?>
        {
            ["path"] = "file.txt",
            ["old_text"] = "search_pattern",
            ["new_text"] = "replacement",
            ["replace_all"] = true
        });

        Assert.True(call.Arguments.ContainsKey("edits"));
        Assert.False(call.Arguments.ContainsKey("old_text"));
        Assert.False(call.Arguments.ContainsKey("new_text"));
        Assert.False(call.Arguments.ContainsKey("replace_all"));

        var edits = Assert.IsAssignableFrom<System.Collections.IEnumerable>(call.Arguments["edits"]);
        var list = edits.Cast<object?>().ToList();
        Assert.Single(list);

        var first = Assert.IsAssignableFrom<IDictionary<string, object?>>(list[0]);
        Assert.Equal("search_pattern", first["OldText"]);
        Assert.Equal("replacement", first["NewText"]);
        Assert.Equal(true, first["ReplaceAll"]);
    }

    [Fact]
    public void ExplicitNull_CoercedToEmptyString_WhenConfiguredInNullAsEmptyStringParameters()
    {
        var mapping = new ToolMapping();
        mapping.AddNullAsEmptyStringParameter("new_text", "edits[].newText");

        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools = { ["edit_tool"] = mapping }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string path, string? old_text = null, string? new_text = null) => $"{path}:{old_text}:{new_text}",
            "edit_tool"));

        // Case 1: Top-level explicit null coerced to ""
        var call1 = dispatcher.NormalizeCall("edit_tool", new Dictionary<string, object?>
        {
            ["path"] = "a.cs",
            ["old_text"] = "deprecated",
            ["new_text"] = null
        });

        Assert.Equal("deprecated", call1.Arguments["old_text"]);
        Assert.Equal(string.Empty, call1.Arguments["new_text"]);

        // Case 2: Omitted parameter is NOT coerced to ""
        var call2 = dispatcher.NormalizeCall("edit_tool", new Dictionary<string, object?>
        {
            ["path"] = "a.cs",
            ["old_text"] = "deprecated"
        });

        Assert.False(call2.Arguments.ContainsKey("new_text"));
    }

    [Fact]
    public void ExplicitNull_CoercedToEmptyString_InsideNestedCollection()
    {
        var mapping = new ToolMapping();
        mapping.AddNullAsEmptyStringParameter("edits[].newText");

        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools = { ["edit_batch"] = mapping }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string path, List<EditChunk> edits) => edits.Count,
            "edit_batch"));

        using var jsonDoc = JsonDocument.Parse(@"{
            ""path"": ""a.cs"",
            ""edits"": [
                {""old_text"": ""remove_me"", ""new_text"": null}
            ]
        }");

        var rawDict = new Dictionary<string, object?>();
        foreach (var p in jsonDoc.RootElement.EnumerateObject())
        {
            rawDict[p.Name] = p.Value;
        }

        var call = dispatcher.NormalizeCall("edit_batch", rawDict);
        var edits = Assert.IsAssignableFrom<System.Collections.IEnumerable>(call.Arguments["edits"]);
        var list = edits.Cast<object?>().ToList();
        Assert.Single(list);

        var first = Assert.IsAssignableFrom<IDictionary<string, object?>>(list[0]);
        Assert.Equal("remove_me", first["OldText"]);
        Assert.Equal(string.Empty, first["NewText"]);
    }

    #endregion

    #region 2.4 First-Class Virtual Tool Translations

    [Fact]
    public void ToolTranslation_SupportsAliasesAndParameterAliases()
    {
        var translation = new ToolTranslation
        {
            SourceTool = "list_dir",
            TargetTool = "bash",
            CommandTemplate = "ls -la {path:.}",
            Aliases = ["listdir", "list_directory", "ls"],
            ParameterAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dir"] = "path",
                ["directory"] = "path",
                ["folder"] = "path"
            }
        };

        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Translations = [translation]
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string command) => $"ran: {command}",
            "bash"));

        // Case 1: Invocation via translation alias "list_directory" and parameter alias "dir"
        var call1 = dispatcher.NormalizeCall("list_directory", new Dictionary<string, object?>
        {
            ["dir"] = "src"
        });

        Assert.Equal("bash", call1.CanonicalToolName);
        Assert.True(call1.WasTranslated);
        Assert.Equal("ls -la 'src'", call1.Arguments["command"]);

        // Case 2: Invocation via alias "ls" with null path falling back to default "."
        var call2 = dispatcher.NormalizeCall("ls", new Dictionary<string, object?>
        {
            ["path"] = null
        });

        Assert.Equal("ls -la '.'", call2.Arguments["command"]);
    }

    [Fact]
    public void Validate_AcceptsVirtualToolInConfigurationTools_WithoutRegisteredAIFunction()
    {
        var translation = new ToolTranslation
        {
            SourceTool = "grep_search",
            TargetTool = "bash",
            CommandTemplate = "rg -n -- {query} {path:.}",
            Aliases = ["grep"]
        };

        var config = new ToolDispatcherConfiguration
        {
            Translations = [translation],
            Tools =
            {
                ["grep_search"] = new ToolMapping(parameterAliases: new Dictionary<string, IEnumerable<string>>
                {
                    ["query"] = ["pattern", "search"]
                })
            }
        };

        var dispatcher = new LenientToolDispatcher(config);
        dispatcher.RegisterTool(AIFunctionFactory.Create((string command) => command, "bash"));

        // Validate should succeed with 0 errors
        var report = dispatcher.Validate(throwOnError: true);
        Assert.True(report.IsValid);
    }

    [Fact]
    public void Validate_DetectsInvalidPlaceholder_InTranslatedToolParameterAliases()
    {
        var translation = new ToolTranslation
        {
            SourceTool = "grep_search",
            TargetTool = "bash",
            CommandTemplate = "rg -n -- {query} {path:.}",
            ParameterAliases = new Dictionary<string, string>
            {
                ["bad_alias"] = "non_existent_placeholder"
            }
        };

        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Translations = [translation]
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create((string command) => command, "bash"));

        var report = dispatcher.Validate(throwOnError: false);
        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, i => i.Severity == ToolValidationSeverity.Error && i.ParameterName == "non_existent_placeholder");
    }

    #endregion

    #region 2.5 Casing and Separator-Insensitive Tool Name Resolution

    [Theory]
    [InlineData("read_file", "readfile")]
    [InlineData("ReadFile", "readfile")]
    [InlineData("read-file", "readfile")]
    [InlineData("readfile", "readfile")]
    [InlineData("list_dir_all", "listdirall")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ToolHelpers_NormalizeToolIdentifier_StripsSeparatorsAndLowercases(string? input, string expected)
    {
        Assert.Equal(expected, ToolHelpers.NormalizeToolIdentifier(input!));
    }

    [Fact]
    public void NormalizeCall_ResolvesTool_AcrossCasingAndSeparators()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string path) => path,
            "read_file"));

        // Match with PascalCase: "ReadFile"
        var call1 = dispatcher.NormalizeCall("ReadFile", new Dictionary<string, object?> { ["path"] = "a.cs" });
        Assert.Equal("read_file", call1.CanonicalToolName);

        // Match with kebab-case: "read-file"
        var call2 = dispatcher.NormalizeCall("read-file", new Dictionary<string, object?> { ["path"] = "a.cs" });
        Assert.Equal("read_file", call2.CanonicalToolName);

        // Match without separators: "readfile"
        var call3 = dispatcher.NormalizeCall("readfile", new Dictionary<string, object?> { ["path"] = "a.cs" });
        Assert.Equal("read_file", call3.CanonicalToolName);
    }

    [Fact]
    public void NormalizeCall_ResolvesTranslation_AcrossCasingAndSeparators()
    {
        var translation = new ToolTranslation
        {
            SourceTool = "grep_search",
            TargetTool = "bash",
            CommandTemplate = "rg {query}"
        };

        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Translations = [translation]
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create((string command) => command, "bash"));

        var call = dispatcher.NormalizeCall("GrepSearch", new Dictionary<string, object?> { ["query"] = "TODO" });
        Assert.Equal("bash", call.CanonicalToolName);
        Assert.Equal("rg 'TODO'", call.Arguments["command"]);
    }

    #endregion

    #region 2.6 Descriptive ArgumentException on Uncoercible Elements

    [Fact]
    public void ParameterCoercion_ThrowsDescriptiveArgumentException_OnUncoercibleScalar()
    {
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            EnableParameterCoercion = true
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string path, int line) => $"{path}:{line}",
            "goto_line"));

        var ex = Assert.Throws<ArgumentException>(() =>
            dispatcher.NormalizeCall("goto_line", new Dictionary<string, object?>
            {
                ["path"] = "test.cs",
                ["line"] = "not-a-number"
            }));

        Assert.Contains("line", ex.Message);
        Assert.Contains("not-a-number", ex.Message);
        Assert.Contains("valid integer", ex.Message);
    }

    [Fact]
    public void ParameterCoercion_ThrowsDescriptiveArgumentException_OnUncoercibleArrayElement()
    {
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            EnableParameterCoercion = true
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (List<int> ids) => ids.Count,
            "delete_items"));

        var ex = Assert.Throws<ArgumentException>(() =>
            dispatcher.NormalizeCall("delete_items", new Dictionary<string, object?>
            {
                ["ids"] = new List<object?> { "1", "invalid_id", "3" }
            }));

        Assert.Contains("ids", ex.Message);
        Assert.Contains("invalid_id", ex.Message);
        Assert.Contains("valid integer", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WrapsJsonException_InArgumentException()
    {
        var dispatcher = new LenientToolDispatcher();
        Func<string, string> lambda = (string data) => throw new JsonException("Failed to deserialize complex type.");
        dispatcher.RegisterTool(AIFunctionFactory.Create(lambda, "mock_parser"));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            dispatcher.ExecuteAsync("mock_parser", new Dictionary<string, object?>
            {
                ["data"] = "invalid_json"
            }));

        Assert.Contains("mock_parser", ex.Message);
        Assert.Contains("Failed to deserialize arguments", ex.Message);
    }

    [Fact]
    public void ParameterCoercion_ThrowsDescriptiveArgumentException_OnNonStringUncoercibleArrayElement()
    {
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            EnableParameterCoercion = true
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (List<int> ids, List<bool> flags, List<double> weights) => ids.Count,
            "update_batch"));

        // Boolean in integer[] should throw ArgumentException
        var exInt = Assert.Throws<ArgumentException>(() =>
            dispatcher.NormalizeCall("update_batch", new Dictionary<string, object?>
            {
                ["ids"] = new List<object?> { 1, true, 3 }
            }));
        Assert.Contains("ids", exInt.Message);
        Assert.Contains("valid integer", exInt.Message);

        // Fractional double in integer[] should throw ArgumentException
        var exFrac = Assert.Throws<ArgumentException>(() =>
            dispatcher.NormalizeCall("update_batch", new Dictionary<string, object?>
            {
                ["ids"] = new List<object?> { 1, 3.14 }
            }));
        Assert.Contains("ids", exFrac.Message);
        Assert.Contains("valid integer", exFrac.Message);

        // Integral double in integer[] should coerce to int
        var okInt = dispatcher.NormalizeCall("update_batch", new Dictionary<string, object?>
        {
            ["ids"] = new List<object?> { 1, 42.0 }
        });
        var coercedIds = Assert.IsAssignableFrom<IList<object?>>(okInt.Arguments["ids"]);
        Assert.Equal(42, coercedIds[1]);

        // Integer out of 0/1 in bool[] should throw ArgumentException
        var exBool = Assert.Throws<ArgumentException>(() =>
            dispatcher.NormalizeCall("update_batch", new Dictionary<string, object?>
            {
                ["flags"] = new List<object?> { true, 99 }
            }));
        Assert.Contains("flags", exBool.Message);
        Assert.Contains("valid boolean", exBool.Message);

        // Boolean in double[] should throw ArgumentException
        var exNum = Assert.Throws<ArgumentException>(() =>
            dispatcher.NormalizeCall("update_batch", new Dictionary<string, object?>
            {
                ["weights"] = new List<object?> { 1.5, false }
            }));
        Assert.Contains("weights", exNum.Message);
        Assert.Contains("valid number", exNum.Message);
    }

    #endregion

    #region 2.7 Review Findings Coverage (Snapshot Isolation, Precedence, Telemetry, Validation, Availability)

    [Fact]
    public void Snapshot_DeepClonesToolTranslations_IsolatingAliasesAndParameterAliases()
    {
        var translation = new ToolTranslation
        {
            SourceTool = "list_dir",
            TargetTool = "bash",
            CommandTemplate = "ls {path:.}",
            Aliases = ["ls"],
            ParameterAliases = { ["dir"] = "path" }
        };

        var config = new ToolDispatcherConfiguration
        {
            Translations = [translation]
        };

        var dispatcher = new LenientToolDispatcher(config);
        dispatcher.RegisterTool(AIFunctionFactory.Create((string command) => command, "bash"));

        // Mutate translation after dispatcher creation
        translation.Aliases.Add("mutated_alias");
        translation.ParameterAliases["mutated_param"] = "non_existent_placeholder";

        // Dispatcher snapshot must be unaffected by post-construction mutations
        var report = dispatcher.Validate(throwOnError: false);
        Assert.True(report.IsValid);

        var call = dispatcher.NormalizeCall("mutated_alias", new Dictionary<string, object?>());
        Assert.False(call.WasTranslated);
    }

    [Fact]
    public void RegistredToolsMetadata_DoesNotCorruptLookups_WhenToolNameContainsOnlySeparators()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.RegisterTool(AIFunctionFactory.Create(() => "separator_tool", "___"));
        dispatcher.RegisterTool(AIFunctionFactory.Create(() => "real_tool", "read_file"));

        Assert.False(dispatcher.IsToolRegistered("---"));
        Assert.Equal("---", dispatcher.ResolveCanonicalToolName("---"));
        Assert.Equal("___", dispatcher.ResolveCanonicalToolName("___"));
    }

    [Fact]
    public void RegistredToolsMetadata_WithTool_UpdatesNormalizedLookupEntry()
    {
        var dispatcher = new LenientToolDispatcher();
        var v1 = AIFunctionFactory.Create((string path) => $"v1:{path}", "read_file");
        var v2 = AIFunctionFactory.Create((string path) => $"v2:{path}", "read_file");

        dispatcher.RegisterTool(v1);
        dispatcher.RegisterTool(v2);

        // Lookup by normalized name ("ReadFile") must return v2, not stale v1
        Assert.True(dispatcher.TryGetRegisteredTool("ReadFile", out var resolved));
        Assert.Same(v2, resolved);
    }

    [Fact]
    public void ResolveCanonicalToolAndMapping_PrioritizesNormalizedRegisteredTool_OverNormalizedAlias()
    {
        // Tool A ("other_tool") has an alias "read_file"
        // Tool B ("read-file") is a registered canonical tool
        // Calling "ReadFile" (normalized "readfile") should prefer the registered tool "read-file" over the alias of "other_tool"
        var config = new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["other_tool"] = new ToolMapping(aliases: ["read_file"])
            }
        };

        var dispatcher = new LenientToolDispatcher(config);
        dispatcher.RegisterTool(AIFunctionFactory.Create(() => "other", "other_tool"));
        dispatcher.RegisterTool(AIFunctionFactory.Create(() => "read", "read-file"));

        // Exact alias "read_file" still resolves to "other_tool"
        Assert.Equal("other_tool", dispatcher.ResolveCanonicalToolName("read_file"));

        // Normalized "ReadFile" resolves to registered tool "read-file"
        Assert.Equal("read-file", dispatcher.ResolveCanonicalToolName("ReadFile"));
    }

    [Fact]
    public void TranslationTelemetry_DistinguishesCasingNormalization_FromToolAliasResolution()
    {
        var telemetry = new LenientDispatchTelemetry();
        var translation = new ToolTranslation
        {
            SourceTool = "grep_search",
            TargetTool = "bash",
            CommandTemplate = "rg {query}",
            Aliases = ["find_text"]
        };

        var dispatcher = new LenientToolDispatcher(
            new ToolDispatcherConfiguration { Translations = [translation] },
            new LenientToolDispatcherOptions { Telemetry = telemetry });
        dispatcher.RegisterTool(AIFunctionFactory.Create((string command) => command, "bash"));

        // 1. Casing difference on SourceTool ("Grep_Search" or "GrepSearch") -> CasingNormalized
        dispatcher.NormalizeCall("Grep_Search", new Dictionary<string, object?> { ["query"] = "a" });
        var snap1 = telemetry.CreateSnapshot();
        Assert.Equal(1, snap1.CasingNormalized);
        Assert.Equal(0, snap1.ToolAliasesResolved);

        // 2. Separator/casing difference on SourceTool ("GrepSearch") -> CasingNormalized
        dispatcher.NormalizeCall("GrepSearch", new Dictionary<string, object?> { ["query"] = "a" });
        var snap2 = telemetry.CreateSnapshot();
        Assert.Equal(2, snap2.CasingNormalized);
        Assert.Equal(0, snap2.ToolAliasesResolved);

        // 3. Invocation via alias ("find_text" or "FindText") -> ToolAliasResolved
        dispatcher.NormalizeCall("find_text", new Dictionary<string, object?> { ["query"] = "a" });
        dispatcher.NormalizeCall("FindText", new Dictionary<string, object?> { ["query"] = "a" });
        var snap3 = telemetry.CreateSnapshot();
        Assert.Equal(2, snap3.CasingNormalized);
        Assert.Equal(2, snap3.ToolAliasesResolved);
    }

    [Fact]
    public void Validate_ChecksRangeToLimitRuleParameters_AndVirtualToolAliasCollisions()
    {
        var translation = new ToolTranslation
        {
            SourceTool = "list_dir",
            TargetTool = "bash",
            CommandTemplate = "ls {path:.}"
        };

        var readMapping = new ToolMapping();
        readMapping.MapEndLineToLimit("non_existent_start", "non_existent_limit", ["end_line"]);
        readMapping.Aliases.Add("shared_alias");

        var virtualMapping = new ToolMapping(aliases: ["shared_alias"]);

        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Translations = [translation],
            Tools =
            {
                ["read_file"] = readMapping,
                ["list_dir"] = virtualMapping
            }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create((string command) => command, "bash"));
        dispatcher.RegisterTool(AIFunctionFactory.Create((string path, int offset, int limit) => path, "read_file"));

        var report = dispatcher.Validate(throwOnError: false);
        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, i => i.Severity == ToolValidationSeverity.Error && i.ParameterName == "non_existent_start");
        Assert.Contains(report.Issues, i => i.Severity == ToolValidationSeverity.Error && i.ParameterName == "non_existent_limit");
        Assert.Contains(report.Issues, i => i.Severity == ToolValidationSeverity.Warning && i.Message.Contains("shared_alias"));
    }

    [Fact]
    public void IsToolAvailable_And_IsToolRegistered_RecognizeTranslationAliases_AndNormalizedNames()
    {
        var translation = new ToolTranslation
        {
            SourceTool = "list_dir",
            TargetTool = "bash",
            CommandTemplate = "ls {path:.}",
            Aliases = ["listdir", "ls"]
        };

        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Translations = [translation]
        });

        var bashTool = AIFunctionFactory.Create((string command) => command, "bash");
        var readTool = AIFunctionFactory.Create((string path) => path, "read_file");
        dispatcher.RegisterTool(bashTool);
        dispatcher.RegisterTool(readTool);

        AITool[] available = [bashTool, readTool];

        // Translation alias and normalized translation name
        Assert.True(dispatcher.IsToolRegistered("ls"));
        Assert.True(dispatcher.IsToolRegistered("ListDir"));
        Assert.True(dispatcher.IsToolAvailable("ls", available));
        Assert.True(dispatcher.IsToolAvailable("ListDir", available));

        // Normalized registered tool name
        Assert.True(dispatcher.IsToolRegistered("ReadFile"));
        Assert.True(dispatcher.IsToolAvailable("ReadFile", available));
        Assert.True(dispatcher.IsToolAvailable("read-file", available));
    }

    [Fact]
    public void ApplyRangeToLimitRules_HandlesCaseDifferentEndLineAlias_AndCleansComputationContext()
    {
        bool endLineStillVisibleInComputed = false;
        var mapping = new ToolMapping(
            computedParameters: new Dictionary<string, ParameterComputer>
            {
                ["extra"] = ctx =>
                {
                    endLineStillVisibleInComputed = ctx.HasExplicitKey("end_line");
                    return "ok";
                }
            });
        mapping.MapEndLineToLimit("offset", "limit", ["end_line"]);

        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools = { ["read_file"] = mapping }
        });

        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (string path, int offset = 1, int limit = 100, string? extra = null) => $"{path}:{offset}:{limit}:{extra}",
            "read_file"));

        // Caller passes "END_LINE" (uppercase) in an ordinal dictionary
        var rawArgs = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["path"] = "a.cs",
            ["offset"] = 10,
            ["END_LINE"] = 20
        };

        var call = dispatcher.NormalizeCall("read_file", rawArgs);
        Assert.Equal(10, Convert.ToInt32(call.Arguments["offset"]));
        Assert.Equal(11, Convert.ToInt32(call.Arguments["limit"]));
        Assert.False(call.Arguments.ContainsKey("END_LINE"));
        Assert.False(call.Arguments.ContainsKey("end_line"));
        Assert.False(endLineStillVisibleInComputed);
    }

    [Fact]
    public void ToolParameterComputationContext_TryParseInt_HandlesBoundsAndSpecialFloatingPointValues()
    {
        // Within bounds
        Assert.True(ToolParameterComputationContext.TryParseInt(42L, out var res1));
        Assert.Equal(42, res1);

        Assert.True(ToolParameterComputationContext.TryParseInt(100.0, out var res2));
        Assert.Equal(100, res2);

        Assert.True(ToolParameterComputationContext.TryParseInt(50.0f, out var res3));
        Assert.Equal(50, res3);

        Assert.True(ToolParameterComputationContext.TryParseInt(1234m, out var res4));
        Assert.Equal(1234, res4);

        // Out of integer bounds
        Assert.False(ToolParameterComputationContext.TryParseInt((long)int.MaxValue + 1L, out _));
        Assert.False(ToolParameterComputationContext.TryParseInt((long)int.MinValue - 1L, out _));
        Assert.False(ToolParameterComputationContext.TryParseInt((double)int.MaxValue + 1000.0, out _));
        Assert.False(ToolParameterComputationContext.TryParseInt((double)int.MinValue - 1000.0, out _));
        Assert.False(ToolParameterComputationContext.TryParseInt(1e15, out _));
        Assert.False(ToolParameterComputationContext.TryParseInt(decimal.MaxValue, out _));
        Assert.False(ToolParameterComputationContext.TryParseInt(decimal.MinValue, out _));

        // NaN and Infinity
        Assert.False(ToolParameterComputationContext.TryParseInt(double.NaN, out _));
        Assert.False(ToolParameterComputationContext.TryParseInt(double.PositiveInfinity, out _));
        Assert.False(ToolParameterComputationContext.TryParseInt(double.NegativeInfinity, out _));
        Assert.False(ToolParameterComputationContext.TryParseInt(float.NaN, out _));
        Assert.False(ToolParameterComputationContext.TryParseInt(float.PositiveInfinity, out _));
        Assert.False(ToolParameterComputationContext.TryParseInt(float.NegativeInfinity, out _));
    }

    [Fact]
    public void ToolParameterComputationContext_TryParseLong_HandlesBoundsAndSpecialFloatingPointValues()
    {
        // Within bounds
        Assert.True(ToolParameterComputationContext.TryParseLong(1234567890123L, out var res1));
        Assert.Equal(1234567890123L, res1);

        Assert.True(ToolParameterComputationContext.TryParseLong(500.0, out var res2));
        Assert.Equal(500L, res2);

        // Out of long bounds
        Assert.False(ToolParameterComputationContext.TryParseLong(1e25, out _));
        Assert.False(ToolParameterComputationContext.TryParseLong(-1e25, out _));
        Assert.False(ToolParameterComputationContext.TryParseLong(decimal.MaxValue, out _));
        Assert.False(ToolParameterComputationContext.TryParseLong(decimal.MinValue, out _));

        // NaN and Infinity
        Assert.False(ToolParameterComputationContext.TryParseLong(double.NaN, out _));
        Assert.False(ToolParameterComputationContext.TryParseLong(double.PositiveInfinity, out _));
        Assert.False(ToolParameterComputationContext.TryParseLong(double.NegativeInfinity, out _));
        Assert.False(ToolParameterComputationContext.TryParseLong(float.NaN, out _));
        Assert.False(ToolParameterComputationContext.TryParseLong(float.PositiveInfinity, out _));
        Assert.False(ToolParameterComputationContext.TryParseLong(float.NegativeInfinity, out _));
    }

    [Fact]
    public void ToolParameterComputationExtensions_GetIntAndGetLong_ReturnDefaultValue_OnOverflow()
    {
        var dict = new Dictionary<string, object?>
        {
            ["overflow_int"] = 5_000_000_000L,
            ["overflow_long"] = 1e25,
            ["nan_val"] = double.NaN,
            ["valid_int"] = 42
        };

        var ctx = new ToolParameterComputationContext(dict);

        Assert.Equal(-1, ctx.GetInt("overflow_int", defaultValue: -1));
        Assert.Equal(-1, ctx.GetLong("overflow_long", defaultValue: -1));
        Assert.Equal(-1, ctx.GetInt("nan_val", defaultValue: -1));
        Assert.Equal(42, ctx.GetInt("valid_int", defaultValue: -1));

        // Also test directly via IDictionary extension methods
        Assert.Equal(-1, dict.GetInt("overflow_int", defaultValue: -1));
        Assert.Equal(-1, dict.GetLong("overflow_long", defaultValue: -1));
        Assert.Equal(-1, dict.GetInt("nan_val", defaultValue: -1));
        Assert.Equal(42, dict.GetInt("valid_int", defaultValue: -1));
    }

    [Fact]
    public void ToolMapping_AddParameterAliases_RegistersAliasesAndCasingVariants()
    {
        var mapping = new ToolMapping();
        var returned = mapping
            .AddParameterAliases("filePath", "path", "file")
            .AddParameterAliases("path", "source_file");

        Assert.Same(mapping, returned);

        // Canonical and aliases map to "filePath"
        Assert.Equal("filePath", mapping.ParameterAliases["filePath"]);
        Assert.Equal("filePath", mapping.ParameterAliases["file_path"]);
        Assert.Equal("filePath", mapping.ParameterAliases["FilePath"]);
        Assert.Equal("filePath", mapping.ParameterAliases["path"]);
        Assert.Equal("filePath", mapping.ParameterAliases["file"]);
        Assert.Equal("filePath", mapping.ParameterAliases["source_file"]);
        Assert.Equal("filePath", mapping.ParameterAliases["sourceFile"]);
        Assert.Equal("filePath", mapping.ParameterAliases["SourceFile"]);

        // NormalizeCall test with dispatcher
        var dispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Tools = { ["read_file"] = mapping }
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create((string filePath) => filePath, "read_file"));

        var call1 = dispatcher.NormalizeCall("read_file", new Dictionary<string, object?> { ["sourceFile"] = "test.txt" });
        Assert.Equal("test.txt", call1.Arguments["filePath"]);

        var call2 = dispatcher.NormalizeCall("read_file", new Dictionary<string, object?> { ["file"] = "other.txt" });
        Assert.Equal("other.txt", call2.Arguments["filePath"]);
    }

    [Fact]
    public void ToolMapping_AddParameterAliases_ValidatesArguments_AndIgnoresEmptyElements()
    {
        var mapping = new ToolMapping();

        Assert.Throws<ArgumentException>(() => mapping.AddParameterAliases("", "alias"));
        Assert.Throws<ArgumentException>(() => mapping.AddParameterAliases("   ", "alias"));
        Assert.Throws<ArgumentNullException>(() => mapping.AddParameterAliases("param", (IEnumerable<string>)null!));

        // Ignores null or whitespace elements in aliases without throwing
        mapping.AddParameterAliases("param", ["", "   ", null!, "valid_alias"]);
        Assert.Equal("param", mapping.ParameterAliases["valid_alias"]);
        Assert.False(mapping.ParameterAliases.ContainsKey(""));
    }

    #endregion
}
