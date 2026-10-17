using Microsoft.Extensions.AI;
using Pmad.AI.Tools;

namespace Pmad.AI;

/// <summary>
/// Extension methods for registering <see cref="LenientToolDispatcher"/> middleware in an <see cref="IChatClient"/> pipeline.
/// </summary>
public static class LenientToolDispatcherExtensions
{
    /// <summary>
    /// Adds <see cref="LenientToolDispatchChatClient"/> middleware to rewrite aliased or translated tool calls.
    /// Place between <see cref="Pmad.AI.Healing.ToolCallHealingChatClient"/> (inner) and <c>FunctionInvokingChatClient</c> (outer).
    /// </summary>
    public static ChatClientBuilder UseLenientToolDispatch(
        this ChatClientBuilder builder,
        LenientToolDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(dispatcher);
        return builder.Use(innerClient => new LenientToolDispatchChatClient(innerClient, dispatcher));
    }
}
