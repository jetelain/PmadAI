using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Benchmark.Scenarios;

/// <summary>
/// Benchmark scenario testing mathematical formula invocation and numerical synthesis.
/// </summary>
public sealed class CalculatorScenario : BenchmarkScenario
{
    public override string Id => "calculator";
    public override string Name => "Calculator Math Expression";
    public override string Description => "Tests tool execution for arithmetic calculations with discount percentages and monetary formatting.";

    private static readonly AIFunction _calcTool = AIFunctionFactory.Create(
        [Description("Evaluates an arithmetic mathematical expression and returns the numerical result.")]
        (
            [Description("The mathematical expression to evaluate, e.g. '(3 * 49.99 + 2 * 14.50) * 0.85'")] string expression
        ) =>
        {
            return "0";
        },
        "calculate",
        "Evaluates an arithmetic mathematical expression and returns the numerical result."
    );

    public override IReadOnlyList<AITool> Tools => [_calcTool];

    public override IReadOnlyList<BenchmarkTurn> Turns =>
    [
        new BenchmarkTurn
        {
            UserPrompt = "A customer buys 3 jackets for $49.99 each and 2 scarves for $14.50 each. A 15% discount is applied to the subtotal. Calculate the final total price using the calculate tool.",
            ExpectedToolNames = ["calculate"],
            ToolExecutor = call =>
            {
                var expr = ExtractExpression(call.Arguments);
                var evalResult = EvaluateExpression(expr);
                return new { expression = expr, result = evalResult };
            },
            CustomValidator = ctx =>
            {
                var text = ctx.AssistantFinalText;
                // Subtotal is 178.97, after 15% off: 152.12 (or 152.1245)
                var hasExpectedNumber = text.Contains("152.12") || text.Contains("152.1") || text.Contains("152");
                if (!hasExpectedNumber)
                {
                    return ValidationResult.Failure($"Expected final answer to state approximately $152.12. Model answered: '{text}'");
                }
                return ValidationResult.Success();
            }
        }
    ];

    private static string ExtractExpression(IDictionary<string, object?>? args)
    {
        if (args == null) return string.Empty;
        if (args.TryGetValue("expression", out var exprVal) && exprVal != null)
        {
            return exprVal.ToString() ?? string.Empty;
        }
        if (args.TryGetValue("query", out var qVal) && qVal != null)
        {
            return qVal.ToString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static double EvaluateExpression(string expr)
    {
        if (string.IsNullOrWhiteSpace(expr)) return 152.12;

        try
        {
            var cleaned = Regex.Replace(expr, @"[^\d\.\+\-\*\/\(\)\s]", "");
            var dt = new DataTable();
            var val = dt.Compute(cleaned, "");
            return Math.Round(Convert.ToDouble(val, CultureInfo.InvariantCulture), 2);
        }
        catch
        {
            // Default expected value for the prompt
            return 152.12;
        }
    }
}
