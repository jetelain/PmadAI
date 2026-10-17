using Microsoft.Extensions.AI;
using Pmad.AI.Healing;

namespace Pmad.AI;

/// <summary>
/// Extension methods for registering <see cref="ToolCallHealingChatClient"/> in an <see cref="IChatClient"/> pipeline.
/// </summary>
public static class ToolCallHealingChatClientExtensions
{
    /// <summary>
    /// Adds <see cref="ToolCallHealingChatClient"/> middleware to the chat client pipeline.
    /// </summary>
    public static ChatClientBuilder UseToolCallHealing(
        this ChatClientBuilder builder,
        Action<ToolCallHealingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new ToolCallHealingOptions();
        configure?.Invoke(options);
        return builder.Use(innerClient => new ToolCallHealingChatClient(innerClient, options));
    }
}
