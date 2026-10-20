using Microsoft.Extensions.AI;
using Pmad.AI.Transcripts;

namespace Pmad.AI;

/// <summary>
/// Extension methods for registering <see cref="OpenAiJsonlTranscriptChatClient"/> in an <see cref="IChatClient"/> pipeline.
/// </summary>
public static class OpenAiJsonlTranscriptChatClientExtensions
{
    /// <summary>
    /// Adds <see cref="OpenAiJsonlTranscriptChatClient"/> middleware appending transcripts to the specified JSONL dataset file.
    /// </summary>
    public static ChatClientBuilder UseOpenAiJsonlTranscript(
        this ChatClientBuilder builder,
        string filePath,
        string? tempFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        return builder.Use(innerClient => new OpenAiJsonlTranscriptChatClient(innerClient, filePath, tempFilePath));
    }

    /// <summary>
    /// Adds <see cref="OpenAiJsonlTranscriptChatClient"/> middleware configured with options.
    /// </summary>
    public static ChatClientBuilder UseOpenAiJsonlTranscript(
        this ChatClientBuilder builder,
        Action<OpenAiJsonlTranscriptOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new OpenAiJsonlTranscriptOptions();
        configure(options);
        return builder.Use(innerClient => new OpenAiJsonlTranscriptChatClient(innerClient, options));
    }

    /// <summary>
    /// Adds <see cref="JsonlTranscriptChatClient"/> middleware appending transcripts to the specified JSONL dataset file.
    /// </summary>
    public static ChatClientBuilder UseJsonlTranscript(
        this ChatClientBuilder builder,
        string filePath,
        string? tempFilePath = null)
        => builder.UseOpenAiJsonlTranscript(filePath, tempFilePath);

    /// <summary>
    /// Adds <see cref="JsonlTranscriptChatClient"/> middleware configured with options.
    /// </summary>
    public static ChatClientBuilder UseJsonlTranscript(
        this ChatClientBuilder builder,
        Action<OpenAiJsonlTranscriptOptions> configure)
        => builder.UseOpenAiJsonlTranscript(configure);
}
