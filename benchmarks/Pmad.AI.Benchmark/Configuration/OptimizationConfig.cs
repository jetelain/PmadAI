namespace Pmad.AI.Benchmark.Configuration;

/// <summary>
/// Defines an optimization configuration evaluated in the benchmark suite,
/// isolating individual utilities or evaluating full combined pipelines.
/// </summary>
public sealed record OptimizationConfig(
    string Id,
    string Label,
    string Description)
{
    public static readonly OptimizationConfig Baseline = new(
        "baseline",
        "Baseline (Raw)",
        "Raw model execution without any optimizations; healing turned off.");

    public static readonly OptimizationConfig Healing = new(
        "opt-healing",
        "Tool Call Healing",
        "ToolCallHealingChatClient repairing malformed JSON and syntax.");

    public static readonly OptimizationConfig ThinkingCompaction = new(
        "opt-thinking-compaction",
        "Thinking Trace Compaction",
        "ThinkingTraceCompactionChatClient pruning historical reasoning traces.");

    public static readonly OptimizationConfig ToolCompaction = new(
        "opt-tool-compaction",
        "Tool Result Compaction",
        "ToolResultCompactionChatClient truncating/compacting historical tool outputs.");

    public static readonly OptimizationConfig SpeculativeSlm = new(
        "opt-speculative-slm",
        "Speculative SLM Compaction",
        "Speculative CPU summarizer + thinking & tool result compaction.");

    public static readonly OptimizationConfig LenientDispatch = new(
        "opt-lenient-dispatch",
        "Lenient Dispatch & Guardrails",
        "LenientToolDispatcher + ToolLoopCircuitBreaker.");

    public static readonly OptimizationConfig All = new(
        "all-optimizations",
        "All Optimizations Combined",
        "Full pipeline: Healing + Compaction + Speculative Summarizer + Dispatcher + Breaker.");

    public static readonly IReadOnlyList<OptimizationConfig> AllConfigs = [
        Baseline,
        Healing,
        ThinkingCompaction,
        ToolCompaction,
        SpeculativeSlm,
        LenientDispatch,
        All
    ];

    public static OptimizationConfig? Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return AllConfigs.FirstOrDefault(c =>
            string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Id.Replace("opt-", ""), id, StringComparison.OrdinalIgnoreCase) ||
            (id.Equals("all", StringComparison.OrdinalIgnoreCase) && c.Id == "all-optimizations") ||
            (id.Equals("thinking", StringComparison.OrdinalIgnoreCase) && c.Id == "opt-thinking-compaction") ||
            (id.Equals("tool", StringComparison.OrdinalIgnoreCase) && c.Id == "opt-tool-compaction") ||
            (id.Equals("speculative", StringComparison.OrdinalIgnoreCase) && c.Id == "opt-speculative-slm") ||
            (id.Equals("dispatch", StringComparison.OrdinalIgnoreCase) && c.Id == "opt-lenient-dispatch"));
    }
}
