using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pmad.AI.LlamaSharp;

namespace Pmad.AI.LlamaSharp.Tests;

public class LlamaSharpOptionsAndLoggingTests
{
    private sealed class TestLogger : ILogger
    {
        public string Name { get; }

        public List<(LogLevel Level, string Message)> Messages { get; } = new();

        public TestLogger(string name = "TestLogger")
        {
            Name = name;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add((logLevel, formatter(state, exception)));
        }
    }

    private sealed class TestLoggerFactory : ILoggerFactory
    {
        public TestLogger Logger { get; } = new("FactoryLogger");

        public void AddProvider(ILoggerProvider provider) { }

        public ILogger CreateLogger(string categoryName) => Logger;

        public void Dispose() { }
    }

    [Fact]
    public void LlamaSharpOptions_DefaultValues_AreSensible()
    {
        var options = new LlamaSharpOptions();

        Assert.False(options.VerboseLogging);
        Assert.Null(options.Logger);
        Assert.Null(options.LoggerFactory);
        Assert.Equal(49152, options.ContextSize);
        Assert.Equal(99, options.GpuLayerCount);
        Assert.Null(options.Threads);
        Assert.Null(options.BatchThreads);
        Assert.Equal(2048u, options.BatchSize);
        Assert.Equal("chatml", options.PromptFormat);
        Assert.Equal(0.7f, options.DefaultTemperature);
        Assert.Equal(2048, options.DefaultMaxOutputTokens);
        Assert.Equal(10, options.RequestTimeoutMinutes);
        Assert.Null(options.EnableReasoning);
        Assert.Null(options.ReasoningEffort);
        Assert.Null(options.SupportsReasoning);
        Assert.Null(options.SupportsReasoningEffort);
    }

    [Fact]
    public void LlamaSharpOptions_CanConfigureThreads()
    {
        var options = new LlamaSharpOptions
        {
            Threads = 2,
            BatchThreads = 4
        };

        Assert.Equal(2, options.Threads);
        Assert.Equal(4, options.BatchThreads);
    }

    [Fact]
    public void LlamaSharpOptions_CanConfigureReasoningOptions()
    {
        var options = new LlamaSharpOptions
        {
            EnableReasoning = true,
            ReasoningEffort = "low",
            SupportsReasoning = true,
            SupportsReasoningEffort = true
        };

        Assert.True(options.EnableReasoning);
        Assert.Equal("low", options.ReasoningEffort);
        Assert.True(options.SupportsReasoning);
        Assert.True(options.SupportsReasoningEffort);
    }

    [Fact]
    public void LlamaSharpOptions_CanConfigurePerChatLoggerAndVerbosity()
    {
        var testLogger = new TestLogger("ChatLogger1");
        var options = new LlamaSharpOptions
        {
            VerboseLogging = true,
            Logger = testLogger
        };

        Assert.True(options.VerboseLogging);
        Assert.Same(testLogger, options.Logger);
    }

    [Fact]
    public void ConfigureNativeLogging_ExecutesSafely()
    {
        var testLogger = new TestLogger("NativeLogger");
        var testFactory = new TestLoggerFactory();

        LlamaSharpChatClient.ConfigureNativeLogging(testLogger, verbose: false);
        LlamaSharpChatClient.ConfigureNativeLogging(testLogger, verbose: true);
        LlamaSharpChatClient.ConfigureNativeLogging(testFactory, verbose: false);
        LlamaSharpChatClient.ConfigureNativeLogging((ILogger?)null, verbose: false);
        LlamaSharpChatClient.ConfigureNativeLogging(NullLogger.Instance);
    }

    [Fact]
    public void CreateFromFile_WhenFileNotFound_ThrowsFileNotFoundExceptionBeforeInitialization()
    {
        var testLogger = new TestLogger("PerChatInstanceLogger");
        var options = new LlamaSharpOptions
        {
            VerboseLogging = true,
            Logger = testLogger
        };

        Assert.Throws<FileNotFoundException>(() =>
        {
            LlamaSharpChatClient.CreateFromFile("non_existent_file.gguf", options, logger: testLogger);
        });
    }

    [Fact]
    public void LlamaLogLevelEnum_ContainsExpectedValues()
    {
        var names = Enum.GetNames<LLama.Native.LLamaLogLevel>();
        Assert.Contains("Error", names);
        Assert.Contains("Warning", names);
        Assert.Contains("Info", names);
        Assert.Contains("Debug", names);
    }

    [Fact]
    public void CreateFromOptions_WhenModelPathNullOrEmpty_ThrowsArgumentException()
    {
        var optionsEmpty = new LlamaSharpOptions { ModelPath = "" };
        Assert.Throws<ArgumentException>(() => LlamaSharpChatClient.CreateFromOptions(optionsEmpty));

        var optionsNull = new LlamaSharpOptions { ModelPath = null };
        Assert.Throws<ArgumentException>(() => LlamaSharpChatClient.CreateFromOptions(optionsNull));
    }

