# Pmad.AI

[![NuGet](https://img.shields.io/nuget/v/Pmad.AI.svg)](https://www.nuget.org/packages/Pmad.AI)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](../../LICENSE)

`Pmad.AI` provides lightweight, Native AOT-ready AI utilities, protocol transformers, and token heuristics for .NET applications, AI gateways, proxies, and agent pipelines.

Originally developed as core components of [OllamaRouter](https://github.com/jetelain/OllamaRouter), these utilities are extracted into an independent, zero-heavy-dependency library suitable for any .NET AI stack.

---

## Features

- **Protocol-Level Tool Call Healing (`ToolCallHealingTransform`)**:
  - Promotes pseudo-tags in assistant content (`<tool_call>{...}</tool_call>` or ReAct `Action: ... \n Action Input: {...}`) into structured tool calls.
  - Strips leading and trailing markdown code fences (````json ... ```` or ```` ... ````) inside function arguments.
  - Injects missing tool call IDs (`call_xxxx`) and normalizes call types.
  - Repairs trailing commas in JSON payloads (`{"arg": "val",}`).
  - Repairs unescaped control characters (literal newlines `\n`, carriage returns `\r`, tabs `\t`) inside JSON string literals.
  - Compatible with both **OpenAI wire format** (`choices[].message`, `choices[].delta`) and **Ollama native format** (`message`).
  - Source-generated regexes and trim-safe for **Native AOT**.

- **Zero-Allocation Heuristic Token Estimator (`HeuristicTokenEstimator`)**:
  - Approximates token counts using character, word, punctuation, and Unicode heuristics without loading tokenizer vocabulary files.
  - Zero heap allocation (`ReadOnlySpan<char>` API).
  - ~100x faster than full BPE tokenization.
  - Perfect for fast prompt budgeting, sliding context truncation, and rate-limiting middleware.

---

## Installation

```bash
dotnet add package Pmad.AI
```

---

## Usage

### 1. Tool Call Healing (`ToolCallHealingTransform`)

Local and open-weights models (Qwen, Gemma, Llama, Mistral, Hermes) frequently produce malformed tool calls when running behind OpenAI-compatible or Ollama-compatible endpoints. `ToolCallHealingTransform` repairs them transparently:

#### Healing a Complete Chat Completion JSON Response / Chunk

```csharp
using System.Text.Json.Nodes;
using Pmad.AI;

// 1. Heal an in-memory JsonNode (OpenAI or Ollama format)
JsonNode responseNode = JsonNode.Parse(rawApiResponseJson)!;
ToolCallHealingTransform.HealChatCompletionResponse(responseNode, logger);

// 2. Or heal raw JSON text directly
string healedJson = ToolCallHealingTransform.HealResponseText(rawApiResponseJson, logger);
```

#### Healing in-memory Microsoft.Extensions.AI Objects (`ChatMessage` / `ChatResponse`)

For in-process LLM adapters (such as `Pmad.AI.LlamaSharp`) or custom client pipelines, you can heal strongly-typed `ChatMessage` or `ChatResponse` objects directly:

```csharp
using Microsoft.Extensions.AI;
using Pmad.AI;

// 1. Promotes pseudo-tags (<tool_call> or ReAct Action:) in TextContent to FunctionCallContent,
//    generates missing call IDs, and sanitizes/parses argument payloads
ToolCallHealingTransform.HealChatMessage(assistantMessage, logger);

// 2. Or heal an entire ChatResponse (also sets FinishReason to ToolCalls if tool calls are present)
ToolCallHealingTransform.HealChatResponse(chatResponse, logger);
```

#### Sanitizing & Parsing Raw Function Arguments or JSON

If you are parsing function arguments or payloads directly in custom clients or formatting pipelines:

```csharp
using Pmad.AI;

// Strips markdown fences, removes trailing commas, escapes unescaped control characters
string rawArgs = "```json\n{\n  \"query\": \"C# 10 features\",\n  \"limit\": 10,\n}\n```";
string sanitized = ToolCallHealingTransform.SanitizeJson(rawArgs);

// Or parse directly to a trim-safe Dictionary without reflection
IDictionary<string, object?> args = ToolCallHealingTransform.ParseArgumentsJson(rawArgs);
```

---

### 2. Heuristic Token Estimation (`HeuristicTokenEstimator`)

When budgeting prompt context or enforcing token limits, loading full BPE tokenizers into memory consumes significant RAM and initialization time. `HeuristicTokenEstimator` provides a fast blended approximation:

```csharp
using Pmad.AI;

// Estimate tokens for a string span using the standard Default instance (zero heap allocation)
ReadOnlySpan<char> promptSpan = prompt.AsSpan();
int estimatedTokens = HeuristicTokenEstimator.Default.Estimate(promptSpan);

// Or estimate across a list of message strings
var messageTexts = new List<string> { systemPrompt, userMessage };
int totalEstimated = HeuristicTokenEstimator.Default.Estimate(messageTexts);

// Or customize factors for your model/tokenizer
var customEstimator = new HeuristicTokenEstimator(
    wordFactor: 1.3,
    symbolFactor: 0.9,
    nonAsciiFactor: 1.1,
    charFloorFactor: 3.5
);
int customTokens = customEstimator.Estimate(promptSpan);

Console.WriteLine($"Estimated token count: {totalEstimated}");
```

---

## Target Frameworks & AOT

- Target: `.NET 10.0`
- Trimmable and Native AOT compatible (`IsAotCompatible = true`).
- Dependencies: Only `Microsoft.Extensions.AI` and `Microsoft.Extensions.Logging.Abstractions`.

---

## License

This project is licensed under the [MIT License](../../LICENSE).
