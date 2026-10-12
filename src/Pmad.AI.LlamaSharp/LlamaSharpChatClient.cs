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
    private readonly ChatDialect _dialect;
    private readonly ChatClientMetadata _metadata;
    private readonly LLamaWeights _weights;
    private readonly LLamaContext _context;
    private readonly MtmdWeights? _clipModel;
    private readonly StatelessExecutor _statelessExecutor;
    private readonly bool _ownsWeights;
    private readonly ILogger _logger;
    private readonly LlamaModelPreset? _preset;

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
        : this(
            weights,
            context,
            modelPath,
            options?.Dialect ?? (promptFormat != "chatml" ? ChatDialect.FromName(promptFormat) : ChatDialect.FromName(options?.PromptFormat ?? promptFormat)),
            options,
            clipModel,
            ownsWeights,
            logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="LlamaSharpChatClient"/> from existing weights and context using a specific <see cref="ChatDialect"/>.
    /// </summary>
    public LlamaSharpChatClient(
        LLamaWeights weights,
        LLamaContext context,
        string modelPath,
        ChatDialect dialect,
        LlamaSharpOptions? options = null,
        MtmdWeights? clipModel = null,
        bool ownsWeights = false,
        ILogger? logger = null)
    {
        _weights = weights ?? throw new ArgumentNullException(nameof(weights));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _modelPath = modelPath;
        _options = options ?? new LlamaSharpOptions();
        _dialect = dialect ?? _options.Dialect ?? ChatDialect.FromName(_options.PromptFormat);
        _clipModel = clipModel;
        _ownsWeights = ownsWeights;
        _preset = LlamaModelPreset.Find(Path.GetFileName(modelPath)) ?? LlamaModelPreset.Find(Path.GetFileNameWithoutExtension(modelPath));
        
        EnsureNativeLogHookInstalled();

        var modelUri = new Uri(Path.GetFullPath(modelPath));
        _metadata = new ChatClientMetadata("llamasharp", modelUri, Path.GetFileName(modelPath));

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
        return CreateFromFile(
            modelPath,
            options?.Dialect ?? (promptFormat != "chatml" ? ChatDialect.FromName(promptFormat) : ChatDialect.FromName(options?.PromptFormat ?? promptFormat)),
            options,
            mmprojPath,
            logger);
    }

    /// <summary>
    /// Creates a self-contained <see cref="LlamaSharpChatClient"/> configured from <see cref="LlamaSharpOptions"/>.
    /// Requires <see cref="LlamaSharpOptions.ModelPath"/> to be set.
    /// Disposing this client will unload native weights.
    /// </summary>
    public static LlamaSharpChatClient CreateFromOptions(
        LlamaSharpOptions options,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ModelPath))
        {
            throw new ArgumentException("ModelPath must be configured on LlamaSharpOptions.", nameof(options));
        }

        return CreateFromFile(
            options.ModelPath,
            options.Dialect ?? ChatDialect.FromName(options.PromptFormat),
            options,
            options.MmprojPath,
            logger);
    }

    /// <summary>
    /// Creates a self-contained <see cref="LlamaSharpChatClient"/> by loading model weights and context from file with a specific <see cref="ChatDialect"/>.
    /// Disposing this client will unload native weights.
    /// </summary>
    public static LlamaSharpChatClient CreateFromFile(
        string modelPath,
        ChatDialect dialect,
        LlamaSharpOptions? options = null,
        string? mmprojPath = null,
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
        var effectiveMmprojPath = mmprojPath ?? options.MmprojPath;
        if (!string.IsNullOrWhiteSpace(effectiveMmprojPath) && File.Exists(effectiveMmprojPath))
        {
            try
            {
                instanceLogger.LogInformation("Loading multimodal projector from '{MmprojPath}'", effectiveMmprojPath);
                clipModel = MtmdWeights.LoadFromFile(effectiveMmprojPath, weights, MtmdContextParams.Default());
            }
            catch (Exception ex)
            {
                instanceLogger.LogWarning(ex, "Failed to load multimodal projector '{MmprojPath}': {Message}", effectiveMmprojPath, ex.Message);
            }
        }

        return new LlamaSharpChatClient(
            weights,
            context,
            modelPath,
            dialect,
            options,
            clipModel,
            ownsWeights: true,
            logger: instanceLogger);
    }

    /// <summary>
    /// Gets the chat prompt template dialect configured for this client.
    /// </summary>
    public ChatDialect Dialect => _dialect;

    /// <summary>
    /// Gets whether reasoning / chain-of-thought is supported by this client configuration.
    /// </summary>
    public bool SupportsReasoning => _options.SupportsReasoning ?? _preset?.SupportsReasoning ?? _dialect.SupportsReasoning;

    /// <summary>
    /// Gets whether reasoning effort parameter tuning is supported by this client configuration.
    /// </summary>
    public bool SupportsReasoningEffort => _options.SupportsReasoningEffort ?? _preset?.SupportsReasoningEffort ?? _dialect.SupportsReasoningEffort;

    /// <summary>
    /// Gets the detected preset metadata for this model, or null if unrecognized or custom.
    /// </summary>
    public LlamaModelPreset? Preset => _preset;

    /// <inheritdoc />
    public ChatClientMetadata Metadata => _metadata;

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.RequestTimeoutMinutes > 0)
        {
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(_options.RequestTimeoutMinutes));
        }
        var ct = timeoutCts.Token;

        return await Task.Run(async () =>
        {
            if (HasImageContent(messages) && _clipModel == null)
            {
                throw new InvalidOperationException(
                    "The chat request contains images, but no multimodal projector (mmproj) was loaded for this local model. " +
                    "Ensure an mmproj-*.gguf file is available.");
            }

            var (enableReasoning, reasoningEffort) = LocalModelChatFormatter.ResolveReasoningParameters(options, _options);
            if (!SupportsReasoning)
            {
                enableReasoning = null;
                reasoningEffort = null;
            }
            else if (!SupportsReasoningEffort)
            {
                reasoningEffort = null;
            }

            var promptText = _dialect.FormatPrompt(messages, options?.Tools, enableReasoning, reasoningEffort);
            int inputTokens = _context.Tokenize(promptText).Length;

            LogOperation("Starting completion for model '{ModelPath}'. Prompt length: {PromptLength} chars, Tokens: {InputTokens}, Reasoning: {ReasoningEnabled}, Effort: {ReasoningEffort}",
                _modelPath, promptText.Length, inputTokens, enableReasoning, reasoningEffort ?? "none");

            int maxOutputTokens = options?.MaxOutputTokens ?? _options.DefaultMaxOutputTokens;
            var inferenceParams = new InferenceParams
            {
                MaxTokens = maxOutputTokens,
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = (float)(options?.Temperature ?? _options.DefaultTemperature)
                },
                AntiPrompts = _dialect.StopTokens.ToList(),
                OverflowStrategy = ContextOverflowStrategy.TruncateAndReprefill
            };

            var embeds = ExtractMediaEmbeds(messages);
            try
            {
                var responseBuilder = new StringBuilder();
                int outputTokenCount = 0;

                if (embeds.Count > 0 && _clipModel != null)
                {
                    LogOperation("Executing multimodal inference with {EmbedCount} media embed(s)", embeds.Count);
                    var interactiveExecutor = new InteractiveExecutor(_context, _clipModel);
                    interactiveExecutor.Embeds.AddRange(embeds);
                    await foreach (var token in interactiveExecutor.InferAsync(promptText, inferenceParams, ct))
                    {
                        responseBuilder.Append(token);
                        outputTokenCount++;
                    }
                }
                else
                {
                    await foreach (var token in _statelessExecutor.InferAsync(promptText, inferenceParams, ct))
                    {
                        responseBuilder.Append(token);
                        outputTokenCount++;
                    }
                }

                var text = responseBuilder.ToString().Trim();
                var assistantMessage = LocalModelChatFormatter.ParseAssistantResponse(text, _dialect, _options.FunctionCallNormalizer);

                LogOperation("Completed inference. Output tokens: {OutputTokens}, Total tokens: {TotalTokens}",
                    outputTokenCount, inputTokens + outputTokenCount);

                var chatResponse = new ChatResponse(new List<ChatMessage> { assistantMessage })
                {
                    FinishReason = assistantMessage.Contents.OfType<FunctionCallContent>().Any()
                        ? ChatFinishReason.ToolCalls
                        : ChatFinishReason.Stop,
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
        }, ct);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.RequestTimeoutMinutes > 0)
        {
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(_options.RequestTimeoutMinutes));
        }
        var ct = timeoutCts.Token;

        if (HasImageContent(messages) && _clipModel == null)
        {
            throw new InvalidOperationException(
                "The chat request contains images, but no multimodal projector (mmproj) was loaded for this local model. " +
                "Ensure an mmproj-*.gguf file is available.");
        }

        var (enableReasoning, reasoningEffort) = LocalModelChatFormatter.ResolveReasoningParameters(options, _options);
        if (!SupportsReasoning)
        {
            enableReasoning = null;
            reasoningEffort = null;
        }
        else if (!SupportsReasoningEffort)
        {
            reasoningEffort = null;
        }

        var promptText = _dialect.FormatPrompt(messages, options?.Tools, enableReasoning, reasoningEffort);
        int maxOutputTokens = options?.MaxOutputTokens ?? _options.DefaultMaxOutputTokens;
        var inferenceParams = new InferenceParams
        {
            MaxTokens = maxOutputTokens,
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = (float)(options?.Temperature ?? _options.DefaultTemperature)
            },
            AntiPrompts = _dialect.StopTokens.ToList(),
            OverflowStrategy = ContextOverflowStrategy.TruncateAndReprefill
        };

        var responseBuilder = new StringBuilder();
        var hasTools = options?.Tools is { Count: > 0 };
        var embeds = ExtractMediaEmbeds(messages);

        LogOperation("Starting streaming inference for model '{ModelPath}'", _modelPath);

        try
        {
            IAsyncEnumerable<string> tokenStream;
            if (embeds.Count > 0 && _clipModel != null)
            {
                LogOperation("Executing multimodal streaming inference with {EmbedCount} media embed(s)", embeds.Count);
                var interactiveExecutor = new InteractiveExecutor(_context, _clipModel);
                interactiveExecutor.Embeds.AddRange(embeds);
                tokenStream = interactiveExecutor.InferAsync(promptText, inferenceParams, ct);
            }
            else
            {
                tokenStream = _statelessExecutor.InferAsync(promptText, inferenceParams, ct);
            }

            await foreach (var token in tokenStream.WithCancellation(ct))
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
            var assistantMessage = LocalModelChatFormatter.ParseAssistantResponse(responseBuilder.ToString().Trim(), _dialect, _options.FunctionCallNormalizer);
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = assistantMessage.Contents.ToList(),
                FinishReason = assistantMessage.Contents.OfType<FunctionCallContent>().Any()
                    ? ChatFinishReason.ToolCalls
                    : ChatFinishReason.Stop
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

        try
        {
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
        catch
        {
            foreach (var embed in embeds)
            {
                embed.Dispose();
            }
            throw;
        }
    }

    private void LogOperation(string message, params object?[] args)
    {
        var level = _options.VerboseLogging ? LogLevel.Information : LogLevel.Debug;
        if (_logger.IsEnabled(level))
        {
            _logger.Log(level, message, args);
        }
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
            if (serviceType == typeof(ChatDialect))
            {
                return _dialect;
            }
            if (serviceType == typeof(LlamaSharpOptions))
            {
                return _options;
            }
            if (serviceType == typeof(LLamaWeights))
            {
                return _weights;
            }
            if (serviceType == typeof(LLamaContext))
            {
                return _context;
            }
            if (serviceType == typeof(LlamaModelPreset) && _preset != null)
            {
                return _preset;
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