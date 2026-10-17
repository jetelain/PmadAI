using Microsoft.Extensions.AI;

namespace Pmad.AI.Benchmark.Scenarios.Bfcl;

/// <summary>
/// Benchmark scenario executing Berkeley Function Calling Leaderboard (BFCL) test cases.
/// </summary>
public class BfclScenario : BenchmarkScenario
{
    private readonly string _id;
    private readonly string _name;
    private readonly string _description;
    private readonly string? _categoryFilter;
    private List<BenchmarkTurn>? _turns;
    private List<AITool>? _tools;

    public BfclScenario(
        string id = "bfcl",
        string name = "BFCL - Berkeley Function Calling (All)",
        string description = "Evaluates function calling across simple, multiple, parallel, and irrelevance categories from BFCL datasets.",
        string? categoryFilter = null)
    {
        _id = id;
        _name = name;
        _description = description;
        _categoryFilter = categoryFilter;
    }

    public override string Id => _id;
    public override string Name => _name;
    public override string Description => _description;

    public override IReadOnlyList<AITool> Tools => _tools ??= LoadAllTools();

    public override IReadOnlyList<BenchmarkTurn> Turns => _turns ??= BuildTurns();

    private List<AITool> LoadAllTools()
    {
        var entries = BfclDatasetLoader.LoadEntries(_categoryFilter);
        var toolsDict = new Dictionary<string, AITool>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            foreach (var tool in entry.Tools)
            {
                if (tool is AIFunction fn && !toolsDict.ContainsKey(fn.Name))
                {
                    toolsDict[fn.Name] = fn;
                }
            }
        }

        return toolsDict.Values.ToList();
    }

    private List<BenchmarkTurn> BuildTurns()
    {
        var entries = BfclDatasetLoader.LoadEntries(_categoryFilter);
        var turns = new List<BenchmarkTurn>(entries.Count);

        foreach (var entry in entries)
        {
            turns.Add(new BenchmarkTurn
            {
                UserPrompt = entry.Question,
                Tools = entry.Tools,
                ClearHistoryBeforeTurn = entry.ClearHistory,
                ExpectedToolNames = entry.GroundTruth.Select(g => g.Name).ToList(),
                ToolExecutor = call => entry.ToolResult ?? $"Executed {call.Name} successfully.",
                CustomValidator = ctx =>
                {
                    if (entry.Category == BfclCategory.Strict)
                    {
                        return BfclAstEvaluator.EvaluateStrict(entry, ctx);
                    }
                    if (entry.ExpectedTextKeywords != null && entry.ExpectedTextKeywords.Count > 0)
                    {
                        var text = ctx.AssistantFinalText ?? string.Empty;
                        foreach (var kw in entry.ExpectedTextKeywords)
                        {
                            if (!text.Contains(kw, StringComparison.OrdinalIgnoreCase))
                            {
                                return ValidationResult.Failure($"Final response did not state '{kw}'. Response: '{text}'");
                            }
                        }
                        return ValidationResult.Success();
                    }
                    return BfclAstEvaluator.Evaluate(entry, ctx);
                }
            });
        }

        return turns;
    }
}

public sealed class BfclSimpleScenario : BfclScenario
{
    public BfclSimpleScenario() : base(
        id: "bfcl-simple",
        name: "BFCL - Simple Function Calling",
        description: "Evaluates single function calls with typed arguments (integers, strings, floats, booleans, enums).",
        categoryFilter: "simple")
    {
    }

    /// <summary>
    /// Rebalanced from 1.0 to 0.5 to reduce simple function calling weight from 20.0 to 10.0.
    /// </summary>
    public override double ComplexityMultiplier => 0.5;
}

public sealed class BfclMultipleScenario : BfclScenario
{
    public BfclMultipleScenario() : base(
        id: "bfcl-multiple",
        name: "BFCL - Multiple Tools Selection",
        description: "Evaluates function selection accuracy when 4 to 10 candidate tools are exposed simultaneously.",
        categoryFilter: "multiple")
    {
    }

    public override double ComplexityMultiplier => 1.25;
}

public sealed class BfclParallelScenario : BfclScenario
{
    public BfclParallelScenario() : base(
        id: "bfcl-parallel",
        name: "BFCL - Parallel Function Calling",
        description: "Evaluates model capability to invoke multiple tools concurrently in a single turn.",
        categoryFilter: "parallel")
    {
    }

    public override double ComplexityMultiplier => 1.5;
}

public sealed class BfclIrrelevanceScenario : BfclScenario
{
    public BfclIrrelevanceScenario() : base(
        id: "bfcl-irrelevance",
        name: "BFCL - Irrelevance (Negative Control)",
        description: "Evaluates negative control: model must provide a direct text response without invoking any candidate tools.",
        categoryFilter: "irrelevance")
    {
    }

    public override double ComplexityMultiplier => 1.0;
}

/// <summary>
/// BFCL Executable Multi-Turn scenario: evaluates multi-step tool calls where
/// output from tool step N is required to formulate arguments for tool step N+1.
/// Loads from benchmarks/Pmad.AI.Benchmark/Fixtures/bfcl/multiturn.jsonl.
/// </summary>
public sealed class BfclMultiTurnScenario : BfclScenario
{
    public BfclMultiTurnScenario() : base(
        id: "bfcl-multiturn",
        name: "BFCL - Executable Multi-Turn Workflows",
        description: "Evaluates chained tool calling where tool output N is strictly required to formulate tool call N+1.",
        categoryFilter: "multiturn")
    {
    }

    public override double ComplexityMultiplier => 2.5;
}

/// <summary>
/// BFCL Strict Schema Validation scenario: evaluates function calling with
/// nested object schemas, strict float/int typing, enums, and deliberate distractor tools.
/// Loads from benchmarks/Pmad.AI.Benchmark/Fixtures/bfcl/strict.jsonl.
/// </summary>
public sealed class BfclStrictScenario : BfclScenario
{
    public BfclStrictScenario() : base(
        id: "bfcl-strict",
        name: "BFCL - Strict Schema & Distractor Validation",
        description: "Evaluates function calling with nested object schemas, strict float/int typing, enums, and deliberate distractor tools.",
        categoryFilter: "strict")
    {
    }

    public override double ComplexityMultiplier => 5.0;
}
