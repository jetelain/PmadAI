using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.AI;
using Pmad.AI.Tools;

namespace Pmad.AI.MicroBenchmarks;

[Config(typeof(FastInProcessConfig))]
public class LenientToolDispatcherBenchmark
{
    private LenientToolDispatcher _dispatcher = null!;

    private const string ExactJsonArgs = """{"path":"src/Pmad.AI/Service.cs","startLine":1,"endLine":50}""";
    private const string AliasedJsonArgs = """{"file_path":"src/Pmad.AI/Service.cs","start":1,"endLine":50}""";
    private const string TranslationJsonArgs = """{"query":"SafeTokenize","searchPath":"src/"}""";

    private Dictionary<string, object?> _exactDictArgs = null!;
    private Dictionary<string, object?> _aliasedDictArgs = null!;

    [GlobalSetup]
    public void Setup()
    {
        _dispatcher = new LenientToolDispatcher(new LenientToolDispatcherOptions
        {
            StripNamespacePrefixes = true
        });

        var readFileFunction = AIFunctionFactory.Create(
            (string path, int? startLine, int? endLine) => "file content",
            "read_file",
            "Reads file contents.");

        var bashFunction = AIFunctionFactory.Create(
            (string command) => "command output",
            "bash",
            "Executes bash command.");

        _dispatcher.RegisterTool(readFileFunction);
        _dispatcher.RegisterTool(bashFunction);

        _dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools = new Dictionary<string, ToolMapping>(StringComparer.OrdinalIgnoreCase)
            {
                ["read_file"] = new ToolMapping(
                    aliases: ["view_file", "cat", "open_file"],
                    parameterAliases: new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["path"] = ["file_path", "filename", "target"],
                        ["startLine"] = ["start", "from_line"]
                    })
            },
            Translations =
            [
                new ToolTranslation
                {
                    SourceTool = "grep_search",
                    TargetTool = "bash",
                    CommandTemplate = "grep -rn {query} {searchPath:.}",
                    EscapeMethod = ParameterEscapeMethod.Bash
                }
            ]
        });

        _exactDictArgs = new Dictionary<string, object?>
        {
            ["path"] = "src/Pmad.AI/Service.cs",
            ["startLine"] = 1,
            ["endLine"] = 50
        };

        _aliasedDictArgs = new Dictionary<string, object?>
        {
            ["file_path"] = "src/Pmad.AI/Service.cs",
            ["start"] = 1,
            ["endLine"] = 50
        };
    }

    [Benchmark(Baseline = true)]
    public DispatchedToolCall NormalizeCall_ExactDict()
    {
        return _dispatcher.NormalizeCall("read_file", _exactDictArgs);
    }

    [Benchmark]
    public DispatchedToolCall NormalizeCall_AliasedDict_WithNamespace()
    {
        return _dispatcher.NormalizeCall("workspace.view_file", _aliasedDictArgs);
    }

    [Benchmark]
    public DispatchedToolCall NormalizeCall_ExactJsonString()
    {
        return _dispatcher.NormalizeCall("read_file", ExactJsonArgs);
    }

    [Benchmark]
    public DispatchedToolCall NormalizeCall_AliasedJsonString_WithNamespace()
    {
        return _dispatcher.NormalizeCall("workspace.view_file", AliasedJsonArgs);
    }

    [Benchmark]
    public DispatchedToolCall NormalizeCall_TranslationToShellCommand()
    {
        return _dispatcher.NormalizeCall("grep_search", TranslationJsonArgs);
    }
}
