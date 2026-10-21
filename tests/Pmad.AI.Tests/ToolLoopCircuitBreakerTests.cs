using Pmad.AI.Guardrails;

namespace Pmad.AI.Tests;

public class ToolLoopCircuitBreakerTests
{
    [Fact]
    public void ToolLoopCircuitBreakerOptions_DefaultValues_AreCalibrated()
    {
        var options = new ToolLoopCircuitBreakerOptions();
        Assert.Equal(3, options.PingPongThreshold);
        Assert.True(options.ResetOnUserPrompt);
    }

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

    [Fact]
    public void EvaluateToolInvocation_DictionaryArguments_TripsOnDuplicate()
    {
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            PingPongThreshold = 2
        });

        var args1 = new Dictionary<string, object?> { ["path"] = "main.cs", ["count"] = 42 };
        var args2 = new Dictionary<string, object?> { ["count"] = 42, ["path"] = "main.cs" };

        var r1 = breaker.EvaluateToolInvocation("read_file", args1, "state_1");
        Assert.False(r1.IsLoopDetected);

        var r2 = breaker.EvaluateToolInvocation("read_file", args2, "state_1");
        Assert.True(r2.IsLoopDetected);
        Assert.Equal(LoopType.ToolPingPong, r2.Type);
    }

    [Fact]
    public void EvaluateToolInvocation_DictionaryArguments_NullArgumentsHandledSafely()
    {
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            PingPongThreshold = 2
        });

        var r1 = breaker.EvaluateToolInvocation("status", (IDictionary<string, object?>?)null, "state_1");
        Assert.False(r1.IsLoopDetected);

        var r2 = breaker.EvaluateToolInvocation("status", new Dictionary<string, object?>(), "state_1");
        Assert.True(r2.IsLoopDetected);
        Assert.Equal(LoopType.ToolPingPong, r2.Type);
    }

    [Fact]
    public void SerializeArguments_NullOrEmpty_ReturnsEmptyJsonObject()
    {
        Assert.Equal("{}", ToolLoopCircuitBreaker.SerializeArguments(null));
        Assert.Equal("{}", ToolLoopCircuitBreaker.SerializeArguments(new Dictionary<string, object?>()));
    }

    [Fact]
    public void SerializeArguments_SerializesSortedDictionary()
    {
        var args = new Dictionary<string, object?>
        {
            ["z"] = "last",
            ["a"] = "first",
            ["b"] = 123,
            ["c"] = true,
            ["d"] = null
        };

        var json = ToolLoopCircuitBreaker.SerializeArguments(args);
        Assert.Equal("{\"a\":\"first\",\"b\":123,\"c\":true,\"d\":null,\"z\":\"last\"}", json);
    }

    [Fact]
    public void SerializeArguments_EscapesQuotesInStrings()
    {
        var args = new Dictionary<string, object?>
        {
            ["text"] = "hello \"world\""
        };

        var json = ToolLoopCircuitBreaker.SerializeArguments(args);
        Assert.Equal("{\"text\":\"hello \\\"world\\\"\"}", json);
    }

    [Fact]
    public void SerializeArguments_HandlesJsonElementAndPrimitives()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("{\"inner\":\"value\"}");
        var args = new Dictionary<string, object?>
        {
            ["elem"] = doc.RootElement,
            ["pi"] = 3.14159,
            ["flag"] = false
        };

        var json = ToolLoopCircuitBreaker.SerializeArguments(args);
        Assert.Contains("\"elem\":{\"inner\":\"value\"}", json);
        Assert.Contains("\"flag\":false", json);
        Assert.Contains("\"pi\":3.14159", json);
    }

    [Fact]
    public void EvaluateToolInvocation_DistinctObservation_ResetsPingPongCounter()
    {
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            PingPongThreshold = 2
        });

        // 1. Initial call and initial observation
        var r1 = breaker.EvaluateToolInvocation("read_file", "{\"path\": \"src/main.cs\"}");
        Assert.False(r1.IsLoopDetected);
        breaker.RecordObservation("Observation 1: lines 1-50 of main.cs");

        // 2. Tool returns a distinct observation (forward progress in multi-step task)
        breaker.RecordObservation("Observation 2: lines 51-100 of main.cs (distinct content)");

        // 3. Re-invoking the tool does NOT trip the breaker because ping-pong counter was reset
        var r2 = breaker.EvaluateToolInvocation("read_file", "{\"path\": \"src/main.cs\"}");
        Assert.False(r2.IsLoopDetected);

        // 4. Repeated invocation on UNCHANGED observation DOES trip the breaker
        breaker.RecordObservation("Observation 2: lines 51-100 of main.cs (distinct content)");
        var r3 = breaker.EvaluateToolInvocation("read_file", "{\"path\": \"src/main.cs\"}");
        Assert.True(r3.IsLoopDetected);
        Assert.Equal(LoopType.ToolPingPong, r3.Type);
    }

    [Fact]
    public void EvaluateToolInvocation_NewUserPrompt_WhenResetOnUserPromptEnabled_ResetsPingPongCounter()
    {
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            PingPongThreshold = 2,
            ResetOnUserPrompt = true
        });

        // Initial call in turn 1
        breaker.RecordUserPrompt("Find bugs in main.cs");
        var r1 = breaker.EvaluateToolInvocation("search", "{\"query\": \"NullReferenceException\"}");
        Assert.False(r1.IsLoopDetected);

        // User provides a new guiding prompt in turn 2
        breaker.RecordUserPrompt("Now check database connections instead.");

        // Calling the same tool now does NOT trip because ResetOnUserPrompt is true
        var r2 = breaker.EvaluateToolInvocation("search", "{\"query\": \"NullReferenceException\"}");
        Assert.False(r2.IsLoopDetected);
    }

    [Fact]
    public void EvaluateToolInvocation_Default_PreservesLoopCounterAcrossPromptVariations()
    {
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            PingPongThreshold = 2
        });

        // Turn 1
        breaker.RecordUserPrompt("[SWE-bench: sympy-20590] Locate where __slots__ is defined.");
        var r1 = breaker.EvaluateToolInvocation("search_code", "{\"query\": \"__slots__\"}");
        Assert.False(r1.IsLoopDetected);

        // Turn 2 with slightly different wording
        breaker.RecordUserPrompt("Try searching for __slots__ again in sympy/core/symbol.py.");
        var r2 = breaker.EvaluateToolInvocation("search_code", "{\"query\": \"__slots__\"}");

        // Trips because default behavior does not wipe consecutive counter on unchanged state
        Assert.True(r2.IsLoopDetected);
        Assert.Equal(LoopType.ToolPingPong, r2.Type);
    }

    [Theory]
    [InlineData("Proceed with step 2.", true)]
    [InlineData("Step 3", true)]
    [InlineData("Turn 4", true)]
    [InlineData("continue", true)]
    [InlineData("next", true)]
    [InlineData("retry", true)]
    [InlineData("again", true)]
    [InlineData("Try searching for __slots__ again in sympy/core/symbol.py.", true)]
    [InlineData("Please search for __slots__ one more time.", true)]
    [InlineData("Search for __slots__ in sympy/core/symbol.py again.", true)]
    [InlineData("Now check database connections instead.", false)]
    [InlineData("Deploy to production cluster.", false)]
    [InlineData("Steps to reproduce: 1. click button", false)]
    [InlineData("Stepwise refinement of algorithm", false)]
    [InlineData("Turning off debug mode", false)]
    [InlineData("Turns out there is a bug", false)]
    [InlineData("Check against the database schema", false)]
    [InlineData("Verify against master branch", false)]
    [InlineData("Vote against the proposal", false)]
    [InlineData("unretryable error caught", false)]
    [InlineData("repertoire of algorithms", false)]
    public void IsContinuationPrompt_IdentifiesContinuationAndRetryPrompts(string prompt, bool expected)
    {
        Assert.Equal(expected, ToolLoopCircuitBreaker.IsContinuationPrompt(prompt));
    }

    [Fact]
    public void EvaluateToolInvocation_JitteredArgumentsIdenticalObservationStreak_TripsOnTurn4()
    {
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            MaxIdenticalObservationStreak = 3
        });

        const string observation = "# sympy/core/symbol.py\n# File empty of direct string literal '__slots__'\n";

        // Turn 1: jittered arg 1
        var r1 = breaker.EvaluateToolInvocation("search_code", "{\"query\":\"__slots__\",\"path\":\"sympy/core/symbol.py\"}");
        Assert.False(r1.IsLoopDetected);
        breaker.RecordObservation(observation, "search_code");

        // Turn 2: jittered arg 2
        var r2 = breaker.EvaluateToolInvocation("search_code", "{\"query\":\"Symbol\",\"path\":\"sympy/sympy/core/symbol.py\"}");
        Assert.False(r2.IsLoopDetected);
        breaker.RecordObservation(observation, "search_code");

        // Turn 3: jittered arg 3
        var r3 = breaker.EvaluateToolInvocation("search_code", "{\"query\":\"slots\",\"path\":\"sympy/core/symbol.py\"}");
        Assert.False(r3.IsLoopDetected);
        breaker.RecordObservation(observation, "search_code");

        // Turn 4: jittered arg 4 -> trips because identical observation streak reached threshold of 3
        var r4 = breaker.EvaluateToolInvocation("search_code", "{\"query\":\"class Symbol\",\"path\":\"sympy/core/symbol.py\"}");
        Assert.True(r4.IsLoopDetected);
        Assert.Equal(LoopType.NoProgress, r4.Type);
        Assert.Contains("Identical tool observation streak detected", r4.DiagnosticMessage);
    }

    [Fact]
    public void EvaluateToolInvocation_VariedObservations_DoNotFalsePositive()
    {
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            MaxIdenticalObservationStreak = 3,
            MaxConsecutiveToolErrors = 3
        });

        // 5 distinct non-error observations
        for (int i = 1; i <= 5; i++)
        {
            var r = breaker.EvaluateToolInvocation("read_file", $"{{\"path\":\"file_{i}.cs\"}}");
            Assert.False(r.IsLoopDetected, $"False positive on call {i}");
            breaker.RecordObservation($"Content of file {i} with distinct information", "read_file");
        }
    }

    [Fact]
    public void EvaluateToolInvocation_ConsecutiveToolErrors_TripsAfterThreshold()
    {
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            MaxConsecutiveToolErrors = 3
        });

        // Turn 1 error
        var r1 = breaker.EvaluateToolInvocation("search_code", "{\"path\":\"a.py\"}");
        Assert.False(r1.IsLoopDetected);
        breaker.RecordObservation("Error: File 'a.py' not found in repository.", "search_code");

        // Turn 2 error (different observation text, varying argument)
        var r2 = breaker.EvaluateToolInvocation("search_code", "{\"path\":\"b.py\"}");
        Assert.False(r2.IsLoopDetected);
        breaker.RecordObservation("Error: File 'b.py' not found in repository.", "search_code");

        // Turn 3 error (different observation text, varying argument)
        var r3 = breaker.EvaluateToolInvocation("search_code", "{\"path\":\"c.py\"}");
        Assert.False(r3.IsLoopDetected);
        breaker.RecordObservation("Error: File 'c.py' not found in repository.", "search_code");

        // Next invocation of failing tool trips circuit breaker
        var r4 = breaker.EvaluateToolInvocation("search_code", "{\"path\":\"d.py\"}");
        Assert.True(r4.IsLoopDetected);
        Assert.Equal(LoopType.NoProgress, r4.Type);
        Assert.Equal("search_code", r4.ToolName);
        Assert.Contains("Tool failure streak detected: tool 'search_code' failed with 3 consecutive errors", r4.DiagnosticMessage);
    }

    [Fact]
    public void EvaluateToolInvocation_InterleavedToolErrors_DoNotResetStreakCounters()
    {
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            MaxConsecutiveToolErrors = 3
        });

        // Interleaved failures: search_code -> read_file -> search_code -> read_file -> search_code
        breaker.EvaluateToolInvocation("search_code", "{\"q\":\"a\"}");
        breaker.RecordObservation("Error: search index offline", "search_code");

        breaker.EvaluateToolInvocation("read_file", "{\"p\":\"x\"}");
        breaker.RecordObservation("Error: file not found: x", "read_file");

        breaker.EvaluateToolInvocation("search_code", "{\"q\":\"b\"}");
        breaker.RecordObservation("Error: search query syntax error", "search_code");

        breaker.EvaluateToolInvocation("read_file", "{\"p\":\"y\"}");
        breaker.RecordObservation("Error: file not found: y", "read_file");

        // Third search_code error: reaches threshold 3 for search_code despite interleaved read_file errors
        breaker.EvaluateToolInvocation("search_code", "{\"q\":\"c\"}");
        breaker.RecordObservation("Error: search timed out", "search_code");

        var r = breaker.EvaluateToolInvocation("search_code", "{\"q\":\"d\"}");
        Assert.True(r.IsLoopDetected);
        Assert.Equal(LoopType.NoProgress, r.Type);
        Assert.Equal("search_code", r.ToolName);
    }

    [Fact]
    public void EvaluateToolInvocation_SuccessfulNonErrorObservation_ResetsStreakCounters()
    {
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            MaxConsecutiveToolErrors = 3,
            MaxIdenticalObservationStreak = 3
        });

        // 2 consecutive errors for tool_x
        breaker.EvaluateToolInvocation("tool_x", "{\"p\":1}");
        breaker.RecordObservation("Error: step 1 failed", "tool_x");
        breaker.EvaluateToolInvocation("tool_x", "{\"p\":2}");
        breaker.RecordObservation("Error: step 2 failed", "tool_x");

        // Forward progress: a successful, non-error, non-repeated observation arrives
        breaker.EvaluateToolInvocation("tool_x", "{\"p\":3}");
        breaker.RecordObservation("Successfully processed data payload without errors.", "tool_x");

        // Next error is only count 1, does NOT trip
        breaker.EvaluateToolInvocation("tool_x", "{\"p\":4}");
        breaker.RecordObservation("Error: step 4 failed", "tool_x");

        var r = breaker.EvaluateToolInvocation("tool_x", "{\"p\":5}");
        Assert.False(r.IsLoopDetected);
    }

    [Theory]
    [InlineData("Error: File 'main.py' not found", true)]
    [InlineData("  [ERROR] Database connection lost", true)]
    [InlineData("Exception: NullReferenceException in module", true)]
    [InlineData("Fatal: Unrecoverable parse error", true)]
    [InlineData("{\"error\": \"Invalid argument\"}", true)]
    [InlineData("{ \"error\": \"Invalid argument\" }", true)]
    [InlineData("{\n  \"error\": \"Database connection timed out\"\n}", true)]
    [InlineData("{\"errors\": [\"Missing token\"]}", true)]
    [InlineData("File sympy/core/symbol.py not found in repo", true)]
    [InlineData("Hunk context mismatch: removed lines were not found", true)]
    [InlineData("public class Program { public static void Main() {} }", false)]
    [InlineData("Lines 1-50 of sympy/core/symbol.py", false)]
    [InlineData("", false)]
    public void IsErrorObservation_CorrectlyIdentifiesToolErrors(string text, bool expected)
    {
        Assert.Equal(expected, ToolLoopCircuitBreaker.IsErrorObservation(text));
    }

    [Fact]
    public void RecordUserPrompt_NewGuidingPrompt_ResetsStreakCountersAndTrippedState()
    {
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            ResetOnUserPrompt = true,
            MaxConsecutiveToolErrors = 2
        });

        breaker.RecordUserPrompt("First user task");
        breaker.RecordObservation("Error: failed 1", "tool_a");
        breaker.RecordObservation("Error: failed 2", "tool_a");

        Assert.True(breaker.IsTripped);
        var r1 = breaker.EvaluateToolInvocation("tool_a", "{}");
        Assert.True(r1.IsLoopDetected);

        // User provides a new, distinct guiding prompt
        breaker.RecordUserPrompt("Second completely different user task");

        Assert.False(breaker.IsTripped);
        var r2 = breaker.EvaluateToolInvocation("tool_a", "{}");
        Assert.False(r2.IsLoopDetected);
    }

    [Fact]
    public void RecordUserPrompt_ContinuationPrompt_PreservesStreakCounters()
    {
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            ResetOnUserPrompt = true,
            MaxConsecutiveToolErrors = 2
        });

        breaker.RecordUserPrompt("First user task");
        breaker.RecordObservation("Error: failed 1", "tool_a");
        breaker.RecordObservation("Error: failed 2", "tool_a");

        Assert.True(breaker.IsTripped);

        // Continuation prompt should NOT reset streaks
        breaker.RecordUserPrompt("retry");
        Assert.True(breaker.IsTripped);

        breaker.RecordUserPrompt("try again");
        Assert.True(breaker.IsTripped);
    }

    [Fact]
    public void EvaluateToolInvocation_ToolFailureStreakOnOneTool_DoesNotBlockDifferentTool()
    {
        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            MaxConsecutiveToolErrors = 3
        });

        // tool_a fails 3 times
        breaker.EvaluateToolInvocation("tool_a", "{\"q\":1}");
        breaker.RecordObservation("Error: tool_a failed 1", "tool_a");
        breaker.EvaluateToolInvocation("tool_a", "{\"q\":2}");
        breaker.RecordObservation("Error: tool_a failed 2", "tool_a");
        breaker.EvaluateToolInvocation("tool_a", "{\"q\":3}");
        breaker.RecordObservation("Error: tool_a failed 3", "tool_a");

        // Invoking tool_a trips because tool_a reached 3 errors
        var rA = breaker.EvaluateToolInvocation("tool_a", "{\"q\":4}");
        Assert.True(rA.IsLoopDetected);
        Assert.Equal("tool_a", rA.ToolName);
        Assert.Equal(LoopType.NoProgress, rA.Type);

        // Invoking a different tool (tool_b) is NOT blocked because tool_b has not failed
        var rB = breaker.EvaluateToolInvocation("tool_b", "{\"p\":1}");
        Assert.False(rB.IsLoopDetected);

        // When tool_b succeeds with a non-error observation, streak counters are cleared
        breaker.RecordObservation("Successfully read file contents from tool_b", "tool_b");
        Assert.False(breaker.IsTripped);

        // Now tool_a can also be invoked again without being tripped
        var rA2 = breaker.EvaluateToolInvocation("tool_a", "{\"q\":5}");
        Assert.False(rA2.IsLoopDetected);
    }
}

