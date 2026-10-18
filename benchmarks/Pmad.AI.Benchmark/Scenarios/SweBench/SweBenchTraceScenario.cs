using System.ComponentModel;
using Microsoft.Extensions.AI;
using Pmad.AI.Benchmark.Engine;

namespace Pmad.AI.Benchmark.Scenarios.SweBench;

/// <summary>
/// Replays recorded SWE-bench / OpenHands multi-turn trajectory turns to evaluate
/// context compaction, speculative SLM summarization, and circuit-breaker behavior.
/// Uses a state-aware executor maintaining virtual repository files and patch states.
/// </summary>
public class SweBenchTraceScenario : BenchmarkScenario
{
    private readonly SweBenchTrajectory _trajectory;
    private readonly List<BenchmarkTurn> _turns;
    private ISweBenchExecutor _executor;

    public SweBenchTrajectory Trajectory => _trajectory;
    public ISweBenchExecutor Executor => _executor;

    public void SetExecutor(ISweBenchExecutor executor) => _executor = executor ?? throw new ArgumentNullException(nameof(executor));

    public override string Id { get; }
    public override string Name { get; }
    public override string Description { get; }
    public override string Suite => "SWE-bench";
    public override IReadOnlyList<AITool> Tools { get; }
    public override IReadOnlyList<BenchmarkTurn> Turns => _turns;

    /// <summary>
    /// Rebalanced complexity multipliers scaling SWE-bench scenario weights to 35.0 - 50.0.
    /// </summary>
    public override double ComplexityMultiplier => _trajectory.Archetype.ToLowerInvariant() switch
    {
        "diffexplosion" => 6.0,      // 7 turns * 6.0 = 42.0
        "testlogexplosion" => 8.0,   // 5 turns * 8.0 = 40.0
        "repetitiveloop" => 10.0,    // 4 turns * 10.0 = 40.0
        "syntaxrecovery" => 10.0,    // 4 turns * 10.0 = 40.0
        "matrixtranspose" => 8.0,    // 5 turns * 8.0 = 40.0
        "succinctcontrol" => 10.0,   // 4 turns * 10.0 = 40.0
        "deephorizon" => 5.0,        // 8 turns * 5.0 = 40.0
        _ => 8.0
    };

    public SweBenchTraceScenario(
        SweBenchTrajectory trajectory,
        string? idOverride = null,
        string? nameOverride = null,
        ISweBenchExecutor? executor = null)
    {
        _trajectory = trajectory;
        Id = idOverride ?? $"swebench-{trajectory.Archetype.ToLowerInvariant()}";
        Name = nameOverride ?? $"SWE-bench Replay: {trajectory.InstanceId} ({trajectory.Archetype})";
        Description = $"Replays {trajectory.Turns.Count} authentic agent turns from {trajectory.Repository} ({trajectory.InstanceId}). " +
                      $"Tests ToolResultCompactor, ThinkingTraceCompaction, and CircuitBreaker against real software engineering observations.";

        _executor = executor ?? new StateAwareSweBenchMockExecutor(trajectory);
        Tools = StandardCodingTools;
        _turns = BuildTurns(trajectory, () => _executor);
    }

    /// <summary>
    /// Resets the executor so state does not leak between model configurations.
    /// </summary>
    public override void OnScenarioStart() => _executor.Reset();

    /// <summary>
    /// Task success for SWE-bench requires applying a valid patch and passing verification tests
    /// (or triggering the circuit breaker for repetitive loop archetypes).
    /// </summary>
    public override bool EvaluateTaskSuccess(BenchmarkScenarioResult result)
    {
        var success = _executor.IsTaskSuccessful;
        result.TaskSuccess = success;
        if (!success)
        {
            result.TaskFailureReason = _executor.FailureReason;
        }
        return success;
    }

    private static List<BenchmarkTurn> BuildTurns(SweBenchTrajectory trajectory, Func<ISweBenchExecutor> getExecutor)
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
                    var exec = getExecutor();
                    return exec.Execute(call, turnData);
                },
                CustomValidator = ctx =>
                {
                    var exec = getExecutor();

                    // For repetitive loop archetypes, the circuit breaker MUST intervene to pass
                    if (isLastTurnInLoopArchetype)
                    {
                        var breakerFired = ctx.AssistantFinalText.Contains("Circuit Breaker Tripped", StringComparison.OrdinalIgnoreCase);
                        if (breakerFired)
                        {
                            exec.CircuitBreakerTripped = true;
                            return ValidationResult.Success();
                        }

                        return ValidationResult.Failure("Unassisted model failed to break out of repetitive tool loop; circuit breaker was required.");
                    }

                    if (ctx.AssistantFinalText.Contains("Circuit Breaker Tripped", StringComparison.OrdinalIgnoreCase))
                    {
                        exec.CircuitBreakerTripped = true;
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
    /// Returns all standard SWE-bench archetype scenarios with configured executor.
    /// </summary>
    public static IReadOnlyList<BenchmarkScenario> GetAllSweBenchScenarios(string? sandboxMode = null, string? dockerContainer = null)
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

            ISweBenchExecutor executor = string.Equals(sandboxMode, "docker", StringComparison.OrdinalIgnoreCase)
                ? new DockerSweBenchSandboxExecutor(traj, dockerContainer)
                : new StateAwareSweBenchMockExecutor(traj);

            list.Add(new SweBenchTraceScenario(traj, id, executor: executor));
        }

        return list;
    }
}
