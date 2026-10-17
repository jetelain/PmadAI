using Microsoft.Extensions.AI;

namespace Pmad.AI.Benchmark.Scenarios;

/// <summary>
/// Context passed to custom validation callbacks for a scenario turn.
/// </summary>
public sealed record TurnValidationContext(
    int TurnIndex,
    string UserPrompt,
    IReadOnlyList<FunctionCallContent> ActualToolCalls,
    string AssistantFinalText,
    string? AssistantReasoning,
    bool CompletedWithoutError
);

/// <summary>
/// Outcome of validating a benchmark turn.
/// </summary>
public sealed record ValidationResult(bool IsSuccess, string? Message = null)
{
    public static ValidationResult Success() => new(true);
    public static ValidationResult Failure(string message) => new(false, message);
}

/// <summary>
/// Represents a single mocked user interaction turn in a multi-turn benchmark scenario.
/// </summary>
public sealed class BenchmarkTurn
{
    /// <summary>
    /// Mocked prompt supplied as the user input for this turn.
    /// </summary>
    public required string UserPrompt { get; init; }

    /// <summary>
    /// Tool function names expected to be called during this turn, or empty if direct answer is expected.
    /// </summary>
    public IReadOnlyList<string> ExpectedToolNames { get; init; } = [];

    /// <summary>
    /// Handler providing deterministic mocked execution for tool calls invoked by the model during this turn.
    /// </summary>
    public required Func<FunctionCallContent, object?> ToolExecutor { get; init; }

    /// <summary>
    /// Optional custom validation callback to inspect the assistant's final response and tool execution.
    /// </summary>
    public Func<TurnValidationContext, ValidationResult>? CustomValidator { get; init; }

    /// <summary>
    /// Marks this turn as a semantic recall probe; any failure on it marks semantic recall as failed.
    /// </summary>
    public bool IsRecallProbe { get; init; }
}
