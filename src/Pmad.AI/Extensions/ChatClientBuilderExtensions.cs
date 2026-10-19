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
    /// Empirically validated on benchmark suites to maximize accuracy, token efficiency, and execution speed.
    /// Omits natural language speculative summarization to preserve KV-cache stability and deterministic code/tool reasoning,
    /// while providing lenient tool dispatch, circuit breaker guardrails, deterministic thinking trace compaction,
    /// deterministic tool result compaction, and tool call healing.
    /// </summary>
    /// <param name="builder">The chat client builder.</param>
    /// <param name="dispatcher">Optional lenient tool dispatcher for handling tool/param aliases and namespaces.</param>
    /// <param name="configureBreaker">Optional configuration delegate for tool loop circuit breaker options.</param>
    /// <param name="configureThinking">Optional configuration delegate for thinking trace compaction options.</param>
    /// <param name="configureToolResult">Optional configuration delegate for deterministic tool result compaction options.</param>
    /// <param name="configureHealing">Optional configuration delegate for tool call healing options.</param>
    /// <returns>The configured builder.</returns>
    public static ChatClientBuilder UseRecommendedSlmPipeline(
        this ChatClientBuilder builder,
        LenientToolDispatcher? dispatcher = null,
        Action<ToolCallHealingOptions>? configureHealing = null,
        Action<ToolLoopCircuitBreakerOptions>? configureBreaker = null,
        Action<ThinkingTraceCompactionOptions>? configureThinking = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // In Microsoft.Extensions.AI ChatClientBuilder, middleware is evaluated in reverse order during Build():
        // The first registered middleware is the outer-most client (receives requests first).
        // The last registered middleware is the inner-most client (wraps raw IChatClient directly).
        //
        // Target Hierarchy:
        // Request -> ThinkingCompaction -> ToolLoopCircuitBreaker -> LenientToolDispatch -> ToolCallHealing -> Raw IChatClient
        // Response <- ThinkingCompaction <- ToolLoopCircuitBreaker <- LenientToolDispatch <- ToolCallHealing <- Raw IChatClient

        // 4. Thinking Trace Compaction (outer-most): trims reasoning traces outside active turn window.
        builder.UseThinkingTraceCompaction(options =>
        {
            options.KeepVerbatimCount = 5;
            options.FallbackCharacterLimit = 2000;
            options.FallbackPrefixLength = 1400;
            options.FallbackSuffixLength = 600;
            options.Summarizer = null;
            configureThinking?.Invoke(options);
        });

        // 3. Tool Loop Circuit Breaker: inspects canonical, normalized tool calls to trip repetitive loops.
        builder.UseToolLoopCircuitBreaker(dispatcher, options =>
        {
            options.PingPongThreshold = 3;
            options.ResetOnUserPrompt = true;
            options.MaxToolHistory = 30;
            configureBreaker?.Invoke(options);
        });

        // 2. Lenient Tool Dispatcher: normalizes aliases, strips namespaces, coerces argument types, and maps value synonyms.
        builder.UseLenientToolDispatch(dispatcher);

        // 1. Tool Call Healing (inner-most, wraps raw client directly):
        // Intercepts raw pseudo-tags (<tool_call>), fences, and unescaped JSON strings.
        if (dispatcher != null)
        {
            builder.UseToolCallHealing(dispatcher, options =>
            {
                options.EnforceAvailableToolsGuardrail = true;
                configureHealing?.Invoke(options);
            });
        }
        else
        {
            builder.UseToolCallHealing(options =>
            {
                options.EnforceAvailableToolsGuardrail = true;
                configureHealing?.Invoke(options);
            });
        }

        return builder;
    }

    /// <summary>
    /// Configures the recommended pipeline for capable Local LLMs (&gt;= 14B parameters).
    /// Provides full optimizations including lenient dispatch, circuit breaker guardrails,
    /// thinking trace compaction (with optional speculative SLM summarizer), deterministic tool result compaction,
    /// and tool call healing with correct inner-to-outer layering.
    /// </summary>
    /// <param name="builder">The chat client builder.</param>
    /// <param name="dispatcher">Optional lenient tool dispatcher for handling tool/param aliases and namespaces.</param>
    /// <param name="summarizer">Optional background speculative summarizer (e.g. OllamaSpeculativeSummarizer) for thinking traces; when null, uses deterministic truncation.</param>
    /// <param name="configureHealing">Optional configuration delegate for tool call healing options.</param>
    /// <param name="configureBreaker">Optional configuration delegate for tool loop circuit breaker options.</param>
    /// <param name="configureToolResult">Optional configuration delegate for tool result compaction options.</param>
    /// <param name="configureThinking">Optional configuration delegate for thinking trace compaction options.</param>
    /// <returns>The configured builder.</returns>
    public static ChatClientBuilder UseRecommendedLocalLlmPipeline(
        this ChatClientBuilder builder,
        LenientToolDispatcher? dispatcher = null,
        ISpeculativeSummarizer? summarizer = null,
        Action<ToolCallHealingOptions>? configureHealing = null,
        Action<ToolLoopCircuitBreakerOptions>? configureBreaker = null,
        Action<ToolResultCompactionOptions>? configureToolResult = null,
        Action<ThinkingTraceCompactionOptions>? configureThinking = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // 5. Thinking Trace Compaction (outer-most)
        builder.UseThinkingTraceCompaction(options =>
        {
            options.KeepVerbatimCount = 5;
            options.FallbackCharacterLimit = 1500;
            options.FallbackPrefixLength = 1000;
            options.FallbackSuffixLength = 400;
            options.Summarizer = summarizer;
            configureThinking?.Invoke(options);
        });

        // 4. Tool Result Compaction
        builder.UseToolResultCompaction(options =>
        {
            options.KeepVerbatimCount = 5;
            options.FallbackCharacterLimit = 4500;
            options.FallbackPrefixLength = 2800;
            options.FallbackSuffixLength = 1200;
            configureToolResult?.Invoke(options);
        });

        // 3. Tool Loop Circuit Breaker
        builder.UseToolLoopCircuitBreaker(dispatcher, options =>
        {
            options.PingPongThreshold = 3;
            options.ResetOnUserPrompt = true;
            options.MaxToolHistory = 30;
            configureBreaker?.Invoke(options);
        });

        // 2. Lenient Tool Dispatcher
        builder.UseLenientToolDispatch(dispatcher);

        // 1. Tool Call Healing (inner-most, wraps raw client directly)
        if (dispatcher != null)
        {
            builder.UseToolCallHealing(dispatcher, options =>
            {
                options.EnforceAvailableToolsGuardrail = true;
                configureHealing?.Invoke(options);
            });
        }
        else
        {
            builder.UseToolCallHealing(options =>
            {
                options.EnforceAvailableToolsGuardrail = true;
                configureHealing?.Invoke(options);
            });
        }

        return builder;
    }
}
