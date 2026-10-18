namespace Pmad.AI.Benchmark.Engine;

/// <summary>
/// Classifies scenario failures into a clear, actionable failure taxonomy without requiring manual transcript inspection.
/// </summary>
public static class FailureTaxonomyClassifier
{
    /// <summary>
    /// Evaluates a completed scenario result and returns its primary diagnostic failure taxonomy category.
    /// </summary>
    public static string Classify(BenchmarkScenarioResult result)
    {
        if (result.TaskSuccess && result.Status == BenchmarkStatus.Passed)
        {
            return "—";
        }

        // 1. Tool loop detection (breaker stats, error message, or assistant final text)
        if ((result.CircuitBreakerStats != null && result.CircuitBreakerStats.TotalTrips > 0) ||
            (!string.IsNullOrEmpty(result.ErrorMessage) &&
             (result.ErrorMessage.Contains("Circuit Breaker", StringComparison.OrdinalIgnoreCase) ||
              result.ErrorMessage.Contains("loop", StringComparison.OrdinalIgnoreCase) ||
              result.ErrorMessage.Contains("ping-pong", StringComparison.OrdinalIgnoreCase))) ||
            result.TurnResults.Any(t =>
                (!string.IsNullOrEmpty(t.AssistantFinalText) && t.AssistantFinalText.Contains("[Circuit Breaker Tripped]", StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(t.ValidationMessage) &&
                 (t.ValidationMessage.Contains("Circuit Breaker", StringComparison.OrdinalIgnoreCase) ||
                  t.ValidationMessage.Contains("loop", StringComparison.OrdinalIgnoreCase) ||
                  t.ValidationMessage.Contains("ping-pong", StringComparison.OrdinalIgnoreCase)))))
        {
            return "Tool Loop";
        }

        var failedTurns = result.TurnResults.Where(t => t.Status == BenchmarkStatus.Failed).ToList();

        if (failedTurns.Count > 0)
        {
            bool hasOverCalling = false;
            bool hasMissingCall = false;
            bool hasWrongTool = false;
            bool hasBadArgs = false;

            foreach (var turn in failedTurns)
            {
                var msg = turn.ValidationMessage ?? string.Empty;

                if (msg.Contains("0 were expected", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("0 expected", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("Expected 0 tool calls", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("irrelevance", StringComparison.OrdinalIgnoreCase))
                {
                    hasOverCalling = true;
                }
                else if (msg.Contains("not invoked", StringComparison.OrdinalIgnoreCase) ||
                         msg.Contains("No tool call", StringComparison.OrdinalIgnoreCase) ||
                         (msg.Contains("Expected", StringComparison.OrdinalIgnoreCase) && msg.Contains("got 0", StringComparison.OrdinalIgnoreCase)))
                {
                    hasMissingCall = true;
                }
                else if (msg.Contains("Expected tool", StringComparison.OrdinalIgnoreCase) ||
                         msg.Contains("Unexpected tool", StringComparison.OrdinalIgnoreCase))
                {
                    hasWrongTool = true;
                }
                else if (msg.Contains("Argument", StringComparison.OrdinalIgnoreCase) ||
                         msg.Contains("mismatch", StringComparison.OrdinalIgnoreCase) ||
                         msg.Contains("did not match", StringComparison.OrdinalIgnoreCase) ||
                         msg.Contains("Invalid argument", StringComparison.OrdinalIgnoreCase))
                {
                    hasBadArgs = true;
                }
            }

            if (hasOverCalling) return "Over-Calling";
            if (hasMissingCall) return "Missing Tool Call";
            if (hasWrongTool) return "Wrong Tool";
            if (hasBadArgs) return "Bad Arguments";
        }

        // 2. Patch verification rejection (SWE-bench or patch mention)
        if (!result.TaskSuccess &&
            ((!string.IsNullOrEmpty(result.TaskFailureReason) && result.TaskFailureReason.Contains("patch", StringComparison.OrdinalIgnoreCase)) ||
             (result.Suite == "SWE-bench" && result.TurnsPassed == result.TotalTurns && result.TotalTurns > 0) ||
             (!string.IsNullOrEmpty(result.ErrorMessage) && result.ErrorMessage.Contains("patch", StringComparison.OrdinalIgnoreCase))))
        {
            return "Patch Rejected";
        }

        if (failedTurns.Count == 0)
        {
            return !result.TaskSuccess ? (result.TaskFailureReason ?? "Task Unsuccessful") : "—";
        }

        return "Validation Failure";
    }
}
