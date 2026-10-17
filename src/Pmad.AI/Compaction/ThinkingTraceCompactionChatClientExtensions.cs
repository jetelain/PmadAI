using Microsoft.Extensions.AI;
using Pmad.AI.Compaction;

namespace Pmad.AI;

/// <summary>
/// Extension methods for registering <see cref="ThinkingTraceCompactionChatClient"/> in an <see cref="IChatClient"/> pipeline.
/// </summary>
public static class ThinkingTraceCompactionChatClientExtensions
{
    /// <summary>
    /// Adds <see cref="ThinkingTraceCompactionChatClient"/> middleware to the chat client pipeline.
    /// </summary>
    public static ChatClientBuilder UseThinkingTraceCompaction(
        this ChatClientBuilder builder,
        Action<ThinkingTraceCompactionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new ThinkingTraceCompactionOptions();
        configure?.Invoke(options);
        return builder.Use(innerClient => new ThinkingTraceCompactionChatClient(innerClient, options));
    }

    /// <summary>
    /// Adds <see cref="ThinkingTraceCompactionChatClient"/> middleware with the specified summarizer and verbatim window.
    /// </summary>
    public static ChatClientBuilder UseThinkingTraceCompaction(
        this ChatClientBuilder builder,
        ISpeculativeSummarizer? summarizer,
        int keepVerbatimCount = 10,
        int fallbackCharacterLimit = 120)
    {
        return builder.UseThinkingTraceCompaction(options =>
        {
            options.Summarizer = summarizer;
            options.KeepVerbatimCount = keepVerbatimCount;
            options.FallbackCharacterLimit = fallbackCharacterLimit;
        });
    }
}
