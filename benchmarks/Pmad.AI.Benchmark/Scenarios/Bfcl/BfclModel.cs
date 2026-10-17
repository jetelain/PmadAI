using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Benchmark.Scenarios.Bfcl;

public enum BfclCategory
{
    Simple,
    Multiple,
    Parallel,
    Irrelevance,
    MultiTurn,
    Strict
}

public sealed record BfclExpectedCall(
    string Name,
    IReadOnlyDictionary<string, object?> Arguments
);

public sealed record BfclEntry(
    string Id,
    BfclCategory Category,
    string Question,
    IReadOnlyList<AITool> Tools,
    IReadOnlyList<BfclExpectedCall> GroundTruth,
    bool ClearHistory = true,
    string? ToolResult = null,
    IReadOnlyList<string>? ExpectedTextKeywords = null
);
