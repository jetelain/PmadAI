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

    public static readonly OptimizationConfig RecommendedSlm = new(
        "opt-recommended-slm",
        "Recommended SLM Pipeline",
        "Lenient Dispatch + Breaker + Deterministic Tool Compaction + Healing (no thinking compaction or SLM summarizer).");

    public static readonly OptimizationConfig AllLocalLlmWithoutSlm = new(
        "opt-all-local-llm-without-slm",
        "All Local LLM without SLM Summarizer",
        "Full pipeline: Lenient Dispatch + Breaker + Thinking Compaction + Deterministic Tool Compaction + Healing.");

    public static readonly OptimizationConfig AllLocalLlmWithSlm = new(
        "opt-all-local-llm-with-slm",
        "All Local LLM with SLM Summarizer",
        "Full pipeline with speculative SLM summarizer: Lenient Dispatch + Breaker + Thinking Compaction + Speculative Tool Compaction + Healing.");

    public static readonly OptimizationConfig HealingLenient = new(
        "opt-healing-lenient",
        "Healing + Lenient Dispatch",
        "Bilateral synergy ablation: Tool Call Healing + Lenient Dispatch + Circuit Breaker.");

    public static readonly OptimizationConfig HealingToolCompaction = new(
        "opt-healing-tool-compaction",
        "Healing + Tool Compaction",
        "Bilateral synergy ablation: Tool Call Healing + Deterministic Tool Result Compaction.");

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
        HealingLenient,
        HealingToolCompaction,
        RecommendedSlm,
        AllLocalLlmWithoutSlm,
        AllLocalLlmWithSlm,
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
            (id.Equals("dispatch", StringComparison.OrdinalIgnoreCase) && c.Id == "opt-lenient-dispatch") ||
            (id.Equals("slm", StringComparison.OrdinalIgnoreCase) && c.Id == "opt-recommended-slm") ||
            (id.Equals("recommended", StringComparison.OrdinalIgnoreCase) && c.Id == "opt-recommended-slm") ||
            (id.Equals("local-llm", StringComparison.OrdinalIgnoreCase) && c.Id == "opt-all-local-llm-without-slm") ||
            (id.Equals("no-slm", StringComparison.OrdinalIgnoreCase) && c.Id == "opt-all-local-llm-without-slm") ||
            (id.Equals("with-slm", StringComparison.OrdinalIgnoreCase) && c.Id == "opt-all-local-llm-with-slm") ||
            (id.Equals("local-llm-slm", StringComparison.OrdinalIgnoreCase) && c.Id == "opt-all-local-llm-with-slm") ||
            (id.Equals("healing-lenient", StringComparison.OrdinalIgnoreCase) && c.Id == "opt-healing-lenient") ||
            (id.Equals("healing+lenient", StringComparison.OrdinalIgnoreCase) && c.Id == "opt-healing-lenient") ||
            (id.Equals("healing-tool", StringComparison.OrdinalIgnoreCase) && c.Id == "opt-healing-tool-compaction") ||
            (id.Equals("healing+tool", StringComparison.OrdinalIgnoreCase) && c.Id == "opt-healing-tool-compaction") ||
            (id.Equals("healing-compaction", StringComparison.OrdinalIgnoreCase) && c.Id == "opt-healing-tool-compaction") ||
            (id.Equals("healing+compaction", StringComparison.OrdinalIgnoreCase) && c.Id == "opt-healing-tool-compaction"));
    }
}
