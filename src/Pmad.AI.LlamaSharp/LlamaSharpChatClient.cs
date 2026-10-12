using System.Runtime.CompilerServices;
using System.Text;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pmad.AI.LlamaSharp;

/// <summary>
/// <see cref="IChatClient"/> adapter for in-process local Small Language Models (SLMs)
/// executed via LLamaSharp (llama.cpp) supporting GGUF models.
/// Supports text-only generation and multimodal vision when paired with an <c>mmproj</c> projector.
/// </summary>
public sealed class LlamaSharpChatClient : IChatClient
{
    private static readonly object _nativeLogLock = new();
    private static ILogger? _nativeLogger;
    private static bool _nativeVerbose;
    private static bool _nativeLogHookInstalled;

    private readonly string _modelPath;
    private readonly LlamaSharpOptions _options;
    private readonly string _promptFormat;
    private readonly LLamaWeights _weights;
    private readonly LLamaContext _context;
    private readonly MtmdWeights? _clipModel;
    private readonly StatelessExecutor _statelessExecutor;
    private readonly bool _ownsWeights;
    private readonly ILogger _logger;

    /// <summary>
    /// Configures process-wide logging for native llama.cpp execution using an <see cref="ILogger"/>.
    /// This is separate from per-chat client logging to avoid mixing loggers between concurrent chats.
    /// </summary>
    /// <param name="logger">The logger to receive native messages, or null to disable native message forwarding.</param>
    /// <param name="verbose">When true, outputs all messages including debug and info; when false, outputs errors only.</param>
    public static void ConfigureNativeLogging(ILogger? logger, bool verbose = false)
    {
        lock (_nativeLogLock)
        {
            _nativeLogger = logger;
            _nativeVerbose = verbose;
            EnsureNativeLogHookInstalled();
        }
    }

