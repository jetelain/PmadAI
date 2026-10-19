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

    /// <summary>
    /// Adds <see cref="ToolLoopCircuitBreakerChatClient"/> middleware configured with custom options.
    /// </summary>
    public static ChatClientBuilder UseToolLoopCircuitBreaker(
        this ChatClientBuilder builder,
        Action<ToolLoopCircuitBreakerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new ToolLoopCircuitBreakerOptions();
        configure(options);
        return builder.Use(innerClient => new ToolLoopCircuitBreakerChatClient(innerClient, new ToolLoopCircuitBreaker(options)));
    }

    /// <summary>
    /// Adds <see cref="ToolLoopCircuitBreakerChatClient"/> middleware with dispatcher awareness and custom breaker options.
    /// </summary>
    public static ChatClientBuilder UseToolLoopCircuitBreaker(
        this ChatClientBuilder builder,
        Pmad.AI.Tools.LenientToolDispatcher? dispatcher,
        Action<ToolLoopCircuitBreakerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new ToolLoopCircuitBreakerOptions();
        configure?.Invoke(options);
        return builder.Use(innerClient => new ToolLoopCircuitBreakerChatClient(innerClient, new ToolLoopCircuitBreaker(options), dispatcher));
    }
}
