using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OllamaSharp;

namespace Pmad.AI.Benchmark.Engine;

/// <summary>
/// Target routing preference for Ollama router proxies.
/// </summary>
public enum OllamaRoutingTarget
{
    Auto,
    Local,
    Remote,
    Cloud
}

/// <summary>
/// Configuration options for Ollama client instantiation.
/// </summary>
public sealed class LlmProviderOptions
{
    /// <summary>
    /// Ollama endpoint base address (e.g. "http://localhost:11434").
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Model identifier / tag.
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// HTTP request timeout in minutes (default: 120 minutes / 2 hours).
    /// </summary>
    public int RequestTimeoutMinutes { get; set; } = 120;

    /// <summary>
    /// Model context window size in tokens (num_ctx).
    /// </summary>
    public int? ContextSize { get; set; }

    /// <summary>
    /// Whether thinking / reasoning is enabled for models that support it.
    /// </summary>
    public bool? EnableReasoning { get; set; }
}

/// <summary>
/// Factory creating and configuring Ollama <see cref="IChatClient"/> instances using OllamaSharp.
/// </summary>
public static class OllamaChatClientFactory
{
    /// <summary>
    /// Default very large timeout for Ollama HTTP communication (2 hours).
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromHours(2);

    /// <summary>
    /// Creates an <see cref="IChatClient"/> using OllamaSharp with provider options.
    /// </summary>
    public static IChatClient CreateOllama(
        LlmProviderOptions options,
        string? modelId = null,
        ILogger? logger = null,
        OllamaRoutingTarget routingTarget = OllamaRoutingTarget.Auto)
    {
        ArgumentNullException.ThrowIfNull(options);

        var endpoint = string.IsNullOrWhiteSpace(options.Endpoint) ? "http://localhost:11434" : options.Endpoint;
        var timeoutMinutes = Math.Max(1, options.RequestTimeoutMinutes);
        var effectiveModel = !string.IsNullOrWhiteSpace(modelId) ? modelId : (options.ModelId ?? "default");

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(endpoint.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(timeoutMinutes)
        };

        if (routingTarget != OllamaRoutingTarget.Auto)
        {
            var targetStr = routingTarget switch
            {
                OllamaRoutingTarget.Local => "Local",
                OllamaRoutingTarget.Remote => "Remote",
                OllamaRoutingTarget.Cloud => "Cloud",
                _ => null
            };
            if (targetStr != null)
            {
                httpClient.DefaultRequestHeaders.Add("X-OllamaRouter-Target", targetStr);
            }
        }

        IChatClient innerClient = new OllamaApiClient(httpClient, effectiveModel, null);
        return new OllamaConfiguringChatClient(innerClient, httpClient, options.ContextSize, options.EnableReasoning);
    }

    /// <summary>
    /// Creates an <see cref="IChatClient"/> using OllamaSharp with individual parameters and very large timeout.
    /// </summary>
    public static IChatClient CreateOllama(
        string baseUrl = "http://localhost:11434",
        string? modelId = null,
        int? contextSize = null,
        bool? enableReasoning = null,
        TimeSpan? timeout = null,
        ILogger? logger = null,
        OllamaRoutingTarget routingTarget = OllamaRoutingTarget.Auto)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;
        var options = new LlmProviderOptions
        {
            Endpoint = baseUrl,
            ModelId = modelId,
            RequestTimeoutMinutes = (int)Math.Max(1, Math.Ceiling(effectiveTimeout.TotalMinutes)),
            ContextSize = contextSize,
            EnableReasoning = enableReasoning
        };

