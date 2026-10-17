using Microsoft.Extensions.AI;
using Pmad.AI.Benchmark.Configuration;
using Pmad.AI.Benchmark.Engine;
using Pmad.AI.Benchmark.Scenarios;
using Pmad.AI.Benchmark.Scenarios.SweBench;
using Pmad.AI.Compaction;
using Pmad.AI.Guardrails;

namespace Pmad.AI.Benchmark.Tests;

public class SweBenchTraceReplayTests
{
    [Fact]
    public void SweBenchTrajectoryLoader_LoadsAllArchetypesSuccessfully()
    {
        var trajectories = SweBenchTrajectoryLoader.LoadAllArchetypes();
        Assert.Equal(7, trajectories.Count);

        Assert.Contains(trajectories, t => t.Archetype.Equals("DiffExplosion", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(trajectories, t => t.Archetype.Equals("TestLogExplosion", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(trajectories, t => t.Archetype.Equals("RepetitiveLoop", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(trajectories, t => t.Archetype.Equals("DeepHorizon", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(trajectories, t => t.Archetype.Equals("SuccinctControl", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(trajectories, t => t.Archetype.Equals("MatrixTranspose", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(trajectories, t => t.Archetype.Equals("SyntaxRecovery", StringComparison.OrdinalIgnoreCase));

        foreach (var t in trajectories)
        {
            Assert.NotEmpty(t.InstanceId);
            Assert.NotEmpty(t.Repository);
            Assert.NotEmpty(t.Turns);
            Assert.True(t.CalculateTotalRawChars() > 0);
            Assert.True(t.CalculateEstimatedRawTokens() > 0);
        }
    }

    [Fact]
    public void BenchmarkScenario_FilterScenarios_IncludesSweBenchScenarios()
    {
        var sweScenarios = BenchmarkScenario.FilterScenarios("swebench");
        Assert.Equal(7, sweScenarios.Count);

        Assert.Contains(sweScenarios, s => s.Id == "swebench-diff");
        Assert.Contains(sweScenarios, s => s.Id == "swebench-logs");
        Assert.Contains(sweScenarios, s => s.Id == "swebench-loop");
        Assert.Contains(sweScenarios, s => s.Id == "swebench-deep");
        Assert.Contains(sweScenarios, s => s.Id == "swebench-succinct");
        Assert.Contains(sweScenarios, s => s.Id == "swebench-matrix");
        Assert.Contains(sweScenarios, s => s.Id == "swebench-syntax");
    }

    [Fact]
    public void ToolResultCompactor_OnSweBenchDiffAndLogs_AchievesSignificantCompaction()
    {
        var diffTrajectory = SweBenchTrajectoryLoader.LoadByArchetype("DiffExplosion");
        Assert.NotNull(diffTrajectory);

        var compactor = new ToolResultCompactor(new ToolResultCompactionOptions
        {
            KeepVerbatimCount = 1,
            FallbackCharacterLimit = 400,
            FallbackPrefixLength = 300,
            FallbackSuffixLength = 100
        });

        // Simulate multi-turn history with tool observations
        var history = new List<ChatMessage>();
        foreach (var turn in diffTrajectory.Turns)
        {
            if (!string.IsNullOrEmpty(turn.UserPrompt))
            {
                history.Add(new ChatMessage(ChatRole.User, turn.UserPrompt));
            }
            if (!string.IsNullOrEmpty(turn.Observation))
            {
                history.Add(new ChatMessage(ChatRole.Tool, turn.Observation));
            }
        }

        var rawChars = history.Where(m => m.Role == ChatRole.Tool).Sum(m => m.Text?.Length ?? 0);
        var compactedHistory = compactor.Compact(history);
        var compactedChars = compactedHistory.Where(m => m.Role == ChatRole.Tool).Sum(m => m.Text?.Length ?? 0);

        Assert.True(rawChars > 3000, $"Expected rawChars > 3000, got {rawChars}");
        Assert.True(compactedChars < rawChars, $"Expected compactedChars < rawChars, got {compactedChars} vs {rawChars}");

        var trr = (1.0 - ((double)compactedChars / rawChars)) * 100.0;
        Assert.True(trr >= 40.0, $"Expected TRR >= 40%, got {trr:F1}%");
    }

    [Fact]
    public void ToolLoopCircuitBreaker_OnSweBenchRepetitiveLoop_DetectsAndBreaksLoop()
    {
        var loopTrajectory = SweBenchTrajectoryLoader.LoadByArchetype("RepetitiveLoop");
        Assert.NotNull(loopTrajectory);

        var breaker = new ToolLoopCircuitBreaker(new ToolLoopCircuitBreakerOptions
        {
            PingPongThreshold = 2,
            NgramMinLength = 4,
            NgramMinOccurrences = 3
        });

        bool loopDetected = false;
        foreach (var turn in loopTrajectory.Turns)
        {
            if (!string.IsNullOrEmpty(turn.ExpectedToolName))
            {
                var args = turn.GetArgumentsDictionary();
                var result = breaker.EvaluateToolInvocation(turn.ExpectedToolName, args);
                if (result.IsLoopDetected)
                {
                    loopDetected = true;
                    break;
                }
            }
        }

        Assert.True(loopDetected, "Expected ToolLoopCircuitBreaker to detect repetitive loop in SymPy trajectory.");
    }

    [Fact]
    public async Task BenchmarkRunner_WithMockClient_RunsSweBenchScenariosSuccessfully()
    {
        using var mockClient = new MockBenchmarkChatClient("mock-test-client", simulatedDelayMs: 0);
        var pipeline = BenchmarkPipelineBuilder.BuildPipeline(mockClient, OptimizationConfig.All);
        var diffScenario = Assert.Single(BenchmarkScenario.FilterScenarios("swebench-diff"));
        var runner = new BenchmarkRunner(pipeline, "mock-test-client", verbose: false);

        var result = await runner.RunScenarioAsync(diffScenario);

        var failures = string.Join("\n", result.TurnResults
            .Where(t => t.Status != BenchmarkStatus.Passed)
            .Select(t => $"Turn {t.TurnIndex}: {t.ValidationMessage}"));
        Assert.True(result.Status == BenchmarkStatus.Passed, $"Scenario failed:\n{failures}");
        Assert.True(result.TotalToolCalls >= 4);
        Assert.True(result.TotalOutputTokens > 0);
        Assert.True(result.TotalPromptTokens > 0);
    }

    [Fact]
    public async Task BenchmarkRunner_WithMockClient_RunsSweBenchLoopScenario()
    {
        using var mockClient = new MockBenchmarkChatClient("mock-test-client", simulatedDelayMs: 0);
        var pipeline = BenchmarkPipelineBuilder.BuildPipeline(mockClient, OptimizationConfig.All);
        var loopScenario = Assert.Single(BenchmarkScenario.FilterScenarios("swebench-loop"));
        var runner = new BenchmarkRunner(pipeline, "mock-test-client", verbose: false);

        var result = await runner.RunScenarioAsync(loopScenario);

        var failures = string.Join("\n", result.TurnResults
            .Where(t => t.Status != BenchmarkStatus.Passed)
            .Select(t => $"Turn {t.TurnIndex}: {t.ValidationMessage}"));
        Assert.True(result.Status == BenchmarkStatus.Passed, $"Loop scenario failed:\n{failures}");
    }

    [Fact]
    public void StateAwareSweBenchMockExecutor_MaintainsFileStateAndPatchValidation()
    {
        var trajectory = SweBenchTrajectoryLoader.LoadByArchetype("SuccinctControl");
        Assert.NotNull(trajectory);

        var executor = new StateAwareSweBenchMockExecutor(trajectory);
        Assert.False(executor.PatchApplied);
        Assert.False(executor.VerificationTestsPassed);
        Assert.False(executor.IsTaskSuccessful);

        // 1. read_file returns authentic content from virtual repository
        var readCall = new FunctionCallContent("call_r1", "read_file", new Dictionary<string, object?>
        {
            ["path"] = "requests/models.py"
        });
        var readOutput = executor.Execute(readCall)?.ToString();
        Assert.NotNull(readOutput);
        Assert.Contains("def json(self", readOutput);

        // 2. run_tests BEFORE patch returns failing test results
        var runTestsPrePatch = new FunctionCallContent("call_t1", "run_tests", new Dictionary<string, object?>
        {
            ["test_target"] = "tests/test_requests.py -k test_json_decode"
        });
        var testOutputPre = executor.Execute(runTestsPrePatch)?.ToString();
        Assert.NotNull(testOutputPre);
        Assert.Contains("1 failed", testOutputPre);
        Assert.False(executor.VerificationTestsPassed);
        Assert.False(executor.IsTaskSuccessful);

        // 3. check_git_diff BEFORE patch returns no modifications
        var diffPre = executor.Execute(new FunctionCallContent("call_d1", "check_git_diff", new Dictionary<string, object?>()))?.ToString();
        Assert.Contains("No modifications", diffPre);

        // 4. apply_patch applies the diff
        var patchCall = new FunctionCallContent("call_p1", "apply_patch", new Dictionary<string, object?>
        {
            ["path"] = "requests/models.py",
            ["patch"] = "@@ -808,2 +808,4 @@\n+        if not self.content:\n+            return None\n"
        });
        var patchOutput = executor.Execute(patchCall)?.ToString();
        Assert.Contains("Patch applied successfully", patchOutput);
        Assert.True(executor.PatchApplied);

        // 5. check_git_diff AFTER patch returns diff
        var diffPost = executor.Execute(new FunctionCallContent("call_d2", "check_git_diff", new Dictionary<string, object?>()))?.ToString();
        Assert.Contains("diff --git a/requests/models.py", diffPost);

        // 6. run_tests AFTER patch returns passing test results
        var testOutputPost = executor.Execute(runTestsPrePatch)?.ToString();
        Assert.NotNull(testOutputPost);
        Assert.Contains("2 passed", testOutputPost);
        Assert.True(executor.VerificationTestsPassed);

        // 7. Overall task success is now true!
        Assert.True(executor.IsTaskSuccessful);
        Assert.Null(executor.FailureReason);
    }

    [Fact]
    public void DockerSweBenchSandboxExecutor_GracefullyHandlesContainerFallback()
    {
        var trajectory = SweBenchTrajectoryLoader.LoadByArchetype("SuccinctControl");
        Assert.NotNull(trajectory);

        // Container does not exist, so it should fall back cleanly to StateAwareSweBenchMockExecutor
        var dockerExecutor = new DockerSweBenchSandboxExecutor(trajectory, "nonexistent_container_12345");
        Assert.False(dockerExecutor.PatchApplied);
        Assert.False(dockerExecutor.VerificationTestsPassed);

        // Tool calls should execute via fallback executor without throwing
        var readCall = new FunctionCallContent("call_r1", "read_file", new Dictionary<string, object?>
        {
            ["path"] = "requests/models.py"
        });
        var output = dockerExecutor.Execute(readCall)?.ToString();
        Assert.NotNull(output);
        Assert.Contains("def json(self", output);
        Assert.Contains("Docker (Fallback: Mock)", dockerExecutor.ExecutionMode);
    }

    [Fact]
    public void StateAwareSweBenchMockExecutor_RejectsIncorrectPatch()
    {
        var trajectory = SweBenchTrajectoryLoader.LoadByArchetype("SuccinctControl");
        Assert.NotNull(trajectory);

        var executor = new StateAwareSweBenchMockExecutor(trajectory);
        Assert.Equal("Mock", executor.ExecutionMode);

        // Apply a syntactically valid unified diff that applies cleanly, but does not fix the bug
        var wrongPatchCall = new FunctionCallContent("call_p_wrong", "apply_patch", new Dictionary<string, object?>
        {
            ["path"] = "requests/models.py",
            ["patch"] = "@@ -1,2 +1,3 @@\n-    def json(self, **kwargs):\n+    def json(self, **kwargs):\n+        # this is an unrelated comment that does not fix the bug\n"
        });
        var patchOutput = executor.Execute(wrongPatchCall)?.ToString();
        Assert.NotNull(patchOutput);
        Assert.True(executor.PatchApplied);
        Assert.False(executor.HasCorrectPatchApplied);

        // run_tests must return failing test output because the bug was NOT fixed
        var runTestsCall = new FunctionCallContent("call_t1", "run_tests", new Dictionary<string, object?>
        {
            ["test_target"] = "tests/test_requests.py -k test_json_decode"
        });
        var testOutput = executor.Execute(runTestsCall)?.ToString();
        Assert.NotNull(testOutput);
        Assert.Contains("1 failed", testOutput);
        Assert.False(executor.VerificationTestsPassed);
        Assert.False(executor.IsTaskSuccessful);
        Assert.Contains("does not contain the required semantic fix", executor.FailureReason);
    }

    [Fact]
    public void SweBenchTraceScenario_OnScenarioStart_ResetsStateBetweenRuns()
    {
        var trajectory = SweBenchTrajectoryLoader.LoadByArchetype("SuccinctControl");
        Assert.NotNull(trajectory);

        var scenario = new SweBenchTraceScenario(trajectory);
        Assert.Equal("Mock", scenario.Executor.ExecutionMode);

        // 1. Mutate executor state (apply correct patch and pass tests)
        var patchCall = new FunctionCallContent("call_p1", "apply_patch", new Dictionary<string, object?>
        {
            ["path"] = "requests/models.py",
            ["patch"] = "@@ -808,2 +808,4 @@\n+        if not self.content:\n+            return None\n"
        });
        scenario.Executor.Execute(patchCall);
        scenario.Executor.Execute(new FunctionCallContent("call_t1", "run_tests", new Dictionary<string, object?>()));

        Assert.True(scenario.Executor.PatchApplied);
        Assert.True(scenario.Executor.VerificationTestsPassed);
        Assert.True(scenario.Executor.IsTaskSuccessful);

        // 2. Call OnScenarioStart as runner does between model configs
        scenario.OnScenarioStart();

        // 3. State must be completely reset!
        Assert.False(scenario.Executor.PatchApplied);
        Assert.False(scenario.Executor.VerificationTestsPassed);
        Assert.False(scenario.Executor.IsTaskSuccessful);
    }
}
