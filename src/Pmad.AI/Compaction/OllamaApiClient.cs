using System.Buffers;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pmad.AI.Compaction;

/// <summary>
/// Lightweight, dependency-free HTTP client for Ollama API endpoints (e.g. http://localhost:11434).
/// Provides high-efficiency, reflection-free, Native AOT compatible interaction with Ollama models.
/// </summary>
public class OllamaApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Uri _baseUrl;
    private readonly Uri _chatEndpoint;
    private readonly string _defaultModel;
    private readonly ILogger _logger;
    private bool _disposed;

    /// <summary>
    /// Gets the base URL for the Ollama server.
    /// </summary>
    public Uri BaseUrl => _baseUrl;

    /// <summary>
    /// Gets the default model tag used when no model is explicitly specified.
    /// </summary>
    public string DefaultModel => _defaultModel;

    /// <summary>
    /// Initializes a new instance of <see cref="OllamaApiClient"/>.
    /// </summary>
    /// <param name="baseUrl">Base URL of the Ollama server (e.g., http://localhost:11434). Defaults to http://localhost:11434.</param>
    /// <param name="defaultModel">Default model to target (defaults to "qwen3.5:2b").</param>
    /// <param name="httpClient">Optional external <see cref="HttpClient"/> instance to use.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public OllamaApiClient(
        Uri? baseUrl = null,
        string defaultModel = "qwen3.5:2b",
        HttpClient? httpClient = null,
        ILogger? logger = null)
    {
        _baseUrl = baseUrl ?? new Uri("http://localhost:11434");
        var baseStr = _baseUrl.ToString().TrimEnd('/') + "/";
        _baseUrl = new Uri(baseStr);
        _chatEndpoint = new Uri(_baseUrl, "api/chat");
        _defaultModel = string.IsNullOrWhiteSpace(defaultModel) ? "qwen3.5:2b" : defaultModel;
        _logger = logger ?? NullLogger.Instance;

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            _ownsHttpClient = true;
        }
    }

    /// <summary>
    /// Initializes a new instance of <see cref="OllamaApiClient"/> with a string URL.
    /// </summary>
    public OllamaApiClient(
        string baseUrl,
        string defaultModel = "qwen3.5:2b",
        HttpClient? httpClient = null,
        ILogger? logger = null)
        : this(new Uri(baseUrl), defaultModel, httpClient, logger)
    {
    }

    /// <summary>
    /// Sends a prompt with an optional system instruction to Ollama's <c>/api/chat</c> endpoint.
    /// </summary>
    public async Task<string?> ChatAsync(
        string prompt,
        string? systemInstruction = null,
        string? model = null,
        int? maxTokens = null,
        float? temperature = null,
        int? contextSize = null,
        string? keepAlive = null,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<(string Role, string Content)>(2);
        if (!string.IsNullOrWhiteSpace(systemInstruction))
        {
            messages.Add(("system", systemInstruction));
        }
        messages.Add(("user", prompt));

        return await ChatAsync(messages, model, maxTokens, temperature, contextSize, keepAlive, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a multi-turn message sequence to Ollama's <c>/api/chat</c> endpoint.
    /// </summary>
    public async Task<string?> ChatAsync(
        IEnumerable<(string Role, string Content)> messages,
        string? model = null,
        int? maxTokens = null,
        float? temperature = null,
        int? contextSize = null,
        string? keepAlive = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var bufferWriter = new ArrayBufferWriter<byte>(1024);
        using (var writer = new Utf8JsonWriter(bufferWriter))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model ?? _defaultModel);
            writer.WriteBoolean("stream", false);

            if (!string.IsNullOrEmpty(keepAlive))
            {
                writer.WriteString("keep_alive", keepAlive);
            }

            if (maxTokens.HasValue || temperature.HasValue || contextSize.HasValue)
            {
                writer.WriteStartObject("options");
                if (maxTokens.HasValue) writer.WriteNumber("num_predict", maxTokens.Value);
                if (temperature.HasValue) writer.WriteNumber("temperature", temperature.Value);
                if (contextSize.HasValue) writer.WriteNumber("num_ctx", contextSize.Value);
                writer.WriteEndObject();
            }

            writer.WriteStartArray("messages");
            foreach (var (role, msgContent) in messages)
            {
                writer.WriteStartObject();
                writer.WriteString("role", role);
                writer.WriteString("content", msgContent);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        using var content = new ReadOnlyMemoryContent(bufferWriter.WrittenMemory);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        using var request = new HttpRequestMessage(HttpMethod.Post, _chatEndpoint)
        {
            Content = content
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to connect to Ollama at {Endpoint}", _chatEndpoint);
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Ollama request failed with HTTP {StatusCode}: {Error}", response.StatusCode, errBody);
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (doc.RootElement.TryGetProperty("message", out var msgEl) &&
                msgEl.TryGetProperty("content", out var contentEl) &&
                contentEl.ValueKind == JsonValueKind.String)
            {
                return contentEl.GetString()?.Trim();
            }

            return null;
        }
    }

    /// <summary>
    /// Checks connectivity to the Ollama server by querying <c>/api/tags</c>.
    /// </summary>
    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return false;

        try
        {
            var tagsEndpoint = new Uri(_baseUrl, "api/tags");
            using var request = new HttpRequestMessage(HttpMethod.Get, tagsEndpoint);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ollama health check failed for {Endpoint}", _baseUrl);
            return false;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
