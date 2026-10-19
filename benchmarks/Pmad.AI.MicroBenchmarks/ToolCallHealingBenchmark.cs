using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.AI;
using Pmad.AI.Healing;

namespace Pmad.AI.MicroBenchmarks;

[Config(typeof(FastInProcessConfig))]
public class ToolCallHealingBenchmark
{
    private const string CleanJson = """{"path":"src/Core/Service.cs","startLine":1,"endLine":100,"filter":"errors"}""";

    private const string FencedJson = """
        ```json
        {
            "path": "src/Core/Service.cs",
            "startLine": 1,
            "endLine": 100,
            "filter": "errors"
        }
        ```
        """;

    private const string TrailingCommaJson = """
        {
            "path": "src/Core/Service.cs",
            "startLine": 1,
            "endLine": 100,
            "filter": "errors",
        }
        """;

    private const string UnescapedControlCharsJson = "{\"code\":\"public class Foo {\n    public void Bar() {\r\n        // do work\t\n    }\n}\"}";

    private const string AllCombinedJson = "```json\n{\n    \"code\": \"public class Foo {\n    public void Bar() {\n        return 42;\n    }\n}\",\n    \"count\": 10,\n}\n```";

    private const string AssistantContentWithToolCall = """
        I will inspect the file to understand the class definition.
        <tool_call>
        {"name": "read_file", "arguments": {"path": "src/Pmad.AI/Service.cs", "startLine": 1, "endLine": 80}}
        </tool_call>
        After inspecting, I will edit the implementation.
        """;

    private const string AssistantContentWithReActAction = """
        Action: read_file
        Action Input: {"path": "src/Pmad.AI/Service.cs", "startLine": 1, "endLine": 80}
        """;

    private const string ChatCompletionResponseJson = """
        {
            "id": "chatcmpl-12345",
            "choices": [
                {
                    "index": 0,
                    "message": {
                        "role": "assistant",
                        "content": null,
                        "tool_calls": [
                            {
                                "id": "call_abc123",
                                "type": "function",
                                "function": {
                                    "name": "edit_file",
                                    "arguments": "```json\n{\n  \"targetFile\": \"src/Pmad.AI/Foo.cs\",\n  \"content\": \"code\",\n}\n```"
                                }
                            }
                        ]
                    }
                }
            ]
        }
        """;

    [Benchmark(Baseline = true)]
    public string Sanitize_CleanJson() => ToolCallHealingTransform.SanitizeJson(CleanJson);

    [Benchmark]
    public string Sanitize_MarkdownFencedJson() => ToolCallHealingTransform.SanitizeJson(FencedJson);

    [Benchmark]
    public string Sanitize_TrailingCommaJson() => ToolCallHealingTransform.SanitizeJson(TrailingCommaJson);

    [Benchmark]
    public string Sanitize_UnescapedControlCharsJson() => ToolCallHealingTransform.SanitizeJson(UnescapedControlCharsJson);

    [Benchmark]
    public string Sanitize_AllCombinedJson() => ToolCallHealingTransform.SanitizeJson(AllCombinedJson);

    [Benchmark]
    public IDictionary<string, object?> ParseArgumentsJson_Clean() => ToolCallHealingTransform.ParseArgumentsJson(CleanJson);

    [Benchmark]
    public IDictionary<string, object?> ParseArgumentsJson_Malformed() => ToolCallHealingTransform.ParseArgumentsJson(AllCombinedJson);

    [Benchmark]
    public bool ExtractPseudoToolCalls_XmlTags()
    {
        var content = AssistantContentWithToolCall;
        return ToolCallHealingTransform.TryExtractToolCalls(ref content, out _, out _, out _);
    }

    [Benchmark]
    public bool ExtractPseudoToolCalls_ReActAction()
    {
        var content = AssistantContentWithReActAction;
        return ToolCallHealingTransform.TryExtractToolCalls(ref content, out _, out _, out _);
    }

    [Benchmark]
    public string HealChatCompletionResponse_OpenAiFormat() => ToolCallHealingTransform.HealResponseText(ChatCompletionResponseJson);
}