    /// <summary>
    /// Configures process-wide logging for native llama.cpp execution using an <see cref="ILoggerFactory"/>.
    /// </summary>
    /// <param name="loggerFactory">The logger factory used to create a logger for native llama.cpp messages.</param>
    /// <param name="verbose">When true, outputs all messages including debug and info; when false, outputs errors only.</param>
    public static void ConfigureNativeLogging(ILoggerFactory loggerFactory, bool verbose = false)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ConfigureNativeLogging(loggerFactory.CreateLogger("LLamaSharp.Native"), verbose);
    }

    private static void EnsureNativeLogHookInstalled()
    {
        if (_nativeLogHookInstalled) return;

        try
        {
            NativeLogConfig.llama_log_set((level, message) =>
            {
                ILogger? logger;
                bool verbose;
                lock (_nativeLogLock)
                {
                    logger = _nativeLogger;
                    verbose = _nativeVerbose;
                }

                if (logger == null) return;

                var logLevel = level switch
                {
                    LLamaLogLevel.Error => LogLevel.Error,
                    LLamaLogLevel.Warning => LogLevel.Warning,
                    LLamaLogLevel.Info => LogLevel.Information,
                    LLamaLogLevel.Debug => LogLevel.Debug,
                    _ => LogLevel.Trace
                };

                if (!verbose && logLevel < LogLevel.Error)
                {
                    return;
                }

                if (logger.IsEnabled(logLevel))
                {
                    logger.Log(logLevel, "[llama.cpp] {Message}", message.TrimEnd());
                }
            });
            _nativeLogHookInstalled = true;
        }
        catch
        {
            // Ignore native logging setup errors in environments without native binaries loaded
        }
    }

    /// <summary>
    /// Initializes a new instance of <see cref="LlamaSharpChatClient"/> from existing weights and context.
    /// </summary>
    public LlamaSharpChatClient(
        LLamaWeights weights,
        LLamaContext context,
        string modelPath,
        LlamaSharpOptions? options = null,
        string promptFormat = "chatml",
        MtmdWeights? clipModel = null,
        bool ownsWeights = false,
        ILogger? logger = null)
    {
        _weights = weights ?? throw new ArgumentNullException(nameof(weights));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _modelPath = modelPath;
        _options = options ?? new LlamaSharpOptions();
        _promptFormat = promptFormat;
        _clipModel = clipModel;
        _ownsWeights = ownsWeights;

        _logger = logger
            ?? _options.Logger
            ?? _options.LoggerFactory?.CreateLogger<LlamaSharpChatClient>()
            ?? NullLogger<LlamaSharpChatClient>.Instance;

        var modelParams = new ModelParams(modelPath)
        {
            ContextSize = (uint)_options.ContextSize,
            GpuLayerCount = _options.GpuLayerCount,
            BatchSize = _options.BatchSize
        };
        _statelessExecutor = new StatelessExecutor(_weights, modelParams);
    }

    /// <summary>
    /// Creates a self-contained <see cref="LlamaSharpChatClient"/> by loading model weights and context from file.
    /// Disposing this client will unload native weights.
    /// </summary>
    public static LlamaSharpChatClient CreateFromFile(
        string modelPath,
        LlamaSharpOptions? options = null,
        string? mmprojPath = null,
        string promptFormat = "chatml",
        ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"Local GGUF model file was not found at '{modelPath}'.", modelPath);
        }

        options ??= new LlamaSharpOptions();
        var instanceLogger = logger
            ?? options.Logger
            ?? options.LoggerFactory?.CreateLogger<LlamaSharpChatClient>()
            ?? NullLogger<LlamaSharpChatClient>.Instance;

        instanceLogger.LogInformation(
            "Loading GGUF model from '{ModelPath}' (ContextSize: {ContextSize}, GpuLayerCount: {GpuLayerCount})",
            modelPath, options.ContextSize, options.GpuLayerCount);

        var modelParams = new ModelParams(modelPath)
        {
            ContextSize = (uint)options.ContextSize,
            GpuLayerCount = options.GpuLayerCount,
            BatchSize = options.BatchSize
        };

        var weights = LLamaWeights.LoadFromFile(modelParams);
        var context = weights.CreateContext(modelParams);

        MtmdWeights? clipModel = null;
        if (!string.IsNullOrWhiteSpace(mmprojPath) && File.Exists(mmprojPath))
        {
            try
            {
                instanceLogger.LogInformation("Loading multimodal projector from '{MmprojPath}'", mmprojPath);
                clipModel = MtmdWeights.LoadFromFile(mmprojPath, weights, MtmdContextParams.Default());
            }
            catch (Exception ex)
            {
                instanceLogger.LogWarning(ex, "Failed to load multimodal projector '{MmprojPath}': {Message}", mmprojPath, ex.Message);
            }
        }

        return new LlamaSharpChatClient(
            weights,
            context,
            modelPath,
            options,
            promptFormat,
            clipModel,
            ownsWeights: true,
            logger: instanceLogger);
    }

    /// <inheritdoc />
    public ChatClientMetadata Metadata => new("llamasharp", new Uri($"file://{_modelPath}"), Path.GetFileName(_modelPath));

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            if (HasImageContent(messages) && _clipModel == null)
            {
                throw new InvalidOperationException(
                    "The chat request contains images, but no multimodal projector (mmproj) was loaded for this local model. " +
                    "Ensure an mmproj-*.gguf file is available.");
            }

            var promptText = LocalModelChatFormatter.FormatPrompt(messages, _promptFormat, options?.Tools);
            int inputTokens = _context.Tokenize(promptText).Length;

            _logger.LogDebug("Starting completion for model '{ModelPath}'. Prompt length: {PromptLength} chars, Tokens: {InputTokens}",
                _modelPath, promptText.Length, inputTokens);

            int maxOutputTokens = options?.MaxOutputTokens ?? _options.DefaultMaxOutputTokens;
            var inferenceParams = new InferenceParams
            {
                MaxTokens = maxOutputTokens,
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = (float)(options?.Temperature ?? _options.DefaultTemperature)
                },
                AntiPrompts = new List<string> { "<|im_end|>", "<|end|>", "<turn|>" },
                OverflowStrategy = ContextOverflowStrategy.TruncateAndReprefill
            };

            var embeds = ExtractMediaEmbeds(messages);
            try
            {
                var responseBuilder = new StringBuilder();
                int outputTokenCount = 0;

                if (embeds.Count > 0 && _clipModel != null)
                {
                    _logger.LogDebug("Executing multimodal inference with {EmbedCount} media embed(s)", embeds.Count);
                    var interactiveExecutor = new InteractiveExecutor(_context, _clipModel);
                    interactiveExecutor.Embeds.AddRange(embeds);
                    await foreach (var token in interactiveExecutor.InferAsync(promptText, inferenceParams, cancellationToken))
                    {
                        responseBuilder.Append(token);
                        outputTokenCount++;
                    }
                }
                else
                {
                    await foreach (var token in _statelessExecutor.InferAsync(promptText, inferenceParams, cancellationToken))
                    {
                        responseBuilder.Append(token);
                        outputTokenCount++;
                    }
                }

                var text = responseBuilder.ToString().Trim();
                var assistantMessage = LocalModelChatFormatter.ParseAssistantResponse(text, _options.FunctionCallNormalizer);

                _logger.LogDebug("Completed inference. Output tokens: {OutputTokens}, Total tokens: {TotalTokens}",
                    outputTokenCount, inputTokens + outputTokenCount);

                var chatResponse = new ChatResponse(new List<ChatMessage> { assistantMessage })
                {
                    Usage = new UsageDetails
                    {
                        InputTokenCount = inputTokens,
                        OutputTokenCount = outputTokenCount,
                        TotalTokenCount = inputTokens + outputTokenCount
                    }
                };

                return chatResponse;
            }
            finally
            {
                foreach (var embed in embeds)
                {
                    embed.Dispose();
                }
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (HasImageContent(messages) && _clipModel == null)
        {
            throw new InvalidOperationException(
                "The chat request contains images, but no multimodal projector (mmproj) was loaded for this local model. " +
                "Ensure an mmproj-*.gguf file is available.");
        }

        var promptText = LocalModelChatFormatter.FormatPrompt(messages, _promptFormat, options?.Tools);
        int maxOutputTokens = options?.MaxOutputTokens ?? _options.DefaultMaxOutputTokens;
        var inferenceParams = new InferenceParams
        {
            MaxTokens = maxOutputTokens,
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = (float)(options?.Temperature ?? _options.DefaultTemperature)
            },
            AntiPrompts = new List<string> { "<|im_end|>", "<|end|>", "<turn|>" },
            OverflowStrategy = ContextOverflowStrategy.TruncateAndReprefill
        };

        var responseBuilder = new StringBuilder();
        var hasTools = options?.Tools is { Count: > 0 };
        var embeds = ExtractMediaEmbeds(messages);

        _logger.LogDebug("Starting streaming inference for model '{ModelPath}'", _modelPath);

        try
        {
            IAsyncEnumerable<string> tokenStream;
            if (embeds.Count > 0 && _clipModel != null)
            {
                _logger.LogDebug("Executing multimodal streaming inference with {EmbedCount} media embed(s)", embeds.Count);
                var interactiveExecutor = new InteractiveExecutor(_context, _clipModel);
                interactiveExecutor.Embeds.AddRange(embeds);
                tokenStream = interactiveExecutor.InferAsync(promptText, inferenceParams, cancellationToken);
            }
            else
            {
                tokenStream = _statelessExecutor.InferAsync(promptText, inferenceParams, cancellationToken);
            }

            await foreach (var token in tokenStream)
            {
                responseBuilder.Append(token);
                if (!hasTools)
                {
                    yield return new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = [new TextContent(token)]
                    };
                }
            }
        }
        finally
        {
            foreach (var embed in embeds)
            {
                embed.Dispose();
            }
        }

        if (hasTools)
        {
            var assistantMessage = LocalModelChatFormatter.ParseAssistantResponse(responseBuilder.ToString().Trim(), _options.FunctionCallNormalizer);
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = assistantMessage.Contents.ToList()
            };
        }
    }

    private static bool IsImageContent(AIContent content)
    {
        return content switch
        {
            DataContent data => data.MediaType is null || data.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase),
            UriContent uri => uri.MediaType is null || uri.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool HasImageContent(IEnumerable<ChatMessage> messages)
    {
        return messages.Any(m => m.Contents.Any(IsImageContent));
    }

    private List<SafeMtmdEmbed> ExtractMediaEmbeds(IEnumerable<ChatMessage> messages)
    {
        var embeds = new List<SafeMtmdEmbed>();
        if (_clipModel == null) return embeds;

        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is DataContent dataContent &&
                    (dataContent.MediaType == null || dataContent.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)))
                {
                    var span = dataContent.Data.Span;
                    if (!span.IsEmpty)
                    {
                        embeds.Add(_clipModel.LoadMedia(span));
                    }
                }
                else if (content is UriContent uriContent &&
                         (uriContent.MediaType == null || uriContent.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)))
                {
                    if (uriContent.Uri.IsFile && File.Exists(uriContent.Uri.LocalPath))
                    {
                        embeds.Add(_clipModel.LoadMedia(uriContent.Uri.LocalPath));
                    }
                }
            }
        }

        return embeds;
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is null)
        {
            if (serviceType == typeof(ILogger) || serviceType == typeof(ILogger<LlamaSharpChatClient>))
            {
                return _logger;
            }
        }
        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsWeights)
        {
            _clipModel?.Dispose();
            _context.Dispose();
            _weights.Dispose();
        }
    }
}