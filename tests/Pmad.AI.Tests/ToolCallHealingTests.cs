using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Moq;

namespace Pmad.AI.Tests;

public class ToolCallHealingTests
{
    [Fact]
    public void HealChatCompletionResponse_OpenAiFormat_StripsMarkdownFencesFromArguments()
    {
        var inputJson = """
        {
            "id": "chatcmpl-123",
            "choices": [
                {
                    "message": {
                        "role": "assistant",
                        "content": null,
                        "tool_calls": [
                            {
                                "id": "call_abc123",
                                "type": "function",
                                "function": {
                                    "name": "edit_file",
                                    "arguments": "```json\n{\n  \"path\": \"src/main.cs\"\n}\n```"
                                }
                            }
                        ]
                    }
                }
            ]
        }
        """;

        var root = JsonNode.Parse(inputJson)!;
        ToolCallHealingTransform.HealChatCompletionResponse(root);

        var args = root["choices"]?[0]?["message"]?["tool_calls"]?[0]?["function"]?["arguments"]?.GetValue<string>();
        Assert.NotNull(args);
        Assert.DoesNotContain("```", args);
        Assert.Equal("{\n  \"path\": \"src/main.cs\"\n}", args);
    }

    [Fact]
    public void HealChatCompletionResponse_OllamaFormat_StripsMarkdownFencesFromArguments()
    {
        var inputJson = """
        {
            "model": "qwen2.5-coder:32b",
            "message": {
                "role": "assistant",
                "content": "",
                "tool_calls": [
                    {
                        "function": {
                            "name": "read_file",
                            "arguments": "```json\n{\"path\":\"a.cs\"}\n```"
                        }
                    }
                ]
            }
        }
        """;

        var root = JsonNode.Parse(inputJson)!;
        ToolCallHealingTransform.HealChatCompletionResponse(root);

        var args = root["message"]?["tool_calls"]?[0]?["function"]?["arguments"]?.GetValue<string>();
        Assert.NotNull(args);
        Assert.Equal("{\"path\":\"a.cs\"}", args);
    }

    [Fact]
    public void HealChatCompletionResponse_PseudoToolCallInContent_PromotesToStructuredToolCalls()
    {
        var inputJson = """
        {
            "choices": [
                {
                    "message": {
                        "role": "assistant",
                        "content": "I will inspect the file now.\n<tool_call>{\"name\": \"read_file\", \"arguments\": {\"path\": \"src/main.cs\"}}</tool_call>\nPlease wait.",
                        "tool_calls": []
                    }
                }
            ]
        }
        """;

        var root = JsonNode.Parse(inputJson)!;
        ToolCallHealingTransform.HealChatCompletionResponse(root);

        var message = root["choices"]?[0]?["message"] as JsonObject;
        Assert.NotNull(message);

        var toolCalls = message["tool_calls"] as JsonArray;
        Assert.NotNull(toolCalls);
        Assert.Single(toolCalls);

        var call = toolCalls[0] as JsonObject;
        Assert.NotNull(call);
        Assert.StartsWith("call_", call["id"]?.GetValue<string>());
        Assert.Equal("function", call["type"]?.GetValue<string>());

        var func = call["function"] as JsonObject;
        Assert.NotNull(func);
        Assert.Equal("read_file", func["name"]?.GetValue<string>());
        Assert.Equal("{\"path\":\"src/main.cs\"}", func["arguments"]?.GetValue<string>());

        // Content must be stripped of the <tool_call> tag
        var remainingContent = message["content"]?.GetValue<string>();
        Assert.NotNull(remainingContent);
        Assert.DoesNotContain("<tool_call>", remainingContent);
        Assert.DoesNotContain("</tool_call>", remainingContent);
        Assert.Contains("I will inspect the file now.", remainingContent);
        Assert.Contains("Please wait.", remainingContent);
    }

