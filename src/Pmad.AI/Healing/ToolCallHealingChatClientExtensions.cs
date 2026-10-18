using Microsoft.Extensions.AI;
using Pmad.AI.Healing;
using Pmad.AI.Tools;

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

    /// <summary>
    /// Adds <see cref="ToolCallHealingChatClient"/> middleware configured with a <see cref="LenientToolDispatcher"/>
    /// to ensure <see cref="ToolCallHealingOptions.EnforceAvailableToolsGuardrail"/> is aware of aliases, translations, and namespaces.
    /// </summary>
    public static ChatClientBuilder UseToolCallHealing(
        this ChatClientBuilder builder,
        LenientToolDispatcher dispatcher,
        Action<ToolCallHealingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(dispatcher);
        var options = new ToolCallHealingOptions { Dispatcher = dispatcher };
        configure?.Invoke(options);
        return builder.Use(innerClient => new ToolCallHealingChatClient(innerClient, options));
    }
}
