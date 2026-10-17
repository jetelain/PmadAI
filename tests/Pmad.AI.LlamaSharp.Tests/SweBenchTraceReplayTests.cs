using Microsoft.Extensions.AI;
using Pmad.AI.Benchmark.Configuration;
using Pmad.AI.Benchmark.Engine;
using Pmad.AI.Benchmark.Scenarios;
using Pmad.AI.Benchmark.Scenarios.SweBench;
using Pmad.AI.Compaction;
using Pmad.AI.Guardrails;

namespace Pmad.AI.LlamaSharp.Tests;

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
}
