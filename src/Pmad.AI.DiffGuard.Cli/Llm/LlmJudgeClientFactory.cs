using System.ClientModel;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using Pmad.AI.DiffGuard.Cli.Cli;

namespace Pmad.AI.DiffGuard.Cli.Llm;

public sealed class LlmJudgeClientFactory : ILlmJudgeClientFactory
{
    public static readonly LlmJudgeClientFactory Instance = new();

    public (IChatClient? Client, IDisposable? Disposable) CreateClient(DiffGuardCliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.EnableLlm || options.LlmProvider == LlmProviderKind.None)
        {
            return (null, null);
        }

        switch (options.LlmProvider)
        {
            case LlmProviderKind.Ollama:
            {
                string endpoint = options.LlmEndpoint
                                  ?? Environment.GetEnvironmentVariable("OLLAMA_HOST")
                                  ?? "http://localhost:11434";

                endpoint = NormalizeEndpointUri(endpoint, defaultPort: 11434);

                string modelId = !string.IsNullOrWhiteSpace(options.LlmModel)
                    ? options.LlmModel
                    : "qwen3:8b";

                var httpClient = new HttpClient { BaseAddress = new Uri(endpoint), Timeout = TimeSpan.FromMinutes(5) };
                var ollamaClient = new OllamaApiClient(httpClient, modelId);
                return (ollamaClient, new DisposableAction(() => { ollamaClient.Dispose(); httpClient.Dispose(); }));
            }

            case LlmProviderKind.OpenAI:
            {
                string? apiKey = options.LlmApiKey
                                 ?? Environment.GetEnvironmentVariable("DIFFGUARD_API_KEY")
                                 ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

                bool hasCustomEndpoint = !string.IsNullOrWhiteSpace(options.LlmEndpoint);
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    if (hasCustomEndpoint)
                    {
                        apiKey = "local-no-key";
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "OpenAI API key is required when using the OpenAI provider. Pass --llm-api-key or set DIFFGUARD_API_KEY or OPENAI_API_KEY.");
                    }
                }

                OpenAIClientOptions? clientOptions = null;
                if (hasCustomEndpoint)
                {
                    clientOptions = new OpenAIClientOptions
                    {
                        Endpoint = new Uri(options.LlmEndpoint!)
                    };
                }

                var openAiClient = clientOptions != null
                    ? new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions)
                    : new OpenAIClient(new ApiKeyCredential(apiKey));

                string modelId = !string.IsNullOrWhiteSpace(options.LlmModel)
                    ? options.LlmModel
                    : "gpt-4.1-mini";

                IChatClient chatClient = openAiClient.GetChatClient(modelId).AsIChatClient();
                return (chatClient, null);
            }

            default:
                return (null, null);
        }
    }

    public static string NormalizeEndpointUri(string raw, int defaultPort)
    {
        string trimmed = raw.Trim();
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "http://" + trimmed;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            string host = uri.Host is "0.0.0.0" or "::" or "[::]" ? "localhost" : uri.Host;
            int port = uri.IsDefaultPort && !trimmed.Contains($":{uri.Port}", StringComparison.Ordinal)
                ? defaultPort
                : uri.Port;
            var builder = new UriBuilder(uri)
            {
                Host = host,
                Port = port
            };
            return builder.Uri.ToString().TrimEnd('/');
        }

        return trimmed;
    }

    private sealed class DisposableAction(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}