    [Fact]
    public void CreateFromOptions_WhenModelFileNotFound_ThrowsFileNotFoundException()
    {
        var options = new LlamaSharpOptions { ModelPath = "non_existent_path.gguf" };
        Assert.Throws<FileNotFoundException>(() => LlamaSharpChatClient.CreateFromOptions(options));
    }

    [Theory]
    [InlineData("phi3.5")]
    [InlineData("phi-3.5")]
    public void ChatDialect_FromName_ResolvesPhi35Correctly(string dialectName)
    {
        var dialect = ChatDialect.FromName(dialectName);
        Assert.Same(ChatDialect.Phi3, dialect);
    }

    [Fact]
    public void Gemma4Dialect_IncludesEndOfTurnStopToken()
    {
        Assert.Contains("<end_of_turn>", ChatDialect.Gemma4.StopTokens);
    }

    [Fact]
    public void SafeCountInputTokens_WhenModelLoaded_SafelyHandlesLargePrompts()
    {
        var modelPath = @"C:\Users\Julien\AppData\Local\pmad\models\qwen3.5-2b-ud-q4_k_xl.gguf";
        if (!File.Exists(modelPath)) return;

        using var client = LlamaSharpChatClient.CreateFromFile(modelPath);

        // Empty / null
        Assert.Equal(0, client.SafeCountInputTokens(""));
        Assert.Equal(0, client.SafeCountInputTokens(null!));

        // Short prompt
        var shortTokens = client.SafeCountInputTokens("Hello world");
        Assert.True(shortTokens > 0);

        // Huge prompt (e.g. 100 KB) that would crash native llama_tokenize
        var hugePrompt = new string('A', 100_000);
        var hugeTokens = client.SafeCountInputTokens(hugePrompt);
        Assert.True(hugeTokens > 0);
        var rawEstimate = client.TokenEstimator.Estimate(hugePrompt);
        Assert.Equal((int)Math.Ceiling(rawEstimate * 1.05), hugeTokens);
        Assert.Same(Pmad.AI.Tokens.HeuristicTokenEstimator.Qwen, client.TokenEstimator);
    }

    [Fact]
    public void SafeInteractiveExecutor_WhenLargeTrajectoryPromptProvided_SafelyChunksAndPreprocesses()
    {
        var modelPath = @"C:\Users\Julien\AppData\Local\pmad\models\qwen3.5-4b-ud-q4_k_xl.gguf";
        if (!File.Exists(modelPath))
        {
            modelPath = @"C:\Users\Julien\AppData\Local\pmad\models\qwen3.5-2b-ud-q4_k_xl.gguf";
        }
        if (!File.Exists(modelPath)) return;

        var fixturePath = Path.GetFullPath(@"..\..\..\..\..\benchmarks\Pmad.AI.Benchmark\Fixtures\compaction\compaction_large_tool.json");
        if (!File.Exists(fixturePath))
        {
            fixturePath = Path.GetFullPath(@"benchmarks\Pmad.AI.Benchmark\Fixtures\compaction\compaction_large_tool.json");
        }
        if (!File.Exists(fixturePath)) return;

        var json = File.ReadAllText(fixturePath);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var turns = doc.RootElement.GetProperty("Turns");

        var messages = new List<ChatMessage>();
        foreach (var turn in turns.EnumerateArray())
        {
            var userPrompt = turn.GetProperty("UserPrompt").GetString()!;
            messages.Add(new ChatMessage(ChatRole.User, userPrompt));

            if (turn.TryGetProperty("ExpectedToolName", out var toolProp) && !string.IsNullOrEmpty(toolProp.GetString()))
            {
                var toolName = toolProp.GetString()!;
                messages.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call_1", toolName, new Dictionary<string, object?>())]));

                var obs = turn.GetProperty("Observation").GetString()!;
                messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_1", obs)]));
            }

            if (turn.GetProperty("TurnIndex").GetInt32() == 5)
            {
                break;
            }
        }

        var clientOptions = new LlamaSharpOptions
        {
            ContextSize = 65536,
            PromptFormat = "chatml"
        };
        using var client = LlamaSharpChatClient.CreateFromFile(modelPath, clientOptions, promptFormat: clientOptions.PromptFormat);

        var promptText = client.Dialect.FormatPrompt(messages, null, false, null);
        Assert.True(promptText.Length > 50000, "Trajectory prompt should be > 50 KB");

        var executor = new SafeInteractiveExecutor(client.Context);
        var stateData = executor.GetStateData();
        Assert.NotNull(stateData);
    }
}

