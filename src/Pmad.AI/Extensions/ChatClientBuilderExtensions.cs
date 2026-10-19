using Microsoft.Extensions.AI;
using Pmad.AI.Compaction;
using Pmad.AI.Guardrails;
using Pmad.AI.Healing;
using Pmad.AI.Tools;

namespace Pmad.AI;

/// <summary>
/// First-class pipeline presets providing battle-tested configurations for Small Language Models (SLMs)
/// and large, capable Local LLMs.
/// </summary>
public static class ChatClientBuilderExtensions
{
    /// <summary>
    /// Configures the recommended pipeline for Small Language Models (SLMs &lt;= 8B parameters).
    /// Omits thinking trace compaction and natural language speculative summarization to preserve KV-cache stability
    /// and deterministic code/tool reasoning, while providing lenient tool dispatch, circuit breaker guardrails,
    /// deterministic tool result compaction, and tool call healing.
    /// </summary>
    /// <param name="builder">The chat client builder.</param>
    /// <param name="dispatcher">Optional lenient tool dispatcher for handling tool/param aliases and namespaces.</param>
    /// <param name="configureBreaker">Optional configuration delegate for tool loop circuit breaker options.</param>
    /// <param name="configureToolResult">Optional configuration delegate for deterministic tool result compaction options.</param>
    /// <param name="configureHealing">Optional configuration delegate for tool call healing options.</param>
    /// <returns>The configured builder.</returns>
    public static ChatClientBuilder UseRecommendedSlmPipeline(
        this ChatClientBuilder builder,
        LenientToolDispatcher? dispatcher = null,
        Action<ToolLoopCircuitBreakerOptions>? configureBreaker = null,
        Action<ToolResultCompactionOptions>? configureToolResult = null,
        Action<ToolCallHealingOptions>? configureHealing = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder
            .UseLenientToolDispatch(dispatcher)
            .UseToolLoopCircuitBreaker(dispatcher, options =>
            {
                options.PingPongThreshold = 3;
                options.ResetOnUserPrompt = true;
                options.MaxToolHistory = 30;
                configureBreaker?.Invoke(options);
            })
            .UseToolResultCompaction(options =>
            {
                options.KeepVerbatimCount = 5;
                options.FallbackCharacterLimit = 4500;
                options.FallbackPrefixLength = 2800;
                options.FallbackSuffixLength = 1200;
                configureToolResult?.Invoke(options);
            });

        if (dispatcher != null)
        {
            builder.UseToolCallHealing(dispatcher, options =>
            {
                configureHealing?.Invoke(options);
            });
        }
        else
        {
            builder.UseToolCallHealing(options =>
            {
                configureHealing?.Invoke(options);
            });
        }

        return builder;
    }

    /// <summary>
    /// Configures the recommended pipeline for capable Local LLMs (&gt;= 14B parameters).
    /// Provides full optimizations including lenient dispatch, circuit breaker guardrails,
    /// thinking trace compaction (with optional speculative SLM summarizer), deterministic tool result compaction,
    /// and tool call healing.
    /// </summary>
    /// <param name="builder">The chat client builder.</param>
    /// <param name="dispatcher">Optional lenient tool dispatcher for handling tool/param aliases and namespaces.</param>
    /// <param name="summarizer">Optional background speculative summarizer (e.g. OllamaSpeculativeSummarizer) for thinking traces; when null, uses deterministic truncation.</param>
    /// <param name="configureBreaker">Optional configuration delegate for tool loop circuit breaker options.</param>
    /// <param name="configureThinking">Optional configuration delegate for thinking trace compaction options.</param>
    /// <param name="configureToolResult">Optional configuration delegate for tool result compaction options.</param>
    /// <param name="configureHealing">Optional configuration delegate for tool call healing options.</param>
    /// <returns>The configured builder.</returns>
    public static ChatClientBuilder UseRecommendedLocalLlmPipeline(
        this ChatClientBuilder builder,
        LenientToolDispatcher? dispatcher = null,
        ISpeculativeSummarizer? summarizer = null,
        Action<ToolLoopCircuitBreakerOptions>? configureBreaker = null,
        Action<ThinkingTraceCompactionOptions>? configureThinking = null,
        Action<ToolResultCompactionOptions>? configureToolResult = null,
        Action<ToolCallHealingOptions>? configureHealing = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder
            .UseLenientToolDispatch(dispatcher)
            .UseToolLoopCircuitBreaker(dispatcher, options =>
            {
                options.PingPongThreshold = 3;
                options.ResetOnUserPrompt = true;
                options.MaxToolHistory = 30;
                configureBreaker?.Invoke(options);
            })
            .UseThinkingTraceCompaction(options =>
            {
                options.KeepVerbatimCount = 5;
                options.FallbackCharacterLimit = 1500;
                options.FallbackPrefixLength = 1000;
                options.FallbackSuffixLength = 400;
                options.Summarizer = summarizer;
                configureThinking?.Invoke(options);
            })
            .UseToolResultCompaction(options =>
            {
                options.KeepVerbatimCount = 5;
                options.FallbackCharacterLimit = 4500;
                options.FallbackPrefixLength = 2800;
                options.FallbackSuffixLength = 1200;
                configureToolResult?.Invoke(options);
            });

        if (dispatcher != null)
        {
            builder.UseToolCallHealing(dispatcher, options =>
            {
                configureHealing?.Invoke(options);
            });
        }
        else
        {
            builder.UseToolCallHealing(options =>
            {
                configureHealing?.Invoke(options);
            });
        }

        return builder;
    }
}
