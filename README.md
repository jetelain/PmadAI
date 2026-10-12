# Pmad.AI.LlamaSharp

[![CI](https://github.com/jetelain/PmadAI/actions/workflows/ci.yml/badge.svg)](https://github.com/jetelain/PmadAI/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Pmad.AI.LlamaSharp.svg)](https://www.nuget.org/packages/Pmad.AI.LlamaSharp)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

An in-process, high-performance GGUF inference adapter implementing [`Microsoft.Extensions.AI.IChatClient`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.ichatclient) powered by [LLamaSharp](https://github.com/SciSharp/LLamaSharp) and [llama.cpp](https://github.com/ggerganov/llama.cpp).

Designed for seamless integration into modern .NET applications with support for:
- Standard `IChatClient` non-streaming and token-by-token streaming APIs
- Multimodal vision inference via companion `mmproj` projectors
- Function / tool calling with `AIFunction` and customizable parameter normalization
- Open-weights prompt templates (ChatML, Gemma 4, Phi-3) and `<think>` / `<thought>` reasoning trace extraction
- Built-in curated presets and automated model downloads via `ModelDownloader`

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
foreach (var content in response.Choices[0].Message.Contents)
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
        new ImageContent(imageBytes, "image/png"),
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

---

## Model Presets

The library includes verified presets with tested prompt templates and context configurations:

| Preset ID | Aliases | Model Name | Format | Context Size | Approx Size | Vision |
|:---|:---|:---|:---|:---:|:---:|:---:|
| `qwen3.5-2b` *(Default)* | `qwen3.5`, `qwen3.5-2b-gguf` | Qwen 3.5 2B (Q4_K_XL) | ChatML | 48k | ~1.2 GB | Yes (`mmproj-bf16`) |
| `qwen3.5-4b` | `qwen3.5-4b-gguf`, `qwen3.5-4b-q4` | Qwen 3.5 4B (Q4_K_XL) | ChatML | 48k | ~2.7 GB | Yes (`mmproj-bf16`) |
| `gemma-4-e2b` | `gemma4-e2b`, `gemma-4-e2b-it` | Gemma 4 E2B (Q4_K_XL) | Gemma 4 | 48k | ~3.0 GB | Yes (`mmproj-bf16`) |
| `gemma-4-e4b` | `gemma4-e4b`, `gemma-4-e4b-it` | Gemma 4 E4B (Q4_K_XL) | Gemma 4 | 48k | ~4.8 GB | Yes (`mmproj-bf16`) |

---

## License

This project is licensed under the [MIT License](LICENSE).
