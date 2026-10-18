using System.ComponentModel;
using Microsoft.Extensions.AI;
using Pmad.AI.Benchmark.Engine;

namespace Pmad.AI.Benchmark.Scenarios.Compaction;

/// <summary>
/// Benchmark scenario testing ToolResultCompactor against large compiler build outputs,
/// test logs, and complete C# source code files (15–35 KB per observation).
/// </summary>
public sealed class CompactionLargeToolScenario : BenchmarkScenario
{
    private readonly CompactionTrajectory _trajectory;
    private readonly List<BenchmarkTurn> _turns;

    public override string Id => "compaction-large-tool";
    public override string Name => "Compaction: Large Tool Results (Build & Test Logs)";
    public override string Description => "Tests ToolResultCompactor against authentic 15–35 KB MSBuild compilation traces and source files over 7 turns.";
    public override string Suite => "Compaction";
    public override double ComplexityMultiplier => 4.0; // 7 turns * 4.0 = 28.0 weight
    public override IReadOnlyList<AITool> Tools => CompactionTools.StandardTools;
    public override IReadOnlyList<BenchmarkTurn> Turns => _turns;

    public CompactionLargeToolScenario()
    {
        _trajectory = CompactionTrajectoryLoader.LoadLargeToolTrajectory();
        _turns = BuildTurns(_trajectory);
    }

    private static List<BenchmarkTurn> BuildTurns(CompactionTrajectory trajectory)
    {
        var turns = new List<BenchmarkTurn>();

        for (int i = 0; i < trajectory.Turns.Count; i++)
        {
            var turnData = trajectory.Turns[i];
            var turn = new BenchmarkTurn
            {
                UserPrompt = turnData.UserPrompt,
                AllowAnyTool = turnData.AllowAnyTool,
                IsRecallProbe = turnData.IsRecallProbe,
                ExpectedToolNames = !string.IsNullOrEmpty(turnData.ExpectedToolName)
                    ? [turnData.ExpectedToolName]
                    : [],
                ToolExecutor = _ => turnData.Observation ?? "Command executed successfully.",
                CustomValidator = ctx =>
                {
                    if (!string.IsNullOrEmpty(turnData.ExpectedCompletionSubstring))
                    {
                        var text = ctx.AssistantFinalText + " " + (ctx.AssistantReasoning ?? string.Empty);
                        if (!text.Contains(turnData.ExpectedCompletionSubstring, StringComparison.OrdinalIgnoreCase) &&
                            ctx.ActualToolCalls.Count == 0)
                        {
                            return ValidationResult.Failure($"Final synthesis did not contain expected substring '{turnData.ExpectedCompletionSubstring}'.");
                        }
                    }

                    return ValidationResult.Success();
                }
            };

            turns.Add(turn);
        }

        return turns;
    }
}

/// <summary>
/// Benchmark scenario testing context retention and needle recall across a deep 10-turn DevOps
/// workflow generating >120 KB of diagnostic telemetry, evaluating both ThinkingTraceCompactor and ToolResultCompactor.
/// </summary>
public sealed class CompactionDeepContextScenario : BenchmarkScenario
{
    private readonly CompactionTrajectory _trajectory;
    private readonly List<BenchmarkTurn> _turns;

    public override string Id => "compaction-deep-context";
    public override string Name => "Compaction: Deep 10-Turn Context with Needle Recall";
    public override string Description => "Tests context retention and semantic probe recall across 10 turns exceeding 70-80% of context window.";
    public override string Suite => "Compaction";
    public override double ComplexityMultiplier => 3.5; // 10 turns * 3.5 = 35.0 weight
    public override IReadOnlyList<AITool> Tools => CompactionTools.StandardTools;
    public override IReadOnlyList<BenchmarkTurn> Turns => _turns;

    public CompactionDeepContextScenario()
    {
        _trajectory = CompactionTrajectoryLoader.LoadDeepContextTrajectory();
        _turns = BuildTurns(_trajectory);
    }

    private static List<BenchmarkTurn> BuildTurns(CompactionTrajectory trajectory)
    {
        var turns = new List<BenchmarkTurn>();

        for (int i = 0; i < trajectory.Turns.Count; i++)
        {
            var turnData = trajectory.Turns[i];
            var turn = new BenchmarkTurn
            {
                UserPrompt = turnData.UserPrompt,
                AllowAnyTool = turnData.AllowAnyTool,
                IsRecallProbe = turnData.IsRecallProbe,
                ExpectedToolNames = !string.IsNullOrEmpty(turnData.ExpectedToolName)
                    ? [turnData.ExpectedToolName]
                    : [],
                ToolExecutor = _ => turnData.Observation ?? "Diagnostic output retrieved.",
                CustomValidator = ctx =>
                {
                    if (!string.IsNullOrEmpty(turnData.ExpectedCompletionSubstring))
                    {
                        var text = ctx.AssistantFinalText + " " + (ctx.AssistantReasoning ?? string.Empty);
                        if (!text.Contains(turnData.ExpectedCompletionSubstring, StringComparison.OrdinalIgnoreCase))
                        {
                            return ValidationResult.Failure($"Audit retrieval did not locate expected security token '{turnData.ExpectedCompletionSubstring}'.");
                        }
                    }

                    return ValidationResult.Success();
                }
            };

            turns.Add(turn);
        }

        return turns;
    }
}

/// <summary>
/// Standard agent tools exposed during compaction scenarios.
/// </summary>
public static class CompactionTools
{
    public static readonly IReadOnlyList<AITool> StandardTools =
    [
        AIFunctionFactory.Create(
            ([Description("Command line instruction to execute.")] string command) => "Command executed successfully.",
            "run_command",
            "Runs a shell command in the repository workspace."),

        AIFunctionFactory.Create(
            ([Description("Target test file or filter argument.")] string? target) => "Test run completed.",
            "run_tests",
            "Executes integration or unit tests."),

        AIFunctionFactory.Create(
            ([Description("Path of the file to inspect.")] string path,
             [Description("Optional line number to start reading.")] int? start_line,
             [Description("Optional line number to stop reading.")] int? end_line) => "File contents.",
            "read_file",
            "Reads file contents from disk."),

        AIFunctionFactory.Create(
            ([Description("Query or regular expression pattern.")] string query,
             [Description("Optional path filter.")] string? path) => "Search results.",
            "search_code",
            "Searches codebase for symbols or text matches."),

        AIFunctionFactory.Create(
            ([Description("Target file to patch.")] string path,
             [Description("Unified diff patch content.")] string patch) => "Patch applied successfully.",
            "apply_patch",
            "Applies a unified diff patch to a source file.")
    ];
}
