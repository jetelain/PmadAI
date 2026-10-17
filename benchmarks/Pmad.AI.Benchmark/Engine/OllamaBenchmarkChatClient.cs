using System.Buffers;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Pmad.AI.LlamaSharp;

namespace Pmad.AI.Benchmark.Engine;

/// <summary>
/// <see cref="IChatClient"/> adapter connecting to a local or remote Ollama server (e.g. http://localhost:11434).
/// Supports tool definitions, multi-turn tool calling, reasoning trace extraction, context size configuration,
/// and accurate usage token tracking.
/// </summary>
public sealed partial class OllamaBenchmarkChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _modelId;
    private readonly Uri _endpoint;
    private readonly int _contextSize;
    private readonly float _temperature;
    private readonly ChatClientMetadata _metadata;

    [GeneratedRegex(@"<(think|thought)>(.*?)</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkRegex();

    public OllamaBenchmarkChatClient(
        string modelId,
        string baseUrl = "http://localhost:11434",
        int contextSize = 131072,
        float temperature = 0.7f,
        HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        _modelId = modelId;
        _contextSize = contextSize > 0 ? contextSize : 131072;
        _temperature = temperature;

        var baseUri = new Uri(baseUrl.TrimEnd('/') + "/");
        _endpoint = new Uri(baseUri, "api/chat");

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            _ownsHttpClient = true;
        }

        _metadata = new ChatClientMetadata("ollama", baseUri, _modelId);
    }

    public ChatClientMetadata Metadata => _metadata;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var requestPayload = BuildRequestBody(messages, options, stream: false);
        using var content = new ReadOnlyMemoryContent(requestPayload.WrittenMemory);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = content
        };

        using var httpResponse = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();

        using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return ParseOllamaResponse(doc.RootElement);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestPayload = BuildRequestBody(messages, options, stream: true);
        using var content = new ReadOnlyMemoryContent(requestPayload.WrittenMemory);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = content
        };

        using var httpResponse = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();

        using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var done = root.TryGetProperty("done", out var doneProp) && doneProp.GetBoolean();
            var contents = new List<AIContent>();

            if (root.TryGetProperty("message", out var msgEl))
            {
                if (msgEl.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
                {
                    var text = contentEl.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        contents.Add(new TextContent(text));
                    }
                }

                if (msgEl.TryGetProperty("tool_calls", out var toolsEl) && toolsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var toolEl in toolsEl.EnumerateArray())
                    {
                        var call = ParseFunctionCall(toolEl);
                        if (call != null)
                        {
                            contents.Add(call);
                        }
                    }
                }
            }

            ChatFinishReason? finishReason = null;
            if (done)
            {
                finishReason = contents.OfType<FunctionCallContent>().Any()
                    ? ChatFinishReason.ToolCalls
                    : ChatFinishReason.Stop;
            }

            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = contents,
                FinishReason = finishReason
            };
        }
    }

    private ArrayBufferWriter<byte> BuildRequestBody(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        bool stream)
    {
        var bufferWriter = new ArrayBufferWriter<byte>(4096);
        using (var writer = new Utf8JsonWriter(bufferWriter, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("model", _modelId);
            writer.WriteBoolean("stream", stream);

            // Options (context size, temperature)
            writer.WriteStartObject("options");
            writer.WriteNumber("num_ctx", _contextSize);
            writer.WriteNumber("temperature", options?.Temperature ?? _temperature);
            writer.WriteEndObject();

            // Tools
            if (options?.Tools != null && options.Tools.Count > 0)
            {
                writer.WriteStartArray("tools");
                foreach (var tool in options.Tools)
                {
                    if (tool is AIFunction fn)
                    {
                        AotJsonUtilities.WriteFunctionDefinition(writer, fn);
                    }
                }
                writer.WriteEndArray();
            }

            // Messages
            writer.WriteStartArray("messages");
            foreach (var message in messages)
            {
                WriteMessage(writer, message);
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return bufferWriter;
    }

    private static void WriteMessage(Utf8JsonWriter writer, ChatMessage message)
    {
        writer.WriteStartObject();

        var roleStr = message.Role switch
        {
            var r when r == ChatRole.System => "system",
            var r when r == ChatRole.User => "user",
            var r when r == ChatRole.Assistant => "assistant",
            var r when r == ChatRole.Tool => "tool",
            _ => message.Role.Value
        };
        writer.WriteString("role", roleStr);

        // Content
        var sb = new StringBuilder();
        var toolCalls = new List<FunctionCallContent>();

        foreach (var content in message.Contents)
        {
            if (content is TextContent tc)
            {
                sb.Append(tc.Text);
            }
            else if (content is TextReasoningContent tr)
            {
                sb.Append("<think>\n").Append(tr.Text).Append("\n</think>\n");
            }
            else if (content is FunctionCallContent fc)
            {
                toolCalls.Add(fc);
            }
            else if (content is FunctionResultContent fr)
            {
                var resStr = fr.Result is string s ? s : JsonSerializer.Serialize(fr.Result);
                sb.Append(resStr);
            }
        }

        writer.WriteString("content", sb.ToString());

        if (toolCalls.Count > 0)
        {
            writer.WriteStartArray("tool_calls");
            foreach (var call in toolCalls)
            {
                writer.WriteStartObject();
                writer.WriteString("id", call.CallId ?? $"call_{Guid.NewGuid():N}");
                writer.WriteString("type", "function");
                writer.WriteStartObject("function");
                writer.WriteString("name", call.Name);
                writer.WritePropertyName("arguments");
                if (call.Arguments != null && call.Arguments.Count > 0)
                {
                    AotJsonUtilities.WriteObject(writer, call.Arguments);
                }
                else
                {
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                }
                writer.WriteEndObject(); // function
                writer.WriteEndObject(); // tool_call
            }
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static ChatResponse ParseOllamaResponse(JsonElement root)
    {
        var contents = new List<AIContent>();
        string? textContent = null;

        if (root.TryGetProperty("message", out var msgEl))
        {
            if (msgEl.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
            {
                textContent = contentEl.GetString() ?? string.Empty;
            }

            // Extract <think>...</think> reasoning traces
            if (!string.IsNullOrWhiteSpace(textContent))
            {
                var thinkMatches = ThinkRegex().Matches(textContent);
                foreach (Match m in thinkMatches)
                {
                    var reasoning = m.Groups[2].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(reasoning))
                    {
                        contents.Add(new TextReasoningContent(reasoning));
                    }
                }

                if (thinkMatches.Count > 0)
                {
                    textContent = ThinkRegex().Replace(textContent, string.Empty).Trim();
                }
            }

            // Extract tool calls
            if (msgEl.TryGetProperty("tool_calls", out var toolsEl) && toolsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var toolEl in toolsEl.EnumerateArray())
                {
                    var call = ParseFunctionCall(toolEl);
                    if (call != null)
                    {
                        contents.Add(call);
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(textContent))
        {
            contents.Add(new TextContent(textContent));
        }

        if (contents.Count == 0)
        {
            contents.Add(new TextContent(string.Empty));
        }

        int promptTokens = 0;
        int outputTokens = 0;

        if (root.TryGetProperty("prompt_eval_count", out var peProp) && peProp.TryGetInt32(out var pe))
        {
            promptTokens = pe;
        }

        if (root.TryGetProperty("eval_count", out var eProp) && eProp.TryGetInt32(out var ec))
        {
            outputTokens = ec;
        }

        var finishReason = contents.OfType<FunctionCallContent>().Any()
            ? ChatFinishReason.ToolCalls
            : ChatFinishReason.Stop;

        var assistantMsg = new ChatMessage(ChatRole.Assistant, contents);
        return new ChatResponse([assistantMsg])
        {
            FinishReason = finishReason,
            Usage = new UsageDetails
            {
                InputTokenCount = promptTokens,
                OutputTokenCount = outputTokens,
                TotalTokenCount = promptTokens + outputTokens
            }
        };
    }

    private static FunctionCallContent? ParseFunctionCall(JsonElement toolEl)
    {
        string? callId = null;
        if (toolEl.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
        {
            callId = idProp.GetString();
        }
        callId ??= $"call_{Guid.NewGuid():N}";

        if (toolEl.TryGetProperty("function", out var fnEl) && fnEl.ValueKind == JsonValueKind.Object)
        {
            string? name = null;
            if (fnEl.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
            {
                name = nameProp.GetString();
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            IDictionary<string, object?> arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (fnEl.TryGetProperty("arguments", out var argsEl))
            {
                if (argsEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in argsEl.EnumerateObject())
                    {
                        arguments[prop.Name] = UnwrapJsonElement(prop.Value);
                    }
                }
                else if (argsEl.ValueKind == JsonValueKind.String)
                {
                    var jsonStr = argsEl.GetString();
                    if (!string.IsNullOrWhiteSpace(jsonStr))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(jsonStr);
                            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var prop in doc.RootElement.EnumerateObject())
                                {
                                    arguments[prop.Name] = UnwrapJsonElement(prop.Value);
                                }
                            }
                        }
                        catch
                        {
                            arguments["input"] = jsonStr;
                        }
                    }
                }
            }

            return new FunctionCallContent(callId, name, arguments);
        }

        return null;
    }

    private static object? UnwrapJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.Clone()
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(OllamaBenchmarkChatClient) ? this : null;

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
