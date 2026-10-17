using Microsoft.Extensions.AI;
using Pmad.AI.Benchmark.Scenarios.Bfcl;
using Pmad.AI.Benchmark.Scenarios.Ruler;

namespace Pmad.AI.Benchmark.Scenarios;

/// <summary>
/// Defines a benchmark scenario testing tool calling, multi-turn interactions, and output synthesis.
/// </summary>
public abstract class BenchmarkScenario
{
    /// <summary>
    /// Unique identifier for this scenario.
    /// </summary>
    public abstract string Id { get; }

    /// <summary>
    /// Descriptive name of this scenario.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Detailed description of what is being evaluated.
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// Tools exposed to the model for this scenario.
    /// </summary>
    public abstract IReadOnlyList<AITool> Tools { get; }

    /// <summary>
    /// Sequence of mocked user interaction turns.
    /// </summary>
    public abstract IReadOnlyList<BenchmarkTurn> Turns { get; }

    /// <summary>
    /// Maximum allowed tool call iterations per turn before terminating the turn loop.
    /// </summary>
    public virtual int MaxToolIterationsPerTurn => 5;

    /// <summary>
    /// Relative cognitive and contextual complexity multiplier for this scenario (default 1.0).
    /// </summary>
    public virtual double ComplexityMultiplier => 1.0;

    /// <summary>
    /// Importance weight of the scenario in the overall benchmark pass rate, derived from
    /// the number of subtest turns and scenario complexity: Weight = Math.Max(1.0, Turns.Count * ComplexityMultiplier).
    /// </summary>
    public virtual double Weight => Math.Max(1.0, Math.Round(Turns.Count * ComplexityMultiplier, 1));

    /// <summary>
    /// Called by the runner before each run of the scenario so stateful scenarios can reset
    /// per-run state (scenario instances are reused across model configurations).
    /// </summary>
    public virtual void OnScenarioStart()
    {
    }

    /// <summary>
    /// Evaluates whether the scenario achieved binary Task Success (Pass@1).
    /// By default, true if all turns passed validation. SWE-bench scenarios override this
    /// to require successful patch application and passing verification tests.
    /// </summary>
    public virtual bool EvaluateTaskSuccess(Engine.BenchmarkScenarioResult result) =>
        result.TurnsPassed == result.TotalTurns && result.TotalTurns > 0;

    /// <summary>
    /// Returns all 6 BFCL function calling scenarios (Simple, Multiple, Parallel, Irrelevance, Multi-Turn, Strict Schema).
    /// </summary>
    public static IReadOnlyList<BenchmarkScenario> GetBfclScenarios() =>
    [
        new BfclSimpleScenario(),
        new BfclMultipleScenario(),
        new BfclParallelScenario(),
        new BfclIrrelevanceScenario(),
        new BfclMultiTurnScenario(),
        new BfclStrictScenario()
    ];

    /// <summary>
    /// Returns all 7 SWE-bench / OpenHands trajectory replay scenarios.
    /// </summary>
    public static IReadOnlyList<BenchmarkScenario> GetSweBenchScenarios(string? sandboxMode = null, string? dockerContainer = null) =>
        SweBench.SweBenchTraceScenario.GetAllSweBenchScenarios(sandboxMode, dockerContainer);

    /// <summary>
    /// Returns all 5 RULER / NIAH context recall scenarios (8k, 16k, 32k, 64k, 128k).
    /// </summary>
    public static IReadOnlyList<BenchmarkScenario> GetRulerScenarios() =>
    [
        new Ruler8kScenario(),
        new Ruler16kScenario(),
        new Ruler32kScenario(),
        new Ruler64kScenario(),
        new Ruler128kScenario()
    ];

    /// <summary>
    /// Returns the deep context scenarios (32k, 64k, 128k).
    /// </summary>
    public static IReadOnlyList<BenchmarkScenario> GetDeepContextScenarios() =>
    [
        new Ruler32kScenario(),
        new Ruler64kScenario(),
        new Ruler128kScenario()
    ];

    /// <summary>
    /// Returns all scenarios excluding the deep context scenarios (15 scenarios: 6 BFCL + 7 SWE-bench + RULER 8k/16k).
    /// </summary>
    public static IReadOnlyList<BenchmarkScenario> GetAllSmallScenarios(string? sandboxMode = null, string? dockerContainer = null) =>
    [
        .. GetBfclScenarios(),
        .. GetSweBenchScenarios(sandboxMode, dockerContainer),
        new Ruler8kScenario(),
        new Ruler16kScenario()
    ];

    /// <summary>
    /// Returns all available benchmark scenarios (18 scenarios across BFCL, SWE-bench, and RULER).
    /// </summary>
    public static IReadOnlyList<BenchmarkScenario> GetAllScenarios(string? sandboxMode = null, string? dockerContainer = null) =>
    [
        .. GetBfclScenarios(),
        .. GetSweBenchScenarios(sandboxMode, dockerContainer),
        .. GetRulerScenarios()
    ];

    /// <summary>
    /// Filters scenarios according to the provided filter string ("all", "all-small", "bfcl", "swebench", "ruler", "deep", comma-separated, or matching Id/Name).
    /// </summary>
    public static IReadOnlyList<BenchmarkScenario> FilterScenarios(string? filter, string? sandboxMode = null, string? dockerContainer = null)
    {
        var all = GetAllScenarios(sandboxMode, dockerContainer);
        var allSmall = GetAllSmallScenarios(sandboxMode, dockerContainer);
        var bfcl = GetBfclScenarios();
        var swebench = GetSweBenchScenarios(sandboxMode, dockerContainer);
        var ruler = GetRulerScenarios();
        var deep = GetDeepContextScenarios();

        if (string.IsNullOrWhiteSpace(filter) ||
            filter.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            filter.Equals("full", StringComparison.OrdinalIgnoreCase) ||
            filter.Equals("everything", StringComparison.OrdinalIgnoreCase))
        {
            return all;
        }

        if (filter.Equals("all-small", StringComparison.OrdinalIgnoreCase) ||
            filter.Equals("small", StringComparison.OrdinalIgnoreCase))
        {
            return allSmall;
        }

        if (filter.Contains(','))
        {
            var parts = filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var combined = new List<BenchmarkScenario>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var part in parts)
            {
                foreach (var sc in FilterScenarios(part))
                {
                    if (seenIds.Add(sc.Id))
                    {
                        combined.Add(sc);
                    }
                }
            }

            return combined;
        }

        if (filter.Equals("bfcl", StringComparison.OrdinalIgnoreCase))
        {
            return bfcl;
        }

        if (filter.Equals("swebench", StringComparison.OrdinalIgnoreCase) || filter.Equals("swe", StringComparison.OrdinalIgnoreCase))
        {
            return swebench;
        }

        if (filter.Equals("ruler", StringComparison.OrdinalIgnoreCase))
        {
            return ruler;
        }

        if (filter.Equals("deep", StringComparison.OrdinalIgnoreCase) || filter.Equals("long", StringComparison.OrdinalIgnoreCase))
        {
            return deep;
        }

        var matched = all.Where(s =>
            s.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            filter.Contains(s.Id, StringComparison.OrdinalIgnoreCase) ||
            s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        if (matched.Count > 0)
        {
            return matched;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[Warning] No benchmark scenario matched filter '{filter}'. Available scenarios: {string.Join(", ", all.Select(s => s.Id))}");
        Console.ResetColor();
        return [];
    }
}
