using Pmad.AI.Guardrails;

namespace Pmad.AI.Tests;

public class ToolLoopCircuitBreakerTests
{
    [Fact]
    public void DetectStreamingRepetition_DetectsDegenerateRepetition()
    {
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            NgramMinLength = 16,
            NgramMinOccurrences = 3
        });

        // 16 chars repeated 3 times
        var repeatedPattern = "1234567890abcdef";
        var text = "Some prefix text " + repeatedPattern + repeatedPattern + repeatedPattern;

        var result = breaker.DetectStreamingRepetition(text.AsSpan());
        Assert.True(result.IsLoopDetected);
        Assert.Equal(LoopType.StreamingRepetition, result.Type);
        Assert.Contains("Streaming repetition loop detected", result.DiagnosticMessage);
    }

    [Fact]
    public void DetectStreamingRepetition_DoesNotTripOnProse()
    {
        var breaker = new ToolLoopCircuitBreaker();
        var text = "This is a normal paragraph with standard English text that does not repeat cyclically.";

        var result = breaker.DetectStreamingRepetition(text.AsSpan());
        Assert.False(result.IsLoopDetected);
        Assert.Equal(LoopType.None, result.Type);
    }

    [Fact]
    public void EvaluateToolInvocation_TripsOnDuplicateCallOnUnchangedState()
    {
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            PingPongThreshold = 2
        });

        // First call
        var r1 = breaker.EvaluateToolInvocation("read_file", "{\"path\": \"src/main.cs\"}", "git_hash_123");
        Assert.False(r1.IsLoopDetected);

        // Second call with same parameters and unchanged state signature
        var r2 = breaker.EvaluateToolInvocation("read_file", "{\"path\": \"src/main.cs\"}", "git_hash_123");
        Assert.True(r2.IsLoopDetected);
        Assert.Equal(LoopType.ToolPingPong, r2.Type);
        Assert.Equal("read_file", r2.ToolName);
    }

    [Fact]
    public void EvaluateToolInvocation_PermutedJsonKeys_DetectedAsDuplicateWhenCanonicalizeTrue()
    {
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            CanonicalizeJsonArguments = true,
            PingPongThreshold = 2
        });

        var r1 = breaker.EvaluateToolInvocation("edit", "{\"path\":\"a.cs\",\"offset\":10}", "state1");
        Assert.False(r1.IsLoopDetected);

        // Permuted keys {"offset":10,"path":"a.cs"}
        var r2 = breaker.EvaluateToolInvocation("edit", "{\"offset\":10,\"path\":\"a.cs\"}", "state1");
        Assert.True(r2.IsLoopDetected);
        Assert.Equal(LoopType.ToolPingPong, r2.Type);
    }

    [Fact]
    public void EvaluateToolInvocation_ChangedState_DoesNotTrip()
    {
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            PingPongThreshold = 2
        });

        var r1 = breaker.EvaluateToolInvocation("read", "{\"path\":\"a.cs\"}", "state1");
        Assert.False(r1.IsLoopDetected);

        // Same call but state changed
        var r2 = breaker.EvaluateToolInvocation("read", "{\"path\":\"a.cs\"}", "state2");
        Assert.False(r2.IsLoopDetected);
    }

    [Fact]
    public void Reset_ClearsHistoryAndState()
    {
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            PingPongThreshold = 2
        });

        breaker.EvaluateToolInvocation("read", "{}", "state1");
        breaker.Reset();

        // After reset, first call should not trip even if matching prior call
        var r = breaker.EvaluateToolInvocation("read", "{}", "state1");
        Assert.False(r.IsLoopDetected);
    }

    [Fact]
    public void EvaluateToolInvocation_AlternatingToolsPingPong_TripsCircuitBreaker()
    {
        var breaker = new ToolLoopCircuitBreaker();

        // Alternating sequence: read -> write -> read -> write on unchanged state
        var r1 = breaker.EvaluateToolInvocation("read", "{\"path\":\"a.cs\"}", "state_xyz");
        Assert.False(r1.IsLoopDetected);

        var r2 = breaker.EvaluateToolInvocation("write", "{\"path\":\"b.cs\"}", "state_xyz");
        Assert.False(r2.IsLoopDetected);

        var r3 = breaker.EvaluateToolInvocation("read", "{\"path\":\"a.cs\"}", "state_xyz");
        Assert.False(r3.IsLoopDetected);

        // Fourth call completes cycle of length 2 repeated twice on unchanged state
        var r4 = breaker.EvaluateToolInvocation("write", "{\"path\":\"b.cs\"}", "state_xyz");
        Assert.True(r4.IsLoopDetected);
        Assert.Equal(LoopType.ToolPingPong, r4.Type);
        Assert.Contains("cycle of 2", r4.DiagnosticMessage);
    }

    [Theory]
    [InlineData(1, 16, 2, 50)] // NgramMinOccurrences < 2
    [InlineData(3, 0, 2, 50)]  // NgramMinLength < 1
    [InlineData(3, 16, 0, 50)] // PingPongThreshold < 1
    [InlineData(3, 16, 2, 0)]  // MaxToolHistory < 1
    public void Constructor_InvalidOptions_ThrowsArgumentOutOfRangeException(int minOccur, int minLen, int pingPong, int maxHistory)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            NgramMinOccurrences = minOccur,
            NgramMinLength = minLen,
            PingPongThreshold = pingPong,
            MaxToolHistory = maxHistory
        }));
    }
}