    [Fact]
    public void HealChatCompletionResponse_MissingToolCallId_GeneratesValidId()
    {
        var inputJson = """
        {
            "choices": [
                {
                    "message": {
                        "role": "assistant",
                        "content": "",
                        "tool_calls": [
                            {
                                "id": "",
                                "type": "function",
                                "function": {
                                    "name": "execute_command",
                                    "arguments": "{\"command\":\"dir\"}"
                                }
                            }
                        ]
                    }
                }
            ]
        }
        """;

        var root = JsonNode.Parse(inputJson)!;
        ToolCallHealingTransform.HealChatCompletionResponse(root);

        var id = root["choices"]?[0]?["message"]?["tool_calls"]?[0]?["id"]?.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.StartsWith("call_", id);
        Assert.Equal(12, id.Length);
    }

    [Fact]
    public void SanitizeArgumentsJson_TrailingCommas_RepairsSyntax()
    {
        var inputWithTrailingCommas = """
        {
            "items": ["one", "two",],
            "config": {
                "flag": true,
            },
        }
        """;

        var sanitized = ToolCallHealingTransform.SanitizeArgumentsJson(inputWithTrailingCommas);
        Assert.DoesNotContain(",]", sanitized);
        Assert.DoesNotContain(",}", sanitized);

        // Must now be parseable as valid JSON
        var parsed = JsonNode.Parse(sanitized);
        Assert.NotNull(parsed);
    }

    [Fact]
    public void SanitizeArgumentsJson_EmptyOrWhitespace_ReturnsEmptyObject()
    {
        Assert.Equal("{}", ToolCallHealingTransform.SanitizeArgumentsJson(""));
        Assert.Equal("{}", ToolCallHealingTransform.SanitizeArgumentsJson("   \n\t"));
    }

    [Fact]
    public void SanitizeArgumentsJson_FencesWithoutNewline_StripsFencesCleanly()
    {
        var sanitized = ToolCallHealingTransform.SanitizeArgumentsJson("```json{\"path\":\"a.cs\"}```");
        Assert.Equal("{\"path\":\"a.cs\"}", sanitized);
    }

