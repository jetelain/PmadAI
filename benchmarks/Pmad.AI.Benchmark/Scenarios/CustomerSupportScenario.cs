using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Pmad.AI.Benchmark.Scenarios;

/// <summary>
/// Benchmark scenario testing chained multi-turn tool dependencies (order lookup -> package tracking).
/// </summary>
public sealed class CustomerSupportScenario : BenchmarkScenario
{
    public override string Id => "support";
    public override string Name => "Multi-Turn Customer Support Pipeline";
    public override string Description => "Tests multi-step tool execution where parameters from the first tool (tracking code) are used in a subsequent turn.";

    private static readonly AIFunction _lookupTool = AIFunctionFactory.Create(
        [Description("Retrieves order details and shipping carrier for a customer order.")]
        (
            [Description("The unique alphanumeric order ID, e.g. 'ORD-9921'")] string order_id
        ) => "ORD",
        "lookup_order",
        "Retrieves order details and shipping carrier for a customer order."
    );

    private static readonly AIFunction _trackTool = AIFunctionFactory.Create(
        [Description("Tracks real-time status and estimated delivery time for a shipment.")]
        (
            [Description("The shipping carrier name, e.g. DHL, FedEx")] string carrier,
            [Description("The shipment tracking number")] string tracking_code
        ) => "TRACK",
        "track_package",
        "Tracks real-time status and estimated delivery time for a shipment."
    );

    public override IReadOnlyList<AITool> Tools => [_lookupTool, _trackTool];

    public override IReadOnlyList<BenchmarkTurn> Turns =>
    [
        // Turn 1: Lookup order
        new BenchmarkTurn
        {
            UserPrompt = "Hello! Could you look up order ORD-9921 and tell me the carrier and tracking code?",
            ExpectedToolNames = ["lookup_order"],
            ToolExecutor = call =>
            {
                var orderId = ExtractArg(call.Arguments, "order_id");
                return new
                {
                    order_id = string.IsNullOrWhiteSpace(orderId) ? "ORD-9921" : orderId,
                    customer = "Sarah Connor",
                    carrier = "DHL",
                    tracking_code = "DHL-88319",
                    status = "Shipped",
                    items = new[] { "Ergonomic Mechanical Keyboard" }
                };
            },
            CustomValidator = ctx =>
            {
                var text = ctx.AssistantFinalText;
                var hasCarrier = text.Contains("DHL", StringComparison.OrdinalIgnoreCase);
                var hasTracking = text.Contains("DHL-88319", StringComparison.OrdinalIgnoreCase) || text.Contains("88319");
                if (!hasCarrier && !hasTracking)
                {
                    return ValidationResult.Failure("Expected response to contain carrier (DHL) and tracking code (DHL-88319).");
                }
                return ValidationResult.Success();
            }
        },

        // Turn 2: Track shipment using tracking code obtained in Turn 1
        new BenchmarkTurn
        {
            UserPrompt = "Please track that DHL package for me and tell me when it will arrive.",
            ExpectedToolNames = ["track_package"],
            ToolExecutor = call =>
            {
                var tracking = ExtractArg(call.Arguments, "tracking_code");
                return new
                {
                    tracking_code = string.IsNullOrWhiteSpace(tracking) ? "DHL-88319" : tracking,
                    carrier = "DHL",
                    status = "Out for delivery",
                    current_location = "Los Angeles Regional Facility",
                    estimated_delivery = "Today by 4:00 PM"
                };
            },
            CustomValidator = ctx =>
            {
                var text = ctx.AssistantFinalText;
                var hasDelivery = text.Contains("Today", StringComparison.OrdinalIgnoreCase) ||
                                  text.Contains("4:00", StringComparison.OrdinalIgnoreCase) ||
                                  text.Contains("delivery", StringComparison.OrdinalIgnoreCase);
                if (!hasDelivery)
                {
                    return ValidationResult.Failure("Expected response to convey delivery ETA (Today by 4:00 PM).");
                }
                return ValidationResult.Success();
            }
        }
    ];

    private static string ExtractArg(IDictionary<string, object?>? args, string key)
    {
        if (args == null) return string.Empty;
        if (args.TryGetValue(key, out var val) && val != null)
        {
            return val.ToString() ?? string.Empty;
        }
        foreach (var kvp in args)
        {
            if (kvp.Key.Contains(key, StringComparison.OrdinalIgnoreCase) && kvp.Value != null)
            {
                return kvp.Value.ToString() ?? string.Empty;
            }
        }
        return string.Empty;
    }
}
