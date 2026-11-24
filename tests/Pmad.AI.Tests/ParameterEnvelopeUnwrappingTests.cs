using System.Text.Json;
using Microsoft.Extensions.AI;
using Pmad.AI.Tools;

namespace Pmad.AI.Tests;

public class ParameterEnvelopeUnwrappingTests
{
    public record QuestionItem(string Prompt, string? Rationale = null, List<string>? Options = null);

    [Fact]
    public async Task ParametersEnvelope_WithNestedQuestionsObject_UnwrapsAndBindsCleanly()
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            Telemetry = telemetry,
            SessionId = "s-questions"
        });

        List<QuestionItem>? captured = null;
        var tool = AIFunctionFactory.Create(
            (List<QuestionItem> questions) =>
            {
                captured = questions;
                return $"Received {questions.Count} question(s)";
            },
            "ask_user");
        dispatcher.RegisterTool(tool);

        var json = """
            {
              "parameters": {
                "questions": [
                  { "prompt": "Proceed?", "rationale": "Need confirmation", "options": ["A", "B"] }
                ]
              }
            }
            """;

        var dispatched = dispatcher.NormalizeCall("ask_user", json);
        Assert.False(dispatched.Arguments.ContainsKey("parameters"));
        Assert.True(dispatched.Arguments.ContainsKey("questions"));

        var snapshotAfterNormalize = telemetry.CreateSnapshot("s-questions");
        Assert.Equal(1, snapshotAfterNormalize.ParameterEnvelopesUnwrapped);
        Assert.Equal(1, snapshotAfterNormalize.TotalHits);

        var result = await dispatcher.ExecuteAsync("ask_user", json);
        Assert.Equal("Received 1 question(s)", result?.ToString());
        Assert.NotNull(captured);
        Assert.Single(captured);
        Assert.Equal("Proceed?", captured[0].Prompt);
        Assert.Equal("Need confirmation", captured[0].Rationale);
        Assert.Equal(["A", "B"], captured[0].Options);

        var snapshot = telemetry.CreateSnapshot("s-questions");
        Assert.Equal(2, snapshot.ParameterEnvelopesUnwrapped);
        Assert.Equal(2, snapshot.TotalHits);
    }

    [Fact]
    public void ArgumentsEnvelope_UnwrapsAndCombinesWithParameterAliasAndCasingResolution()
    {
        var telemetry = new LenientDispatchTelemetry();
        var config = new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["read_file"] = new ToolMapping(
                    parameterAliases: new Dictionary<string, IEnumerable<string>>
                    {
                        ["path"] = ["file_path", "filePath"]
                    })
            }
        };

        var dispatcher = new LenientToolDispatcher(config, new LenientToolDispatcherOptions
        {
            Telemetry = telemetry,
            SessionId = "s-alias"
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create((string path, int offset) => $"{path}:{offset}", "read_file"));

        var rawArgs = new Dictionary<string, object?>
        {
            ["ARGUMENTS"] = new Dictionary<string, object?>
            {
                ["file_path"] = "foo.cs",
                ["OFFSET"] = "42"
            }
        };

        var dispatched = dispatcher.NormalizeCall("read_file", rawArgs);

        Assert.False(dispatched.Arguments.ContainsKey("ARGUMENTS"));
        Assert.Equal("foo.cs", dispatched.Arguments["path"]);
        Assert.Equal(42, dispatched.Arguments["offset"]);

        // Caller's dictionary must not be mutated
        Assert.True(rawArgs.ContainsKey("ARGUMENTS"));

        var snapshot = telemetry.CreateSnapshot("s-alias");
        Assert.Equal(1, snapshot.ParameterEnvelopesUnwrapped);
        Assert.Equal(1, snapshot.ParameterAliasesResolved);
        Assert.Equal(1, snapshot.CasingNormalized);
        Assert.Equal(3, snapshot.TotalHits);
    }

    [Fact]
    public void ParametersEnvelope_WithAliasInside_ResolvesAliasToCanonical()
    {
        var config = new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["read_file"] = new ToolMapping(
                    parameterAliases: new Dictionary<string, IEnumerable<string>>
                    {
                        ["path"] = ["file_path"]
                    })
            }
        };
        var dispatcher = new LenientToolDispatcher(config);
        dispatcher.RegisterTool(AIFunctionFactory.Create((string path) => path, "read_file"));

        var dispatched = dispatcher.NormalizeCall("read_file", new Dictionary<string, object?>
        {
            ["parameters"] = new Dictionary<string, object?>
            {
                ["file_path"] = "foo.cs"
            }
        });

        Assert.Equal("foo.cs", dispatched.Arguments["path"]);
        Assert.False(dispatched.Arguments.ContainsKey("parameters"));
        Assert.False(dispatched.Arguments.ContainsKey("file_path"));
    }

    [Fact]
    public void StringifiedJsonEnvelope_UnwrapsCleanly()
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            Telemetry = telemetry
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create((string path) => path, "read_file"));

        var dispatched = dispatcher.NormalizeCall("read_file", new Dictionary<string, object?>
        {
            ["parameters"] = "{\"path\": \"foo.cs\"}"
        });

        Assert.Equal("foo.cs", dispatched.Arguments["path"]);
        Assert.False(dispatched.Arguments.ContainsKey("parameters"));

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(1, snapshot.ParameterEnvelopesUnwrapped);
        Assert.Equal(0, snapshot.StringifiedJsonRepaired);
        Assert.Equal(1, snapshot.TotalHits);
    }

    [Fact]
    public void StringifiedJsonEnvelope_WithJsonElementStringAndTruncation_RepairsAndUnwraps()
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            Telemetry = telemetry
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create((string path, int limit) => $"{path}:{limit}", "read_file"));

        using var doc = JsonDocument.Parse("""
            {
              "parameters": "{\"path\": \"foo.cs\", \"limit\": 10"
            }
            """);

        var rawArgs = new Dictionary<string, object?>
        {
            ["parameters"] = doc.RootElement.GetProperty("parameters").Clone()
        };

        var dispatched = dispatcher.NormalizeCall("read_file", rawArgs);
        Assert.Equal("foo.cs", dispatched.Arguments["path"]);
        Assert.Equal(10, dispatched.Arguments["limit"]);
        Assert.False(dispatched.Arguments.ContainsKey("parameters"));

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(1, snapshot.ParameterEnvelopesUnwrapped);
        Assert.Equal(1, snapshot.StringifiedJsonRepaired);
        Assert.Equal(2, snapshot.TotalHits);
    }

    [Fact]
    public void MarkdownFencedEnvelopeValue_IsLeftUntouchedWithoutTelemetry()
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            Telemetry = telemetry
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create((string path) => path, "read_file"));

        const string fenced = "```json\n{\"path\": \"foo.cs\"}\n```";
        var dispatched = dispatcher.NormalizeCall("read_file", new Dictionary<string, object?>
        {
            ["parameters"] = fenced
        });

        Assert.True(dispatched.Arguments.ContainsKey("parameters"));
        Assert.Equal(fenced, dispatched.Arguments["parameters"]);
        Assert.False(dispatched.Arguments.ContainsKey("path"));

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(0, snapshot.ParameterEnvelopesUnwrapped);
        Assert.Equal(0, snapshot.StringifiedJsonRepaired);
        Assert.Equal(0, snapshot.TotalHits);
    }

    [Theory]
    [InlineData("parameters")]
    [InlineData("arguments")]
    public void SchemaGuard_DeclaredEnvelopeParameter_IsNotUnwrapped(string paramName)
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            Telemetry = telemetry
        });

        if (paramName == "parameters")
        {
            dispatcher.RegisterTool(AIFunctionFactory.Create(
                (Dictionary<string, string> parameters) => parameters.Count,
                "configure_tool"));
        }
        else
        {
            dispatcher.RegisterTool(AIFunctionFactory.Create(
                (Dictionary<string, string> arguments) => arguments.Count,
                "configure_tool"));
        }

        var dispatched = dispatcher.NormalizeCall("configure_tool", new Dictionary<string, object?>
        {
            [paramName] = new Dictionary<string, object?> { ["foo"] = "bar" }
        });

        Assert.True(dispatched.Arguments.ContainsKey(paramName));
        Assert.False(dispatched.Arguments.ContainsKey("foo"));

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(0, snapshot.ParameterEnvelopesUnwrapped);
        Assert.Equal(0, snapshot.TotalHits);
    }

    [Theory]
    [InlineData("parameters")]
    [InlineData("arguments")]
    public async Task SchemaGuard_DeclaredEnvelopeParameter_ExecutesEndToEnd(string paramName)
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            Telemetry = telemetry
        });

        Dictionary<string, string>? captured = null;
        if (paramName == "parameters")
        {
            dispatcher.RegisterTool(AIFunctionFactory.Create(
                (Dictionary<string, string> parameters) =>
                {
                    captured = parameters;
                    return $"count={parameters.Count}";
                },
                "configure_tool"));
        }
        else
        {
            dispatcher.RegisterTool(AIFunctionFactory.Create(
                (Dictionary<string, string> arguments) =>
                {
                    captured = arguments;
                    return $"count={arguments.Count}";
                },
                "configure_tool"));
        }

        var json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            [paramName] = new Dictionary<string, string> { ["foo"] = "bar" }
        });
        var result = await dispatcher.ExecuteAsync("configure_tool", json);

        Assert.Equal("count=1", result?.ToString());
        Assert.NotNull(captured);
        Assert.Equal("bar", captured["foo"]);

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(0, snapshot.ParameterEnvelopesUnwrapped);
        Assert.Equal(0, snapshot.TotalHits);
    }

    [Fact]
    public async Task SchemaGuard_CrossWrappedEnvelope_UnwrapsOuterAndStopsAtDeclaredParameter()
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            Telemetry = telemetry
        });

        Dictionary<string, string>? captured = null;
        dispatcher.RegisterTool(AIFunctionFactory.Create(
            (Dictionary<string, string> parameters) =>
            {
                captured = parameters;
                return $"count={parameters.Count}";
            },
            "configure_tool"));

        var json = """{"arguments": {"parameters": {"foo": "bar"}}}""";
        var result = await dispatcher.ExecuteAsync("configure_tool", json);

        Assert.Equal("count=1", result?.ToString());
        Assert.NotNull(captured);
        Assert.Equal("bar", captured["foo"]);

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(1, snapshot.ParameterEnvelopesUnwrapped);
        Assert.Equal(1, snapshot.TotalHits);
    }

    [Fact]
    public void MappingGuard_ParameterAliasNamedParameters_IsNotUnwrapped()
    {
        var telemetry = new LenientDispatchTelemetry();
        var config = new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["run_custom"] = new ToolMapping(
                    parameterAliases: new Dictionary<string, IEnumerable<string>>
                    {
                        ["payload"] = ["parameters"]
                    })
            }
        };
        var dispatcher = new LenientToolDispatcher(config, new LenientToolDispatcherOptions
        {
            Telemetry = telemetry
        });

        var dispatched = dispatcher.NormalizeCall("run_custom", new Dictionary<string, object?>
        {
            ["parameters"] = new Dictionary<string, object?> { ["inner"] = "val" }
        });

        Assert.True(dispatched.Arguments.ContainsKey("payload"));
        Assert.False(dispatched.Arguments.ContainsKey("inner"));

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(0, snapshot.ParameterEnvelopesUnwrapped);
        Assert.Equal(1, snapshot.ParameterAliasesResolved);
    }

    [Fact]
    public void NonObjectEnvelopeValues_AreLeftUntouchedWithoutThrowing()
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            Telemetry = telemetry
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create((string path) => path, "read_file"));

        var cases = new object?[]
        {
            "plain text",
            "[1, 2]",
            new List<object?> { 1, 2 },
            42,
            null
        };

        foreach (var value in cases)
        {
            var dispatched = dispatcher.NormalizeCall("read_file", new Dictionary<string, object?>
            {
                ["parameters"] = value
            });

            Assert.True(dispatched.Arguments.ContainsKey("parameters"));
            Assert.False(dispatched.Arguments.ContainsKey("path"));
        }

        // Also test JSON array JsonElement via NormalizeCall(name, json)
        var arrayJsonDispatched = dispatcher.NormalizeCall("read_file", """{"parameters": [1, 2]}""");
        Assert.True(arrayJsonDispatched.Arguments.ContainsKey("parameters"));

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(0, snapshot.ParameterEnvelopesUnwrapped);
        Assert.Equal(0, snapshot.TotalHits);
    }

    [Fact]
    public void DisabledViaOptions_LeavesEnvelopeUntouched_AndClonePreservesSetting()
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            UnwrapParameterEnvelopes = false,
            Telemetry = telemetry
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create((string path) => path, "read_file"));

        var dispatched = dispatcher.NormalizeCall("read_file", """{"parameters": {"path": "foo.cs"}}""");
        Assert.True(dispatched.Arguments.ContainsKey("parameters"));
        Assert.False(dispatched.Arguments.ContainsKey("path"));

        var cloned = dispatcher.Clone();
        var clonedDispatched = cloned.NormalizeCall("read_file", """{"arguments": {"path": "bar.cs"}}""");
        Assert.True(clonedDispatched.Arguments.ContainsKey("arguments"));
        Assert.False(clonedDispatched.Arguments.ContainsKey("path"));

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(0, snapshot.ParameterEnvelopesUnwrapped);
        Assert.Equal(0, snapshot.TotalHits);
    }

    [Fact]
    public void NestedEnvelopes_RespectsBoundedDepthCap()
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            Telemetry = telemetry
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create((string path) => path, "read_file"));

        // 2 levels: unwrapped completely
        var twoLevels = dispatcher.NormalizeCall("read_file", """{"arguments": {"parameters": {"path": "foo.cs"}}}""");
        Assert.Equal("foo.cs", twoLevels.Arguments["path"]);
        Assert.False(twoLevels.Arguments.ContainsKey("arguments"));
        Assert.False(twoLevels.Arguments.ContainsKey("parameters"));
        Assert.Equal(2, telemetry.CreateSnapshot().ParameterEnvelopesUnwrapped);

        // 3 levels: stops after 2 unwraps
        var telemetry3 = new LenientDispatchTelemetry();
        var dispatcher3 = new LenientToolDispatcher(new LenientToolDispatcherOptions { Telemetry = telemetry3 });
        dispatcher3.RegisterTool(AIFunctionFactory.Create((string path) => path, "read_file"));

        var threeLevels = dispatcher3.NormalizeCall("read_file", """{"arguments": {"parameters": {"arguments": {"path": "foo.cs"}}}}""");
        Assert.True(threeLevels.Arguments.ContainsKey("arguments"));
        Assert.False(threeLevels.Arguments.ContainsKey("path"));
        Assert.Equal(2, telemetry3.CreateSnapshot().ParameterEnvelopesUnwrapped);
    }

    [Fact]
    public void MultiKeyEnvelope_PreservesExtraTopLevelKeys_AndSkipsWhenDeclaredParamPresentAtTopLevel()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.RegisterTool(AIFunctionFactory.Create((string path) => path, "read_file"));

        // Top-level has an undeclared key "extra" and envelope "parameters"; none of the schema parameters ("path") are at top level.
        var unwrapped = dispatcher.NormalizeCall("read_file", new Dictionary<string, object?>
        {
            ["extra"] = "keep",
            ["parameters"] = new Dictionary<string, object?>
            {
                ["path"] = "foo.cs",
                ["extra"] = "ignored"
            }
        });

        Assert.Equal("foo.cs", unwrapped.Arguments["path"]);
        Assert.Equal("keep", unwrapped.Arguments["extra"]);
        Assert.False(unwrapped.Arguments.ContainsKey("parameters"));

        // Top-level already has declared parameter "path": do not unwrap "parameters".
        var kept = dispatcher.NormalizeCall("read_file", new Dictionary<string, object?>
        {
            ["path"] = "top.cs",
            ["parameters"] = new Dictionary<string, object?>
            {
                ["path"] = "inner.cs"
            }
        });

        Assert.Equal("top.cs", kept.Arguments["path"]);
        Assert.True(kept.Arguments.ContainsKey("parameters"));
    }

    [Fact]
    public void TranslationTool_UnwrapsParameterEnvelopeBeforeRendering()
    {
        var telemetry = new LenientDispatchTelemetry();
        var config = new ToolDispatcherConfiguration
        {
            Translations =
            [
                new ToolTranslation
                {
                    SourceTool = "list_dir",
                    TargetTool = "run_shell",
                    CommandTemplate = "ls {path}",
                    EscapeMethod = ParameterEscapeMethod.None
                }
            ]
        };

        var dispatcher = new LenientToolDispatcher(config, new LenientToolDispatcherOptions
        {
            Telemetry = telemetry
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create((string command) => command, "run_shell"));

        var dispatched = dispatcher.NormalizeCall("list_dir", """{"parameters": {"path": "src"}}""");
        Assert.True(dispatched.WasTranslated);
        Assert.Equal("run_shell", dispatched.CanonicalToolName);
        Assert.Equal("ls src", dispatched.RenderedCommand);
        Assert.Equal(1, telemetry.CreateSnapshot().ParameterEnvelopesUnwrapped);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("{\"truncated_key")]
    public void UnrecoverableStringifiedEnvelope_IsLeftUntouched_WithoutTelemetry(string rawEnvelope)
    {
        var telemetry = new LenientDispatchTelemetry();
        var config = new ToolDispatcherConfiguration
        {
            Translations =
            [
                new ToolTranslation
                {
                    SourceTool = "list_dir",
                    TargetTool = "run_shell",
                    CommandTemplate = "ls {path}",
                    EscapeMethod = ParameterEscapeMethod.None
                }
            ]
        };
        var dispatcher = new LenientToolDispatcher(config, new LenientToolDispatcherOptions
        {
            Telemetry = telemetry
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create((string path) => path, "read_file"));
        dispatcher.RegisterTool(AIFunctionFactory.Create((string command) => command, "run_shell"));

        var dispatched = dispatcher.NormalizeCall("read_file", new Dictionary<string, object?>
        {
            ["parameters"] = rawEnvelope
        });

        Assert.True(dispatched.Arguments.ContainsKey("parameters"));
        Assert.False(dispatched.Arguments.ContainsKey("path"));
        Assert.Equal(0, telemetry.CreateSnapshot().ParameterEnvelopesUnwrapped);

        // Translation path bypasses NormalizeValueDeep, proving TryExtractEnvelopeEntriesFromString
        // itself rejects unrecoverable payloads without recording StringifiedJsonRepaired or unwrapping.
        var translationTelemetry = new LenientDispatchTelemetry();
        var translationDispatcher = new LenientToolDispatcher(config, new LenientToolDispatcherOptions
        {
            Telemetry = translationTelemetry
        });
        translationDispatcher.RegisterTool(AIFunctionFactory.Create((string command) => command, "run_shell"));

        Assert.Throws<KeyNotFoundException>(() => translationDispatcher.NormalizeCall("list_dir", new Dictionary<string, object?>
        {
            ["parameters"] = rawEnvelope
        }));

        var snapshot = translationTelemetry.CreateSnapshot();
        Assert.Equal(0, snapshot.ParameterEnvelopesUnwrapped);
        Assert.Equal(0, snapshot.StringifiedJsonRepaired);
        Assert.Equal(0, snapshot.TotalHits);
    }

    [Fact]
    public void ValidEmptyEnvelopes_UnwrapToEmptyArguments()
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            Telemetry = telemetry
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create(() => "ok", "ping"));

        var emptyObject = dispatcher.NormalizeCall("ping", """{"parameters": {}}""");
        Assert.Empty(emptyObject.Arguments);

        var emptyStringified = dispatcher.NormalizeCall("ping", new Dictionary<string, object?>
        {
            ["arguments"] = "{}"
        });
        Assert.Empty(emptyStringified.Arguments);

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(2, snapshot.ParameterEnvelopesUnwrapped);
        Assert.Equal(0, snapshot.StringifiedJsonRepaired);
        Assert.Equal(2, snapshot.TotalHits);
    }

    [Fact]
    public void DictionaryVariants_HandlesNonStringKeysTypedDictAndReadOnlyDictionary()
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            Telemetry = telemetry
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create((string path) => path, "read_file"));

        // Non-generic IDictionary with non-string keys must NOT be unwrapped or wiped
        var intKeyed = new Dictionary<int, string> { [1] = "foo.cs" };
        var nonStringResult = dispatcher.NormalizeCall("read_file", new Dictionary<string, object?>
        {
            ["parameters"] = intKeyed
        });
        Assert.True(nonStringResult.Arguments.ContainsKey("parameters"));
        Assert.False(nonStringResult.Arguments.ContainsKey("path"));
        Assert.Equal(0, telemetry.CreateSnapshot().ParameterEnvelopesUnwrapped);

        // Typed IDictionary (Dictionary<string, string> implements non-generic IDictionary) unwraps cleanly
        var stringKeyed = dispatcher.NormalizeCall("read_file", new Dictionary<string, object?>
        {
            ["parameters"] = new Dictionary<string, string> { ["path"] = "typed.cs" }
        });
        Assert.Equal("typed.cs", stringKeyed.Arguments["path"]);
        Assert.False(stringKeyed.Arguments.ContainsKey("parameters"));

        // Custom IReadOnlyDictionary<string, object?> that does NOT implement IDictionary unwraps cleanly
        var readOnlyDict = new ReadOnlyOnlyDictionary(new Dictionary<string, object?> { ["path"] = "ro.cs" });
        var roResult = dispatcher.NormalizeCall("read_file", new Dictionary<string, object?>
        {
            ["arguments"] = readOnlyDict
        });
        Assert.Equal("ro.cs", roResult.Arguments["path"]);
        Assert.False(roResult.Arguments.ContainsKey("arguments"));

        Assert.Equal(2, telemetry.CreateSnapshot().ParameterEnvelopesUnwrapped);
    }

    [Fact]
    public void MultipleTopLevelEnvelopeKeys_SkipsNonObjectAndUnwrapsValidEnvelope()
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            Telemetry = telemetry
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create((string path) => path, "read_file"));

        var dispatched = dispatcher.NormalizeCall("read_file", new Dictionary<string, object?>
        {
            ["parameters"] = "plain note",
            ["arguments"] = new Dictionary<string, object?> { ["path"] = "foo.cs" }
        });

        Assert.Equal("foo.cs", dispatched.Arguments["path"]);
        Assert.Equal("plain note", dispatched.Arguments["parameters"]);
        Assert.False(dispatched.Arguments.ContainsKey("arguments"));
        Assert.Equal(1, telemetry.CreateSnapshot().ParameterEnvelopesUnwrapped);
    }

    [Fact]
    public void InnerDuplicateAndCaseVariantKeys_UseLastValueWinsInsteadOfNullOrFirst()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.RegisterTool(AIFunctionFactory.Create((string path) => path, "read_file"));

        // Case-variant inside envelope: later "path" must override earlier "PATH": null
        var caseVariant = dispatcher.NormalizeCall("read_file", """{"parameters": {"PATH": null, "path": "foo.cs"}}""");
        Assert.Equal("foo.cs", caseVariant.Arguments["path"]);
        Assert.Single(caseVariant.Arguments.Keys, k => string.Equals(k, "path", StringComparison.Ordinal));
        Assert.DoesNotContain("PATH", caseVariant.Arguments.Keys, StringComparer.Ordinal);

        // Exact duplicate key inside raw JSON envelope: standard JSON last-wins
        var duplicate = dispatcher.NormalizeCall("read_file", """{"parameters": {"path": "first.cs", "path": "second.cs"}}""");
        Assert.Equal("second.cs", duplicate.Arguments["path"]);

        // Trailing null case-variant inside envelope must NOT overwrite an earlier non-null value
        var trailingNullVariant = dispatcher.NormalizeCall("read_file", """{"parameters": {"path": "foo.cs", "PATH": null}}""");
        Assert.Equal("foo.cs", trailingNullVariant.Arguments["path"]);
        Assert.Single(trailingNullVariant.Arguments.Keys, k => string.Equals(k, "path", StringComparison.Ordinal));
        Assert.DoesNotContain("PATH", trailingNullVariant.Arguments.Keys, StringComparer.Ordinal);

        // Trailing exact-duplicate null inside envelope must NOT overwrite an earlier non-null value
        var trailingNullDuplicate = dispatcher.NormalizeCall("read_file", """{"parameters": {"path": "first.cs", "path": null}}""");
        Assert.Equal("first.cs", trailingNullDuplicate.Arguments["path"]);
    }

    [Fact]
    public void MappingAndJsonSuffixGuards_ProtectDeclaredParametersAndAllowUndeclaredSiblings()
    {
        var telemetry = new LenientDispatchTelemetry();
        var config = new ToolDispatcherConfiguration
        {
            Tools =
            {
                ["computed_tool"] = new ToolMapping(
                    computedParameters: new Dictionary<string, ParameterComputer>
                    {
                        ["target"] = args => args.TryGetValue("src", out var s) ? s : null
                    }),
                ["promoted_tool"] = new ToolMapping()
                    .AddPromotedCollectionParameter("items", "item_text"),
                ["propagated_tool"] = new ToolMapping()
                    .AddPropagatedParameter("parameters"),
                ["range_tool"] = new ToolMapping()
                    .MapEndLineToLimit("offset", "limit", ["end_line"]),
                ["nested_envelope_tool"] = new ToolMapping()
                    .AddNestedParameterAlias("parameters", "inner_key", "inner"),
                ["nested_config_tool"] = new ToolMapping()
                    .AddNestedParameterAlias("config.nested", "field", "fld")
            }
        };

        var dispatcher = new LenientToolDispatcher(config, new LenientToolDispatcherOptions
        {
            Telemetry = telemetry
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create((string path) => path, "read_file"));
        dispatcher.RegisterTool(AIFunctionFactory.Create((List<string> questions) => questions.Count, "ask_user"));

        // 1. ComputedParameters declares "target" -> top-level "target" blocks unwrapping "parameters"
        var computedBlocked = dispatcher.NormalizeCall("computed_tool", new Dictionary<string, object?>
        {
            ["target"] = "top",
            ["parameters"] = new Dictionary<string, object?> { ["src"] = "inner" }
        });
        Assert.True(computedBlocked.Arguments.ContainsKey("parameters"));

        // ComputedParameters counts as declared metadata -> undeclared sibling "extra" allows unwrapping
        var computedAllowed = dispatcher.NormalizeCall("computed_tool", new Dictionary<string, object?>
        {
            ["extra"] = "keep",
            ["parameters"] = new Dictionary<string, object?> { ["src"] = "inner" }
        });
        Assert.False(computedAllowed.Arguments.ContainsKey("parameters"));
        Assert.Equal("inner", computedAllowed.Arguments["src"]);

        // 2. PromotedCollectionParameters declares "item_text" -> blocks unwrapping "parameters"
        var promotedBlocked = dispatcher.NormalizeCall("promoted_tool", new Dictionary<string, object?>
        {
            ["item_text"] = "hello",
            ["parameters"] = new Dictionary<string, object?> { ["other"] = "val" }
        });
        Assert.True(promotedBlocked.Arguments.ContainsKey("parameters"));

        // 3. PropagatedParameters declares "parameters" directly -> never unwrapped
        var propagatedBlocked = dispatcher.NormalizeCall("propagated_tool", new Dictionary<string, object?>
        {
            ["parameters"] = new Dictionary<string, object?> { ["inner"] = "val" }
        });
        Assert.True(propagatedBlocked.Arguments.ContainsKey("parameters"));
        Assert.False(propagatedBlocked.Arguments.ContainsKey("inner"));

        // 4. RangeToLimitRules declares "end_line" -> blocks unwrapping "parameters"
        var rangeBlocked = dispatcher.NormalizeCall("range_tool", new Dictionary<string, object?>
        {
            ["end_line"] = 10,
            ["parameters"] = new Dictionary<string, object?> { ["offset"] = 1 }
        });
        Assert.True(rangeBlocked.Arguments.ContainsKey("parameters"));

        // 5. _json-suffixed parameter ("questions_json") matches schema "questions" -> blocks unwrapping "parameters"
        var jsonSuffixBlocked = dispatcher.NormalizeCall("ask_user", new Dictionary<string, object?>
        {
            ["questions_json"] = "[\"Q1\"]",
            ["parameters"] = new Dictionary<string, object?> { ["extra"] = "val" }
        });
        Assert.True(jsonSuffixBlocked.Arguments.ContainsKey("parameters"));

        // 6. NestedParameterAliases targeting root "parameters" blocks unwrapping "parameters"
        var nestedEnvelopeBlocked = dispatcher.NormalizeCall("nested_envelope_tool", new Dictionary<string, object?>
        {
            ["parameters"] = new Dictionary<string, object?> { ["inner"] = "val" }
        });
        Assert.True(nestedEnvelopeBlocked.Arguments.ContainsKey("parameters"));
        Assert.False(nestedEnvelopeBlocked.Arguments.ContainsKey("inner"));
        Assert.False(nestedEnvelopeBlocked.Arguments.ContainsKey("inner_key"));

        // 7. NestedParameterAliases targeting root "config" blocks unwrapping when "config" is at top level,
        //    and counts as declared metadata so an undeclared sibling "extra" allows unwrapping "parameters".
        var nestedConfigBlocked = dispatcher.NormalizeCall("nested_config_tool", new Dictionary<string, object?>
        {
            ["config"] = new Dictionary<string, object?> { ["fld"] = "a" },
            ["parameters"] = new Dictionary<string, object?> { ["other"] = "val" }
        });
        Assert.True(nestedConfigBlocked.Arguments.ContainsKey("parameters"));

        var nestedConfigAllowed = dispatcher.NormalizeCall("nested_config_tool", new Dictionary<string, object?>
        {
            ["extra"] = "keep",
            ["parameters"] = new Dictionary<string, object?> { ["other"] = "val" }
        });
        Assert.False(nestedConfigAllowed.Arguments.ContainsKey("parameters"));
        Assert.Equal("val", nestedConfigAllowed.Arguments["other"]);

        Assert.Equal(2, telemetry.CreateSnapshot().ParameterEnvelopesUnwrapped);
    }

    [Fact]
    public void UnregisteredToolWithoutMapping_UnwrapsSingleKeyEnvelope_SkipsMultiKeyEnvelope()
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            Telemetry = telemetry
        });

        var singleKey = dispatcher.NormalizeCall("unknown_tool", """{"parameters": {"path": "foo.cs"}}""");
        Assert.Equal("foo.cs", singleKey.Arguments["path"]);
        Assert.False(singleKey.Arguments.ContainsKey("parameters"));

        // Multi-key on unregistered tool with no metadata must NOT unwrap
        var multiKey = dispatcher.NormalizeCall("unknown_tool", new Dictionary<string, object?>
        {
            ["extra"] = "keep",
            ["parameters"] = new Dictionary<string, object?> { ["path"] = "foo.cs" }
        });
        Assert.True(multiKey.Arguments.ContainsKey("parameters"));
        Assert.False(multiKey.Arguments.ContainsKey("path"));

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(1, snapshot.ParameterEnvelopesUnwrapped);
        Assert.Equal(0, snapshot.StringifiedJsonRepaired);
    }

    [Fact]
    public void TranslationTool_WhenEnvelopeNameIsDeclared_DoesNotUnwrap()
    {
        var telemetry = new LenientDispatchTelemetry();
        var config = new ToolDispatcherConfiguration
        {
            Translations =
            [
                new ToolTranslation
                {
                    SourceTool = "run_tpl",
                    TargetTool = "run_shell",
                    CommandTemplate = "cat {parameters}",
                    EscapeMethod = ParameterEscapeMethod.Bash
                },
                new ToolTranslation
                {
                    SourceTool = "run_alias_tpl",
                    TargetTool = "run_shell",
                    CommandTemplate = "cat {payload}",
                    EscapeMethod = ParameterEscapeMethod.Bash,
                    ParameterAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["arguments"] = "payload"
                    }
                },
                new ToolTranslation
                {
                    SourceTool = "ls_dir",
                    TargetTool = "run_shell",
                    CommandTemplate = "ls {target}",
                    Aliases = ["list_dir"],
                    EscapeMethod = ParameterEscapeMethod.Bash
                }
            ],
            Tools =
            {
                ["list_dir"] = new ToolMapping(
                    parameterAliases: new Dictionary<string, IEnumerable<string>>
                    {
                        ["target"] = ["parameters"]
                    })
            }
        };

        var dispatcher = new LenientToolDispatcher(config, new LenientToolDispatcherOptions
        {
            Telemetry = telemetry
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create((string command) => command, "run_shell"));

        // 1. Template placeholder {parameters} with JSON-object payload (would unwrap if guard failed)
        const string jsonPayload = """{"nested":"val"}""";
        var tplDispatched = dispatcher.NormalizeCall("run_tpl", new Dictionary<string, object?>
        {
            ["parameters"] = jsonPayload
        });
        Assert.Equal($"cat '{jsonPayload}'", tplDispatched.RenderedCommand);

        // 2. translation.ParameterAliases maps "arguments" -> "payload" with JSON-object payload
        var aliasDispatched = dispatcher.NormalizeCall("run_alias_tpl", new Dictionary<string, object?>
        {
            ["arguments"] = jsonPayload
        });
        Assert.Equal($"cat '{jsonPayload}'", aliasDispatched.RenderedCommand);

        // 3. Namespaced originalName matching mapping registered under stripped alias ("list_dir")
        var namespacedDispatched = dispatcher.NormalizeCall(" workspace__list_dir ", new Dictionary<string, object?>
        {
            ["parameters"] = jsonPayload
        });
        Assert.Equal($"ls '{jsonPayload}'", namespacedDispatched.RenderedCommand);

        // 4. Multi-key guard runs before TryExtractEnvelopeEntries (no StringifiedJsonRepaired recorded)
        var multiKeyBlocked = dispatcher.NormalizeCall("ls_dir", new Dictionary<string, object?>
        {
            ["target"] = "src",
            ["parameters"] = "{\"extra\": 1"
        });
        Assert.Equal("ls 'src'", multiKeyBlocked.RenderedCommand);

        var snapshot = telemetry.CreateSnapshot();
        Assert.Equal(0, snapshot.ParameterEnvelopesUnwrapped);
        Assert.Equal(0, snapshot.StringifiedJsonRepaired);
    }

    [Fact]
    public void ShadowedInnerEnvelopeKeys_AreNotDroppedOrFalselyUnwrapped()
    {
        var telemetry = new LenientDispatchTelemetry();
        var dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            Telemetry = telemetry
        });
        dispatcher.RegisterTool(AIFunctionFactory.Create((string path) => path, "read_file"));

        // Non-object "parameters" alongside "arguments" wrapping another "parameters":
        // unwrapping "arguments" would silently drop the inner "parameters" payload.
        var objectShadowed = dispatcher.NormalizeCall("read_file", new Dictionary<string, object?>
        {
            ["parameters"] = "plain note",
            ["arguments"] = new Dictionary<string, object?>
            {
                ["parameters"] = new Dictionary<string, object?> { ["path"] = "foo.cs" }
            }
        });

        Assert.Equal("plain note", objectShadowed.Arguments["parameters"]);
        Assert.True(objectShadowed.Arguments.ContainsKey("arguments"));
        Assert.False(objectShadowed.Arguments.ContainsKey("path"));
        Assert.Equal(0, telemetry.CreateSnapshot().ParameterEnvelopesUnwrapped);

        // Translation path bypasses NormalizeValueDeep, proving UnwrapTopLevelParameterEnvelopes
        // itself does not unwrap or emit StringifiedJsonRepaired for a truncated shadowed envelope.
        var translationTelemetry = new LenientDispatchTelemetry();
        var translationDispatcher = new LenientToolDispatcher(new ToolDispatcherConfiguration
        {
            Translations =
            [
                new ToolTranslation
                {
                    SourceTool = "list_dir",
                    TargetTool = "run_shell",
                    CommandTemplate = "ls {target:.}",
                    EscapeMethod = ParameterEscapeMethod.Bash
                }
            ]
        }, new LenientToolDispatcherOptions { Telemetry = translationTelemetry });
        translationDispatcher.RegisterTool(AIFunctionFactory.Create((string command) => command, "run_shell"));

        var stringifiedShadowed = translationDispatcher.NormalizeCall("list_dir", new Dictionary<string, object?>
        {
            ["parameters"] = "plain note",
            ["arguments"] = "{\"parameters\": {\"target\": \"src\""
        });

        Assert.Equal("ls '.'", stringifiedShadowed.RenderedCommand);

        var snapshot = translationTelemetry.CreateSnapshot();
        Assert.Equal(0, snapshot.ParameterEnvelopesUnwrapped);
        Assert.Equal(0, snapshot.StringifiedJsonRepaired);
        Assert.Equal(0, snapshot.TotalHits);
    }

    private sealed class ReadOnlyOnlyDictionary(IReadOnlyDictionary<string, object?> inner)
        : IReadOnlyDictionary<string, object?>
    {
        public object? this[string key] => inner[key];
        public IEnumerable<string> Keys => inner.Keys;
        public IEnumerable<object?> Values => inner.Values;
        public int Count => inner.Count;
        public bool ContainsKey(string key) => inner.ContainsKey(key);
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => inner.GetEnumerator();
        public bool TryGetValue(string key, out object? value) => inner.TryGetValue(key, out value);
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => inner.GetEnumerator();
    }
}
