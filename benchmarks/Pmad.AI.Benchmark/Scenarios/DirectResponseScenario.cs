using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Benchmark.Scenarios;

/// <summary>
/// Benchmark scenario testing negative tool call constraint: the model must NOT invoke tools when direct answering is appropriate.
/// </summary>
public sealed class DirectResponseScenario : BenchmarkScenario
{
    public override string Id => "direct";
    public override string Name => "Direct Knowledge (Negative Tool Test)";
    public override string Description => "Tests that the model does not hallucinate unnecessary tool calls when asked a direct general knowledge question.";

    private static readonly AIFunction _dummyTool = AIFunctionFactory.Create(
        [Description("Fetches Wikipedia summaries for historical events.")]
        ([Description("Topic or query")] string query) => "Wiki result",
        "search_wikipedia",
        "Fetches Wikipedia summaries for historical events."
    );

    public override IReadOnlyList<AITool> Tools => [_dummyTool];

    public override IReadOnlyList<BenchmarkTurn> Turns =>
    [
        new BenchmarkTurn
        {
            UserPrompt = "What is the capital of France? Answer directly in one sentence without invoking any tools.",
            ExpectedToolNames = [],
            ToolExecutor = _ => null,
            CustomValidator = ctx =>
            {
                if (ctx.ActualToolCalls.Count > 0)
                {
                    return ValidationResult.Failure($"Model invoked {ctx.ActualToolCalls.Count} tool call(s) ({string.Join(", ", ctx.ActualToolCalls.Select(c => c.Name))}) when none were requested.");
                }

                if (!ctx.AssistantFinalText.Contains("Paris", StringComparison.OrdinalIgnoreCase))
                {
                    return ValidationResult.Failure("Model failed to state Paris as the capital of France.");
                }

                return ValidationResult.Success();
            }
        }
    ];
}
