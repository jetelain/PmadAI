# Pmad.AI Tool Calling Benchmark Report

Generated on: 2026-10-21 08:19:34 UTC

## Environment

- **CPU:** Intel(R) Core(TM) i7-14700KF (28 logical cores)
- **GPU:** NVIDIA GeForce RTX 4080 SUPER, 15,99 GB VRAM, Driver: 610.88
- **OS:** Microsoft Windows 10.0.26200

> [!NOTE]
> 📄 **Execution Transcripts:** Individual turn-by-turn verbatim transcripts and failure analyses for each configuration are saved in the `transcripts/` directory and linked directly in the tables below.

## Summary Comparison

| Model | Backend | Reasoning | Scenarios Passed | Pass@1 (Wtd) | Pass@1 (Unwtd) | Step Acc | Pass Rate | Tool Calls | Gen Tokens | Avg Speed (tok/s) | Sys RAM | GPU VRAM | Total Time |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| [`Qwen 3.5 4B (Q4_K_XL) [Baseline (Raw)]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_baseline.transcript.md) | Vulkan | Default | 5/15 | 25,5% | 33,3% | 87,8% | 44,2% | 223 | 48824 | 114,8 | 3,11 GB | 7,36 GB | 425,25s |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Call Healing]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing.transcript.md) | Vulkan | Default | 7/15 | 32,8% | 46,7% | 81,6% | 47,5% | 217 | 45404 | 108,6 | 3,15 GB | 7,36 GB | 418,02s |
| [`Qwen 3.5 4B (Q4_K_XL) [Thinking Trace Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-thinking-compaction.transcript.md) | Vulkan | Default | 6/15 | 23,3% | 40,0% | 82,6% | 41,1% | 253 | 53141 | 109,1 | 3,18 GB | 7,56 GB | 487,00s |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Result Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-tool-compaction.transcript.md) | Vulkan | Default | 6/15 | 29,4% | 40,0% | 86,2% | 46,5% | 231 | 48728 | 111,7 | 3,19 GB | 7,46 GB | 436,35s |
| [`Qwen 3.5 4B (Q4_K_XL) [Speculative SLM Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-speculative-slm.transcript.md) | Vulkan | Default | 9/15 | 47,6% | 60,0% | 92,6% | 61,1% | 220 | 48380 | 107,1 | 3,29 GB | 7,46 GB | 451,66s |
| [`Qwen 3.5 4B (Q4_K_XL) [Lenient Dispatch & Guardrails]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-lenient-dispatch.transcript.md) | Vulkan | Default | 6/15 | 23,3% | 40,0% | 88,7% | 42,9% | 220 | 45386 | 115,4 | 3,30 GB | 7,50 GB | 393,42s |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Lenient Dispatch]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md) | Vulkan | Default | 7/15 | 41,9% | 53,3% | 86,9% | 55,4% | 183 | 41169 | 114,3 | 3,25 GB | 7,55 GB | 360,33s |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Tool Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-tool-compaction.transcript.md) | Vulkan | Default | 7/15 | 29,0% | 46,7% | 83,1% | 45,2% | 240 | 49821 | 112,9 | 3,29 GB | 7,68 GB | 441,41s |
| [`Qwen 3.5 4B (Q4_K_XL) [Recommended SLM Pipeline]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md) | Vulkan | Default | 7/15 | 39,7% | 46,7% | 89,7% | 54,7% | 207 | 45364 | 115,3 | 3,31 GB | 7,64 GB | 393,56s |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM without SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md) | Vulkan | Default | 6/15 | 47,6% | 53,3% | 84,6% | 58,7% | 192 | 47869 | 117,7 | 3,34 GB | 7,57 GB | 406,81s |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM with SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | Vulkan | Default | 9/15 | 47,6% | 60,0% | 94,2% | 61,6% | 197 | 45001 | 112,3 | 3,37 GB | 7,62 GB | 400,80s |
| [`Qwen 3.5 4B (Q4_K_XL) [All Optimizations Combined]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md) | Vulkan | Default | 7/15 | 35,8% | 46,7% | 92,5% | 52,8% | 207 | 46577 | 114,9 | 3,43 GB | 7,76 GB | 405,51s |

## Benchmark Suite Breakdown (BFCL / SWE-bench / RULER)

> Evaluates performance separated by benchmark domain: schema adherence and tool call syntax (BFCL), multi-turn agentic software engineering (SWE-bench), and needle-in-a-haystack retrieval (RULER).

| Model | Suite | Scenarios | Pass@1 (Task Success) | Step Acc | Turns Passed | Turn Acc | Weighted Pass Rate |
|---|---|---|---|---|---|---|---|
| [`Qwen 3.5 4B (Q4_K_XL) [Baseline (Raw)]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_baseline.transcript.md) | **BFCL** | 2/6 | 27,3% | 94,8% | 78/82 | 95,1% | 47,5% |
| [`Qwen 3.5 4B (Q4_K_XL) [Baseline (Raw)]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_baseline.transcript.md) | **SWE-bench** | 1/7 | 14,5% | 83,4% | 33/38 | 86,8% | 35,2% |
| [`Qwen 3.5 4B (Q4_K_XL) [Baseline (Raw)]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_baseline.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Call Healing]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing.transcript.md) | **BFCL** | 4/6 | 56,8% | 96,6% | 80/82 | 97,6% | 68,8% |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Call Healing]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing.transcript.md) | **SWE-bench** | 1/7 | 14,5% | 73,4% | 29/38 | 76,3% | 32,2% |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Call Healing]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Thinking Trace Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-thinking-compaction.transcript.md) | **BFCL** | 4/6 | 56,8% | 96,6% | 80/82 | 97,6% | 68,8% |
| [`Qwen 3.5 4B (Q4_K_XL) [Thinking Trace Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-thinking-compaction.transcript.md) | **SWE-bench** | 0/7 | 0,0% | 74,8% | 29/38 | 76,3% | 22,4% |
| [`Qwen 3.5 4B (Q4_K_XL) [Thinking Trace Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-thinking-compaction.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Result Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-tool-compaction.transcript.md) | **BFCL** | 3/6 | 43,2% | 95,7% | 79/82 | 96,3% | 58,9% |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Result Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-tool-compaction.transcript.md) | **SWE-bench** | 1/7 | 14,5% | 80,7% | 31/38 | 81,6% | 34,3% |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Result Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-tool-compaction.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Speculative SLM Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-speculative-slm.transcript.md) | **BFCL** | 5/6 | 79,5% | 97,7% | 81/82 | 98,8% | 85,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Speculative SLM Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-speculative-slm.transcript.md) | **SWE-bench** | 2/7 | 28,3% | 89,7% | 35/38 | 92,1% | 46,7% |
| [`Qwen 3.5 4B (Q4_K_XL) [Speculative SLM Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-speculative-slm.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Lenient Dispatch & Guardrails]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-lenient-dispatch.transcript.md) | **BFCL** | 4/6 | 56,8% | 96,6% | 80/82 | 97,6% | 68,8% |
| [`Qwen 3.5 4B (Q4_K_XL) [Lenient Dispatch & Guardrails]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-lenient-dispatch.transcript.md) | **SWE-bench** | 0/7 | 0,0% | 84,1% | 32/38 | 84,2% | 25,2% |
| [`Qwen 3.5 4B (Q4_K_XL) [Lenient Dispatch & Guardrails]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-lenient-dispatch.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Lenient Dispatch]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md) | **BFCL** | 4/6 | 56,8% | 96,6% | 80/82 | 97,6% | 68,8% |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Lenient Dispatch]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md) | **SWE-bench** | 2/7 | 28,3% | 81,4% | 30/38 | 78,9% | 44,2% |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Lenient Dispatch]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Tool Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-tool-compaction.transcript.md) | **BFCL** | 5/6 | 79,5% | 97,7% | 81/82 | 98,8% | 85,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Tool Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-tool-compaction.transcript.md) | **SWE-bench** | 0/7 | 0,0% | 75,2% | 29/38 | 76,3% | 22,5% |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Tool Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-tool-compaction.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Recommended SLM Pipeline]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md) | **BFCL** | 3/6 | 47,7% | 96,1% | 79/82 | 96,3% | 62,3% |
| [`Qwen 3.5 4B (Q4_K_XL) [Recommended SLM Pipeline]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md) | **SWE-bench** | 2/7 | 28,3% | 85,9% | 33/38 | 86,8% | 45,5% |
| [`Qwen 3.5 4B (Q4_K_XL) [Recommended SLM Pipeline]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM without SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md) | **BFCL** | 3/6 | 43,2% | 95,7% | 79/82 | 96,3% | 58,9% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM without SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md) | **SWE-bench** | 3/7 | 42,1% | 78,3% | 29/38 | 76,3% | 52,9% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM without SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM with SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | **BFCL** | 5/6 | 79,5% | 97,7% | 81/82 | 98,8% | 85,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM with SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | **SWE-bench** | 2/7 | 28,3% | 92,1% | 35/38 | 92,1% | 47,4% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM with SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Optimizations Combined]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md) | **BFCL** | 4/6 | 70,5% | 97,3% | 80/82 | 97,6% | 78,5% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Optimizations Combined]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md) | **SWE-bench** | 1/7 | 13,8% | 89,7% | 35/38 | 92,1% | 36,6% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Optimizations Combined]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |

