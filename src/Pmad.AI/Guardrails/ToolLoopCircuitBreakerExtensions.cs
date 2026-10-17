using Microsoft.Extensions.AI;
using Pmad.AI.Guardrails;

namespace Pmad.AI;

/// <summary>
/// Extension methods for registering <see cref="ToolLoopCircuitBreakerChatClient"/> middleware in an <see cref="IChatClient"/> pipeline.
/// </summary>
public static class ToolLoopCircuitBreakerExtensions
{
    /// <summary>
    /// Adds <see cref="ToolLoopCircuitBreakerChatClient"/> middleware to detect and halt runaway loops or ping-pong tool invocations.
    /// </summary>
    public static ChatClientBuilder UseToolLoopCircuitBreaker(
        this ChatClientBuilder builder,
        ToolLoopCircuitBreaker? breaker = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Use(innerClient => new ToolLoopCircuitBreakerChatClient(innerClient, breaker));
    }
}
