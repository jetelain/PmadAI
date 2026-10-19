using Microsoft.Extensions.AI;
using Pmad.AI.Benchmark.Configuration;
using Pmad.AI.Compaction;
using Pmad.AI.Guardrails;
using Pmad.AI.Healing;
using Pmad.AI.Tools;
using Pmad.AI.Transcripts;

namespace Pmad.AI.Benchmark.Engine;

/// <summary>
/// Assembles <see cref="IChatClient"/> middleware pipelines according to the specified <see cref="OptimizationConfig"/>.
/// Allows testing isolated gains of individual utilities or total cumulative synergy.
/// </summary>
public static class BenchmarkPipelineBuilder
{
    public static IChatClient BuildPipeline(
        IChatClient innerClient,
        OptimizationConfig config,
        ISpeculativeSummarizer? speculativeSummarizer = null,
        string? transcriptDir = null,
        string? transcriptFilePath = null,
        VerbatimTranscriptOptions? transcriptOptions = null,
        CompactionTelemetry? compactionTelemetry = null)
    {
        ArgumentNullException.ThrowIfNull(innerClient);
        ArgumentNullException.ThrowIfNull(config);

        var telemetry = compactionTelemetry ?? new CompactionTelemetry();

        IChatClient client = config.Id switch
        {
            "baseline" => innerClient,

            "opt-healing" => new ToolCallHealingChatClient(innerClient, new ToolCallHealingOptions
            {
                Telemetry = new ToolCallHealingTelemetry()
            }),

            "opt-thinking-compaction" => new ThinkingTraceCompactionChatClient(
                innerClient,
                new ThinkingTraceCompactionOptions
                {
                    Telemetry = telemetry
                }),

            "opt-tool-compaction" => new ToolResultCompactionChatClient(
                innerClient,
                new ToolResultCompactionOptions
                {
                    Telemetry = telemetry
                }),

            "opt-speculative-slm" => BuildSpeculativeCompactionPipeline(innerClient, speculativeSummarizer, telemetry),

            "opt-lenient-dispatch" => BuildLenientDispatchPipeline(innerClient),

            "opt-healing-lenient" => BuildHealingLenientPipeline(innerClient),

            "opt-healing-tool-compaction" => BuildHealingToolCompactionPipeline(innerClient, telemetry),

            "opt-recommended-slm" => BuildRecommendedSlmPipeline(innerClient, telemetry),

            "opt-all-local-llm-without-slm" => BuildAllLocalLlmWithoutSlmPipeline(innerClient, telemetry),

            "opt-all-local-llm-with-slm" => BuildAllLocalLlmWithSlmPipeline(innerClient, speculativeSummarizer, telemetry),

            "all-optimizations" => BuildAllOptimizationsPipeline(innerClient, speculativeSummarizer, telemetry),

            _ => innerClient
        };

        if (transcriptOptions != null)
        {
            transcriptOptions.CompactionTelemetry ??= telemetry;
            client = new VerbatimTranscriptChatClient(client, transcriptOptions);
        }
        else if (!string.IsNullOrWhiteSpace(transcriptFilePath))
        {
            client = new VerbatimTranscriptChatClient(client, new VerbatimTranscriptOptions
            {
                FilePath = transcriptFilePath,
                AutoSaveOnDispose = true,
                FlushAfterEachTurn = true,
                CompactionTelemetry = telemetry
            });
        }
        else if (!string.IsNullOrWhiteSpace(transcriptDir))
        {
            client = new VerbatimTranscriptChatClient(client, new VerbatimTranscriptOptions
            {
                OutputDirectory = transcriptDir,
                AutoSaveOnDispose = true,
                FlushAfterEachTurn = true,
                CompactionTelemetry = telemetry
            });
        }

        return client;
    }