        return CreateOllama(options, modelId, logger, routingTarget);
    }

    /// <summary>
    /// Checks whether the Ollama server is responsive at the given base URL.
    /// </summary>
    public static async Task<bool> IsOllamaRunningAsync(
        string baseUrl = "http://localhost:11434",
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(5)
            };
            var ollama = new OllamaApiClient(client, defaultModel: "");
            return await ollama.IsRunningAsync(cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Retrieves the list of local models installed in the Ollama instance.
    /// </summary>
    public static async Task<List<string>> GetAvailableModelsAsync(
        string baseUrl = "http://localhost:11434",
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(10)
            };
            var ollama = new OllamaApiClient(client, defaultModel: "");
            var models = await ollama.ListLocalModelsAsync(cancellationToken);
            return models.Select(m => m.Name).ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Preloads model weights into Ollama memory/VRAM with a specified keep_alive duration.
    /// </summary>
    public static async Task PreloadModelAsync(
        string modelId,
        string baseUrl = "http://localhost:11434",
        string keepAlive = "1h",
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
                Timeout = DefaultTimeout
            };
            var endpoint = new Uri(client.BaseAddress, "api/generate");

            var payload = JsonSerializer.Serialize(new
            {
                model = modelId,
                keep_alive = keepAlive
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(endpoint, content, cancellationToken);
        }
        catch
        {
            // Best-effort preload
        }
    }

    /// <summary>
    /// Forcefully offloads model weights from Ollama memory/VRAM by setting keep_alive to 0.
    /// </summary>
    public static async Task OffloadModelAsync(
        string modelId,
        string baseUrl = "http://localhost:11434",
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(30)
            };
            var endpoint = new Uri(client.BaseAddress, "api/generate");

            var payload = JsonSerializer.Serialize(new
            {
                model = modelId,
                keep_alive = 0
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(endpoint, content, cancellationToken);
        }
        catch
        {
            // Best-effort offload
        }
    }

    /// <summary>
    /// Creates default chat options with context size and reasoning settings.
    /// </summary>
    public static ChatOptions CreateDefaultChatOptions(
        string? modelId = null,
        float? temperature = null,
        string? reasoningEffort = null,
        bool isOpenAi = false,
        int? contextSize = null,
        bool? enableReasoning = null)
    {
        var options = new ChatOptions
        {
            ModelId = modelId,
            Temperature = temperature,
            AdditionalProperties = new AdditionalPropertiesDictionary()
        };

        if (contextSize.HasValue && contextSize.Value > 0)
        {
            options.AdditionalProperties["num_ctx"] = contextSize.Value;
        }

        if (enableReasoning.HasValue)
        {
            options.AdditionalProperties["think"] = enableReasoning.Value;
        }

        if (!string.IsNullOrWhiteSpace(reasoningEffort))
        {
            options.AdditionalProperties["reasoning_effort"] = NormalizeReasoningEffort(reasoningEffort, isOpenAi);
        }

        return options;
    }

    /// <summary>
    /// Normalizes reasoning effort string.
    /// </summary>
    public static string NormalizeReasoningEffort(string? effort, bool isOpenAi = false)
    {
        var val = string.IsNullOrWhiteSpace(effort) ? "xhigh" : effort.Trim().ToLowerInvariant();
        if (isOpenAi && val == "xhigh")
        {
            return "high";
        }
        return val;
    }

    /// <summary>
    /// Delegating client that ensures Ollama context size (num_ctx) and reasoning (think)
    /// options are attached to each outgoing request, and disposes the underlying HttpClient upon completion.
    /// </summary>
    private sealed class OllamaConfiguringChatClient : DelegatingChatClient
    {
        private readonly HttpClient _httpClient;
        private readonly int? _contextSize;
        private readonly bool? _enableReasoning;

        public OllamaConfiguringChatClient(
            IChatClient innerClient,
            HttpClient httpClient,
            int? contextSize = null,
            bool? enableReasoning = null)
            : base(innerClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _contextSize = contextSize;
            _enableReasoning = enableReasoning;
        }

        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return base.GetResponseAsync(messages, ApplyOptions(options), cancellationToken);
        }

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return base.GetStreamingResponseAsync(messages, ApplyOptions(options), cancellationToken);
        }

        public override object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceType == typeof(HttpClient))
            {
                return _httpClient;
            }
            return base.GetService(serviceType, serviceKey);
        }

        private ChatOptions? ApplyOptions(ChatOptions? options)
        {
            if (!_contextSize.HasValue && !_enableReasoning.HasValue)
            {
                return options;
            }

            var clone = options?.Clone() ?? new ChatOptions();
            clone.AdditionalProperties ??= new AdditionalPropertiesDictionary();

            if (_contextSize.HasValue && _contextSize.Value > 0 && !clone.AdditionalProperties.ContainsKey("num_ctx"))
            {
                clone.AdditionalProperties["num_ctx"] = _contextSize.Value;
            }

            if (_enableReasoning.HasValue && !clone.AdditionalProperties.ContainsKey("think"))
            {
                clone.AdditionalProperties["think"] = _enableReasoning.Value;
            }

            return clone;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                base.Dispose(disposing);
                _httpClient.Dispose();
            }
        }
    }
}