## Pmad.AI Optimization Matrix: Empirical Utility Gains

> Quantifies prompt token reduction, context ceiling capping, execution time savings, and accuracy improvements achieved by Pmad.AI utilities vs. unassisted baseline model execution.

| Model | Configuration | Pass Rate | Prompt Tokens | Token Savings % | Peak Context | Context Reduction % | Gen Speed | Duration | Time Savings % |
|---|---|---|---|---|---|---|---|---|---|
| `Qwen 3.5 4B (Q4_K_XL)` | [**Baseline (Raw)**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_baseline.transcript.md) | 44,2% | 676761 | — | 19841 tok | — | 114,8 tok/s | 425,25s | — |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Tool Call Healing**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing.transcript.md) | 47,5% | 618963 | **-8,5%** | 19246 tok | **-3,0%** | 108,6 tok/s | 418,02s | **-1,7%** |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Thinking Trace Compaction**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-thinking-compaction.transcript.md) | 41,1% | 846686 | +25,1% | 19308 tok | **-2,7%** | 109,1 tok/s | 487,00s | +14,5% |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Tool Result Compaction**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-tool-compaction.transcript.md) | 46,5% | 749389 | +10,7% | 19122 tok | **-3,6%** | 111,7 tok/s | 436,35s | +2,6% |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Speculative SLM Compaction**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-speculative-slm.transcript.md) | 61,1% | 741226 | +9,5% | 19205 tok | **-3,2%** | 107,1 tok/s | 451,66s | +6,2% |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Lenient Dispatch & Guardrails**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-lenient-dispatch.transcript.md) | 42,9% | 632097 | **-6,6%** | 19540 tok | **-1,5%** | 115,4 tok/s | 393,42s | **-7,5%** |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Healing + Lenient Dispatch**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md) | 55,4% | 486516 | **-28,1%** | 19313 tok | **-2,7%** | 114,3 tok/s | 360,33s | **-15,3%** |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Healing + Tool Compaction**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-tool-compaction.transcript.md) | 45,2% | 788632 | +16,5% | 19834 tok | **-0,0%** | 112,9 tok/s | 441,41s | +3,8% |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Recommended SLM Pipeline**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md) | 54,7% | 602570 | **-11,0%** | 19905 tok | +0,3% | 115,3 tok/s | 393,56s | **-7,5%** |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Local LLM without SLM Summarizer**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md) | 58,7% | 535694 | **-20,8%** | 19831 tok | **-0,1%** | 117,7 tok/s | 406,81s | **-4,3%** |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Local LLM with SLM Summarizer**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | 61,6% | 584049 | **-13,7%** | 20253 tok | +2,1% | 112,3 tok/s | 400,80s | **-5,7%** |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Optimizations Combined**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md) | 52,8% | 658439 | **-2,7%** | 19463 tok | **-1,9%** | 114,9 tok/s | 405,51s | **-4,6%** |

## Compaction Efficiency & Telemetry Matrix

> Reports characters and tokens pruned, speculative SLM hit rate, and breakdown of collapsed reasoning traces vs. truncated/summarized tool results.

| Model | Configuration | Pruned Chars | Pruned Tokens | Speculative Hit Rate | Thinking Traces Collapsed | Tool Results Truncated | Tool Results Summarized | Total Events |
|---|---|---|---|---|---|---|---|---|
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Local LLM with SLM Summarizer**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | 4,360 | 1,090 | 0.0% (0/4) | 4 | 0 | 0 | 4 |

## Pmad.AI Utility Interventions & Telemetry Matrix

> Quantifies runtime interventions by Pmad.AI components: Tool Call Healing (malformed JSON, pseudo-tag extractions, fence strippings), Circuit Breaker (loop and ping-pong interceptions), and Lenient Dispatch (tool/parameter normalization).

| Model | Configuration | Healing Total | Pseudo-Tags | JSON Repairs | Breaker Total | Breaker Loops | Repetitions | Dispatch Total | Alias Hits | Param Hits | Casing/Norm |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `Qwen 3.5 4B (Q4_K_XL)` | [**Lenient Dispatch & Guardrails**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-lenient-dispatch.transcript.md) | 0 | 0 | 0 | 2 | 2 | 0 | 0 | 0 | 0 | 0 |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Healing + Lenient Dispatch**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md) | 0 | 0 | 0 | 4 | 4 | 0 | 0 | 0 | 0 | 0 |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Recommended SLM Pipeline**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md) | 0 | 0 | 0 | 3 | 3 | 0 | 0 | 0 | 0 | 0 |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Local LLM without SLM Summarizer**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md) | 0 | 0 | 0 | 3 | 3 | 0 | 0 | 0 | 0 | 0 |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Local LLM with SLM Summarizer**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | 0 | 0 | 0 | 3 | 3 | 0 | 0 | 0 | 0 | 0 |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Optimizations Combined**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md) | 0 | 0 | 0 | 3 | 3 | 0 | 0 | 0 | 0 | 0 |

