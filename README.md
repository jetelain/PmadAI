# PmadAI

[![CI](https://github.com/jetelain/PmadAI/actions/workflows/ci.yml/badge.svg)](https://github.com/jetelain/PmadAI/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**PmadAI** is a modular .NET 10 toolkit providing high-performance local AI inference adapters, protocol transformers, and agent utilities for modern .NET applications.

---

## Packages & Projects

| Project | NuGet | Description |
|:---|:---:|:---|
| [**`Pmad.AI`**](src/Pmad.AI/README.md) | [![NuGet](https://img.shields.io/nuget/v/Pmad.AI.svg)](https://www.nuget.org/packages/Pmad.AI) | Core agent utilities: protocol-level tool call healing (`ToolCallHealingChatClient`), history compaction (`ThinkingTraceCompactionChatClient`, `ToolResultCompactor`), loop circuit breaker (`ToolLoopCircuitBreaker`), shell security validator (`ReadOnlyCommandValidator`), verbatim audit transcripts (`VerbatimTranscriptChatClient`), declarative tool dispatching (`LenientToolDispatcher`), and token heuristics. Native AOT ready (`IsAotCompatible = true`). |
| [**`Pmad.AI.SourceGenerators`**](src/Pmad.AI.SourceGenerators) | *Analyzer* | Roslyn incremental source generator and analyzers emitting compile-time diagnostics (`PMAD001`-`PMAD003`) and automatic tool mapping registries from `[ToolAlias]` attributes. |
| [**`Pmad.AI.LlamaSharp`**](src/Pmad.AI.LlamaSharp/README.md) | [![NuGet](https://img.shields.io/nuget/v/Pmad.AI.LlamaSharp.svg)](https://www.nuget.org/packages/Pmad.AI.LlamaSharp) | High-performance in-process GGUF inference adapter implementing [`Microsoft.Extensions.AI.IChatClient`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.ichatclient) powered by [LLamaSharp](https://github.com/SciSharp/LLamaSharp) and [llama.cpp](https://github.com/ggerganov/llama.cpp). Includes the `LlamaSharpSpeculativeSummarizer` background CPU engine. |
| [**`Pmad.AI.Benchmark`**](benchmarks/Pmad.AI.Benchmark) | *Tool* | Evaluation suite measuring tool calling accuracy, compaction efficiency, loop breaker trips, and inference tok/s, latency, RAM, and GPU VRAM across CPU and Vulkan backends. |

---

## Quick Navigation

- **Agent Utilities & Pipeline Middleware**: [`src/Pmad.AI/README.md`](src/Pmad.AI/README.md)
  - Resilient tool call healing (`PrefixTriggered` & `FullBuffer`)
  - Loop circuit breaking (runaway streaming repetition & tool ping-pong)
  - Multi-dialect shell security validation (Bash & PowerShell default-deny policies)
  - Audit-grade verbatim Markdown transcript generation (`FlushAfterEachTurn`)
  - Conversation history compaction (reasoning traces and bulky tool outputs)
  - Declarative tool dispatching and Roslyn source generation
  - Zero-allocation heuristic token estimation
- **Local LLM Inference & Speculative Summarization**: [`src/Pmad.AI.LlamaSharp/README.md`](src/Pmad.AI.LlamaSharp/README.md)
  - Backend setup (CPU, Vulkan, CUDA 12)
  - Speculative CPU SLM summarization engine (`LlamaSharpSpeculativeSummarizer`)
  - Quickstart: Basic completion, streaming, and tool calling with `AIFunction`
  - Multimodal vision inference (`mmproj`)
  - Chat dialects (ChatML, Gemma 4, Phi-3) and reasoning effort configuration
  - Verified model presets and auto-downloader
- **Performance & Benchmarks**:
  - [Tool Calling Benchmark Report](docs/benchmark.md)
  - [Full Execution Transcript](docs/transcript.md)

---

## Overview & Highlights

### 1. `Pmad.AI`: Protocol Healing & Zero-Allocation Token Estimation

Originally developed for [OllamaRouter](https://github.com/jetelain/OllamaRouter), these utilities solve common operational hurdles when integrating local and open-weights models:

- **Repair malformed tool calls on the fly**: Local models frequently produce broken JSON (trailing commas, unescaped newlines in strings, or enclosing markdown code fences). `ToolCallHealingTransform` cleans and normalizes payloads across both OpenAI and Ollama formats without crashing strict JSON parsers.
- **Estimate tokens instantly without loading vocabularies**: Full BPE tokenizers require large vocabulary files and heap allocations. `HeuristicTokenEstimator` operates directly over `ReadOnlySpan<char>` with zero heap allocation, making it ideal for high-throughput rate limiters and context window budgeting.

```csharp
using Pmad.AI.Healing;
using Pmad.AI.Tokens;

// Sanitize malformed tool argument JSON
string cleanJson = ToolCallHealingTransform.SanitizeArgumentsJson("```json\n{\"city\": \"Paris\",}\n```");

// Estimate tokens with 0 MB allocations
int tokens = HeuristicTokenEstimator.Default.Estimate("Summarize this document...".AsSpan());
```

👉 *See the full [Pmad.AI Documentation](src/Pmad.AI/README.md) for details.*

---

### 2. `Pmad.AI.LlamaSharp`: In-Process GGUF Chat Client

Embed local models directly in your .NET process using standard `Microsoft.Extensions.AI` interfaces:

```csharp
using Microsoft.Extensions.AI;
using Pmad.AI.LlamaSharp;

// Initialize client with local GGUF model
using var chatClient = LlamaSharpChatClient.CreateFromFile(
    modelPath: "models/qwen3.5-2b-ud-q4_k_xl.gguf",
    options: new LlamaSharpOptions { GpuLayerCount = 99 },
    promptFormat: "chatml");

// Stream tokens in real time
await foreach (var update in chatClient.GetStreamingResponseAsync("Why is the sky blue?"))
{
    Console.Write(update.Text);
}
```

👉 *See the full [Pmad.AI.LlamaSharp Documentation](src/Pmad.AI.LlamaSharp/README.md) for installation, backend selection, multimodal vision, tool calling, and preset downloads.*

---

## Building and Testing

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- Optional: C++ build tools and Vulkan / CUDA runtime if running GPU inference locally

### Build & Run Tests

```bash
# Clone the repository
git clone https://github.com/jetelain/PmadAI.git
cd PmadAI

# Restore dependencies
dotnet restore

# Build solution
dotnet build -c Release

# Run all test suites
dotnet test -c Release --no-build
```

### Run Benchmarks

```bash
# Run tool calling benchmark in mock simulation mode (no weights required)
dotnet run --project benchmarks/Pmad.AI.Benchmark -- --mock
```

---

## License

This repository is licensed under the [MIT License](LICENSE).
