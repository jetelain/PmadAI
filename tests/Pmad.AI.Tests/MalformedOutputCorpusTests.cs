using Microsoft.Extensions.AI;
using Pmad.AI.Healing;

namespace Pmad.AI.Tests;

public class MalformedOutputCorpusTests
{
    public static IEnumerable<object[]> Get100MalformedCases()
    {
        // Category 1: 30 cases with markdown fences inside function argument values
        for (int i = 0; i < 30; i++)
        {
            var raw = $"```json\n{{\"index\": {i}, \"code\": \"var x = {i};\"}}\n```";
            yield return new object[]
            {
                $"FenceCase_{i}",
                new ChatMessage(ChatRole.Assistant, [
                    new FunctionCallContent($"call_{i}", "apply_patch", new Dictionary<string, object?> { ["input"] = raw })
                ]),
                "apply_patch"
            };
        }

        // Category 2: 30 cases emitting pseudo-tags in text (<tool_call> or <function_call>)
        for (int i = 0; i < 30; i++)
        {
            var tag = i % 2 == 0 ? "tool_call" : "function_call";
            var text = "Thinking step " + i + "...\n<" + tag + ">{\"name\": \"tool_" + i + "\", \"arguments\": {\"arg\": \"val_" + i + "\"}}</" + tag + ">\nDone.";
            yield return new object[]
            {
                $"PseudoTagCase_{i}",
                new ChatMessage(ChatRole.Assistant, text),
                $"tool_{i}"
            };
        }

        // Category 3: 20 cases with trailing commas in JSON argument payloads
        for (int i = 0; i < 20; i++)
        {
            var jsonWithTrailingCommas = "{\"param_" + i + "\": \"value\", \"array\": [1, 2, ], \"nested\": {\"id\": " + i + ", }, }";
            var text = "<tool_call>{\"name\": \"call_trailing_" + i + "\", \"arguments\": " + jsonWithTrailingCommas + "}</tool_call>";
            yield return new object[]
            {
                $"TrailingCommaCase_{i}",
                new ChatMessage(ChatRole.Assistant, text),
                $"call_trailing_{i}"
            };
        }

        // Category 4: 20 cases with unescaped literal newlines inside JSON string parameters
        for (int i = 0; i < 20; i++)
        {
            var jsonWithLiteralNewlines = "{\"query\": \"line1\nline2\r\nline3\", \"test_id\": " + i + "}";
            var text = "<tool_call>{\"name\": \"call_newline_" + i + "\", \"arguments\": " + jsonWithLiteralNewlines + "}</tool_call>";
            yield return new object[]
            {
                $"UnescapedNewlineCase_{i}",
                new ChatMessage(ChatRole.Assistant, text),
                $"call_newline_{i}"
            };
        }
    }

    [Theory]
    [MemberData(nameof(Get100MalformedCases))]
    public void MalformedCorpus_All100CasesRecoverSuccessfully(string caseName, ChatMessage rawMessage, string expectedToolName)
    {
        var healed = ToolCallHealingTransform.HealChatMessage(rawMessage);
        var fcc = healed.SelectMany(m => m.Contents).OfType<FunctionCallContent>().FirstOrDefault();

        Assert.NotNull(fcc);
        Assert.Equal(expectedToolName, fcc.Name);
        Assert.NotNull(fcc.Arguments);
        Assert.True(fcc.Arguments.Count > 0, $"Expected parsed arguments for case {caseName}");
    }
}
