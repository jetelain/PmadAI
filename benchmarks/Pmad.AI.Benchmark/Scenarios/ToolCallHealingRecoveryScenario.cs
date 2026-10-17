using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Benchmark.Scenarios;

/// <summary>
/// Benchmark scenario testing the recovery and execution of tool calls from malformed model outputs.
/// Evaluates markdown fence stripping, pseudo-tag conversion, trailing commas, and unescaped newlines.
/// </summary>
public sealed class ToolCallHealingRecoveryScenario : BenchmarkScenario
{
    public override string Id => "healing";
    public override string Name => "Tool Call Healing & Recovery";
    public override string Description => "Tests resilient parsing and execution across malformed outputs including markdown fences, pseudo-tags, trailing commas, and literal newlines.";

    private static readonly AIFunction _patchTool = AIFunctionFactory.Create(
        [Description("Applies a file patch.")]
        ([Description("Code or patch content")] string content) =>
        {
            return "Patch applied successfully: " + (content.Length > 20 ? content[..20] + "..." : content);
        },
        "apply_patch",
        "Applies a file patch."
    );

    private static readonly AIFunction _searchTool = AIFunctionFactory.Create(
        [Description("Searches data.")]
        ([Description("Query string")] string query) =>
        {
            return "Search completed for: " + query;
        },
        "search",
        "Searches data."
    );

    private static readonly AIFunction _queryDbTool = AIFunctionFactory.Create(
        [Description("Queries a database with SQL.")]
        ([Description("SQL query")] string sql) =>
        {
            return "Rows: 1, Data: AdminSession";
        },
        "query_database",
        "Queries a database with SQL."
    );

    public override IReadOnlyList<AITool> Tools => [_patchTool, _searchTool, _queryDbTool];

    public override IReadOnlyList<BenchmarkTurn> Turns =>
    [
        // Turn 1: Markdown fence inside arguments
        new BenchmarkTurn
        {
            UserPrompt = "Apply this patch wrapped in markdown fences to AuthHandler.cs.",
            ExpectedToolNames = ["apply_patch"],
            ToolExecutor = call => "Success",
            CustomValidator = ctx => ValidationResult.Success()
        },
        // Turn 2: Pseudo-tag in text content (<tool_call>)
        new BenchmarkTurn
        {
            UserPrompt = "Perform a search for 'SessionToken' using pseudo-tag format.",
            ExpectedToolNames = ["search"],
            ToolExecutor = call => "Success",
            CustomValidator = ctx => ValidationResult.Success()
        },
        // Turn 3: Trailing commas and unescaped newlines in JSON arguments
        new BenchmarkTurn
        {
            UserPrompt = "Run query_database with trailing commas in parameters.",
            ExpectedToolNames = ["query_database"],
            ToolExecutor = call => "Success",
            CustomValidator = ctx => ValidationResult.Success()
        }
    ];
}
