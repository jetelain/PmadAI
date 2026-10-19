using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Pmad.AI.SourceGenerators;
using Pmad.AI.Tools;

namespace Pmad.AI.Tests;

public class ToolDispatcherSourceGeneratorTests
{
    private static Compilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ToolAliasAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.AI.AIFunctionFactory).Assembly.Location)
        };

        var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimePath = Path.Combine(coreDir, "System.Runtime.dll");
        if (File.Exists(runtimePath))
        {
            references.Add(MetadataReference.CreateFromFile(runtimePath));
        }

        return CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Fact]
    public void Generator_EmitsGeneratedToolMappings()
    {
        var source = """
        using Pmad.AI;
        using Pmad.AI.Tools;

        public static class MyTools
        {
            [ToolAlias("read_file", "cat", Name = "read")]
            [ToolTranslation("list_dir", "ls -la -- {path:.}", EscapeMethod = ParameterEscapeMethod.Bash)]
            public static void ReadFile([ToolParameterAlias("filePath")] string path)
            {
            }
        }
        """;

        var compilation = CreateCompilation(source);
        var generator = new ToolDispatcherGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var runResult = driver.GetRunResult();
        Assert.Single(runResult.GeneratedTrees);

        var generatedText = runResult.GeneratedTrees[0].GetText().ToString();
        Assert.Contains("GeneratedToolMappings", generatedText);
        Assert.Contains("public static ToolDispatcherConfiguration Create()", generatedText);
        Assert.Contains("[\"read\"]", generatedText);
        Assert.Contains("\"read_file\"", generatedText);
        Assert.Contains("\"cat\"", generatedText);
        Assert.Contains("[\"path\"]", generatedText);
        Assert.Contains("\"filePath\"", generatedText);
        Assert.Contains("\"list_dir\"", generatedText);
    }

    [Fact]
    public async Task Analyzer_EmitsPmad001_OnConflictingParameterAlias()
    {
        var source = """
        using Pmad.AI;
        using Pmad.AI.Tools;

        public static class MyTools
        {
            [ToolAlias("my_tool", Name = "tool")]
            public static void MyMethod(
                [ToolParameterAlias("target")] string path,
                string target)
            {
            }
        }
        """;

        var compilation = CreateCompilation(source);
        var analyzer = new ToolDispatcherAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        Assert.Contains(diagnostics, d => d.Id == ToolDispatcherAnalyzer.DiagnosticIdPmad001);
    }

    [Fact]
    public async Task Analyzer_EmitsPmad002_OnDuplicateToolAliasAcrossDistinctTools()
    {
        var source = """
        using Pmad.AI;
        using Pmad.AI.Tools;

        public static class MyTools
        {
            [ToolAlias("shared_alias", Name = "tool_a")]
            public static void ToolA() { }

            [ToolAlias("shared_alias", Name = "tool_b")]
            public static void ToolB() { }
        }
        """;

        var compilation = CreateCompilation(source);
        var analyzer = new ToolDispatcherAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        Assert.Contains(diagnostics, d => d.Id == ToolDispatcherAnalyzer.DiagnosticIdPmad002 && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task Analyzer_EmitsPmad003_OnUnnamedLambdaToolAliasOutsideAIFunctionFactory()
    {
        var source = """
        using System;
        using Pmad.AI;
        using Pmad.AI.Tools;

        public static class MyTools
        {
            public static void Register()
            {
                var lambda = [ToolAlias("my_alias")] () => Console.WriteLine("hi");
            }
        }
        """;

        var compilation = CreateCompilation(source);
        var analyzer = new ToolDispatcherAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        Assert.Contains(diagnostics, d => d.Id == ToolDispatcherAnalyzer.DiagnosticIdPmad003 && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_EmitsGeneratedToolMappings_ForAIFunctionFactoryLambda()
    {
        var source = """
        using System;
        using Microsoft.Extensions.AI;
        using Pmad.AI;
        using Pmad.AI.Tools;

        public static class MyTools
        {
            public static void Register()
            {
                var tool = AIFunctionFactory.Create(
                    [ToolAlias("modify", "change")]
                    (
                        [ToolParameterAlias("file_path")] string path,
                        string content
                    ) => path + content,
                    name: "edit"
                );
            }
        }
        """;

        var compilation = CreateCompilation(source);
        var generator = new ToolDispatcherGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var runResult = driver.GetRunResult();
        Assert.Single(runResult.GeneratedTrees);

        var generatedText = runResult.GeneratedTrees[0].GetText().ToString();
        Assert.Contains("GeneratedToolMappings", generatedText);
        Assert.Contains("public static ToolDispatcherConfiguration Create()", generatedText);
        Assert.Contains("[\"edit\"]", generatedText);
        Assert.Contains("\"modify\"", generatedText);
        Assert.Contains("\"change\"", generatedText);
        Assert.Contains("[\"path\"]", generatedText);
        Assert.Contains("\"file_path\"", generatedText);
    }

    [Fact]
    public void Generator_EmitsGeneratedToolMappings_ForAIFunctionFactoryPositionalNameLambda()
    {
        var source = """
        using System;
        using Microsoft.Extensions.AI;
        using Pmad.AI;
        using Pmad.AI.Tools;

        public static class MyTools
        {
            public static void Register()
            {
                var tool = AIFunctionFactory.Create(
                    [ToolAlias("fetch", "download")]
                    ([ToolParameterAlias("uri")] string url) => url,
                    "get_content"
                );
            }
        }
        """;

        var compilation = CreateCompilation(source);
        var generator = new ToolDispatcherGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var runResult = driver.GetRunResult();
        Assert.Single(runResult.GeneratedTrees);

        var generatedText = runResult.GeneratedTrees[0].GetText().ToString();
        Assert.Contains("GeneratedToolMappings", generatedText);
        Assert.Contains("public static ToolDispatcherConfiguration Create()", generatedText);
        Assert.Contains("[\"get_content\"]", generatedText);
        Assert.Contains("\"fetch\"", generatedText);
        Assert.Contains("\"download\"", generatedText);
        Assert.Contains("[\"url\"]", generatedText);
        Assert.Contains("\"uri\"", generatedText);
    }

    [Fact]
    public async Task Analyzer_DoesNotEmitPmad003_WhenInsideAIFunctionFactoryCreate()
    {
        var source = """
        using System;
        using Microsoft.Extensions.AI;
        using Pmad.AI;
        using Pmad.AI.Tools;

        public static class MyTools
        {
            public static void Register()
            {
                var tool = AIFunctionFactory.Create(
                    [ToolAlias("my_alias")]
                    ([ToolParameterAlias("q")] string query) => "result",
                    "search"
                );
            }
        }
        """;

        var compilation = CreateCompilation(source);
        var analyzer = new ToolDispatcherAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        Assert.DoesNotContain(diagnostics, d => d.Id == ToolDispatcherAnalyzer.DiagnosticIdPmad003);
    }

    [Fact]
    public void Generator_EmitsValidMappings_WhenCommandTemplateContainsNewlines()
    {
        var source = """
        using Pmad.AI;
        using Pmad.AI.Tools;

        public static class MyTools
        {
            [ToolTranslation("multi_line_cmd", "line1\nline2\r\nline3 \"quotes\"", EscapeMethod = ParameterEscapeMethod.Bash)]
            public static void Exec()
            {
            }
        }
        """;

        var compilation = CreateCompilation(source);
        var generator = new ToolDispatcherGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var runResult = driver.GetRunResult();
        Assert.Single(runResult.GeneratedTrees);

        var generatedTree = runResult.GeneratedTrees[0];
        var parseDiagnostics = generatedTree.GetDiagnostics();
        Assert.DoesNotContain(parseDiagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var generatedText = generatedTree.GetText().ToString();
        Assert.Contains(""""CommandTemplate = "line1\nline2\r\nline3 \"quotes\"","""", generatedText);
    }

    [Fact]
    public void Generator_EmitsNestedParameterAliases_ForComplexRecordParameters()
    {
        var source = """
        using System.Collections.Generic;
        using Pmad.AI;
        using Pmad.AI.Tools;

        public record EditChunk(
            [property: ToolParameterAlias("old_text", "search")] string OldText,
            [property: ToolParameterAlias("new_text", "replace")] string NewText);

        public static class MyTools
        {
            [ToolAlias("apply_edits", Name = "apply_edits")]
            public static void ApplyEdits(List<EditChunk> edits)
            {
            }
        }
        """;

        var compilation = CreateCompilation(source);
        var generator = new ToolDispatcherGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var runResult = driver.GetRunResult();
        Assert.Single(runResult.GeneratedTrees);

        var generatedText = runResult.GeneratedTrees[0].GetText().ToString();
        Assert.Contains("nestedParameterAliases:", generatedText);
        Assert.Contains("[\"edits[]\"]", generatedText);
        Assert.Contains("[\"OldText\"]", generatedText);
        Assert.Contains("\"old_text\"", generatedText);
        Assert.Contains("\"search\"", generatedText);
        Assert.Contains("[\"NewText\"]", generatedText);
        Assert.Contains("\"new_text\"", generatedText);
        Assert.Contains("\"replace\"", generatedText);
    }

    [Fact]
    public void Generator_EmitsDeterministicNestedAliases_ForIReadOnlyListAndMultipleParameters()
    {
        var source = """
        using System.Collections.Generic;
        using Pmad.AI;
        using Pmad.AI.Tools;

        public record QuestionItem(
            [property: ToolParameterAlias("prompt", "text")] string Question,
            [property: ToolParameterAlias("why", "explanation")] string Rationale);

        public static class AgentTools
        {
            [ToolAlias("ask_questions", Name = "ask_questions")]
            public static void Ask(IReadOnlyList<QuestionItem> questions)
            {
            }
        }
        """;

        var compilation = CreateCompilation(source);
        var generator = new ToolDispatcherGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var runResult = driver.GetRunResult();
        Assert.Single(runResult.GeneratedTrees);

        var generatedText = runResult.GeneratedTrees[0].GetText().ToString();
        Assert.Contains("[\"questions[]\"]", generatedText);
        Assert.Contains("[\"Question\"]", generatedText);
        Assert.Contains("\"prompt\"", generatedText);
        Assert.Contains("\"text\"", generatedText);
        Assert.Contains("[\"Rationale\"]", generatedText);
        Assert.Contains("\"why\"", generatedText);
        Assert.Contains("\"explanation\"", generatedText);
    }

    [Fact]
    public void Generator_EmitsParameterValueAliases_ForEnumWithToolValueAlias()
    {
        var source = """
        using Pmad.AI;
        using Pmad.AI.Tools;

        public enum ResourceType
        {
            [ToolValueAlias("memory", "memory_limit", "mem", "ram")]
            MemoryMb,

            [ToolValueAlias("cpu", "cpu_limit", "cores", "vcpu")]
            CpuCores,

            [ToolValueAlias("storage", "disk", "disk_size")]
            StorageGb
        }

        public static class InfraTools
        {
            [ToolAlias("provision")]
            public static void Provision(ResourceType resourceType)
            {
            }
        }
        """;

        var compilation = CreateCompilation(source);
        var generator = new ToolDispatcherGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var runResult = driver.GetRunResult();
        Assert.Single(runResult.GeneratedTrees);

        var generatedText = runResult.GeneratedTrees[0].GetText().ToString();
        Assert.Contains("parameterValueAliases:", generatedText);
        Assert.Contains("[\"resourceType\"]", generatedText);
        Assert.Contains("[\"MemoryMb\"]", generatedText);
        Assert.Contains("\"memory_limit\"", generatedText);
        Assert.Contains("\"ram\"", generatedText);
        Assert.Contains("[\"CpuCores\"]", generatedText);
        Assert.Contains("\"vcpu\"", generatedText);
    }

    [Fact]
    public void Generator_EmitsParameterValueAliases_ForMethodWithToolParameterValueAlias()
    {
        var source = """
        using Pmad.AI;
        using Pmad.AI.Tools;

        public static class BioTools
        {
            [ToolAlias("calculate_population")]
            [ToolParameterValueAlias("species", "wild tiger", "wild tigers", "tiger", "tigers")]
            [ToolParameterValueAlias("species", "african elephant", "african elephants", "elephant")]
            public static double CalculatePopulation(string species, int censusCount)
            {
                return 0.0;
            }
        }
        """;

        var compilation = CreateCompilation(source);
        var generator = new ToolDispatcherGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var runResult = driver.GetRunResult();
        Assert.Single(runResult.GeneratedTrees);

        var generatedText = runResult.GeneratedTrees[0].GetText().ToString();
        Assert.Contains("parameterValueAliases:", generatedText);
        Assert.Contains("[\"species\"]", generatedText);
        Assert.Contains("[\"wild tiger\"]", generatedText);
        Assert.Contains("\"wild tigers\"", generatedText);
        Assert.Contains("[\"african elephant\"]", generatedText);
        Assert.Contains("\"african elephants\"", generatedText);
    }

    [Fact]
    public void Generator_EmitsParameterValueAliases_ForComplexTypePropertyAndRecordParameterWithThreeArguments()
    {
        var source = """
        using Pmad.AI;
        using Pmad.AI.Tools;

        public class DeployRequest
        {
            [ToolParameterValueAlias("Region", "us-east-1", "useast", "virginia")]
            public string Region { get; set; } = string.Empty;
        }

        public record ClusterOptions(
            [ToolParameterValueAlias("Zone", "zone-a", "za", "primary-zone")] string Zone);

        public static class CloudTools
        {
            [ToolAlias("deploy_service")]
            public static void Deploy(DeployRequest config, ClusterOptions options)
            {
            }
        }
        """;

        var compilation = CreateCompilation(source);
        var generator = new ToolDispatcherGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var runResult = driver.GetRunResult();
        Assert.Single(runResult.GeneratedTrees);

        var generatedText = runResult.GeneratedTrees[0].GetText().ToString();
        Assert.Contains("parameterValueAliases:", generatedText);
        // Property value alias on DeployRequest
        Assert.Contains("[\"us-east-1\"]", generatedText);
        Assert.Contains("\"useast\"", generatedText);
        Assert.Contains("\"virginia\"", generatedText);
        // Record constructor parameter value alias on ClusterOptions
        Assert.Contains("[\"zone-a\"]", generatedText);
        Assert.Contains("\"za\"", generatedText);
        Assert.Contains("\"primary-zone\"", generatedText);
    }
}

