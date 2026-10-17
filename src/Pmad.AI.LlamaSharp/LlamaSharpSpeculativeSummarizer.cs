using System.Collections.Concurrent;
using System.Threading.Channels;
using LLama.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pmad.AI.Compaction;

namespace Pmad.AI.LlamaSharp;

/// <summary>
/// Configuration options for <see cref="LlamaSharpSpeculativeSummarizer"/>.
/// </summary>
public class LlamaSharpSpeculativeSummarizerOptions
{
    /// <summary>Explicit absolute path to a local .gguf model file for CPU summarization.</summary>
    public string? ModelPath { get; set; }

    /// <summary>Curated preset ID (e.g. "qwen3.5-2b" or "gemma-4-e2b"). Default: "qwen3.5-2b".</summary>
    public string Preset { get; set; } = "qwen3.5-2b";

    /// <summary>Number of CPU threads to allocate for summarization inference. Default: 4.</summary>
    public int CpuThreads { get; set; } = 4;

    /// <summary>Context window allocated for the summarizer. Default: 4096 tokens.</summary>
    public int ContextSize { get; set; } = 4096;

    /// <summary>Maximum token budget for generated summaries. Default: 150.</summary>
    public int MaxSummaryTokens { get; set; } = 150;

    /// <summary>Maximum queue capacity for pending summarization tasks. Default: 100.</summary>
    public int QueueCapacity { get; set; } = 100;

    /// <summary>Queue drop behavior when capacity is exceeded. Default: DropWrite (newest items dropped).</summary>
    public BoundedChannelFullMode DropPolicy { get; set; } = BoundedChannelFullMode.DropWrite;
}

/// <summary>
/// Speculative CPU SLM summarization engine executing on dedicated CPU threads (GpuLayerCount = 0).
/// Precomputes rich semantic summaries of older conversation history during inter-turn idle time,
/// providing instantaneous O(1) memory lookup for compaction with zero latency overhead.
/// </summary>
public class LlamaSharpSpeculativeSummarizer : ISpeculativeSummarizer
{
    private readonly LlamaSharpSpeculativeSummarizerOptions _options;
    private readonly IChatClient _chatClient;
    private readonly bool _ownsClient;
    private readonly ILogger _logger;
    private readonly Channel<SummarizationJob> _channel;
    private readonly ConcurrentDictionary<(string SessionId, string ItemKey), string> _cache = new();
    private readonly HashSet<(string SessionId, string ItemKey)> _queuedKeys = [];
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _workerTask;
    private bool _disposed;

    private sealed record SummarizationJob(
        string SessionId,
        string ItemKey,
        SummarizationTarget Target,
        string RawText);

    public LlamaSharpSpeculativeSummarizer(
        LlamaSharpSpeculativeSummarizerOptions options,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _logger = logger ?? NullLogger.Instance;

        var modelPath = ResolveModelPath(options);
        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
        {
            throw new FileNotFoundException($"Model file for speculative summarization not found: '{modelPath}' (preset: '{options.Preset}').");
        }

        var clientOptions = new LlamaSharpOptions
        {
            ModelPath = modelPath,
            GpuLayerCount = 0, // Enforce CPU execution
            ContextSize = options.ContextSize,
            EnableReasoning = false,
            Logger = _logger,
            VerboseLogging = false
        };

        _chatClient = LlamaSharpChatClient.CreateFromFile(modelPath, clientOptions, logger: _logger);
        _ownsClient = true;

        _channel = Channel.CreateBounded<SummarizationJob>(new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = options.DropPolicy,
            SingleReader = true,
            SingleWriter = false
        });

