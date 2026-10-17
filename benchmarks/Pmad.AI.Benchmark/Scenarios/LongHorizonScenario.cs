using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Benchmark.Scenarios;

/// <summary>
/// Base class for ultra-long (50-100 turns) agent sessions. Anchor facts are planted in tool results early
/// in the session and recalled by probe turns much later, after history compaction has had every chance to discard them.
/// </summary>
public abstract class LongHorizonScenario : BenchmarkScenario
{
    /// <summary>A fact planted in the tool result of a given turn.</summary>
    protected sealed record Anchor(int Turn, string Tool, string Prompt, string Fact);

    /// <summary>A semantic recall probe: the answer must contain every required fragment.</summary>
    protected sealed record Probe(int Turn, string Prompt, string[] RequiredFragments);

    protected abstract int TotalTurns { get; }

    protected abstract string Topic { get; }

    protected abstract IReadOnlyList<string> ToolNames { get; }

    protected abstract IReadOnlyList<Anchor> Anchors { get; }

    protected abstract IReadOnlyList<Probe> Probes { get; }

    public override IReadOnlyList<AITool> Tools => ToolNames
        .Select(name => (AITool)AIFunctionFactory.Create(
            ([Description("Target of the operation (path, pattern, test filter or arguments)")] string target) =>
                $"{name} executed on {target}",
            name,
            $"Agent tool '{name}'."))
        .ToList();

    public override IReadOnlyList<BenchmarkTurn> Turns => BuildTurns();

    private List<BenchmarkTurn> BuildTurns()
    {
        var turns = new List<BenchmarkTurn>(TotalTurns);
        var tools = ToolNames;

        for (var t = 1; t <= TotalTurns; t++)
        {
            var step = t;
            var probe = Probes.FirstOrDefault(p => p.Turn == step);
            if (probe != null)
            {
                turns.Add(new BenchmarkTurn
                {
                    UserPrompt = $"[LONG-PROBE] {probe.Prompt}",
                    ExpectedToolNames = [],
                    ToolExecutor = _ => null,
                    IsRecallProbe = true,
                    CustomValidator = ctx =>
                    {
                        var missing = probe.RequiredFragments
                            .Where(f => !ctx.AssistantFinalText.Contains(f, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        return missing.Count == 0
                            ? ValidationResult.Success()
                            : ValidationResult.Failure($"Semantic recall probe failed at turn {step}. Missing: {string.Join(", ", missing)}. Response: '{ctx.AssistantFinalText}'");
                    }
                });
                continue;
            }

            var anchor = Anchors.FirstOrDefault(a => a.Turn == step);
            var tool = anchor?.Tool ?? tools[step % tools.Count];
            var prompt = anchor != null
                ? $"[LONG-ACTION:{tool}] Step {step}: {anchor.Prompt}"
                : $"[LONG-ACTION:{tool}] Step {step}: continue the {Topic} by calling {tool} on module_{step}.";

            turns.Add(new BenchmarkTurn
            {
                UserPrompt = prompt,
                ExpectedToolNames = [tool],
                ToolExecutor = _ => BuildToolOutput(tool, step, anchor?.Fact),
                CustomValidator = _ => ValidationResult.Success()
            });
        }

        return turns;
    }

    private static string BuildToolOutput(string tool, int step, string? fact)
    {
        var lines = new List<string> { $"[{tool}] result for step {step}" };
        if (fact != null)
        {
            lines.Add(fact);
        }
        lines.AddRange(Enumerable.Range(1, 40).Select(i =>
            $"    // step {step} diagnostic line {i}: module_{step} component buffer {step}-{i} processed without anomalies"));
        return string.Join("\n", lines);
    }
}