## Detailed Scenario Breakdown

### Model: `Qwen 3.5 4B (Q4_K_XL) [Baseline (Raw)]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_baseline.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 10,0 | `Bad Arguments` | 21 | 100% | 13405 | 638 | 1131 tok | 7407 | 129,9 tok/s | 3,09 GB | 7,10 GB | 57027 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Bad Arguments` | 20 | 100% | 15332 | 767 | 679 tok | 5277 | 128,2 tok/s | 3,10 GB | 7,11 GB | 41153 ms |
| **BFCL - Parallel Function Calling** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 93,3% | 28,0% | 22,5 | `Bad Arguments` | 33 | 100% | 9814 | 297 | 542 tok | 5713 | 138,3 tok/s | 3,10 GB | 7,12 GB | 41318 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 6107 | 147,2 tok/s | 3,10 GB | 7,20 GB | 41479 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Missing Tool Call` | 7 | 88% | 14899 | 2128 | 1568 tok | 1790 | 122,8 tok/s | 3,10 GB | 7,19 GB | 14571 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2881 | 960 | 647 tok | 729 | 130,7 tok/s | 3,10 GB | 7,23 GB | 5579 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Patch Rejected` | 27 | 100% | 149588 | 5540 | 9105 tok | 4240 | 92,8 tok/s | 3,10 GB | 7,36 GB | 45668 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 42,0 | `—` | 18 | 100% | 110574 | 6143 | 8858 tok | 3860 | 97,5 tok/s | 3,09 GB | 7,36 GB | 39602 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 11 | 75% | 19667 | 1788 | 3116 tok | 1139 | 106,5 tok/s | 3,10 GB | 7,31 GB | 10690 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Missing Tool Call` | 25 | 80% | 66540 | 2662 | 4851 tok | 1488 | 89,7 tok/s | 3,10 GB | 7,31 GB | 16589 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 46885 | 2344 | 4166 tok | 1635 | 102,7 tok/s | 3,10 GB | 7,29 GB | 15926 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 50,0% | 15,0% | 40,0 | `Missing Tool Call` | 15 | 50% | 30011 | 2001 | 4258 tok | 1831 | 114,7 tok/s | 3,11 GB | 7,31 GB | 15960 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 48,0 | `Patch Rejected` | 21 | 100% | 135151 | 6436 | 12215 tok | 4202 | 94,6 tok/s | 3,10 GB | 7,31 GB | 44401 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20304 | 20304 | 10499 tok | 891 | 99,3 tok/s | 3,10 GB | 7,35 GB | 8972 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38589 | 38589 | 19841 tok | 2515 | 95,6 tok/s | 3,10 GB | 7,33 GB | 26310 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Simple Function Calling (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 6: Tool call count mismatch: expected 1 call(s), but model generated 2.

> [!WARNING]
> **Partial Outcome Details for BFCL - Multiple Tools Selection (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 20: AST evaluation failed for 'calculate_effective_population_size': Argument 'species' value mismatch: expected 'wild tiger' (String), got 'wild tigers' (String)

> [!WARNING]
> **Partial Outcome Details for BFCL - Parallel Function Calling (Score: 28,0%, 14/15 turns passed):** 
> - End-to-end task failed: 1 of 15 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 5: Tool call count mismatch: expected 2 call(s), but model generated 4.

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 8: Expected tool call 'patch_deployment_resource' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 30,0%, 8/8 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Patch Rejected`

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose) (Score: 24,0%, 4/5 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: Circuit breaker did not trip to break out of repetitive tool loop.
> - Failure Taxonomy: `Over-Calling`
> - Turn 4: Model invoked 5 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 15,0%, 2/4 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 2: Expected tool call 'run_tests' was not invoked by model.
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 30,0%, 6/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

### Model: `Qwen 3.5 4B (Q4_K_XL) [Tool Call Healing]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 11757 | 588 | 461 tok | 6519 | 124,6 tok/s | 3,13 GB | 7,36 GB | 52316 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Bad Arguments` | 20 | 100% | 15281 | 764 | 650 tok | 5365 | 122,8 tok/s | 3,14 GB | 7,30 GB | 43695 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 9441 | 304 | 527 tok | 5084 | 123,3 tok/s | 3,14 GB | 7,33 GB | 41221 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 5849 | 129,1 tok/s | 3,13 GB | 7,27 GB | 45320 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 17113 | 2139 | 1831 tok | 2049 | 115,8 tok/s | 3,15 GB | 7,20 GB | 17699 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2734 | 911 | 517 tok | 527 | 123,2 tok/s | 3,15 GB | 7,21 GB | 4276 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 87,5% | 26,2% | 40,0 | `Missing Tool Call` | 22 | 86% | 114890 | 5222 | 8272 tok | 3348 | 87,8 tok/s | 3,15 GB | 7,25 GB | 38130 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 42,0 | `—` | 16 | 100% | 91776 | 5736 | 8091 tok | 3168 | 91,9 tok/s | 3,14 GB | 7,23 GB | 34465 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 17 | 75% | 39604 | 2330 | 4049 tok | 1622 | 97,6 tok/s | 3,15 GB | 7,22 GB | 16613 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Missing Tool Call` | 22 | 80% | 66475 | 3022 | 5588 tok | 2159 | 94,9 tok/s | 3,14 GB | 7,25 GB | 22752 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 38023 | 1901 | 3695 tok | 1299 | 90,9 tok/s | 3,14 GB | 7,25 GB | 14298 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 50,0% | 15,0% | 40,0 | `Missing Tool Call` | 11 | 50% | 30196 | 2745 | 4206 tok | 3875 | 120,3 tok/s | 3,14 GB | 7,23 GB | 32219 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 50,0% | 15,0% | 48,0 | `Over-Calling` | 25 | 60% | 120123 | 4805 | 8399 tok | 2885 | 87,3 tok/s | 3,14 GB | 7,23 GB | 33063 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20435 | 20435 | 10630 tok | 1022 | 91,4 tok/s | 3,11 GB | 7,24 GB | 11179 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 37994 | 37994 | 19246 tok | 633 | 58,8 tok/s | 3,12 GB | 7,23 GB | 10770 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Multiple Tools Selection (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 20: AST evaluation failed for 'calculate_effective_population_size': Argument 'species' value mismatch: expected 'wild tiger' (String), got 'tiger' (String)

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 8: AST evaluation failed for 'patch_deployment_resource': Argument 'resource_type' value mismatch: expected 'memory_mb' (String), got 'memory_limit' (String)

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 26,2%, 7/8 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 6: Expected tool call 'run_tests' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose) (Score: 24,0%, 4/5 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: Circuit breaker did not trip to break out of repetitive tool loop.
> - Failure Taxonomy: `Over-Calling`
> - Turn 4: Model invoked 5 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 15,0%, 2/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.
> - Turn 4: Expected tool call 'run_tests' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 15,0%, 3/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Over-Calling`
> - Turn 4: Expected tool call 'apply_patch' was not invoked by model.
> - Turn 5: Expected tool call 'run_tests' was not invoked by model.
> - Turn 6: Model invoked 5 tool call(s) when 0 were expected.

### Model: `Qwen 3.5 4B (Q4_K_XL) [Thinking Trace Compaction]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-thinking-compaction.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 11976 | 599 | 567 tok | 7109 | 135,2 tok/s | 3,14 GB | 7,28 GB | 52568 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Bad Arguments` | 20 | 100% | 15022 | 751 | 629 tok | 5191 | 125,4 tok/s | 3,14 GB | 7,48 GB | 41407 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 8920 | 288 | 412 tok | 5097 | 131,5 tok/s | 3,13 GB | 7,48 GB | 38762 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 5509 | 136,7 tok/s | 3,13 GB | 7,45 GB | 40307 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 20507 | 2563 | 2548 tok | 2636 | 119,7 tok/s | 3,14 GB | 7,42 GB | 22021 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2734 | 911 | 517 tok | 621 | 125,1 tok/s | 3,15 GB | 7,27 GB | 4965 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 87,5% | 26,2% | 40,0 | `Missing Tool Call` | 27 | 86% | 140748 | 5213 | 8637 tok | 3785 | 94,7 tok/s | 3,15 GB | 7,56 GB | 39948 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 71,4% | 21,4% | 42,0 | `Over-Calling` | 31 | 83% | 215521 | 6952 | 13637 tok | 8271 | 95,8 tok/s | 3,15 GB | 7,50 GB | 86318 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 20 | 75% | 48872 | 2444 | 4544 tok | 1968 | 101,0 tok/s | 3,15 GB | 7,28 GB | 19493 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Missing Tool Call` | 25 | 80% | 76549 | 3062 | 5479 tok | 1929 | 81,7 tok/s | 3,15 GB | 7,29 GB | 23604 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 40783 | 2039 | 3837 tok | 1387 | 92,0 tok/s | 3,17 GB | 7,44 GB | 15073 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 50,0% | 15,0% | 40,0 | `Missing Tool Call` | 20 | 50% | 43550 | 2178 | 4136 tok | 1408 | 94,6 tok/s | 3,17 GB | 7,36 GB | 14884 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 83,3% | 25,0% | 48,0 | `Over-Calling` | 26 | 100% | 159647 | 6140 | 11685 tok | 5022 | 91,5 tok/s | 3,15 GB | 7,37 GB | 54865 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20680 | 20680 | 10875 tok | 1959 | 111,3 tok/s | 3,16 GB | 7,33 GB | 17598 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38056 | 38056 | 19308 tok | 1249 | 82,3 tok/s | 3,17 GB | 7,26 GB | 15181 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Multiple Tools Selection (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 20: AST evaluation failed for 'calculate_effective_population_size': Argument 'species' value mismatch: expected 'wild tiger' (String), got 'wild tigers' (String)

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 8: AST evaluation failed for 'patch_deployment_resource': Argument 'resource_type' value mismatch: expected 'memory_mb' (String), got 'memory_limit' (String)

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 26,2%, 7/8 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 5: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: django__django-11099 (DiffExplosion) (Score: 21,4%, 5/7 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Over-Calling`
> - Turn 6: Expected tool call 'run_tests' was not invoked by model.
> - Turn 7: Model invoked 5 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose) (Score: 24,0%, 4/5 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: Circuit breaker did not trip to break out of repetitive tool loop.
> - Failure Taxonomy: `Over-Calling`
> - Turn 4: Model invoked 5 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 15,0%, 2/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.
> - Turn 4: Expected tool call 'run_tests' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 25,0%, 5/6 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Over-Calling`
> - Turn 6: Model invoked 5 tool call(s) when 0 were expected.

### Model: `Qwen 3.5 4B (Q4_K_XL) [Tool Result Compaction]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-tool-compaction.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 11663 | 583 | 455 tok | 5363 | 131,2 tok/s | 3,19 GB | 7,25 GB | 40876 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Bad Arguments` | 20 | 100% | 15143 | 757 | 579 tok | 5461 | 130,5 tok/s | 3,18 GB | 7,24 GB | 41863 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 9129 | 294 | 492 tok | 5183 | 133,0 tok/s | 3,17 GB | 7,25 GB | 38983 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 93,3% | 28,0% | 15,0 | `Validation Failure` | 0 | 100% | 3121 | — | 240 tok | 8806 | 139,7 tok/s | 3,16 GB | 7,28 GB | 63052 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 18885 | 2361 | 2431 tok | 2309 | 117,2 tok/s | 3,16 GB | 7,28 GB | 19695 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2734 | 911 | 517 tok | 651 | 120,7 tok/s | 3,17 GB | 7,26 GB | 5392 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 28 | 86% | 163846 | 5852 | 10624 tok | 4041 | 84,3 tok/s | 3,16 GB | 7,27 GB | 47916 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 42,0 | `—` | 17 | 100% | 112918 | 6642 | 9822 tok | 4656 | 104,5 tok/s | 3,16 GB | 7,24 GB | 44565 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 14 | 75% | 28856 | 2061 | 3458 tok | 1234 | 106,8 tok/s | 3,16 GB | 7,24 GB | 11555 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Missing Tool Call` | 25 | 80% | 68400 | 2736 | 5369 tok | 1812 | 93,6 tok/s | 3,16 GB | 7,23 GB | 19353 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 39329 | 1966 | 3780 tok | 1339 | 94,2 tok/s | 3,17 GB | 7,22 GB | 14218 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 20 | 75% | 51491 | 2575 | 5297 tok | 1776 | 99,0 tok/s | 3,17 GB | 7,25 GB | 17944 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 83,3% | 25,0% | 48,0 | `Over-Calling` | 23 | 100% | 165697 | 7204 | 12810 tok | 4656 | 90,9 tok/s | 3,17 GB | 7,46 GB | 51209 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20307 | 20307 | 10502 tok | 891 | 92,3 tok/s | 3,17 GB | 7,44 GB | 9656 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 37870 | 37870 | 19122 tok | 550 | 54,6 tok/s | 3,19 GB | 7,42 GB | 10070 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Multiple Tools Selection (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 20: AST evaluation failed for 'calculate_effective_population_size': Argument 'species' value mismatch: expected 'wild tiger' (String), got 'wild tigers' (String)

> [!WARNING]
> **Partial Outcome Details for BFCL - Irrelevance (Negative Control) (Score: 28,0%, 14/15 turns passed):** 
> - End-to-end task failed: 1 of 15 turns failed validation.
> - Failure Taxonomy: `Validation Failure`
> - Turn 12: Expected direct conversational text response, but assistant returned empty output.

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 8: AST evaluation failed for 'patch_deployment_resource': Argument 'resource_type' value mismatch: expected 'memory_mb' (String), got 'memory' (String)

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 22,5%, 6/8 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Over-Calling`
> - Turn 6: Expected tool call 'run_tests' was not invoked by model.
> - Turn 8: Model invoked 1 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose) (Score: 24,0%, 4/5 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: Circuit breaker did not trip to break out of repetitive tool loop.
> - Failure Taxonomy: `Over-Calling`
> - Turn 4: Model invoked 5 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 4: Expected tool call 'run_tests' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 25,0%, 5/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Over-Calling`
> - Turn 6: Model invoked 2 tool call(s) when 0 were expected.

### Model: `Qwen 3.5 4B (Q4_K_XL) [Speculative SLM Compaction]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-speculative-slm.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 12016 | 601 | 816 tok | 6246 | 127,1 tok/s | 3,20 GB | 7,46 GB | 49154 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 25,0 | `—` | 20 | 100% | 14925 | 746 | 501 tok | 4643 | 122,8 tok/s | 3,21 GB | 7,43 GB | 37817 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 9046 | 292 | 508 tok | 5038 | 127,4 tok/s | 3,20 GB | 7,39 GB | 39540 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 6263 | 132,7 tok/s | 3,21 GB | 7,40 GB | 47183 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 18238 | 2280 | 1809 tok | 2236 | 114,1 tok/s | 3,22 GB | 7,34 GB | 19604 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2734 | 911 | 517 tok | 643 | 116,4 tok/s | 3,23 GB | 7,30 GB | 5522 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Patch Rejected` | 27 | 100% | 154106 | 5708 | 9689 tok | 4313 | 87,5 tok/s | 3,23 GB | 7,38 GB | 49290 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 42,0 | `—` | 23 | 100% | 145845 | 6341 | 10408 tok | 3931 | 84,5 tok/s | 3,23 GB | 7,45 GB | 46521 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 10 | 75% | 20428 | 2043 | 3111 tok | 1290 | 106,2 tok/s | 3,23 GB | 7,34 GB | 12151 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 40,0 | `—` | 16 | 100% | 47691 | 2981 | 5155 tok | 2067 | 94,0 tok/s | 3,25 GB | 7,37 GB | 21996 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 45096 | 2255 | 4498 tok | 2015 | 102,2 tok/s | 3,27 GB | 7,37 GB | 19712 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 20 | 75% | 55241 | 2762 | 6071 tok | 2985 | 106,8 tok/s | 3,27 GB | 7,40 GB | 27937 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 48,0 | `Patch Rejected` | 20 | 100% | 154864 | 7743 | 12536 tok | 5564 | 95,7 tok/s | 3,29 GB | 7,37 GB | 58137 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 19922 | 19922 | 10117 tok | 513 | 75,1 tok/s | 3,27 GB | 7,33 GB | 6832 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 37953 | 37953 | 19205 tok | 633 | 61,7 tok/s | 3,27 GB | 7,33 GB | 10261 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 8: AST evaluation failed for 'patch_deployment_resource': Argument 'resource_type' value mismatch: expected 'memory_mb' (String), got 'memory' (String)

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 30,0%, 8/8 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: Circuit breaker did not trip to break out of repetitive tool loop.
> - Failure Taxonomy: `Over-Calling`
> - Turn 4: Model invoked 5 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 4: Expected tool call 'run_tests' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 30,0%, 6/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

### Model: `Qwen 3.5 4B (Q4_K_XL) [Lenient Dispatch & Guardrails]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-lenient-dispatch.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 12209 | 610 | 872 tok | 7033 | 137,4 tok/s | 3,29 GB | 7,31 GB | 51185 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Bad Arguments` | 20 | 100% | 15363 | 768 | 673 tok | 5374 | 129,9 tok/s | 3,29 GB | 7,31 GB | 41359 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 9107 | 294 | 510 tok | 4933 | 139,1 tok/s | 3,30 GB | 7,31 GB | 35474 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 6489 | 144,3 tok/s | 3,30 GB | 7,29 GB | 44981 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 19854 | 2482 | 2496 tok | 2416 | 122,1 tok/s | 3,30 GB | 7,31 GB | 19794 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2732 | 911 | 517 tok | 542 | 125,4 tok/s | 3,27 GB | 7,31 GB | 4321 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Tool Loop` | 26 | 86% | 152203 | 5854 | 9352 tok | 4507 | 94,0 tok/s | 3,26 GB | 7,36 GB | 47954 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 57,1% | 17,1% | 42,0 | `Missing Tool Call` | 7 | 50% | 32952 | 4707 | 3703 tok | 1047 | 103,9 tok/s | 3,25 GB | 7,35 GB | 10076 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 20 | 75% | 46005 | 2300 | 4312 tok | 1591 | 95,5 tok/s | 3,25 GB | 7,38 GB | 16652 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Missing Tool Call` | 25 | 80% | 69573 | 2783 | 5184 tok | 1653 | 93,3 tok/s | 3,24 GB | 7,35 GB | 17720 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 18 | 100% | 37321 | 2073 | 3694 tok | 1322 | 101,6 tok/s | 3,23 GB | 7,32 GB | 13008 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Tool Loop` | 19 | 75% | 48891 | 2573 | 5249 tok | 1586 | 97,2 tok/s | 3,24 GB | 7,33 GB | 16313 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 48,0 | `Patch Rejected` | 21 | 100% | 123680 | 5890 | 10772 tok | 3965 | 89,3 tok/s | 3,20 GB | 7,50 GB | 44403 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20798 | 20798 | 10993 tok | 1608 | 111,1 tok/s | 3,20 GB | 7,48 GB | 14480 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38288 | 38288 | 19540 tok | 1320 | 84,1 tok/s | 3,20 GB | 7,44 GB | 15696 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Multiple Tools Selection (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 20: AST evaluation failed for 'calculate_effective_population_size': Argument 'species' value mismatch: expected 'wild tiger' (String), got 'tiger' (String)

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 8: AST evaluation failed for 'patch_deployment_resource': Argument 'resource_type' value mismatch: expected 'memory_mb' (String), got 'memory_limit' (String)

> ⚡ **Circuit Breaker Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon):** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 30,0%, 8/8 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Tool Loop`

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: django__django-11099 (DiffExplosion) (Score: 17,1%, 4/7 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 4: Expected tool call 'apply_patch' was not invoked by model.
> - Turn 5: Expected tool call 'check_git_diff' was not invoked by model.
> - Turn 6: Expected tool call 'run_tests' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose) (Score: 24,0%, 4/5 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: Circuit breaker did not trip to break out of repetitive tool loop.
> - Failure Taxonomy: `Over-Calling`
> - Turn 4: Model invoked 3 tool call(s) when 0 were expected.

> ⚡ **Circuit Breaker Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl):** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 30,0%, 4/4 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Tool Loop`

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 30,0%, 6/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

### Model: `Qwen 3.5 4B (Q4_K_XL) [Healing + Lenient Dispatch]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 11930 | 596 | 592 tok | 5852 | 134,6 tok/s | 3,20 GB | 7,43 GB | 43461 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Bad Arguments` | 20 | 100% | 15211 | 761 | 593 tok | 5077 | 125,4 tok/s | 3,21 GB | 7,42 GB | 40490 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 94% | 9307 | 300 | 531 tok | 4938 | 124,7 tok/s | 3,23 GB | 7,50 GB | 39586 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 6177 | 130,1 tok/s | 3,24 GB | 7,54 GB | 47496 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 17782 | 2223 | 1716 tok | 2087 | 111,1 tok/s | 3,23 GB | 7,39 GB | 18782 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2732 | 911 | 517 tok | 636 | 118,2 tok/s | 3,21 GB | 7,36 GB | 5380 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 50,0% | 15,0% | 40,0 | `Missing Tool Call` | 7 | 43% | 33261 | 4752 | 3373 tok | 1099 | 102,0 tok/s | 3,21 GB | 7,36 GB | 10773 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ✅ PASS | 85,7% | 95,7% | 42,0 | `—` | 13 | 100% | 74863 | 5759 | 7030 tok | 2608 | 95,1 tok/s | 3,21 GB | 7,41 GB | 27435 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 12 | 75% | 21159 | 1763 | 3044 tok | 1264 | 112,1 tok/s | 3,22 GB | 7,39 GB | 11276 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Missing Tool Call` | 25 | 80% | 78821 | 3153 | 6363 tok | 2476 | 98,9 tok/s | 3,21 GB | 7,51 GB | 25034 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 40,0 | `—` | 6 | 33% | 9116 | 1519 | 1446 tok | 742 | 111,2 tok/s | 3,22 GB | 7,53 GB | 6674 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 15 | 75% | 36614 | 2441 | 4521 tok | 3353 | 119,1 tok/s | 3,21 GB | 7,52 GB | 28161 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 48,0 | `Patch Rejected` | 21 | 100% | 114165 | 5436 | 9595 tok | 2991 | 86,7 tok/s | 3,21 GB | 7,55 GB | 34500 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20373 | 20373 | 10418 tok | 1037 | 105,5 tok/s | 3,22 GB | 7,53 GB | 9829 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38061 | 38061 | 19313 tok | 832 | 72,7 tok/s | 3,24 GB | 7,52 GB | 11450 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Multiple Tools Selection (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 20: AST evaluation failed for 'calculate_effective_population_size': Argument 'species' value mismatch: expected 'wild tiger' (String), got 'tigers' (String)

> ⚡ **Circuit Breaker Details for BFCL - Parallel Function Calling:** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 8: AST evaluation failed for 'patch_deployment_resource': Argument 'resource_type' value mismatch: expected 'memory_mb' (String), got 'memory_limit' (String)

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 15,0%, 4/8 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 4: Expected tool call 'search_code' was not invoked by model.
> - Turn 5: Expected tool call 'apply_patch' was not invoked by model.
> - Turn 6: Expected tool call 'run_tests' was not invoked by model.
> - Turn 7: Expected tool call 'check_git_diff' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: django__django-11099 (DiffExplosion) (Score: 95,7%, 6/7 turns passed):** 
> - Turn 7: Model invoked 2 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose) (Score: 24,0%, 4/5 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> ⚡ **Circuit Breaker Details for SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop):** 3 trip(s), 0 ping-pong loop trip(s), 0 streaming repetition trip(s), 3 semantic/no-progress loop trip(s).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 30,0%, 6/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

### Model: `Qwen 3.5 4B (Q4_K_XL) [Healing + Tool Compaction]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-tool-compaction.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 11639 | 582 | 460 tok | 5920 | 134,3 tok/s | 3,28 GB | 7,67 GB | 44085 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 25,0 | `—` | 20 | 100% | 14996 | 750 | 501 tok | 5040 | 124,8 tok/s | 3,29 GB | 7,68 GB | 40392 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 9167 | 296 | 491 tok | 5226 | 128,8 tok/s | 3,27 GB | 7,49 GB | 40573 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 5809 | 136,7 tok/s | 3,24 GB | 7,60 GB | 42495 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 19590 | 2449 | 2495 tok | 2421 | 126,5 tok/s | 3,25 GB | 7,57 GB | 19144 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2902 | 967 | 668 tok | 734 | 135,0 tok/s | 3,26 GB | 7,54 GB | 5439 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 34 | 86% | 184107 | 5415 | 8544 tok | 3308 | 82,9 tok/s | 3,27 GB | 7,57 GB | 39910 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 85,7% | 25,7% | 42,0 | `Over-Calling` | 27 | 100% | 179135 | 6635 | 10841 tok | 3752 | 81,1 tok/s | 3,25 GB | 7,54 GB | 46288 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 50,0% | 15,0% | 40,0 | `Missing Tool Call` | 16 | 50% | 31037 | 1940 | 3413 tok | 1359 | 106,0 tok/s | 3,26 GB | 7,54 GB | 12815 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Missing Tool Call` | 25 | 80% | 81134 | 3245 | 5747 tok | 1981 | 94,2 tok/s | 3,25 GB | 7,50 GB | 21023 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 41378 | 2069 | 3869 tok | 1394 | 100,2 tok/s | 3,24 GB | 7,58 GB | 13917 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 20 | 75% | 45620 | 2281 | 4442 tok | 1571 | 101,5 tok/s | 3,24 GB | 7,55 GB | 15485 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 83,3% | 25,0% | 48,0 | `Missing Tool Call` | 14 | 80% | 105459 | 7533 | 17496 tok | 7003 | 115,7 tok/s | 3,25 GB | 7,60 GB | 60541 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20765 | 20765 | 10960 tok | 2027 | 120,7 tok/s | 3,24 GB | 7,57 GB | 16799 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38582 | 38582 | 19834 tok | 2276 | 101,1 tok/s | 3,10 GB | 7,57 GB | 22502 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 8: AST evaluation failed for 'patch_deployment_resource': Argument 'resource_type' value mismatch: expected 'memory_mb' (String), got 'memory_limit' (String)

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 22,5%, 6/8 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Over-Calling`
> - Turn 5: Expected tool call 'apply_patch' was not invoked by model.
> - Turn 8: Model invoked 5 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: django__django-11099 (DiffExplosion) (Score: 25,7%, 6/7 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Over-Calling`
> - Turn 7: Model invoked 5 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery) (Score: 15,0%, 2/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.
> - Turn 4: Expected tool call 'run_tests' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose) (Score: 24,0%, 4/5 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: Circuit breaker did not trip to break out of repetitive tool loop.
> - Failure Taxonomy: `Over-Calling`
> - Turn 4: Model invoked 5 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 4: Expected tool call 'run_tests' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 25,0%, 5/6 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 4: Expected tool call 'apply_patch' was not invoked by model.

### Model: `Qwen 3.5 4B (Q4_K_XL) [Recommended SLM Pipeline]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 10,0 | `Missing Tool Call` | 19 | 95% | 11182 | 588 | 411 tok | 5556 | 137,1 tok/s | 3,16 GB | 7,53 GB | 40520 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Bad Arguments` | 20 | 100% | 14993 | 750 | 527 tok | 4951 | 134,6 tok/s | 3,18 GB | 7,49 GB | 36793 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 94% | 9265 | 299 | 510 tok | 5149 | 137,9 tok/s | 3,21 GB | 7,49 GB | 37343 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 6070 | 140,0 tok/s | 3,19 GB | 7,59 GB | 43364 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 19248 | 2406 | 2404 tok | 2339 | 123,7 tok/s | 3,22 GB | 7,54 GB | 18911 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2732 | 911 | 517 tok | 663 | 126,0 tok/s | 3,23 GB | 7,52 GB | 5262 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 87,5% | 26,2% | 40,0 | `Tool Loop` | 27 | 86% | 152946 | 5665 | 9854 tok | 3905 | 91,3 tok/s | 3,25 GB | 7,58 GB | 42794 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 42,0 | `—` | 10 | 100% | 67829 | 6783 | 7617 tok | 3172 | 101,0 tok/s | 3,26 GB | 7,64 GB | 31397 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 16 | 75% | 37876 | 2367 | 4476 tok | 2054 | 102,1 tok/s | 3,29 GB | 7,51 GB | 20123 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Missing Tool Call` | 25 | 80% | 72721 | 2909 | 5792 tok | 1505 | 83,9 tok/s | 3,30 GB | 7,42 GB | 17936 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 40,0 | `—` | 15 | 100% | 27336 | 1822 | 3194 tok | 1091 | 98,1 tok/s | 3,27 GB | 7,38 GB | 11125 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 12 | 75% | 24653 | 2054 | 3651 tok | 1273 | 102,5 tok/s | 3,27 GB | 7,37 GB | 12423 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 83,3% | 25,0% | 48,0 | `Missing Tool Call` | 19 | 80% | 98891 | 5205 | 8313 tok | 2903 | 91,1 tok/s | 3,28 GB | 7,41 GB | 31883 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 21124 | 21124 | 11319 tok | 2561 | 121,1 tok/s | 3,30 GB | 7,34 GB | 21147 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38653 | 38653 | 19905 tok | 2172 | 96,4 tok/s | 3,31 GB | 7,33 GB | 22542 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Simple Function Calling (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 15: Expected tool call 'find_real_estate' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for BFCL - Multiple Tools Selection (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 12: AST evaluation failed for 'get_battle_details': Argument 'battle' value mismatch: expected 'Battle of Stalingrad' (String), got 'Stalingrad' (String)

> ⚡ **Circuit Breaker Details for BFCL - Parallel Function Calling:** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 8: AST evaluation failed for 'patch_deployment_resource': Argument 'resource_type' value mismatch: expected 'memory_mb' (String), got 'memory' (String)

> ⚡ **Circuit Breaker Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon):** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 26,2%, 7/8 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Tool Loop`
> - Turn 5: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose) (Score: 24,0%, 4/5 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> ⚡ **Circuit Breaker Details for SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop):** 1 trip(s), 0 ping-pong loop trip(s), 0 streaming repetition trip(s), 1 semantic/no-progress loop trip(s).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 25,0%, 5/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 5: Expected tool call 'run_tests' was not invoked by model.

### Model: `Qwen 3.5 4B (Q4_K_XL) [All Local LLM without SLM Summarizer]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 12279 | 614 | 890 tok | 8112 | 132,8 tok/s | 3,33 GB | 7,37 GB | 61067 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Bad Arguments` | 20 | 100% | 15341 | 767 | 651 tok | 5598 | 128,1 tok/s | 3,34 GB | 7,39 GB | 43702 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 9235 | 298 | 496 tok | 5247 | 128,5 tok/s | 3,33 GB | 7,37 GB | 40842 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 93,3% | 28,0% | 15,0 | `Over-Calling` | 1 | 100% | 3535 | 3535 | 414 tok | 7909 | 143,1 tok/s | 3,32 GB | 7,46 GB | 55278 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 17309 | 2164 | 1786 tok | 2160 | 123,7 tok/s | 3,33 GB | 7,41 GB | 17465 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2732 | 911 | 517 tok | 624 | 130,7 tok/s | 3,30 GB | 7,43 GB | 4773 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 62,5% | 18,8% | 40,0 | `Missing Tool Call` | 19 | 57% | 91458 | 4814 | 6149 tok | 2243 | 91,8 tok/s | 3,30 GB | 7,42 GB | 24434 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ✅ PASS | 71,4% | 91,4% | 42,0 | `—` | 20 | 83% | 119710 | 5986 | 8597 tok | 3707 | 96,6 tok/s | 3,30 GB | 7,56 GB | 38367 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 12 | 75% | 20094 | 1674 | 2907 tok | 1096 | 106,3 tok/s | 3,30 GB | 7,55 GB | 10315 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ✅ PASS | 80,0% | 94,0% | 40,0 | `—` | 21 | 80% | 69979 | 3332 | 6522 tok | 2865 | 106,4 tok/s | 3,31 GB | 7,52 GB | 26922 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 40,0 | `—` | 6 | 33% | 9454 | 1576 | 1498 tok | 849 | 111,1 tok/s | 3,30 GB | 7,53 GB | 7641 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 12 | 75% | 23270 | 1939 | 3838 tok | 1373 | 111,3 tok/s | 3,30 GB | 7,57 GB | 12332 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 83,3% | 25,0% | 48,0 | `Missing Tool Call` | 17 | 80% | 82026 | 4825 | 6457 tok | 2739 | 95,3 tok/s | 3,28 GB | 7,54 GB | 28750 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20544 | 20544 | 10739 tok | 1307 | 98,4 tok/s | 3,27 GB | 7,45 GB | 13277 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38728 | 38728 | 19831 tok | 2040 | 94,2 tok/s | 3,28 GB | 7,53 GB | 21650 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Multiple Tools Selection (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 20: AST evaluation failed for 'calculate_effective_population_size': Argument 'species' value mismatch: expected 'wild tiger' (String), got 'wild tigers' (String)

> [!WARNING]
> **Partial Outcome Details for BFCL - Irrelevance (Negative Control) (Score: 28,0%, 14/15 turns passed):** 
> - End-to-end task failed: 1 of 15 turns failed validation.
> - Failure Taxonomy: `Over-Calling`
> - Turn 10: Model invoked 1 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 8: AST evaluation failed for 'patch_deployment_resource': Argument 'resource_type' value mismatch: expected 'memory_mb' (String), got 'memory_limit' (String)

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 18,8%, 5/8 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 5: Expected tool call 'apply_patch' was not invoked by model.
> - Turn 6: Expected tool call 'run_tests' was not invoked by model.
> - Turn 7: Expected tool call 'check_git_diff' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: django__django-11099 (DiffExplosion) (Score: 91,4%, 5/7 turns passed):** 
> - Turn 6: Expected tool call 'run_tests' was not invoked by model.
> - Turn 7: Model invoked 5 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose) (Score: 94,0%, 4/5 turns passed):** 
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> ⚡ **Circuit Breaker Details for SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop):** 3 trip(s), 0 ping-pong loop trip(s), 0 streaming repetition trip(s), 3 semantic/no-progress loop trip(s).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 25,0%, 5/6 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 4: Expected tool call 'apply_patch' was not invoked by model.

### Model: `Qwen 3.5 4B (Q4_K_XL) [All Local LLM with SLM Summarizer]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 11811 | 591 | 596 tok | 5776 | 134,8 tok/s | 3,29 GB | 7,62 GB | 42834 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 25,0 | `—` | 20 | 100% | 15069 | 753 | 506 tok | 5034 | 124,1 tok/s | 3,30 GB | 7,52 GB | 40576 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 8974 | 290 | 449 tok | 4996 | 124,0 tok/s | 3,30 GB | 7,58 GB | 40282 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 6679 | 137,8 tok/s | 3,29 GB | 7,60 GB | 48464 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 19899 | 2487 | 2492 tok | 2462 | 114,5 tok/s | 3,33 GB | 7,62 GB | 21511 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2732 | 911 | 517 tok | 630 | 116,4 tok/s | 3,35 GB | 7,56 GB | 5412 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 87,5% | 26,2% | 40,0 | `Tool Loop` | 24 | 71% | 135826 | 5659 | 8571 tok | 3753 | 89,4 tok/s | 3,37 GB | 7,61 GB | 41986 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 42,0 | `—` | 15 | 100% | 97209 | 6481 | 8007 tok | 3186 | 93,2 tok/s | 3,33 GB | 7,56 GB | 34192 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 7 | 75% | 12936 | 1848 | 2707 tok | 1101 | 112,4 tok/s | 3,34 GB | 7,54 GB | 9796 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Missing Tool Call` | 25 | 80% | 78969 | 3159 | 6244 tok | 2594 | 99,0 tok/s | 3,33 GB | 7,58 GB | 26205 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 40,0 | `—` | 12 | 67% | 18145 | 1512 | 2679 tok | 910 | 105,7 tok/s | 3,34 GB | 7,61 GB | 8609 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Patch Rejected` | 9 | 100% | 19316 | 2146 | 3678 tok | 1681 | 124,2 tok/s | 3,34 GB | 7,61 GB | 13539 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 48,0 | `Patch Rejected` | 21 | 100% | 100744 | 4797 | 8220 tok | 3146 | 92,7 tok/s | 3,36 GB | 7,52 GB | 33930 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20297 | 20297 | 10492 tok | 879 | 90,8 tok/s | 3,33 GB | 7,51 GB | 9679 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 39001 | 39001 | 20253 tok | 2174 | 91,4 tok/s | 3,33 GB | 7,58 GB | 23789 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 8: AST evaluation failed for 'patch_deployment_resource': Argument 'resource_type' value mismatch: expected 'memory_mb' (String), got 'memory_limit' (String)

