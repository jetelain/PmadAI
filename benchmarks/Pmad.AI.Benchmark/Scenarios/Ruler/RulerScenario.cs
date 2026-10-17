using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Benchmark.Scenarios.Ruler;

/// <summary>
/// Benchmark scenario evaluating context window retention and semantic recall under conversation compaction (RULER / NIAH).
/// Generates authentic haystacks of configurable token depths with needle anchor facts embedded at specific depths.
/// </summary>
public class RulerScenario : BenchmarkScenario
{
    private readonly string _id;
    private readonly string _name;
    private readonly string _description;
    private readonly int _targetTokens;
    private readonly IReadOnlyList<(double DepthFraction, string NeedleText, string Key, string ExpectedValue)> _needles;

    public RulerScenario(
        string id = "ruler-8k",
        string name = "RULER / NIAH Context Recall (8k)",
        string description = "Evaluates single and multi-needle recall across an 8,192-token haystack under baseline vs compaction.",
        int targetTokens = 8192)
    {
        _id = id;
        _name = name;
        _description = description;
        _targetTokens = targetTokens;

        _needles =
        [
            (0.15, "The emergency rollback authorization secret key is 'ROLLBACK-SEC-99042'.", "rollback_secret", "ROLLBACK-SEC-99042"),
            (0.50, "The database replica node DB-NODE-ALPHA was migrated to target IP address '10.240.18.42'.", "migrated_ip", "10.240.18.42"),
            (0.85, "The maximum allowable connection timeout for cluster 'omega-9' is configured to 850 milliseconds.", "timeout", "850")
        ];
    }

    public override string Id => _id;
    public override string Name => _name;
    public override string Description => _description;

    public override double ComplexityMultiplier => _targetTokens switch
    {
        <= 8192 => 5.0,  // 2 turns * 5.0 = 10.0 weight
        <= 16384 => 15.0,
        <= 32768 => 20.0,
        <= 65536 => 25.0,
        _ => 30.0
    };

    private static readonly AIFunction _fetchAuditLogsTool = AIFunctionFactory.Create(
        [Description("Fetches system architecture and audit logs for the specified repository partition.")]
        ([Description("Partition identifier or cluster name")] string partition) =>
        {
            return "Audit log payload loaded.";
        },
        "fetch_audit_logs",
        "Fetches system architecture and audit logs for the specified repository partition."
    );

    public override IReadOnlyList<AITool> Tools => [_fetchAuditLogsTool];

    public override IReadOnlyList<BenchmarkTurn> Turns =>
    [
        // Turn 1: Tool invocation fetching the large haystack containing needles
        new BenchmarkTurn
        {
            UserPrompt = "Please fetch the system architecture and audit logs for cluster 'omega-9'.",
            ExpectedToolNames = ["fetch_audit_logs"],
            ToolExecutor = call =>
            {
                var needleTuples = _needles.Select(n => (n.DepthFraction, n.NeedleText)).ToList();
                return RulerHaystackGenerator.GenerateHaystack(_targetTokens, needleTuples);
            },
            CustomValidator = ctx => ValidationResult.Success()
        },

        // Turn 2: Needle recall probe - tests retention of facts planted in Turn 1
        new BenchmarkTurn
        {
            UserPrompt = "Based on the audit logs retrieved in Turn 1, answer the following three questions:\n" +
                         "1. What is the emergency rollback authorization secret key?\n" +
                         "2. What is the migrated target IP address for DB-NODE-ALPHA?\n" +
                         "3. What is the maximum allowable connection timeout in milliseconds for cluster 'omega-9'?",
            ExpectedToolNames = [],
            ToolExecutor = _ => null,
            IsRecallProbe = true,
            CustomValidator = ctx =>
            {
                var text = ctx.AssistantFinalText;
                var missing = new List<string>();

                foreach (var needle in _needles)
                {
                    if (!text.Contains(needle.ExpectedValue, StringComparison.OrdinalIgnoreCase))
                    {
                        missing.Add($"{needle.Key} (expected '{needle.ExpectedValue}')");
                    }
                }

                if (missing.Count > 0)
                {
                    return ValidationResult.Failure(
                        $"RULER recall probe failed at depth {_targetTokens} tokens. Missing needles: [{string.Join(", ", missing)}]. Model response: '{text}'");
                }

                return ValidationResult.Success();
            }
        }
    ];
}

public sealed class Ruler8kScenario : RulerScenario
{
    public Ruler8kScenario() : base(
        id: "ruler-8k",
        name: "RULER Context Recall (8k)",
        description: "Evaluates multi-needle recall across an 8,192-token context depth.",
        targetTokens: 8192)
    {
    }
}

public sealed class Ruler16kScenario : RulerScenario
{
    public Ruler16kScenario() : base(
        id: "ruler-16k",
        name: "RULER Context Recall (16k)",
        description: "Evaluates multi-needle recall across a 16,384-token context depth.",
        targetTokens: 16384)
    {
    }
}

public sealed class Ruler32kScenario : RulerScenario
{
    public Ruler32kScenario() : base(
        id: "ruler-32k",
        name: "RULER Context Recall (32k)",
        description: "Evaluates multi-needle recall across a 32,768-token context depth.",
        targetTokens: 32768)
    {
    }
}

public sealed class Ruler64kScenario : RulerScenario
{
    public Ruler64kScenario() : base(
        id: "ruler-64k",
        name: "RULER Context Recall (64k)",
        description: "Evaluates multi-needle recall across a 65,536-token context depth for SLM context ceilings.",
        targetTokens: 65536)
    {
    }
}

public sealed class Ruler128kScenario : RulerScenario
{
    public Ruler128kScenario() : base(
        id: "ruler-128k",
        name: "RULER Context Recall (128k)",
        description: "Evaluates multi-needle recall across a 131,072-token context depth for LLMs (9B+ and Ollama).",
        targetTokens: 131072)
    {
    }
}
