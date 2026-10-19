# Pmad.AI.LlamaSharp

[![NuGet](https://img.shields.io/nuget/v/Pmad.AI.LlamaSharp.svg)](https://www.nuget.org/packages/Pmad.AI.LlamaSharp)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](../../LICENSE)

An in-process, high-performance GGUF inference adapter implementing [`Microsoft.Extensions.AI.IChatClient`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.ichatclient) powered by [LLamaSharp](https://github.com/SciSharp/LLamaSharp) and [llama.cpp](https://github.com/ggerganov/llama.cpp).

Designed for seamless integration into modern .NET applications with support for:
- Standard `IChatClient` non-streaming and token-by-token streaming APIs
- Multimodal vision inference via companion `mmproj` projectors
- Function / tool calling with `AIFunction` and customizable parameter normalization
- Open-weights prompt templates (ChatML, Gemma 4, Phi-3) and `<think>` / `<thought>` reasoning trace extraction
- Built-in curated presets and automated model downloads via `ModelDownloader`
- Native AOT trimming and single-file deployment compatibility

---

## Installation

`Pmad.AI.LlamaSharp` uses the standard **two-package pattern** for LLamaSharp. The core library contains managed code and contracts, while native llama.cpp runtimes are distributed in backend packages.

1. Install the core library:
   ```bash
   dotnet add package Pmad.AI.LlamaSharp
   ```

2. Add one of the native backend packages according to your execution environment and hardware:
   - **CPU (Headless CI, containers, universal fallback)**:
     ```bash
     dotnet add package LLamaSharp.Backend.Cpu --version 0.27.0
     ```
   - **Vulkan (Cross-platform AMD / Intel / NVIDIA GPU acceleration)**:
     ```bash
     dotnet add package LLamaSharp.Backend.Vulkan --version 0.27.0
     ```
   - **CUDA 12 (NVIDIA GPUs on Windows / Linux)**:
     ```bash
     dotnet add package LLamaSharp.Backend.Cuda12 --version 0.27.0
     ```

---

## Quickstart

### 1. Basic Completion

Create a client from a local `.gguf` file and execute completions:

```csharp
using Microsoft.Extensions.AI;
using Pmad.AI.LlamaSharp;

using var chatClient = LlamaSharpChatClient.CreateFromFile(
    modelPath: "models/qwen3.5-2b-ud-q4_k_xl.gguf",
    options: new LlamaSharpOptions
    {
        ContextSize = 49152, // 48k tokens context
        GpuLayerCount = 99   // Offload all layers to GPU (set 0 for CPU only)
    },
    promptFormat: "chatml");

var response = await chatClient.GetResponseAsync("Explain quantum computing in one concise sentence.");
Console.WriteLine(response.Text);
```

### 2. Token-by-Token Streaming

Stream tokens in real time using `GetStreamingResponseAsync`:

```csharp
using Microsoft.Extensions.AI;
using Pmad.AI.LlamaSharp;

using var chatClient = LlamaSharpChatClient.CreateFromFile("models/qwen3.5-2b-ud-q4_k_xl.gguf");

await foreach (var update in chatClient.GetStreamingResponseAsync("Write a haiku about programming."))
{
    Console.Write(update.Text);
}
Console.WriteLine();
```

### 3. Tool / Function Calling with `AIFunction`

Define functions and provide them to `ChatOptions.Tools`:

```csharp
using System.ComponentModel;
using Microsoft.Extensions.AI;
using Pmad.AI.LlamaSharp;

[Description("Fetches the current weather for a city.")]
static string GetWeather(string city) => $"The current weather in {city} is sunny, 22°C.";

var weatherTool = AIFunctionFactory.Create(GetWeather, "get_weather", "Fetches current weather for a city");

using var chatClient = LlamaSharpChatClient.CreateFromFile("models/qwen3.5-2b-ud-q4_k_xl.gguf");

var options = new ChatOptions
{
    Tools = [weatherTool]
};

var response = await chatClient.GetResponseAsync("What's the weather in Paris?", options);

// Inspect assistant contents (which may contain TextReasoningContent, FunctionCallContent, and TextContent)
foreach (var content in response.Messages[0].Contents)
{
    if (content is FunctionCallContent toolCall)
    {
        Console.WriteLine($"Function requested: {toolCall.Name}");
        Console.WriteLine($"Arguments: {System.Text.Json.JsonSerializer.Serialize(toolCall.Arguments)}");
    }
    else if (content is TextContent text)
    {
        Console.WriteLine(text.Text);
    }
}
```

#### Resilient Tool Calling Pipeline with `UseRecommendedSlmPipeline`

`LlamaSharpChatClient` serves as the raw, strict inference engine focusing on native model template formatting. For production SLM deployments, wrap `LlamaSharpChatClient` with `UseRecommendedSlmPipeline` (from `Pmad.AI`) to add automated tool call healing (repairing truncated arguments, malformed JSON, Markdown code blocks, and ReAct syntax), lenient tool dispatching, execution loop breakers, and deterministic context compaction:

```csharp
using Microsoft.Extensions.AI;
using Pmad.AI;
using Pmad.AI.LlamaSharp;

using var rawClient = LlamaSharpChatClient.CreateFromFile("models/qwen3.5-2b-ud-q4_k_xl.gguf");

// Wrap with recommended SLM resilience pipeline
var client = new ChatClientBuilder(rawClient)
    .UseRecommendedSlmPipeline()
    .Build();

var response = await client.GetResponseAsync("What's the weather in Paris?", options);
```

### 4. Multimodal Vision Inference

Pair a GGUF language model with a companion `mmproj` vision projector to analyze images:

```csharp
using Microsoft.Extensions.AI;
using Pmad.AI.LlamaSharp;

using var chatClient = LlamaSharpChatClient.CreateFromFile(
    modelPath: "models/qwen3.5-2b-ud-q4_k_xl.gguf",
    mmprojPath: "models/qwen3.5-2b-mmproj-bf16.gguf",
    options: new LlamaSharpOptions { GpuLayerCount = 99 },
    promptFormat: "chatml");

var imageBytes = await File.ReadAllBytesAsync("diagram.png");
var messages = new List<ChatMessage>
{
    new(ChatRole.User, [
        new DataContent(imageBytes, "image/png"),
        new TextContent("Describe what is shown in this image.")
    ])
};

var response = await chatClient.GetResponseAsync(messages);
Console.WriteLine(response.Text);
```

### 5. Auto-Downloading Presets with `ModelDownloader`

Download verified GGUF presets and projector artifacts on demand:

```csharp
using Pmad.AI.LlamaSharp;

// 1. Resolve preset by ID or alias (e.g. "qwen3.5-2b", "gemma-4-e2b")
var preset = LlamaModelPreset.Find("qwen3.5-2b") ?? LlamaModelPreset.Default;

// 2. Resolve cross-platform models cache directory (%LOCALAPPDATA%/pmad/models or ~/.pmad/models)
var modelsDir = LocalModelPathResolver.ResolveModelsDirectory(null);

// 3. Download weights and companion vision projector with progress updates
var progress = new Progress<ModelDownloadProgress>(p =>
{
    if (p.ProgressPercentage.HasValue)
        Console.WriteLine($"Downloading {p.FileName}: {p.ProgressPercentage:F1}%");
});

var downloader = new ModelDownloader(statusLogger: Console.WriteLine, progress: progress);
await downloader.DownloadModelAsync(
    preset,
    destinationDir: modelsDir,
    downloadVision: true);

// 4. Instantiate client with resolved local paths
using var chatClient = LlamaSharpChatClient.CreateFromFile(
    modelPath: preset.GetLocalPath(modelsDir),
    mmprojPath: preset.GetMmprojLocalPath(modelsDir),
    promptFormat: preset.PromptFormat);
```

### 6. Logging with Microsoft.Extensions.Logging

Logging is cleanly separated between process-wide native llama.cpp logs and per-chat client instance logs (preventing log mixing across concurrent chats):

```csharp
using Microsoft.Extensions.Logging;
using Pmad.AI.LlamaSharp;

// 1. Configure process-wide native llama.cpp logging
LlamaSharpChatClient.ConfigureNativeLogging(loggerFactory, verbose: false);

// 2. Configure isolated per-chat-client logging
var options = new LlamaSharpOptions
{
    Logger = loggerFactory.CreateLogger<LlamaSharpChatClient>(),
    VerboseLogging = false
};

using var chatClient = LlamaSharpChatClient.CreateFromFile("models/qwen3.5-2b-ud-q4_k_xl.gguf", options);
```

### 7. Prompt Formatting & Chat Dialects

Prompt formatting uses polymorphic dialects (`ChatDialect`) to cleanly handle model-specific template tokens, stop tokens, tool call wrapping, and generation prompts:

- `ChatMLDialect` (`ChatDialect.ChatML`): For Qwen 3.5, Hermes, and standard ChatML fine-tunes (`<|im_start|>`, `<|im_end|>`).
- `Phi3Dialect` (`ChatDialect.Phi3`): For Microsoft Phi-3 and Phi-3.5 models (`<|system|>`, `<|user|>`, `<|assistant|>`, `<|end|>`).
- `Gemma4Dialect` (`ChatDialect.Gemma4`): For Google Gemma 4 models (`<|turn|>system`, `<|turn|>model`, `<turn|>`).

```csharp
// Format prompts directly using strongly-typed dialect singletons
var promptChatml = ChatDialect.ChatML.FormatPrompt(messages, tools);
var promptPhi3   = ChatDialect.Phi3.FormatPrompt(messages, tools);
var promptGemma  = ChatDialect.Gemma4.FormatPrompt(messages, tools);

// Resolve dialect dynamically by format name
var dialect = ChatDialect.FromName("phi3"); // Returns Phi3Dialect.Instance

// Pass dialect directly to LlamaSharpChatClient
using var chatClient = LlamaSharpChatClient.CreateFromFile(
    modelPath: "models/phi-3-mini.gguf",
    dialect: ChatDialect.Phi3);

// Custom dialects can be added by subclassing ChatDialect
public sealed class CustomDialect : ChatDialect { ... }
```

### 8. Reasoning & Thinking Mode Configuration

For open-weights models supporting internal chain-of-thought (e.g. Qwen 3.5, Google Gemma 4), reasoning behavior can be toggled on/off and tuned for depth:

```csharp
using Microsoft.Extensions.AI;
using Pmad.AI.LlamaSharp;

// 1. Configure reasoning effort per-request via standard Microsoft.Extensions.AI ChatOptions
var chatOptions = new ChatOptions
{
    Reasoning = new ReasoningOptions
    {
        Effort = ReasoningEffort.Medium // None, Low, Medium, High, ExtraHigh
    }
};

var response = await chatClient.GetResponseAsync("Solve this logic puzzle.", chatOptions);

// 2. Or disable reasoning to get instant, low-latency direct responses
var fastOptions = new ChatOptions
{
    Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None }
};

// 3. You can also configure client-wide defaults via LlamaSharpOptions
var clientOptions = new LlamaSharpOptions
{
    EnableReasoning = true,
    ReasoningEffort = "medium" // "low", "medium", "high", "xhigh", or "none"
};
```

When reasoning is disabled (`enable = false` or `effort = none`), dialects pre-fill a closed `<think>\n\n</think>\n\n` block to instruct the model to skip internal deliberations. For models supporting tiered effort, effort instructions (`low`, `medium`, `xhigh`) are injected into the system prompt matching upstream chat templates.

---

## Model Presets

The library includes verified presets with tested prompt templates, reasoning capabilities, and context configurations:

| Preset ID | Aliases | Model Name | Format | Context Size | Approx Size | Vision | MTP Draft Model | Reasoning | Effort Tuning |
|:---|:---|:---|:---|:---:|:---:|:---:|:---:|:---:|:---:|
| `qwen3.5-2b` *(Default)* | `qwen3.5`, `qwen3.5-2b-gguf` | Qwen 3.5 2B (Q4_K_XL) | ChatML | 48k | ~1.2 GB | Yes (`mmproj-bf16`) | No | Yes | No (Binary toggle) |
| `qwen3.5-4b` | `qwen3.5-4b-gguf`, `qwen3.5-4b-q4` | Qwen 3.5 4B (Q4_K_XL) | ChatML | 48k | ~2.7 GB | Yes (`mmproj-bf16`) | No | Yes | No (Binary toggle) |
| `qwen3.5-9b` | `qwen-9b`, `qwen3.5-9b-gguf` | Qwen 3.5 9B (Q4_K_XL) | ChatML | 48k | ~5.6 GB | Yes (`mmproj-bf16`) | No | Yes | No (Binary toggle) |
| `gemma-4-e2b` | `gemma4-e2b`, `gemma-4-e2b-it` | Gemma 4 E2B (Q4_K_XL) | Gemma 4 | 48k | ~3.0 GB | Yes (`mmproj-bf16`) | Yes (`mtp-gemma-4-e2b-it`) | Yes | No (Binary toggle) |
| `gemma-4-e4b` | `gemma4-e4b`, `gemma-4-e4b-it` | Gemma 4 E4B (Q4_K_XL) | Gemma 4 | 48k | ~4.8 GB | Yes (`mmproj-bf16`) | Yes (`mtp-gemma-4-e4b-it`) | Yes | No (Binary toggle) |
| `gemma-4-12b` | `gemma4-12b`, `gemma-4-12b-it` | Gemma 4 12B (Q4_K_XL) | Gemma 4 | 48k | ~7.2 GB | Yes (`mmproj-bf16`) | Yes (`mtp-gemma-4-12b-it`) | Yes | No (Binary toggle) |

### Recommended Models by Target Platform

Based on tool calling, reasoning, and hardware acceleration evaluations:

| Target Platform | Recommended Model | Backend | Reasoning | Pass Rate | Memory Footprint | Key Benefit |
|---|---|---|---|---|---|---|
| **Tablet (Android)** | **Gemma 4 E2B (Q4_K_XL)** | Vulkan (or CPU) | **Enabled** | **100%** | ~1.8 – 1.9 GB RAM (~4.4 GB VRAM) | Fits mobile RAM limits, no token runaway, fast & reliable |
| **Desktop (Fast / Snappy)** | **Gemma 4 E4B (Q4_K_XL)** | Vulkan | **Enabled** | **100%** | 3.3 GB RAM / 6.1 GB VRAM | 100% pass rate across all tests, zero reasoning token bloat |
| **Desktop (Deep Reasoning)** | **Qwen 3.5 4B (Q4_K_XL)** | Vulkan | **Enabled** | **100%** | 3.0 GB RAM / 5.65 GB VRAM | Thorough chain-of-thought for complex multi-turn logic |
| **8 GB GPU (Coding Agent - Qwen)** | **Qwen 3.5 9B (Q4_K_XL)** | Vulkan / CUDA | **Enabled** | High | ~5.6 GB VRAM | High-accuracy coding agent capable of fitting comfortably within 8 GB VRAM |
| **8 GB GPU (Coding Agent - Gemma)** | **Gemma 4 12B (Q4_K_XL)** | Vulkan / CUDA | **Enabled** | High | ~7.2 GB VRAM | Advanced coding model with companion MTP speculative decoding drafter for 8 GB VRAM |

---

## License

This project is licensed under the [MIT License](../../LICENSE).
