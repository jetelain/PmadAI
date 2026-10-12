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
        Assert.Equal(2048u, options.BatchSize);
        Assert.Equal("chatml", options.PromptFormat);
        Assert.Equal(0.7f, options.DefaultTemperature);
        Assert.Equal(2048, options.DefaultMaxOutputTokens);
        Assert.Equal(10, options.RequestTimeoutMinutes);
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
}
