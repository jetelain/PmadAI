using Microsoft.Extensions.AI;

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
    /// Returns all available standard benchmark scenarios.
    /// </summary>
    public static IReadOnlyList<BenchmarkScenario> GetAllScenarios() =>
    [
        new WeatherScenario(),
        new CalculatorScenario(),
        new CustomerSupportScenario(),
        new DirectResponseScenario(),
        new AgentMultiTurnCompactionScenario(),
        new ToolCallHealingRecoveryScenario(),
        new StreamingRunawayAndPingPongScenario()
    ];

    /// <summary>
    /// Filters scenarios according to the provided filter string ("all" or matching Id/Name).
    /// </summary>
    public static IReadOnlyList<BenchmarkScenario> FilterScenarios(string? filter)
    {
        var all = GetAllScenarios();
        if (string.IsNullOrWhiteSpace(filter) || filter.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return all;
        }

        var matched = all.Where(s =>
            s.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        return matched.Count > 0 ? matched : all;
    }
}
