using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Benchmark.Scenarios;

/// <summary>
/// Benchmark scenario testing single/multi-tool queries and conversational context retention.
/// </summary>
public sealed class WeatherScenario : BenchmarkScenario
{
    public override string Id => "weather";
    public override string Name => "Weather Multi-City & Comparison";
    public override string Description => "Tests function calling for multiple entities (Paris, Tokyo) followed by contextual reasoning over tool outputs.";

    private static readonly AIFunction _weatherTool = AIFunctionFactory.Create(
        [Description("Retrieves the current weather report for a given city.")]
        (
            [Description("Name of the city (e.g. Paris, Tokyo, London)")] string city,
            [Description("Temperature unit ('celsius' or 'fahrenheit')")] string unit = "celsius"
        ) =>
        {
            // Dummy factory implementation; the benchmark runner intercepts calls through ToolExecutor
            return $"Weather for {city}: 20°C";
        },
        "get_weather",
        "Retrieves the current weather report for a given city."
    );

    public override IReadOnlyList<AITool> Tools => [_weatherTool];

    public override IReadOnlyList<BenchmarkTurn> Turns =>
    [
        // Turn 1: Multi-entity tool query
        new BenchmarkTurn
        {
            UserPrompt = "What is the current weather in Paris and Tokyo?",
            ExpectedToolNames = ["get_weather"],
            ToolExecutor = call =>
            {
                var city = ExtractCity(call.Arguments);
                if (city.Contains("paris", StringComparison.OrdinalIgnoreCase))
                {
                    return new { city = "Paris", temperature = 18, unit = "celsius", condition = "Partly Cloudy", humidity = "65%" };
                }
                if (city.Contains("tokyo", StringComparison.OrdinalIgnoreCase))
                {
                    return new { city = "Tokyo", temperature = 22, unit = "celsius", condition = "Sunny", humidity = "45%" };
                }
                return new { city, temperature = 20, unit = "celsius", condition = "Clear", humidity = "50%" };
            },
            CustomValidator = ctx =>
            {
                var text = ctx.AssistantFinalText;
                var hasParis = text.Contains("Paris", StringComparison.OrdinalIgnoreCase) || text.Contains("18");
                var hasTokyo = text.Contains("Tokyo", StringComparison.OrdinalIgnoreCase) || text.Contains("22");
                if (!hasParis && !hasTokyo)
                {
                    return ValidationResult.Failure("Expected assistant output to reference weather for Paris or Tokyo.");
                }
                return ValidationResult.Success();
            }
        },

        // Turn 2: Contextual follow-up without needing further tools
        new BenchmarkTurn
        {
            UserPrompt = "Which city is warmer, and by how many degrees celsius?",
            ExpectedToolNames = [],
            ToolExecutor = _ => null,
            CustomValidator = ctx =>
            {
                var text = ctx.AssistantFinalText;
                var mentionsTokyo = text.Contains("Tokyo", StringComparison.OrdinalIgnoreCase);
                var mentionsDiff = text.Contains("4") || text.Contains("four", StringComparison.OrdinalIgnoreCase);
                if (!mentionsTokyo)
                {
                    return ValidationResult.Failure("Expected assistant to identify Tokyo as the warmer city.");
                }
                if (!mentionsDiff)
                {
                    return ValidationResult.Failure("Expected assistant to compute temperature difference of 4°C.");
                }
                return ValidationResult.Success();
            }
        }
    ];

    private static string ExtractCity(IDictionary<string, object?>? args)
    {
        if (args == null) return string.Empty;
        if (args.TryGetValue("city", out var cityVal) && cityVal != null)
        {
            return cityVal.ToString() ?? string.Empty;
        }
        if (args.TryGetValue("location", out var locVal) && locVal != null)
        {
            return locVal.ToString() ?? string.Empty;
        }
        return string.Empty;
    }
}
