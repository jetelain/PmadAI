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

    /// <summary>
    /// Attaches a <see cref="LenientToolDispatcher"/> to <see cref="ChatOptions.AdditionalProperties"/>
    /// for downstream middleware awareness.
    /// </summary>
    public static ChatOptions SetToolDispatcher(this ChatOptions options, LenientToolDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dispatcher);
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties[nameof(LenientToolDispatcher)] = dispatcher;
        return options;
    }

    /// <summary>
    /// Retrieves any attached <see cref="LenientToolDispatcher"/> from <see cref="ChatOptions.AdditionalProperties"/>.
    /// </summary>
    public static LenientToolDispatcher? GetToolDispatcher(this ChatOptions? options)
    {
        if (options?.AdditionalProperties != null &&
            options.AdditionalProperties.TryGetValue(nameof(LenientToolDispatcher), out var val) &&
            val is LenientToolDispatcher dispatcher)
        {
            return dispatcher;
        }
        return null;
    }
}