> ⚡ **Circuit Breaker Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon):** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 26,2%, 7/8 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Tool Loop`
> - Turn 5: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose) (Score: 24,0%, 4/5 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> ⚡ **Circuit Breaker Details for SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop):** 2 trip(s), 0 ping-pong loop trip(s), 0 streaming repetition trip(s), 2 semantic/no-progress loop trip(s).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 30,0%, 4/4 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

> ✂️ **Compaction Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion):** 4,360 chars pruned (~1,090 tokens), 4 thinking traces collapsed, 0 tool results truncated, 0 summarized (Speculative Hit Rate: 0.0%).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 30,0%, 6/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

### Model: `Qwen 3.5 4B (Q4_K_XL) [All Optimizations Combined]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 10,0 | `Missing Tool Call` | 19 | 95% | 11196 | 589 | 431 tok | 7449 | 133,4 tok/s | 3,35 GB | 7,64 GB | 55827 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 25,0 | `—` | 20 | 100% | 15022 | 751 | 505 tok | 4966 | 130,3 tok/s | 3,36 GB | 7,53 GB | 38123 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 94% | 8862 | 286 | 412 tok | 4289 | 135,0 tok/s | 3,35 GB | 7,76 GB | 31775 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 6878 | 144,3 tok/s | 3,35 GB | 7,62 GB | 47679 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 18508 | 2314 | 2382 tok | 2226 | 121,6 tok/s | 3,36 GB | 7,56 GB | 18304 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2923 | 974 | 708 tok | 836 | 127,8 tok/s | 3,36 GB | 7,59 GB | 6544 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Patch Rejected` | 27 | 100% | 146440 | 5424 | 9206 tok | 4278 | 96,2 tok/s | 3,39 GB | 7,62 GB | 44464 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 42,0 | `Tool Loop` | 21 | 100% | 134859 | 6422 | 9940 tok | 3370 | 86,2 tok/s | 3,43 GB | 7,74 GB | 39076 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 7 | 75% | 11861 | 1694 | 2576 tok | 992 | 116,8 tok/s | 3,40 GB | 7,71 GB | 8495 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 40,0 | `—` | 14 | 100% | 38896 | 2778 | 4503 tok | 1810 | 106,8 tok/s | 3,40 GB | 7,73 GB | 16953 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 42267 | 2113 | 3936 tok | 1441 | 98,0 tok/s | 3,41 GB | 7,71 GB | 14702 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Tool Loop` | 14 | 50% | 29412 | 2101 | 3735 tok | 1178 | 102,2 tok/s | 3,41 GB | 7,71 GB | 11525 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 48,0 | `Patch Rejected` | 21 | 100% | 136596 | 6505 | 11970 tok | 4885 | 99,7 tok/s | 3,41 GB | 7,70 GB | 49021 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20156 | 20156 | 10351 tok | 721 | 89,1 tok/s | 3,40 GB | 7,62 GB | 8090 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38320 | 38320 | 19463 tok | 1258 | 84,3 tok/s | 3,37 GB | 7,60 GB | 14928 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Simple Function Calling (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 12: Expected tool call 'calculate_displacement' was not invoked by model.

> ⚡ **Circuit Breaker Details for BFCL - Parallel Function Calling:** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 8: AST evaluation failed for 'patch_deployment_resource': Argument 'resource_type' value mismatch: expected 'memory_mb' (String), got 'memory_limit' (String)

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 30,0%, 8/8 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

> ⚡ **Circuit Breaker Details for SWE-bench Replay: django__django-11099 (DiffExplosion):** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: django__django-11099 (DiffExplosion) (Score: 30,0%, 7/7 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Tool Loop`

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: Circuit breaker did not trip to break out of repetitive tool loop.
> - Failure Taxonomy: `Over-Calling`
> - Turn 4: Model invoked 5 tool call(s) when 0 were expected.

> ⚡ **Circuit Breaker Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl):** 1 trip(s), 0 ping-pong loop trip(s), 0 streaming repetition trip(s), 1 semantic/no-progress loop trip(s).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Tool Loop`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 30,0%, 6/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

