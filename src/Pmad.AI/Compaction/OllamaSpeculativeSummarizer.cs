using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pmad.AI.Compaction;

/// <summary>
/// Configuration options for <see cref="OllamaSpeculativeSummarizer"/>.
/// </summary>
public class OllamaSpeculativeSummarizerOptions
{
    /// <summary>Endpoint URL for the Ollama server. Default: "http://localhost:11434".</summary>
    public Uri Endpoint { get; set; } = new("http://localhost:11434");

    /// <summary>Target model tag for speculative summarization. Default: "gemma4:e2b".</summary>
    public string Model { get; set; } = "gemma4:e2b";

    /// <summary>Context size allocated for the summarizer in tokens. Default: 4096.</summary>
    public int ContextSize { get; set; } = 4096;

    /// <summary>Maximum token budget for generated summaries. Default: 150.</summary>
    public int MaxSummaryTokens { get; set; } = 150;

    /// <summary>Sampling temperature for summary generation. Default: 0.2.</summary>
    public float Temperature { get; set; } = 0.2f;

    /// <summary>Keep-alive duration in Ollama memory (e.g. "15m", "1h"). Default: "15m".</summary>
    public string KeepAlive { get; set; } = "15m";

    /// <summary>Maximum queue capacity for pending summarization tasks. Default: 100.</summary>
    public int QueueCapacity { get; set; } = 100;

    /// <summary>Queue drop behavior when capacity is exceeded. Default: DropWrite.</summary>
    public BoundedChannelFullMode DropPolicy { get; set; } = BoundedChannelFullMode.DropWrite;

    /// <summary>Optional custom system prompt for thinking monologue summarization.</summary>
    public string? ThinkingMonologueSystemPrompt { get; set; }

    /// <summary>Number of GPU layers to offload to Ollama. Default: 0 (CPU-only execution, guaranteeing zero host GPU VRAM contention with primary model).</summary>
    public int NumGpu { get; set; } = 0;

    /// <summary>Number of CPU threads to allocate to Ollama for summarization (null uses Ollama default). Default: null.</summary>
    public int? NumThread { get; set; }

    /// <summary>Timeout duration for individual speculative summarization network requests. Default: 30 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Timeout duration for individual speculative summarization queue jobs. Default: 30 seconds.</summary>
    public TimeSpan JobTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Speculative SLM summarization engine offloading precomputations to a lightweight Ollama HTTP client.
/// Uses background asynchronous requests to precompute concise semantic summaries during inter-turn idle time,
/// providing instantaneous O(1) memory lookup for compaction with zero host GPU VRAM overhead and zero CPU contention.
/// Defaults to "gemma4:e2b".
/// </summary>
public class OllamaSpeculativeSummarizer : ISpeculativeSummarizer
{
    private const string DefaultThinkingMonologuePrompt =
        """
        You are a concise technical summarizer. Compress the following internal reasoning into an ultra-dense summary:
        [THINKING_SUMMARY]
        - Finding: <key technical observation or diagnosis>
        - Decision: <concrete action taken and why>
        - Ruled_Out: <hypotheses or approaches considered but rejected, if any>
        [/THINKING_SUMMARY]
        Output strictly the [THINKING_SUMMARY] block. Limit output to 2-3 dense lines. Do not use code blocks or conversational filler.
        """;

    private readonly struct JobPriority : IComparable<JobPriority>
    {
        public readonly int TextLength;  // Larger length = higher priority
        public readonly long Sequence;   // Smaller sequence = earlier arrival (FIFO tie-breaker)

        public JobPriority(int textLength, long sequence)
        {
            TextLength = textLength;
            Sequence = sequence;
        }

        public int CompareTo(JobPriority other)
        {
            // Larger texts before smaller texts (descending order)
            int lenComp = other.TextLength.CompareTo(TextLength);
            if (lenComp != 0) return lenComp;

            // FIFO tie-breaker for identical length
            return Sequence.CompareTo(other.Sequence);
        }
    }

    private readonly OllamaSpeculativeSummarizerOptions _options;
    private readonly OllamaApiClient _client;
    private readonly bool _ownsClient;
    private readonly ILogger _logger;
    private readonly PriorityQueue<SummarizationJob, JobPriority> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly ConcurrentDictionary<(string SessionId, string ItemKey), string> _cache = new();
    private readonly HashSet<(string SessionId, string ItemKey)> _queuedKeys = [];
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _workerTask;
    private long _sequenceCounter;
    private bool _disposed;

    private sealed record SummarizationJob(
        string SessionId,
        string ItemKey,
        string RawText);

    public OllamaSpeculativeSummarizer(
        OllamaSpeculativeSummarizerOptions? options = null,
        HttpClient? httpClient = null,
        ILogger? logger = null)
    {
        _options = options ?? new OllamaSpeculativeSummarizerOptions();
        _logger = logger ?? NullLogger.Instance;
        _client = new OllamaApiClient(_options.Endpoint, _options.Model, httpClient, _logger);
        _ownsClient = true;
        _workerTask = Task.Run(ProcessQueueAsync);
    }

    public OllamaSpeculativeSummarizer(
        OllamaApiClient client,
        OllamaSpeculativeSummarizerOptions? options = null,
        ILogger? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? new OllamaSpeculativeSummarizerOptions();
        _logger = logger ?? NullLogger.Instance;
        _ownsClient = false;
        _workerTask = Task.Run(ProcessQueueAsync);
    }