    private static IChatClient BuildSpeculativeCompactionPipeline(
        IChatClient innerClient,
        ISpeculativeSummarizer? speculativeSummarizer,
        CompactionTelemetry telemetry)
    {
        var toolOptions = new ToolResultCompactionOptions
        {
            Telemetry = telemetry
        };
        var withToolCompaction = new ToolResultCompactionChatClient(innerClient, toolOptions);

        var thinkOptions = new ThinkingTraceCompactionOptions
        {
            Summarizer = speculativeSummarizer,
            Telemetry = telemetry
        };
        return new ThinkingTraceCompactionChatClient(withToolCompaction, thinkOptions);
    }

    private static IChatClient BuildLenientDispatchPipeline(IChatClient innerClient)
    {
        var breakerTelemetry = new CircuitBreakerTelemetry();
        var dispatchTelemetry = new LenientDispatchTelemetry();

        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            Telemetry = breakerTelemetry
        });
        var dispatcher = CreateConfiguredDispatcher();
        dispatcher.Telemetry = dispatchTelemetry;

        var withBreaker = new ToolLoopCircuitBreakerChatClient(innerClient, breaker, dispatcher);
        return new LenientToolDispatchChatClient(withBreaker, dispatcher);
    }

    private static IChatClient BuildRecommendedSlmPipeline(
        IChatClient innerClient,
        CompactionTelemetry telemetry)
    {
        var dispatcher = CreateConfiguredDispatcher();
        var dispatchTelemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = dispatchTelemetry;

        var healingTelemetry = new ToolCallHealingTelemetry();
        var breakerTelemetry = new CircuitBreakerTelemetry();

        var builder = new ChatClientBuilder(innerClient);
        builder.UseRecommendedSlmPipeline(
            dispatcher: dispatcher,
            configureBreaker: opts =>
            {
                opts.Telemetry = breakerTelemetry;
            },
            configureToolResult: opts =>
            {
                opts.Telemetry = telemetry;
            },
            configureHealing: opts =>
            {
                opts.Telemetry = healingTelemetry;
            });

        return builder.Build();
    }

    private static IChatClient BuildAllLocalLlmWithoutSlmPipeline(
        IChatClient innerClient,
        CompactionTelemetry telemetry)
    {
        var dispatcher = CreateConfiguredDispatcher();
        var dispatchTelemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = dispatchTelemetry;

        var healingTelemetry = new ToolCallHealingTelemetry();
        var breakerTelemetry = new CircuitBreakerTelemetry();

        var builder = new ChatClientBuilder(innerClient);
        builder.UseRecommendedLocalLlmPipeline(
            dispatcher: dispatcher,
            summarizer: null,
            configureBreaker: opts =>
            {
                opts.Telemetry = breakerTelemetry;
            },
            configureThinking: opts =>
            {
                opts.Telemetry = telemetry;
            },
            configureToolResult: opts =>
            {
                opts.Telemetry = telemetry;
            },
            configureHealing: opts =>
            {
                opts.Telemetry = healingTelemetry;
            });

        return builder.Build();
    }

    private static IChatClient BuildAllLocalLlmWithSlmPipeline(
        IChatClient innerClient,
        ISpeculativeSummarizer? speculativeSummarizer,
        CompactionTelemetry telemetry)
    {
        var dispatcher = CreateConfiguredDispatcher();
        var dispatchTelemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = dispatchTelemetry;

        var healingTelemetry = new ToolCallHealingTelemetry();
        var breakerTelemetry = new CircuitBreakerTelemetry();

        var builder = new ChatClientBuilder(innerClient);
        builder.UseRecommendedLocalLlmPipeline(
            dispatcher: dispatcher,
            summarizer: speculativeSummarizer,
            configureBreaker: opts =>
            {
                opts.Telemetry = breakerTelemetry;
            },
            configureThinking: opts =>
            {
                opts.Telemetry = telemetry;
            },
            configureToolResult: opts =>
            {
                opts.Telemetry = telemetry;
            },
            configureHealing: opts =>
            {
                opts.Telemetry = healingTelemetry;
            });

        return builder.Build();
    }

    private static IChatClient BuildHealingLenientPipeline(IChatClient innerClient)
    {
        var dispatcher = CreateConfiguredDispatcher();
        var dispatchTelemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = dispatchTelemetry;

        var healingTelemetry = new ToolCallHealingTelemetry();
        var breakerTelemetry = new CircuitBreakerTelemetry();

        var builder = new ChatClientBuilder(innerClient);
        builder
            .UseLenientToolDispatch(dispatcher)
            .UseToolLoopCircuitBreaker(dispatcher, opts =>
            {
                opts.Telemetry = breakerTelemetry;
            })
            .UseToolCallHealing(dispatcher, opts =>
            {
                opts.Telemetry = healingTelemetry;
            });

        return builder.Build();
    }

    private static IChatClient BuildHealingToolCompactionPipeline(
        IChatClient innerClient,
        CompactionTelemetry telemetry)
    {
        var healingTelemetry = new ToolCallHealingTelemetry();

        var builder = new ChatClientBuilder(innerClient);
        builder
            .UseToolResultCompaction(opts =>
            {
                opts.Telemetry = telemetry;
            })
            .UseToolCallHealing(opts =>
            {
                opts.Telemetry = healingTelemetry;
            });

        return builder.Build();
    }

    private static IChatClient BuildAllOptimizationsPipeline(
        IChatClient innerClient,
        ISpeculativeSummarizer? speculativeSummarizer,
        CompactionTelemetry telemetry)
    {
        var dispatcher = CreateConfiguredDispatcher();
        var dispatchTelemetry = new LenientDispatchTelemetry();
        dispatcher.Telemetry = dispatchTelemetry;

        var healingTelemetry = new ToolCallHealingTelemetry();
        var breakerTelemetry = new CircuitBreakerTelemetry();

        // 1. Tool call healing wraps raw client with dispatcher awareness
        IChatClient current = new ToolCallHealingChatClient(innerClient, new ToolCallHealingOptions
        {
            Dispatcher = dispatcher,
            Telemetry = healingTelemetry
        });

        // 2. Tool result compaction (deterministic head/tail truncation)
        current = new ToolResultCompactionChatClient(current, new ToolResultCompactionOptions
        {
            Telemetry = telemetry
        });

        // 3. Thinking trace compaction (speculative SLM background summarizer)
        current = new ThinkingTraceCompactionChatClient(current, new ThinkingTraceCompactionOptions
        {
            Summarizer = speculativeSummarizer,
            Telemetry = telemetry
        });

        // 4. Circuit breaker & lenient dispatch
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            Telemetry = breakerTelemetry
        });
        current = new ToolLoopCircuitBreakerChatClient(current, breaker, dispatcher);
        current = new LenientToolDispatchChatClient(current, dispatcher);

        return current;
    }

    private static LenientToolDispatcher CreateConfiguredDispatcher()
    {
        var dispatcher = new LenientToolDispatcher();
        dispatcher.Configure(new ToolDispatcherConfiguration
        {
            Tools = new Dictionary<string, ToolMapping>(StringComparer.OrdinalIgnoreCase)
            {
                ["get_weather"] = new(aliases: ["get_current_weather", "weather", "getCurrentWeather", "fetch_weather"]),
                ["calculate"] = new(aliases: ["calc", "compute", "calculator", "eval"]),
                ["read_file"] = new(aliases: ["readFile", "view_file"]),
                ["read"] = new(aliases: ["cat"]),
                ["run_tests"] = new(aliases: ["execute_tests", "runTests", "executeTests", "test"]),
                ["search_code"] = new(aliases: ["grep", "searchCode", "find_in_files"]),
                ["search"] = new(aliases: ["find", "query"]),
                ["apply_patch"] = new(aliases: ["patch", "applyPatch", "git_apply"]),
                ["query_database"] = new(aliases: ["queryDb", "sql_query", "database_query"])
            }
        });
        return dispatcher;
    }
}
