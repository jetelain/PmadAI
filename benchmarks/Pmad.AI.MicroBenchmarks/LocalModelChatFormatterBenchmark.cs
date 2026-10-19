using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.AI;
using Pmad.AI.LlamaSharp;

namespace Pmad.AI.MicroBenchmarks;

[Config(typeof(FastInProcessConfig))]
public class LocalModelChatFormatterBenchmark
{
    private List<ChatMessage> _conversation = null!;
    private List<AITool> _tools = null!;
    private AIFunction _readFileFunction = null!;

    private const string AssistantOutputWithThoughtAndTool = """
        <think>
        The user wants to find all occurrences of 'SafeTokenize' in the repository.
        I will invoke the grep tool to search for the symbol across the codebase.
        </think>
        <tool_call>
        {"name": "grep_search", "arguments": {"query": "SafeTokenize", "searchPath": "src/"}}
        </tool_call>
        """;

    private const string AssistantPlainOutput = """
        Based on my review of the changes, the tokenization chunking fix resolves the MSVC regex recursion issue.
        Performance has improved and all 95 tests pass.
        """;

    [GlobalSetup]
    public void Setup()
    {
        _readFileFunction = AIFunctionFactory.Create(
            (string path, int? startLine, int? endLine) => "file content",
            "read_file",
            "Reads the contents of a file within the workspace.");

        var grepFunction = AIFunctionFactory.Create(
            (string query, string? searchPath) => "match results",
            "grep_search",
            "Searches for regex or exact string matches across files.");

        var editFunction = AIFunctionFactory.Create(
            (string targetFile, string instruction, string replacementContent) => "edit success",
            "edit_file",
            "Edits an existing file in the workspace.");

        _tools = [_readFileFunction, grepFunction, editFunction];

        _conversation =
        [
            new ChatMessage(ChatRole.System, "You are a helpful software engineering assistant."),
            new ChatMessage(ChatRole.User, "Can you check the current implementation of SafeInteractiveExecutor?"),
            new ChatMessage(ChatRole.Assistant, [
                new TextReasoningContent("Checking file location in src/Pmad.AI.LlamaSharp."),
                new FunctionCallContent("call_1", "read_file", new Dictionary<string, object?>
                {
                    ["path"] = "src/Pmad.AI.LlamaSharp/SafeInteractiveExecutor.cs",
                    ["startLine"] = 1,
                    ["endLine"] = 50
                })
            ]),
            new ChatMessage(ChatRole.Tool, [
                new FunctionResultContent("call_1", "// Copyright (c) Julien Etelain...\npublic class SafeInteractiveExecutor { }")
            ]),
            new ChatMessage(ChatRole.Assistant, "The class is defined and contains the safe tokenization logic."),
            new ChatMessage(ChatRole.User, "Great, now please check the tests for it.")
        ];
    }

    [Benchmark(Baseline = true)]
    public string FormatPrompt_ChatML()
    {
        return LocalModelChatFormatter.FormatPrompt(_conversation, "chatml", _tools);
    }

    [Benchmark]
    public string FormatPrompt_Gemma()
    {
        return LocalModelChatFormatter.FormatPrompt(_conversation, "gemma4", _tools);
    }

    [Benchmark]
    public string FormatPrompt_Phi3()
    {
        return LocalModelChatFormatter.FormatPrompt(_conversation, "phi-3", _tools);
    }

    [Benchmark]
    public ChatMessage ParseAssistant_ReasoningAndToolCall()
    {
        return LocalModelChatFormatter.ParseAssistantResponse(AssistantOutputWithThoughtAndTool);
    }

    [Benchmark]
    public ChatMessage ParseAssistant_PlainContent()
    {
        return LocalModelChatFormatter.ParseAssistantResponse(AssistantPlainOutput);
    }

    [Benchmark]
    public string AotJson_SerializeFunctionDefinition()
    {
        return AotJsonUtilities.SerializeFunctionDefinition(_readFileFunction);
    }
}
