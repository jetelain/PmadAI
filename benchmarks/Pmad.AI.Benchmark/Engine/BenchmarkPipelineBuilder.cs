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
        string? transcriptDir = null)
    {
        ArgumentNullException.ThrowIfNull(innerClient);
        ArgumentNullException.ThrowIfNull(config);

        return config.Id switch
        {
            "baseline" => innerClient,

            "opt-healing" => new ToolCallHealingChatClient(innerClient),

            "opt-thinking-compaction" => new ThinkingTraceCompactionChatClient(
                innerClient,
                new ThinkingTraceCompactionOptions
                {
                    KeepVerbatimCount = 2
                }),

            "opt-tool-compaction" => new ToolResultCompactionChatClient(
                innerClient,
                new ToolResultCompactionOptions
                {
                    KeepVerbatimCount = 2,
                    FallbackCharacterLimit = 400,
                    FallbackPrefixLength = 300,
                    FallbackSuffixLength = 100
                }),

            "opt-speculative-slm" => BuildSpeculativeCompactionPipeline(innerClient, speculativeSummarizer),

            "opt-lenient-dispatch" => BuildLenientDispatchPipeline(innerClient),

            "all-optimizations" => BuildAllOptimizationsPipeline(innerClient, speculativeSummarizer, transcriptDir),

            _ => innerClient
        };
    }

    private static IChatClient BuildSpeculativeCompactionPipeline(
        IChatClient innerClient,
        ISpeculativeSummarizer? speculativeSummarizer)
    {
        var toolOptions = new ToolResultCompactionOptions
        {
            KeepVerbatimCount = 2,
            FallbackCharacterLimit = 400,
            FallbackPrefixLength = 300,
            FallbackSuffixLength = 100,
            Summarizer = speculativeSummarizer
        };
        var withToolCompaction = new ToolResultCompactionChatClient(innerClient, toolOptions);

        var thinkOptions = new ThinkingTraceCompactionOptions
        {
            KeepVerbatimCount = 2,
            Summarizer = speculativeSummarizer
        };
        return new ThinkingTraceCompactionChatClient(withToolCompaction, thinkOptions);
    }

    private static IChatClient BuildLenientDispatchPipeline(IChatClient innerClient)
    {
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            PingPongThreshold = 2,
            NgramMinLength = 4,
            NgramMinOccurrences = 3
        });
        var withBreaker = new ToolLoopCircuitBreakerChatClient(innerClient, breaker);

        var dispatcher = CreateConfiguredDispatcher();
        return new LenientToolDispatchChatClient(withBreaker, dispatcher);
    }

    private static IChatClient BuildAllOptimizationsPipeline(
        IChatClient innerClient,
        ISpeculativeSummarizer? speculativeSummarizer,
        string? transcriptDir)
    {
        // 1. Tool call healing wraps raw client
        IChatClient current = new ToolCallHealingChatClient(innerClient);

        // 2. Tool result compaction
        current = new ToolResultCompactionChatClient(current, new ToolResultCompactionOptions
        {
            KeepVerbatimCount = 2,
            FallbackCharacterLimit = 400,
            FallbackPrefixLength = 300,
            FallbackSuffixLength = 100,
            Summarizer = speculativeSummarizer
        });

        // 3. Thinking trace compaction
        current = new ThinkingTraceCompactionChatClient(current, new ThinkingTraceCompactionOptions
        {
            KeepVerbatimCount = 2,
            Summarizer = speculativeSummarizer
        });

        // 4. Circuit breaker & lenient dispatch
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            PingPongThreshold = 2,
            NgramMinLength = 4,
            NgramMinOccurrences = 3
        });
        current = new ToolLoopCircuitBreakerChatClient(current, breaker);

        var dispatcher = CreateConfiguredDispatcher();
        current = new LenientToolDispatchChatClient(current, dispatcher);

        // 5. Verbatim transcript (optional)
        if (!string.IsNullOrWhiteSpace(transcriptDir))
        {
            current = new VerbatimTranscriptChatClient(current, new VerbatimTranscriptOptions
            {
                OutputDirectory = transcriptDir
            });
        }

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
                ["lookup_order"] = new(aliases: ["lookup_customer", "get_order", "order_lookup", "getOrder"]),
                ["track_package"] = new(aliases: ["trackPackage", "track", "check_shipping", "get_tracking"]),
                ["read_file"] = new(aliases: ["readFile", "view_file"]),
                ["read"] = new(aliases: ["cat"]),
                ["execute_tests"] = new(aliases: ["run_tests", "runTests", "executeTests", "test"]),
                ["search_code"] = new(aliases: ["grep", "searchCode", "find_in_files"]),
                ["search"] = new(aliases: ["find", "query"]),
                ["apply_patch"] = new(aliases: ["patch", "applyPatch", "git_apply"]),
                ["query_database"] = new(aliases: ["queryDb", "sql_query", "database_query"]),
                ["check_status"] = new(aliases: ["status", "checkStatus", "poll_status"]),

                // Canonical tools of the long-horizon scenarios (exact names take precedence over aliases above)
                ["run_tests"] = new(aliases: []),
                ["write_file"] = new(aliases: []),
                ["compile_project"] = new(aliases: []),
                ["check_git_diff"] = new(aliases: []),
                ["execute_sast_scan"] = new(aliases: []),
                ["build_solution"] = new(aliases: []),
                ["run_unit_tests"] = new(aliases: []),
                ["run_benchmarks"] = new(aliases: []),
                ["git_command"] = new(aliases: [])
            }
        });
        return dispatcher;
    }
}