    [Fact]
    public void HealChatCompletionResponse_LogsRepairOperations()
    {
        var inputJson = """
        {
            "choices": [
                {
                    "message": {
                        "role": "assistant",
                        "content": "<tool_call>{\"name\":\"read\",\"arguments\":\"```json\\n{\\\"path\\\":\\\"a.cs\\\",}\\n```\"}</tool_call>"
                    }
                }
            ]
        }
        """;

        var loggerMock = new Mock<ILogger>();
        var root = JsonNode.Parse(inputJson)!;
        ToolCallHealingTransform.HealChatCompletionResponse(root, loggerMock.Object);

        // Verify logger was invoked for repair operations
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Promoted pseudo-tool-call") || v.ToString()!.Contains("Stripped markdown")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public void SanitizeJson_StripsFencesAndTrailingCommas()
    {
        var raw = "```json\n{\"city\": \"Tokyo\", \"temp\": 25,}\n```";
        var sanitized = ToolCallHealingTransform.SanitizeJson(raw);

        Assert.Equal("{\"city\": \"Tokyo\", \"temp\": 25}", sanitized);
    }

    [Fact]
    public void ParseArgumentsJson_ValidAndRepairedJson_ReturnsExpectedDictionary()
    {
        var raw = "```json\n{\"city\": \"Paris\", \"count\": 42, \"active\": true,}\n```";
        var dict = ToolCallHealingTransform.ParseArgumentsJson(raw);

        Assert.Equal(3, dict.Count);
        Assert.Equal("Paris", dict["city"]);
        Assert.Equal(42, dict["count"]);
        Assert.Equal(true, dict["active"]);
    }

    [Fact]
    public void TryExtractToolCalls_XmlToolCall_ExtractsAndStripsFromText()
    {
        var text = "Here is the forecast:\n<tool_call>{\"name\":\"get_weather\",\"arguments\":{\"city\":\"Berlin\"}}</tool_call>\nHope that helps!";
        var success = ToolCallHealingTransform.TryExtractToolCalls(ref text, out var toolCalls);

        Assert.True(success);
        Assert.Single(toolCalls);
        Assert.Equal("get_weather", toolCalls[0].Name);
        Assert.Equal("Berlin", toolCalls[0].Arguments?["city"]);
        Assert.DoesNotContain("<tool_call>", text);
        Assert.DoesNotContain("</tool_call>", text);
        Assert.Contains("Here is the forecast:", text);
        Assert.Contains("Hope that helps!", text);
    }

    [Fact]
    public void TryExtractToolCalls_ReActActionToolCall_ExtractsAndStripsFromText()
    {
        var text = "Action: search_database\nAction Input: {\"query\": \"SELECT * FROM users\",}\nI am waiting for query results.";
        var success = ToolCallHealingTransform.TryExtractToolCalls(ref text, out var toolCalls);

        Assert.True(success);
        Assert.Single(toolCalls);
        Assert.Equal("search_database", toolCalls[0].Name);
        Assert.Equal("SELECT * FROM users", toolCalls[0].Arguments?["query"]);
        Assert.DoesNotContain("Action:", text);
        Assert.Contains("I am waiting for query results.", text);
    }

    [Fact]
    public void HealChatMessage_WithEmbeddedPseudoToolCall_PromotesToFunctionCallContent()
    {
        var message = new Microsoft.Extensions.AI.ChatMessage(
            Microsoft.Extensions.AI.ChatRole.Assistant,
            "Let me check:\n<tool_call>{\"name\":\"calc\",\"arguments\":{\"expr\":\"2+2\"}}</tool_call>");

        ToolCallHealingTransform.HealChatMessage(message);

        var calls = message.Contents.OfType<Microsoft.Extensions.AI.FunctionCallContent>().ToList();
        Assert.Single(calls);
        Assert.Equal("calc", calls[0].Name);
        Assert.Equal("2+2", calls[0].Arguments?["expr"]);
        Assert.StartsWith("call_", calls[0].CallId);

        var texts = message.Contents.OfType<Microsoft.Extensions.AI.TextContent>().ToList();
        Assert.Single(texts);
        Assert.Equal("Let me check:", texts[0].Text);
    }

    [Fact]
    public void HealChatMessage_ExistingToolCallWithMissingIdAndInputJson_RepairsIdAndParsesArguments()
    {
        var message = new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, [
            new Microsoft.Extensions.AI.FunctionCallContent(
                callId: "",
                name: "lookup",
                arguments: new Dictionary<string, object?> { ["input"] = "```json\n{\"id\": 123,}\n```" })
        ]);

        ToolCallHealingTransform.HealChatMessage(message);

        var call = message.Contents.OfType<Microsoft.Extensions.AI.FunctionCallContent>().Single();
        Assert.False(string.IsNullOrWhiteSpace(call.CallId));
        Assert.StartsWith("call_", call.CallId);
        Assert.Equal(123, call.Arguments?["id"]);
    }

    [Fact]
    public void HealChatResponse_WithPromotedToolCalls_SetsFinishReasonToToolCalls()
    {
        var msg = new Microsoft.Extensions.AI.ChatMessage(
            Microsoft.Extensions.AI.ChatRole.Assistant,
            "<tool_call>{\"name\":\"ping\",\"arguments\":{}}</tool_call>");

        var response = new Microsoft.Extensions.AI.ChatResponse(new List<Microsoft.Extensions.AI.ChatMessage> { msg })
        {
            FinishReason = Microsoft.Extensions.AI.ChatFinishReason.Stop
        };

        ToolCallHealingTransform.HealChatResponse(response);

        Assert.Equal(Microsoft.Extensions.AI.ChatFinishReason.ToolCalls, response.FinishReason);
        Assert.Single(msg.Contents.OfType<Microsoft.Extensions.AI.FunctionCallContent>());
    }
}
