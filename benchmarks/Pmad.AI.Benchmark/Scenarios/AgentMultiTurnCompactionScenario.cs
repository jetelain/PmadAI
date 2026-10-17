using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Benchmark.Scenarios;

/// <summary>
/// Benchmark scenario evaluating multi-turn conversation history compaction,
/// token reduction, and late semantic probe recall across a 15-turn coding agent session.
/// </summary>
public sealed class AgentMultiTurnCompactionScenario : BenchmarkScenario
{
    public override string Id => "compaction";
    public override string Name => "Agent Multi-Turn History Compaction (15 Turns)";
    public override string Description => "Simulates a 15-turn coding workflow with large file reads, reasoning traces, and test execution dumps to evaluate prompt token reduction and semantic recall.";

    private static readonly AIFunction _readFileTool = AIFunctionFactory.Create(
        [Description("Reads content from a workspace file.")]
        ([Description("Path to file")] string path) =>
        {
            return $"// Content of {path}\n" + string.Join("\n", Enumerable.Range(1, 50).Select(i => $"public void Method{i}() => Console.WriteLine({i});"));
        },
        "read_file",
        "Reads content from a workspace file."
    );

    private static readonly AIFunction _executeTestsTool = AIFunctionFactory.Create(
        [Description("Executes test suite and returns test results.")]
        ([Description("Test filter")] string filter) =>
        {
            return $"Running tests for {filter}...\nFailed: 1, Passed: 42, ExitCode: 101, Reason: NullReferenceException in AuthHandler.cs:L42";
        },
        "execute_tests",
        "Executes test suite and returns test results."
    );

    private static readonly AIFunction _searchCodeTool = AIFunctionFactory.Create(
        [Description("Searches code for matching patterns.")]
        ([Description("Search query")] string query) =>
        {
            return $"Found 3 matches for {query} in src/AuthHandler.cs, src/TokenService.cs, src/UserService.cs";
        },
        "search_code",
        "Searches code for matching patterns."
    );

    public override IReadOnlyList<AITool> Tools => [_readFileTool, _executeTestsTool, _searchCodeTool];

    public override IReadOnlyList<BenchmarkTurn> Turns => Generate15Turns();

    private static List<BenchmarkTurn> Generate15Turns()
    {
        var list = new List<BenchmarkTurn>();

        // Turn 1: Inspect files
        list.Add(new BenchmarkTurn
        {
            UserPrompt = "Please inspect src/AuthHandler.cs and tell me its structure.",
            ExpectedToolNames = ["read_file"],
            ToolExecutor = call => "File src/AuthHandler.cs contains AuthHandler class with 50 methods.",
            CustomValidator = ctx => ValidationResult.Success()
        });

        // Turn 2: Run tests (Crucial turn for probe question on Turn 15!)
        list.Add(new BenchmarkTurn
        {
            UserPrompt = "Run the unit tests to see what is failing.",
            ExpectedToolNames = ["execute_tests"],
            ToolExecutor = call => "Test run failed with ExitCode: 101, Reason: NullReferenceException in AuthHandler.cs:L42.",
            CustomValidator = ctx => ValidationResult.Success()
        });

        // Turns 3 to 14: Various searches, inspections, and retests
        for (int i = 3; i <= 14; i++)
        {
            var turnNum = i;
            list.Add(new BenchmarkTurn
            {
                UserPrompt = $"Turn {turnNum}: Search for pattern 'TokenValidator_{turnNum}' in the codebase.",
                ExpectedToolNames = ["search_code"],
                ToolExecutor = call => $"Match found for TokenValidator_{turnNum} in Module{turnNum}.cs line {turnNum * 10}.",
                CustomValidator = ctx => ValidationResult.Success()
            });
        }

        // Turn 15: Probe question recalling information from Turn 2
        list.Add(new BenchmarkTurn
        {
            UserPrompt = "Based on our initial test run in turn 2, what was the exact exit code and failure reason?",
            ExpectedToolNames = [],
            ToolExecutor = _ => null,
            CustomValidator = ctx =>
            {
                var text = ctx.AssistantFinalText;
                var hasExitCode = text.Contains("101", StringComparison.OrdinalIgnoreCase);
                var hasReason = text.Contains("NullReferenceException", StringComparison.OrdinalIgnoreCase) || text.Contains("AuthHandler", StringComparison.OrdinalIgnoreCase);

                if (!hasExitCode && !hasReason)
                {
                    return ValidationResult.Failure($"Semantic recall probe failed. Expected exit code 101 or AuthHandler failure reason. Model response: '{text}'");
                }
                return ValidationResult.Success();
            }
        });

        return list;
    }
}
