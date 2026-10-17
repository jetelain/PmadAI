using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Benchmark.Scenarios;

/// <summary>
/// Benchmark scenario testing loop circuit breaking against runaway repetitive streaming output
/// and duplicate tool invocation ping-pong on unchanged state.
/// </summary>
public sealed class StreamingRunawayAndPingPongScenario : BenchmarkScenario
{
    public override string Id => "circuit-breaker";
    public override string Name => "Streaming Runaway & Tool Ping-Pong Circuit Breaker";
    public override string Description => "Tests rapid trip and abort when infinite token repetition or repeated identical tool calls occur.";

    private static readonly AIFunction _readTool = AIFunctionFactory.Create(
        [Description("Reads a workspace file.")]
        ([Description("File path")] string path) => "File content of " + path,
        "read",
        "Reads a workspace file."
    );

    public override IReadOnlyList<AITool> Tools => [_readTool];

    public override IReadOnlyList<BenchmarkTurn> Turns =>
    [
        new BenchmarkTurn
        {
            UserPrompt = "Read same.cs repeatedly without modifying state.",
            ExpectedToolNames = ["read"],
            ToolExecutor = call => "Content unchanged",
            CustomValidator = ctx => ValidationResult.Success()
        }
    ];
}