    public OllamaSpeculativeSummarizer(
        HttpClient httpClient,
        OllamaSpeculativeSummarizerOptions? options = null,
        ILogger? logger = null)
        : this(options, httpClient, logger)
    {
    }

    public OllamaSpeculativeSummarizer(
        string endpoint,
        string model = "gemma4:e2b",
        ILogger? logger = null)
        : this(new OllamaSpeculativeSummarizerOptions
        {
            Endpoint = new Uri(endpoint),
            Model = model
        }, httpClient: null, logger: logger)
    {
    }

    public OllamaSpeculativeSummarizer(
        Uri endpoint,
        string model = "gemma4:e2b",
        ILogger? logger = null)
        : this(new OllamaSpeculativeSummarizerOptions
        {
            Endpoint = endpoint,
            Model = model
        }, httpClient: null, logger: logger)
    {
    }

    /// <inheritdoc/>
    public void EnqueueSummarization(string sessionId, string itemKey, string rawText)
    {
        if (_disposed || string.IsNullOrWhiteSpace(rawText)) return;

        var key = (sessionId, itemKey);
        lock (_lock)
        {
            if (_cache.ContainsKey(key) || _queuedKeys.Contains(key))
            {
                return;
            }

            if (_queue.Count >= _options.QueueCapacity)
            {
                _logger.LogDebug("Speculative summarization queue full; dropped item '{ItemKey}' in session '{SessionId}'", itemKey, sessionId);
                return;
            }

            _queuedKeys.Add(key);

            var job = new SummarizationJob(sessionId, itemKey, rawText);
            var priority = new JobPriority(rawText.Length, ++_sequenceCounter);
            _queue.Enqueue(job, priority);
            _signal.Release();
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
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            SummarizationJob? job = null;
            lock (_lock)
            {
                while (_queue.TryDequeue(out var candidate, out _))
                {
                    var k = (candidate.SessionId, candidate.ItemKey);
                    if (_queuedKeys.Contains(k))
                    {
                        job = candidate;
                        break;
                    }
                }
            }

            if (job == null) continue;

            try
            {
                using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                jobCts.CancelAfter(_options.JobTimeout);
                var summary = await GenerateSummaryAsync(job, jobCts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    _cache[(job.SessionId, job.ItemKey)] = summary;
                    _logger.LogDebug("Precomputed speculative summary for '{ItemKey}' ({Length} chars)", job.ItemKey, summary.Length);
                }
            }
            catch (OperationCanceledException) when (!_cts.Token.IsCancellationRequested)
            {
                _logger.LogWarning("Speculative summarization timed out for item '{ItemKey}' after {Timeout}s", job.ItemKey, _options.JobTimeout.TotalSeconds);
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
        string systemInstruction = _options.ThinkingMonologueSystemPrompt ?? DefaultThinkingMonologuePrompt;

        var promptText = job.RawText;
        var maxChars = _options.ContextSize * 4;
        if (promptText.Length > maxChars)
        {
            int half = maxChars / 2;
            promptText = string.Concat(promptText.AsSpan(0, half), "\n... [truncated for summarization] ...\n", promptText.AsSpan(promptText.Length - half));
        }

        var rawSummary = await _client.ChatAsync(
            prompt: promptText,
            systemInstruction: systemInstruction,
            model: _options.Model,
            maxTokens: _options.MaxSummaryTokens,
            temperature: _options.Temperature,
            contextSize: _options.ContextSize,
            keepAlive: _options.KeepAlive,
            numGpu: _options.NumGpu,
            numThread: _options.NumThread,
            timeout: _options.RequestTimeout,
            cancellationToken: ct).ConfigureAwait(false);

        return rawSummary != null ? CleanSummary(rawSummary) : null;
    }

    private static string CleanSummary(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                trimmed = trimmed.Substring(firstNewline + 1);
            }
            if (trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 3).Trim();
            }
        }
        return trimmed;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _signal.Release();

        try
        {
            await _workerTask.ConfigureAwait(false);
        }
        catch
        {
            // Suppress cancellation exceptions
        }

        _cts.Dispose();
        _signal.Dispose();

        if (_ownsClient)
        {
            _client.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Remote Ollama speculative summarizer offloading SLM background summarization to an external Ollama endpoint or OllamaRouter.
/// Subclass of <see cref="OllamaSpeculativeSummarizer"/>.
/// </summary>
public class RemoteOllamaSpeculativeSummarizer : OllamaSpeculativeSummarizer
{
    public RemoteOllamaSpeculativeSummarizer(
        OllamaSpeculativeSummarizerOptions? options = null,
        HttpClient? httpClient = null,
        ILogger? logger = null)
        : base(options, httpClient, logger)
    {
    }

    public RemoteOllamaSpeculativeSummarizer(
        OllamaApiClient client,
        OllamaSpeculativeSummarizerOptions? options = null,
        ILogger? logger = null)
        : base(client, options, logger)
    {
    }

    public RemoteOllamaSpeculativeSummarizer(
        string endpoint = "http://localhost:11434",
        string model = "gemma4:e2b",
        ILogger? logger = null)
        : base(endpoint, model, logger)
    {
    }

    public RemoteOllamaSpeculativeSummarizer(
        Uri endpoint,
        string model = "gemma4:e2b",
        ILogger? logger = null)
        : base(endpoint, model, logger)
    {
    }
}
