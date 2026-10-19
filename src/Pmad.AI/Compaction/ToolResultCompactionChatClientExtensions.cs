using Microsoft.Extensions.AI;
using Pmad.AI.Compaction;

namespace Pmad.AI;

/// <summary>
/// Extension methods for registering <see cref="ToolResultCompactionChatClient"/> in an <see cref="IChatClient"/> pipeline.
/// </summary>
public static class ToolResultCompactionChatClientExtensions
{
    /// <summary>
    /// Adds <see cref="ToolResultCompactionChatClient"/> middleware to the chat client pipeline.
    /// </summary>
    public static ChatClientBuilder UseToolResultCompaction(
        this ChatClientBuilder builder,
        Action<ToolResultCompactionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new ToolResultCompactionOptions();
        configure?.Invoke(options);
        return builder.Use(innerClient => new ToolResultCompactionChatClient(innerClient, options));
    }

    /// <summary>
    /// Adds <see cref="ToolResultCompactionChatClient"/> middleware with the specified summarizer and verbatim window.
    /// </summary>
    public static ChatClientBuilder UseToolResultCompaction(
        this ChatClientBuilder builder,
        int keepVerbatimCount = 20,
        int fallbackCharacterLimit = 1750)
    {
        return builder.UseToolResultCompaction(options =>
        {
            options.KeepVerbatimCount = keepVerbatimCount;
            options.FallbackCharacterLimit = fallbackCharacterLimit;
        });
    }
}
