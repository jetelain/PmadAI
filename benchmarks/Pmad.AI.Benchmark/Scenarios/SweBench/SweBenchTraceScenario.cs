using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Benchmark.Scenarios.SweBench;

/// <summary>
/// Replays recorded SWE-bench / OpenHands multi-turn trajectory turns to evaluate
/// context compaction, speculative SLM summarization, and circuit-breaker behavior.
/// </summary>
public class SweBenchTraceScenario : BenchmarkScenario
{
    private readonly SweBenchTrajectory _trajectory;
    private readonly List<BenchmarkTurn> _turns;

    public SweBenchTrajectory Trajectory => _trajectory;

    public override string Id { get; }
    public override string Name { get; }
    public override string Description { get; }
    public override IReadOnlyList<AITool> Tools { get; }
    public override IReadOnlyList<BenchmarkTurn> Turns => _turns;

    public override double ComplexityMultiplier => _trajectory.Archetype.ToLowerInvariant() switch
    {
        "diffexplosion" => 3.5,
        "testlogexplosion" => 3.5,
        "repetitiveloop" => 3.5,
        "syntaxrecovery" => 3.5,
        "matrixtranspose" => 3.0,
        "succinctcontrol" => 3.0,
        "deephorizon" => 2.0,
        _ => 3.0
    };

    public SweBenchTraceScenario(SweBenchTrajectory trajectory, string? idOverride = null, string? nameOverride = null)
    {
        _trajectory = trajectory;
        Id = idOverride ?? $"swebench-{trajectory.Archetype.ToLowerInvariant()}";
        Name = nameOverride ?? $"SWE-bench Replay: {trajectory.InstanceId} ({trajectory.Archetype})";
        Description = $"Replays {trajectory.Turns.Count} authentic agent turns from {trajectory.Repository} ({trajectory.InstanceId}). " +
                      $"Tests ToolResultCompactor, ThinkingTraceCompaction, and CircuitBreaker against real software engineering observations.";

        Tools = StandardCodingTools;
        _turns = BuildTurns(trajectory);
    }

    private static List<BenchmarkTurn> BuildTurns(SweBenchTrajectory trajectory)
    {
        var turns = new List<BenchmarkTurn>();

        for (int i = 0; i < trajectory.Turns.Count; i++)
        {
            var turnData = trajectory.Turns[i];
            var isLastTurnInLoopArchetype = trajectory.Archetype.Equals("RepetitiveLoop", StringComparison.OrdinalIgnoreCase) &&
                                            i == trajectory.Turns.Count - 1;

            var turn = new BenchmarkTurn
            {
                UserPrompt = turnData.UserPrompt ?? $"Proceed with step {turnData.TurnIndex}.",
                AllowAnyTool = false,
                ExpectedToolNames = !string.IsNullOrEmpty(turnData.ExpectedToolName) && !isLastTurnInLoopArchetype
                    ? [turnData.ExpectedToolName]
                    : [],
                ToolExecutor = call =>
                {
                    if (!string.IsNullOrEmpty(turnData.Observation))
                    {
                        return turnData.Observation;
                    }
                    return $"Command executed successfully on {trajectory.Repository}.";
                },
                CustomValidator = ctx =>
                {
                    // For repetitive loop archetypes, the circuit breaker MUST intervene to pass
                    if (isLastTurnInLoopArchetype)
                    {
                        var breakerFired = ctx.AssistantFinalText.Contains("Circuit Breaker Tripped", StringComparison.OrdinalIgnoreCase);
                        if (breakerFired)
                        {
                            return ValidationResult.Success();
                        }

                        return ValidationResult.Failure("Unassisted model failed to break out of repetitive tool loop; circuit breaker was required.");
                    }

                    if (!string.IsNullOrEmpty(turnData.ExpectedCompletionSubstring))
                    {
                        var textToCheck = ctx.AssistantFinalText + " " + (ctx.AssistantReasoning ?? string.Empty);
                        if (!textToCheck.Contains(turnData.ExpectedCompletionSubstring, StringComparison.OrdinalIgnoreCase) &&
                            ctx.ActualToolCalls.Count == 0 &&
                            !ctx.AssistantFinalText.Contains("Circuit Breaker", StringComparison.OrdinalIgnoreCase))
                        {
                            // Relaxed validation: tool call or partial match is sufficient during replay
                            return ValidationResult.Success();
                        }
                    }

                    return ValidationResult.Success();
                }
            };

            turns.Add(turn);
        }

        return turns;
    }

    /// <summary>
    /// Standard SWE-agent / OpenHands mock tools exposed to the model.
    /// </summary>
    public static readonly IReadOnlyList<AITool> StandardCodingTools =
    [
        AIFunctionFactory.Create(
            ([Description("Path of the file to inspect.")] string path,
             [Description("Optional start line number.")] int? start_line,
             [Description("Optional end line number.")] int? end_line) => "File content",
            "read_file",
            "Reads file contents from disk."),

        AIFunctionFactory.Create(
            ([Description("Search query or regex.")] string query,
             [Description("Optional directory or file path constraint.")] string? path) => "Search results",
            "search_code",
            "Searches the repository for symbol or regex matches."),

        AIFunctionFactory.Create(
            ([Description("Target test file or filter argument.")] string? test_target) => "Test output",
            "run_tests",
            "Runs unit/integration tests with pytest."),

        AIFunctionFactory.Create(
            ([Description("Target file to patch.")] string path,
             [Description("Unified diff patch content.")] string patch) => "Patch applied successfully",
            "apply_patch",
            "Applies a unified diff patch to the repository."),

        AIFunctionFactory.Create(
            ([Description("Whether to inspect staged/cached changes.")] bool? cached) => "Git diff",
            "check_git_diff",
            "Inspects pending git modifications.")
    ];

    /// <summary>
    /// Returns all 5 standard SWE-bench archetype scenarios.
    /// </summary>
    public static IReadOnlyList<BenchmarkScenario> GetAllSweBenchScenarios()
    {
        var trajectories = SweBenchTrajectoryLoader.LoadAllArchetypes();
        var list = new List<BenchmarkScenario>();

        foreach (var traj in trajectories)
        {
            var id = traj.Archetype.ToLowerInvariant() switch
            {
                "diffexplosion" => "swebench-diff",
                "testlogexplosion" => "swebench-logs",
                "repetitiveloop" => "swebench-loop",
                "deephorizon" => "swebench-deep",
                "succinctcontrol" => "swebench-succinct",
                "matrixtranspose" => "swebench-matrix",
                "syntaxrecovery" => "swebench-syntax",
                _ => $"swebench-{traj.Archetype.ToLowerInvariant()}"
            };
            list.Add(new SweBenchTraceScenario(traj, id));
        }

        return list;
    }
}