        _workerTask = Task.Run(ProcessQueueAsync);
    }

    /// <summary>
    /// Test constructor accepting an explicit <see cref="IChatClient"/> instance.
    /// </summary>
    public LlamaSharpSpeculativeSummarizer(
        IChatClient chatClient,
        LlamaSharpSpeculativeSummarizerOptions? options = null,
        ILogger? logger = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _options = options ?? new LlamaSharpSpeculativeSummarizerOptions();
        _logger = logger ?? NullLogger.Instance;
        _ownsClient = false;

        _channel = Channel.CreateBounded<SummarizationJob>(new BoundedChannelOptions(_options.QueueCapacity)
        {
            FullMode = _options.DropPolicy,
            SingleReader = true,
            SingleWriter = false
        });

        _workerTask = Task.Run(ProcessQueueAsync);
    }

    /// <inheritdoc/>
    public void EnqueueSummarization(string sessionId, string itemKey, SummarizationTarget target, string rawText)
    {
        if (_disposed || string.IsNullOrWhiteSpace(rawText)) return;

        var key = (sessionId, itemKey);
        lock (_lock)
        {
            if (_cache.ContainsKey(key) || _queuedKeys.Contains(key))
            {
                return;
            }
            _queuedKeys.Add(key);
        }

        var job = new SummarizationJob(sessionId, itemKey, target, rawText);
        if (!_channel.Writer.TryWrite(job))
        {
            _logger.LogDebug("Speculative summarization queue full; dropped item '{ItemKey}' in session '{SessionId}'", itemKey, sessionId);
            lock (_lock)
            {
                _queuedKeys.Remove(key);
            }
        }
    }

    /// <inheritdoc/>
    public bool TryGetSummary(string sessionId, string itemKey, out string? summary)
    {
        return _cache.TryGetValue((sessionId, itemKey), out summary);
    }

    /// <inheritdoc/>
    public void EvictSession(string sessionId)
    {
        lock (_lock)
        {
            var keysToRemove = _cache.Keys.Where(k => k.SessionId == sessionId).ToList();
            foreach (var k in keysToRemove)
            {
                _cache.TryRemove(k, out _);
            }

            _queuedKeys.RemoveWhere(k => k.SessionId == sessionId);
        }
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
        }
        catch
        {
            // Thread priority adjustment may be restricted in some containerized environments
        }

        var reader = _channel.Reader;
        while (!_cts.Token.IsCancellationRequested)
        {
            SummarizationJob job;
            try
            {
                job = await reader.ReadAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ChannelClosedException)
            {
                break;
            }

            try
            {
                var summary = await GenerateSummaryAsync(job, _cts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    _cache[(job.SessionId, job.ItemKey)] = summary;
                    _logger.LogDebug("Precomputed speculative summary for '{ItemKey}' ({Length} chars)", job.ItemKey, summary.Length);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to generate speculative summary for item '{ItemKey}'", job.ItemKey);
            }
            finally
            {
                lock (_lock)
                {
                    _queuedKeys.Remove((job.SessionId, job.ItemKey));
                }
            }
        }
    }

    private async Task<string?> GenerateSummaryAsync(SummarizationJob job, CancellationToken ct)
    {
        string systemInstruction = job.Target switch
        {
            SummarizationTarget.ToolResult =>
                "You are a concise technical summarizer. Summarize the following tool execution output in 2-4 bullet points or 1-2 concise sentences focusing only on key facts, identifiers, error messages, and output data. Be extremely brief (under 100 tokens). Output only the summary without conversational filler.",

            SummarizationTarget.ThinkingMonologue =>
                "You are a concise technical summarizer. Summarize the following reasoning monologue in 2-4 bullet points capturing key architectural decisions, hypotheses, constraints, and conclusions reached. Be extremely brief (under 100 tokens). Output only the summary without conversational filler.",

            _ => "Summarize the text concisely."
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemInstruction),
            new(ChatRole.User, job.RawText)
        };

        var options = new ChatOptions
        {
            MaxOutputTokens = _options.MaxSummaryTokens,
            Temperature = 0.2f
        };

        var response = await _chatClient.GetResponseAsync(messages, options, ct).ConfigureAwait(false);
        return response?.Text?.Trim();
    }

    private static string? ResolveModelPath(LlamaSharpSpeculativeSummarizerOptions options)
    {
        if (!string.IsNullOrEmpty(options.ModelPath))
        {
            return options.ModelPath;
        }

        var preset = LlamaModelPreset.Find(options.Preset);
        if (preset != null)
        {
            var defaultDir = LocalModelPathResolver.GetDefaultModelsDirectory();
            var candidate = Path.Combine(defaultDir, preset.FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _channel.Writer.TryComplete();
        _cts.Cancel();

        try
        {
            await _workerTask.ConfigureAwait(false);
        }
        catch
        {
            // Suppress cancellation exceptions
        }

        _cts.Dispose();

        if (_ownsClient && _chatClient != null)
        {
            _chatClient.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
