using Microsoft.Extensions.AI;
using Pmad.AI.Transcripts;

namespace Pmad.AI;

/// <summary>
/// Extension methods for registering <see cref="VerbatimTranscriptChatClient"/> in an <see cref="IChatClient"/> pipeline.
/// </summary>
public static class VerbatimTranscriptChatClientExtensions
{
    /// <summary>
    /// Adds <see cref="VerbatimTranscriptChatClient"/> middleware to the chat client pipeline.
    /// </summary>
    public static ChatClientBuilder UseVerbatimTranscript(
        this ChatClientBuilder builder,
        Action<VerbatimTranscriptOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new VerbatimTranscriptOptions();
        configure?.Invoke(options);
        return builder.Use(innerClient => new VerbatimTranscriptChatClient(innerClient, options));
    }

    /// <summary>
    /// Adds <see cref="VerbatimTranscriptChatClient"/> middleware saving transcripts to the specified directory.
    /// </summary>
    public static ChatClientBuilder UseVerbatimTranscript(
        this ChatClientBuilder builder,
        string outputDirectory,
        string? sessionTitle = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        return builder.UseVerbatimTranscript(options =>
        {
            options.OutputDirectory = outputDirectory;
            options.SessionTitle = sessionTitle;
        });
    }
}
