using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Benchmark.Scenarios;

/// <summary>
/// Benchmark scenario testing loop circuit breaking against runaway repetitive streaming output
/// and duplicate tool invocation ping-pong on unchanged state.
/// </summary>
public sealed class StreamingRunawayAndPingPongScenario : BenchmarkScenario
{
    public override string Id => "runaway-loop";
    public override string Name => "Runaway Loop & Circuit Breaker";
    public override string Description => "Tests circuit breaker protection against repetitive tool invocation loops on unchanged state and runaway token repetition.";

    private static readonly AIFunction _checkStatusTool = AIFunctionFactory.Create(
        [Description("Checks service health and deployment status.")]
        ([Description("Target service name")] string service) => "Status: Pending, retry shortly",
        "check_status",
        "Checks service health and deployment status."
    );

    private static readonly AIFunction _readTool = AIFunctionFactory.Create(
        [Description("Reads a workspace file.")]
        ([Description("File path")] string path) => "File content of " + path,
        "read",
        "Reads a workspace file."
    );

    public override IReadOnlyList<AITool> Tools => [_checkStatusTool, _readTool];

    public override IReadOnlyList<BenchmarkTurn> Turns =>
    [
        // Turn 1: Check status until ready (triggers tool retry loop on unchanged state)
        new BenchmarkTurn
        {
            UserPrompt = "Check status of auth-service until ready.",
            ExpectedToolNames = ["check_status"],
            ToolExecutor = call => "Status: Pending, retry shortly",
            CustomValidator = ctx => ValidationResult.Success()
        },
        // Turn 2: Repeated read on unchanged state
        new BenchmarkTurn
        {
            UserPrompt = "Read same.cs repeatedly without modifying state.",
            ExpectedToolNames = ["read"],
            ToolExecutor = call => "Content unchanged",
            CustomValidator = ctx => ValidationResult.Success()
        }
    ];
}
