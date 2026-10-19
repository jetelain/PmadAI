using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.AI;
using Pmad.AI.Compaction;

namespace Pmad.AI.MicroBenchmarks;

[Config(typeof(FastInProcessConfig))]
public class CompactionBenchmark
{
    private ThinkingTraceCompactor _thinkingCompactorInPlace = null!;
    private ThinkingTraceCompactor _thinkingCompactorCloning = null!;
    private ToolResultCompactor _toolCompactorInPlace = null!;
    private ToolResultCompactor _toolCompactorCloning = null!;

    private List<ChatMessage> _smallHistory = null!;
    private List<ChatMessage> _largeThinkingHistory = null!;
    private List<ChatMessage> _largeToolResultHistory = null!;

    [GlobalSetup]
    public void Setup()
    {
        _thinkingCompactorInPlace = new ThinkingTraceCompactor(new ThinkingTraceCompactionOptions
        {
            KeepVerbatimCount = 5,
            MutateInPlace = true,
            FallbackPrefixLength = 200,
            FallbackSuffixLength = 50,
            FallbackCharacterLimit = 300
        });

        _thinkingCompactorCloning = new ThinkingTraceCompactor(new ThinkingTraceCompactionOptions
        {
            KeepVerbatimCount = 5,
            MutateInPlace = false,
            FallbackPrefixLength = 200,
            FallbackSuffixLength = 50,
            FallbackCharacterLimit = 300
        });

        _toolCompactorInPlace = new ToolResultCompactor(new ToolResultCompactionOptions
        {
            KeepVerbatimCount = 5,
            MutateInPlace = true,
            FallbackPrefixLength = 500,
            FallbackSuffixLength = 200,
            FallbackCharacterLimit = 800
        });

        _toolCompactorCloning = new ToolResultCompactor(new ToolResultCompactionOptions
        {
            KeepVerbatimCount = 5,
            MutateInPlace = false,
            FallbackPrefixLength = 500,
            FallbackSuffixLength = 200,
            FallbackCharacterLimit = 800
        });

        _smallHistory = CreateHistory(reasoningTurns: 3, toolTurns: 3, toolResultSize: 500);
        _largeThinkingHistory = CreateHistory(reasoningTurns: 20, toolTurns: 5, toolResultSize: 500);
        _largeToolResultHistory = CreateHistory(reasoningTurns: 5, toolTurns: 20, toolResultSize: 8000);
    }

    private static List<ChatMessage> CreateHistory(int reasoningTurns, int toolTurns, int toolResultSize)
    {
        var list = new List<ChatMessage>();
        list.Add(new ChatMessage(ChatRole.System, "You are a coding assistant."));

        int max = Math.Max(reasoningTurns, toolTurns);
        for (int i = 0; i < max; i++)
        {
            list.Add(new ChatMessage(ChatRole.User, $"User step request #{i}"));

            if (i < reasoningTurns)
            {
                var assistantMsg = new ChatMessage(ChatRole.Assistant, $"Assistant response #{i}");
                assistantMsg.Contents.Add(new TextReasoningContent($"<thought>Thinking step {i}: analyzing the code structure in depth and planning next edits carefully...</thought>"));
                list.Add(assistantMsg);
            }

            if (i < toolTurns)
            {
                var toolResultPayload = new string('X', toolResultSize);
                var toolMsg = new ChatMessage(ChatRole.Tool, toolResultPayload);
                toolMsg.Contents.Add(new FunctionResultContent($"call_{i}", toolResultPayload));
                list.Add(toolMsg);
            }
        }

        return list;
    }

    private List<ChatMessage> CloneMessages(List<ChatMessage> source)
    {
        var copy = new List<ChatMessage>(source.Count);
        foreach (var m in source)
        {
            var msg = new ChatMessage(m.Role, m.Text);
            foreach (var c in m.Contents)
            {
                msg.Contents.Add(c);
            }
            copy.Add(msg);
        }
        return copy;
    }

    [Benchmark(Baseline = true)]
    public IList<ChatMessage> Thinking_SmallHistory_NoOp()
    {
        return _thinkingCompactorInPlace.Compact(_smallHistory);
    }

    [Benchmark]
    public IList<ChatMessage> Thinking_LargeHistory_InPlace()
    {
        var copy = CloneMessages(_largeThinkingHistory);
        return _thinkingCompactorInPlace.Compact(copy);
    }

    [Benchmark]
    public IList<ChatMessage> Thinking_LargeHistory_Cloning()
    {
        return _thinkingCompactorCloning.Compact(_largeThinkingHistory);
    }

    [Benchmark]
    public IList<ChatMessage> ToolResult_SmallHistory_NoOp()
    {
        return _toolCompactorInPlace.Compact(_smallHistory);
    }

    [Benchmark]
    public IList<ChatMessage> ToolResult_LargeHistory_InPlace()
    {
        var copy = CloneMessages(_largeToolResultHistory);
        return _toolCompactorInPlace.Compact(copy);
    }

    [Benchmark]
    public IList<ChatMessage> ToolResult_LargeHistory_Cloning()
    {
        return _toolCompactorCloning.Compact(_largeToolResultHistory);
    }
}
