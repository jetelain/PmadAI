# Pmad.AI Tool Calling Benchmark Report

Generated on: 2026-10-19 11:39:43 UTC

## Environment

- **CPU:** Intel(R) Core(TM) i7-14700KF (28 logical cores)
- **GPU:** NVIDIA GeForce RTX 4080 SUPER, 15,99 GB VRAM, Driver: 610.88
- **OS:** Microsoft Windows 10.0.26200

> [!NOTE]
> 📄 **Execution Transcripts:** Individual turn-by-turn verbatim transcripts and failure analyses for each configuration are saved in the `transcripts/` directory and linked directly in the tables below.

## Summary Comparison

| Model | Backend | Reasoning | Scenarios Passed | Pass@1 (Wtd) | Pass@1 (Unwtd) | Step Acc | Pass Rate | Tool Calls | Gen Tokens | Avg Speed (tok/s) | Sys RAM | GPU VRAM | Total Time |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| [`Qwen 3.5 4B (Q4_K_XL) [Baseline (Raw)]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_baseline.transcript.md) | Vulkan | Default | 6/15 | 32,8% | 46,7% | 91,4% | 50,4% | 227 | 46096 | 118,6 | 3,11 GB | 7,56 GB | 388,67s |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Call Healing]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing.transcript.md) | Vulkan | Default | 6/15 | 32,8% | 40,0% | 81,8% | 47,5% | 189 | 43896 | 126,6 | 3,14 GB | 7,56 GB | 346,59s |
| [`Qwen 3.5 4B (Q4_K_XL) [Thinking Trace Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-thinking-compaction.transcript.md) | Vulkan | Default | 7/15 | 32,8% | 46,7% | 89,4% | 49,8% | 232 | 46827 | 117,2 | 3,16 GB | 7,56 GB | 399,40s |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Result Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-tool-compaction.transcript.md) | Vulkan | Default | 6/15 | 35,8% | 46,7% | 82,7% | 49,9% | 239 | 53129 | 112,4 | 3,19 GB | 7,67 GB | 472,47s |
| [`Qwen 3.5 4B (Q4_K_XL) [Speculative SLM Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-speculative-slm.transcript.md) | Vulkan | Default | 5/15 | 21,0% | 33,3% | 82,2% | 39,4% | 248 | 47327 | 108,4 | 3,28 GB | 7,60 GB | 436,66s |
| [`Qwen 3.5 4B (Q4_K_XL) [Lenient Dispatch & Guardrails]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-lenient-dispatch.transcript.md) | Vulkan | Default | 8/15 | 41,9% | 53,3% | 92,3% | 57,1% | 222 | 53302 | 118,8 | 3,24 GB | 7,94 GB | 448,79s |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Lenient Dispatch]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md) | Vulkan | Default | 6/15 | 32,8% | 46,7% | 88,5% | 49,5% | 215 | 46044 | 119,1 | 3,21 GB | 7,79 GB | 386,70s |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Tool Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-tool-compaction.transcript.md) | Vulkan | Default | 5/15 | 36,2% | 40,0% | 88,3% | 51,9% | 194 | 42732 | 119,7 | 3,28 GB | 7,80 GB | 356,91s |
| [`Qwen 3.5 4B (Q4_K_XL) [Recommended SLM Pipeline]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md) | Vulkan | Default | 5/15 | 21,0% | 33,3% | 88,1% | 41,2% | 207 | 43859 | 120,7 | 3,30 GB | 7,79 GB | 363,37s |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM without SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md) | Vulkan | Default | 7/15 | 36,2% | 46,7% | 84,5% | 50,7% | 206 | 44476 | 119,9 | 3,37 GB | 7,72 GB | 370,87s |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM with SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | Vulkan | Default | 6/15 | 23,3% | 40,0% | 88,5% | 42,8% | 228 | 49625 | 118,3 | 3,37 GB | 7,72 GB | 419,60s |
| [`Qwen 3.5 4B (Q4_K_XL) [All Optimizations Combined]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md) | Vulkan | Default | 6/15 | 26,7% | 40,0% | 85,9% | 44,5% | 222 | 48581 | 119,8 | 3,45 GB | 7,72 GB | 405,51s |

## Benchmark Suite Breakdown (BFCL / SWE-bench / RULER)

> Evaluates performance separated by benchmark domain: schema adherence and tool call syntax (BFCL), multi-turn agentic software engineering (SWE-bench), and needle-in-a-haystack retrieval (RULER).

| Model | Suite | Scenarios | Pass@1 (Task Success) | Step Acc | Turns Passed | Turn Acc | Weighted Pass Rate |
|---|---|---|---|---|---|---|---|
| [`Qwen 3.5 4B (Q4_K_XL) [Baseline (Raw)]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_baseline.transcript.md) | **BFCL** | 4/6 | 56,8% | 96,6% | 80/82 | 97,6% | 68,8% |
| [`Qwen 3.5 4B (Q4_K_XL) [Baseline (Raw)]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_baseline.transcript.md) | **SWE-bench** | 1/7 | 14,5% | 88,3% | 34/38 | 89,5% | 36,6% |
| [`Qwen 3.5 4B (Q4_K_XL) [Baseline (Raw)]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_baseline.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Call Healing]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing.transcript.md) | **BFCL** | 3/6 | 56,8% | 96,4% | 79/82 | 96,3% | 68,7% |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Call Healing]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing.transcript.md) | **SWE-bench** | 1/7 | 14,5% | 73,8% | 28/38 | 73,7% | 32,3% |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Call Healing]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Thinking Trace Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-thinking-compaction.transcript.md) | **BFCL** | 4/6 | 56,8% | 96,6% | 80/82 | 97,6% | 68,8% |
| [`Qwen 3.5 4B (Q4_K_XL) [Thinking Trace Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-thinking-compaction.transcript.md) | **SWE-bench** | 1/7 | 14,5% | 85,2% | 33/38 | 86,8% | 35,7% |
| [`Qwen 3.5 4B (Q4_K_XL) [Thinking Trace Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-thinking-compaction.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Result Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-tool-compaction.transcript.md) | **BFCL** | 4/6 | 70,5% | 97,3% | 80/82 | 97,6% | 78,5% |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Result Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-tool-compaction.transcript.md) | **SWE-bench** | 1/7 | 13,8% | 74,8% | 29/38 | 76,3% | 32,1% |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Result Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-tool-compaction.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Speculative SLM Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-speculative-slm.transcript.md) | **BFCL** | 3/6 | 47,7% | 96,1% | 79/82 | 96,3% | 62,3% |
| [`Qwen 3.5 4B (Q4_K_XL) [Speculative SLM Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-speculative-slm.transcript.md) | **SWE-bench** | 0/7 | 0,0% | 74,5% | 29/38 | 76,3% | 22,3% |
| [`Qwen 3.5 4B (Q4_K_XL) [Speculative SLM Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-speculative-slm.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Lenient Dispatch & Guardrails]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-lenient-dispatch.transcript.md) | **BFCL** | 4/6 | 56,8% | 96,6% | 80/82 | 97,6% | 68,8% |
| [`Qwen 3.5 4B (Q4_K_XL) [Lenient Dispatch & Guardrails]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-lenient-dispatch.transcript.md) | **SWE-bench** | 2/7 | 28,3% | 89,7% | 35/38 | 92,1% | 46,7% |
| [`Qwen 3.5 4B (Q4_K_XL) [Lenient Dispatch & Guardrails]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-lenient-dispatch.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Lenient Dispatch]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md) | **BFCL** | 4/6 | 56,8% | 96,6% | 80/82 | 97,6% | 68,8% |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Lenient Dispatch]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md) | **SWE-bench** | 1/7 | 14,5% | 83,8% | 32/38 | 84,2% | 35,3% |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Lenient Dispatch]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Tool Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-tool-compaction.transcript.md) | **BFCL** | 2/6 | 34,1% | 95,2% | 78/82 | 95,1% | 52,4% |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Tool Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-tool-compaction.transcript.md) | **SWE-bench** | 2/7 | 28,3% | 84,1% | 33/38 | 86,8% | 45,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Tool Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-tool-compaction.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Recommended SLM Pipeline]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md) | **BFCL** | 3/6 | 47,7% | 96,1% | 79/82 | 96,3% | 62,3% |
| [`Qwen 3.5 4B (Q4_K_XL) [Recommended SLM Pipeline]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md) | **SWE-bench** | 0/7 | 0,0% | 83,4% | 32/38 | 84,2% | 25,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Recommended SLM Pipeline]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM without SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md) | **BFCL** | 4/6 | 70,5% | 97,3% | 80/82 | 97,6% | 78,5% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM without SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md) | **SWE-bench** | 1/7 | 14,5% | 77,6% | 30/38 | 78,9% | 33,4% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM without SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM with SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | **BFCL** | 4/6 | 56,8% | 96,6% | 80/82 | 97,6% | 68,8% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM with SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | **SWE-bench** | 0/7 | 0,0% | 83,8% | 32/38 | 84,2% | 25,1% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM with SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Optimizations Combined]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md) | **BFCL** | 4/6 | 70,5% | 97,3% | 80/82 | 97,6% | 78,5% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Optimizations Combined]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md) | **SWE-bench** | 0/7 | 0,0% | 79,7% | 31/38 | 81,6% | 23,9% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Optimizations Combined]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |

## Pmad.AI Optimization Matrix: Empirical Utility Gains

> Quantifies prompt token reduction, context ceiling capping, execution time savings, and accuracy improvements achieved by Pmad.AI utilities vs. unassisted baseline model execution.

| Model | Configuration | Pass Rate | Prompt Tokens | Token Savings % | Peak Context | Context Reduction % | Gen Speed | Duration | Time Savings % |
|---|---|---|---|---|---|---|---|---|---|
| `Qwen 3.5 4B (Q4_K_XL)` | [**Baseline (Raw)**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_baseline.transcript.md) | 50,4% | 730948 | — | 19434 tok | — | 118,6 tok/s | 388,67s | — |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Tool Call Healing**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing.transcript.md) | 47,5% | 459208 | **-37,2%** | 19533 tok | +0,5% | 126,6 tok/s | 346,59s | **-10,8%** |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Thinking Trace Compaction**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-thinking-compaction.transcript.md) | 49,8% | 713531 | **-2,4%** | 19595 tok | +0,8% | 117,2 tok/s | 399,40s | +2,8% |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Tool Result Compaction**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-tool-compaction.transcript.md) | 49,9% | 783258 | +7,2% | 19475 tok | +0,2% | 112,4 tok/s | 472,47s | +21,6% |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Speculative SLM Compaction**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-speculative-slm.transcript.md) | 39,4% | 816171 | +11,7% | 19855 tok | +2,2% | 108,4 tok/s | 436,66s | +12,3% |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Lenient Dispatch & Guardrails**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-lenient-dispatch.transcript.md) | 57,1% | 720306 | **-1,5%** | 19882 tok | +2,3% | 118,8 tok/s | 448,79s | +15,5% |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Healing + Lenient Dispatch**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md) | 49,5% | 709616 | **-2,9%** | 19863 tok | +2,2% | 119,1 tok/s | 386,70s | **-0,5%** |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Healing + Tool Compaction**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-tool-compaction.transcript.md) | 51,9% | 588582 | **-19,5%** | 19749 tok | +1,6% | 119,7 tok/s | 356,91s | **-8,2%** |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Recommended SLM Pipeline**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md) | 41,2% | 545558 | **-25,4%** | 19840 tok | +2,1% | 120,7 tok/s | 363,37s | **-6,5%** |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Local LLM without SLM Summarizer**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md) | 50,7% | 598474 | **-18,1%** | 19188 tok | **-1,3%** | 119,9 tok/s | 370,87s | **-4,6%** |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Local LLM with SLM Summarizer**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | 42,8% | 709639 | **-2,9%** | 19481 tok | +0,2% | 118,3 tok/s | 419,60s | +8,0% |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Optimizations Combined**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md) | 44,5% | 634962 | **-13,1%** | 19955 tok | +2,7% | 119,8 tok/s | 405,51s | +4,3% |

## Compaction Efficiency & Telemetry Matrix

> Reports characters and tokens pruned, speculative SLM hit rate, and breakdown of collapsed reasoning traces vs. truncated/summarized tool results.

| Model | Configuration | Pruned Chars | Pruned Tokens | Speculative Hit Rate | Thinking Traces Collapsed | Tool Results Truncated | Tool Results Summarized | Total Events |
|---|---|---|---|---|---|---|---|---|
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Local LLM without SLM Summarizer**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md) | 2,790 | 698 | 0.0% (0/6) | 6 | 0 | 0 | 6 |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Local LLM with SLM Summarizer**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | 1,908 | 477 | 0.0% (0/6) | 6 | 0 | 0 | 6 |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Optimizations Combined**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md) | 10 | 2 | 0.0% (0/10) | 10 | 0 | 0 | 10 |

## Pmad.AI Utility Interventions & Telemetry Matrix

> Quantifies runtime interventions by Pmad.AI components: Tool Call Healing (malformed JSON, pseudo-tag extractions, fence strippings), Circuit Breaker (loop and ping-pong interceptions), and Lenient Dispatch (tool/parameter normalization).

| Model | Configuration | Healing Total | Pseudo-Tags | JSON Repairs | Breaker Total | Breaker Loops | Repetitions | Dispatch Total | Alias Hits | Param Hits | Casing/Norm |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `Qwen 3.5 4B (Q4_K_XL)` | [**Healing + Lenient Dispatch**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md) | 0 | 0 | 0 | 1 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Recommended SLM Pipeline**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md) | 0 | 0 | 0 | 2 | 2 | 0 | 0 | 0 | 0 | 0 |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Local LLM without SLM Summarizer**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md) | 0 | 0 | 0 | 1 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Local LLM with SLM Summarizer**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | 0 | 0 | 0 | 3 | 3 | 0 | 0 | 0 | 0 | 0 |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Optimizations Combined**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md) | 0 | 0 | 0 | 3 | 3 | 0 | 0 | 0 | 0 | 0 |

## Detailed Scenario Breakdown

### Model: `Qwen 3.5 4B (Q4_K_XL) [Baseline (Raw)]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_baseline.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 12173 | 609 | 727 tok | 5936 | 141,2 tok/s | 3,09 GB | 7,37 GB | 42035 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Bad Arguments` | 20 | 100% | 15101 | 755 | 570 tok | 4863 | 137,1 tok/s | 3,10 GB | 7,48 GB | 35475 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 9079 | 293 | 514 tok | 5514 | 145,2 tok/s | 3,10 GB | 7,47 GB | 37971 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 5543 | 149,7 tok/s | 3,10 GB | 7,49 GB | 37025 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Missing Tool Call` | 7 | 88% | 16179 | 2311 | 1626 tok | 2101 | 128,2 tok/s | 3,10 GB | 7,48 GB | 16394 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2734 | 911 | 517 tok | 637 | 134,2 tok/s | 3,10 GB | 7,47 GB | 4748 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Patch Rejected` | 23 | 100% | 124723 | 5423 | 8614 tok | 3924 | 103,1 tok/s | 3,10 GB | 7,48 GB | 38056 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ✅ PASS | 85,7% | 95,7% | 42,0 | `—` | 23 | 100% | 153819 | 6688 | 10231 tok | 4284 | 93,9 tok/s | 3,10 GB | 7,55 GB | 45619 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 12 | 75% | 31961 | 2663 | 3892 tok | 1979 | 119,5 tok/s | 3,10 GB | 7,55 GB | 16559 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Missing Tool Call` | 25 | 80% | 88259 | 3530 | 6340 tok | 2518 | 100,0 tok/s | 3,11 GB | 7,55 GB | 25189 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 43509 | 2175 | 3944 tok | 1473 | 102,9 tok/s | 3,11 GB | 7,54 GB | 14310 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Patch Rejected` | 20 | 100% | 53936 | 2697 | 5197 tok | 1752 | 103,4 tok/s | 3,10 GB | 7,55 GB | 16941 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 48,0 | `Patch Rejected` | 21 | 100% | 118110 | 5624 | 10586 tok | 3603 | 98,9 tok/s | 3,10 GB | 7,56 GB | 36421 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20062 | 20062 | 10257 tok | 671 | 92,8 tok/s | 3,10 GB | 7,53 GB | 7234 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38182 | 38182 | 19434 tok | 1298 | 88,3 tok/s | 3,11 GB | 7,54 GB | 14697 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Multiple Tools Selection (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 12: AST evaluation failed for 'get_battle_details': Argument 'battle' value mismatch: expected 'Battle of Stalingrad' (String), got 'Stalingrad' (String)

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 8: Expected tool call 'patch_deployment_resource' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 30,0%, 8/8 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: django__django-11099 (DiffExplosion) (Score: 95,7%, 6/7 turns passed):** 
> - Turn 7: Model invoked 1 tool call(s) when 0 were expected.

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
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 30,0%, 4/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Patch Rejected`

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 30,0%, 6/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

### Model: `Qwen 3.5 4B (Q4_K_XL) [Tool Call Healing]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 10,0 | `Missing Tool Call` | 19 | 95% | 11183 | 589 | 464 tok | 7216 | 143,7 tok/s | 3,13 GB | 7,54 GB | 50214 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 25,0 | `—` | 20 | 100% | 15384 | 769 | 589 tok | 4994 | 137,3 tok/s | 3,13 GB | 7,51 GB | 36366 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 9542 | 308 | 528 tok | 5601 | 143,6 tok/s | 3,12 GB | 7,46 GB | 39001 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 93,3% | 28,0% | 15,0 | `Validation Failure` | 0 | 100% | 3121 | — | 240 tok | 7313 | 150,4 tok/s | 3,12 GB | 7,48 GB | 48637 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Missing Tool Call` | 7 | 88% | 15389 | 2198 | 1702 tok | 1993 | 127,5 tok/s | 3,13 GB | 7,46 GB | 15629 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2734 | 911 | 517 tok | 690 | 135,7 tok/s | 3,12 GB | 7,45 GB | 5083 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 50,0% | 15,0% | 40,0 | `Missing Tool Call` | 6 | 43% | 34085 | 5681 | 3371 tok | 1152 | 118,1 tok/s | 3,12 GB | 7,45 GB | 9754 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 42,0 | `—` | 11 | 100% | 65096 | 5918 | 6713 tok | 2616 | 106,9 tok/s | 3,13 GB | 7,44 GB | 24478 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 12 | 75% | 20053 | 1671 | 2887 tok | 1069 | 111,3 tok/s | 3,14 GB | 7,44 GB | 9603 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Missing Tool Call` | 25 | 80% | 62812 | 2512 | 4890 tok | 1589 | 95,1 tok/s | 3,14 GB | 7,46 GB | 16710 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 44461 | 2223 | 4090 tok | 1589 | 104,8 tok/s | 3,14 GB | 7,46 GB | 15157 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 50,0% | 15,0% | 40,0 | `Missing Tool Call` | 13 | 50% | 23117 | 1778 | 3147 tok | 3029 | 132,0 tok/s | 3,13 GB | 7,56 GB | 22948 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 83,3% | 25,0% | 48,0 | `Missing Tool Call` | 20 | 80% | 93461 | 4673 | 7787 tok | 2183 | 89,8 tok/s | 3,12 GB | 7,52 GB | 24298 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20489 | 20489 | 10572 tok | 1469 | 114,9 tok/s | 3,12 GB | 7,39 GB | 12789 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38281 | 38281 | 19533 tok | 1393 | 87,5 tok/s | 3,08 GB | 7,49 GB | 15926 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Simple Function Calling (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 15: Expected tool call 'find_real_estate' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for BFCL - Irrelevance (Negative Control) (Score: 28,0%, 14/15 turns passed):** 
> - End-to-end task failed: 1 of 15 turns failed validation.
> - Failure Taxonomy: `Validation Failure`
> - Turn 2: Expected direct conversational text response, but assistant returned empty output.

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 8: Expected tool call 'patch_deployment_resource' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 15,0%, 4/8 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 4: Expected tool call 'search_code' was not invoked by model.
> - Turn 5: Expected tool call 'apply_patch' was not invoked by model.
> - Turn 6: Expected tool call 'run_tests' was not invoked by model.
> - Turn 7: Expected tool call 'check_git_diff' was not invoked by model.

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
> - Turn 2: Expected tool call 'run_tests' was not invoked by model.
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 25,0%, 5/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 1: Expected tool call 'run_tests' was not invoked by model.

### Model: `Qwen 3.5 4B (Q4_K_XL) [Thinking Trace Compaction]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-thinking-compaction.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 12115 | 606 | 577 tok | 6105 | 138,0 tok/s | 3,12 GB | 7,56 GB | 44228 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Bad Arguments` | 21 | 100% | 15865 | 756 | 596 tok | 5627 | 138,8 tok/s | 3,13 GB | 7,46 GB | 40536 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 8904 | 287 | 412 tok | 4812 | 141,4 tok/s | 3,13 GB | 7,42 GB | 34033 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 5338 | 146,1 tok/s | 3,12 GB | 7,47 GB | 36535 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 19178 | 2397 | 2393 tok | 2366 | 126,2 tok/s | 3,15 GB | 7,40 GB | 18741 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2734 | 911 | 517 tok | 478 | 128,8 tok/s | 3,15 GB | 7,40 GB | 3710 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 87,5% | 26,2% | 40,0 | `Missing Tool Call` | 27 | 86% | 137234 | 5083 | 8173 tok | 3245 | 92,2 tok/s | 3,16 GB | 7,55 GB | 35187 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 42,0 | `—` | 17 | 100% | 106387 | 6258 | 8757 tok | 3608 | 101,8 tok/s | 3,14 GB | 7,46 GB | 35458 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 19 | 75% | 50583 | 2662 | 4307 tok | 1712 | 100,6 tok/s | 3,16 GB | 7,46 GB | 17010 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Missing Tool Call` | 25 | 80% | 82077 | 3283 | 5961 tok | 2468 | 92,1 tok/s | 3,16 GB | 7,49 GB | 26800 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 41110 | 2056 | 4016 tok | 1558 | 103,6 tok/s | 3,15 GB | 7,44 GB | 15039 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 18 | 75% | 45529 | 2529 | 4289 tok | 3373 | 124,3 tok/s | 3,14 GB | 7,50 GB | 27143 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 48,0 | `Patch Rejected` | 21 | 100% | 129538 | 6168 | 11283 tok | 2998 | 87,1 tok/s | 3,15 GB | 7,51 GB | 34401 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20813 | 20813 | 11008 tok | 2178 | 118,5 tok/s | 3,14 GB | 7,56 GB | 18385 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38343 | 38343 | 19595 tok | 961 | 78,8 tok/s | 3,14 GB | 7,55 GB | 12192 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Multiple Tools Selection (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 13: Tool call count mismatch: expected 1 call(s), but model generated 2.

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
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 30,0%, 6/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

### Model: `Qwen 3.5 4B (Q4_K_XL) [Tool Result Compaction]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-tool-compaction.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 10,0 | `Bad Arguments` | 22 | 100% | 15093 | 686 | 1556 tok | 7498 | 139,1 tok/s | 3,16 GB | 7,58 GB | 53904 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 25,0 | `—` | 20 | 100% | 15016 | 751 | 674 tok | 4634 | 129,4 tok/s | 3,18 GB | 7,67 GB | 35804 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 9518 | 307 | 588 tok | 5143 | 130,8 tok/s | 3,17 GB | 7,56 GB | 39315 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 5484 | 134,6 tok/s | 3,16 GB | 7,53 GB | 40738 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 20149 | 2519 | 2636 tok | 2583 | 116,6 tok/s | 3,17 GB | 7,54 GB | 22151 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2734 | 911 | 517 tok | 548 | 122,0 tok/s | 3,19 GB | 7,51 GB | 4493 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 87,5% | 26,2% | 40,0 | `Missing Tool Call` | 27 | 86% | 137819 | 5104 | 7996 tok | 3345 | 88,3 tok/s | 3,19 GB | 7,55 GB | 37868 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 71,4% | 21,4% | 42,0 | `Over-Calling` | 29 | 83% | 188729 | 6508 | 11952 tok | 8937 | 99,3 tok/s | 3,19 GB | 7,59 GB | 89970 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 13 | 75% | 30772 | 2367 | 4203 tok | 1896 | 104,4 tok/s | 3,17 GB | 7,55 GB | 18166 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ✅ PASS | 80,0% | 94,0% | 40,0 | `—` | 25 | 80% | 82311 | 3292 | 6915 tok | 2877 | 101,3 tok/s | 3,17 GB | 7,52 GB | 28409 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 41494 | 2075 | 3827 tok | 1373 | 95,5 tok/s | 3,15 GB | 7,50 GB | 14377 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 50,0% | 15,0% | 40,0 | `Missing Tool Call` | 13 | 50% | 29902 | 2300 | 3824 tok | 3495 | 127,8 tok/s | 3,16 GB | 7,48 GB | 27346 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 83,3% | 25,0% | 48,0 | `Over-Calling` | 26 | 100% | 148247 | 5702 | 10028 tok | 3750 | 91,8 tok/s | 3,16 GB | 7,48 GB | 40833 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20130 | 20130 | 10325 tok | 718 | 94,3 tok/s | 3,16 GB | 7,48 GB | 7617 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38223 | 38223 | 19475 tok | 848 | 73,9 tok/s | 3,16 GB | 7,51 GB | 11481 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Simple Function Calling (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 6: Tool call count mismatch: expected 1 call(s), but model generated 3.

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 8: AST evaluation failed for 'patch_deployment_resource': Argument 'resource_type' value mismatch: expected 'memory_mb' (String), got 'memory_limit' (String)

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 26,2%, 7/8 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 5: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: django__django-11099 (DiffExplosion) (Score: 21,4%, 5/7 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Over-Calling`
> - Turn 5: Expected tool call 'check_git_diff' was not invoked by model.
> - Turn 7: Model invoked 5 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose) (Score: 94,0%, 4/5 turns passed):** 
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
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Over-Calling`
> - Turn 6: Model invoked 5 tool call(s) when 0 were expected.

### Model: `Qwen 3.5 4B (Q4_K_XL) [Speculative SLM Compaction]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-speculative-slm.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 10,0 | `Missing Tool Call` | 19 | 95% | 11387 | 599 | 428 tok | 5678 | 138,9 tok/s | 3,19 GB | 7,50 GB | 40876 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Missing Tool Call` | 19 | 95% | 14414 | 759 | 501 tok | 4633 | 134,0 tok/s | 3,21 GB | 7,49 GB | 34575 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 9141 | 295 | 527 tok | 4752 | 139,3 tok/s | 3,20 GB | 7,47 GB | 34120 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 5846 | 147,2 tok/s | 3,21 GB | 7,47 GB | 39702 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 19023 | 2378 | 2391 tok | 2383 | 116,8 tok/s | 3,22 GB | 7,52 GB | 20405 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2734 | 911 | 517 tok | 488 | 114,8 tok/s | 3,23 GB | 7,52 GB | 4253 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Patch Rejected` | 27 | 100% | 150929 | 5590 | 9400 tok | 3801 | 85,2 tok/s | 3,21 GB | 7,55 GB | 44591 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 71,4% | 21,4% | 42,0 | `Over-Calling` | 31 | 83% | 211928 | 6836 | 12116 tok | 6086 | 84,2 tok/s | 3,22 GB | 7,57 GB | 72244 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 17 | 75% | 39942 | 2350 | 4154 tok | 1731 | 99,8 tok/s | 3,24 GB | 7,60 GB | 17350 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Missing Tool Call` | 25 | 80% | 76529 | 3061 | 5439 tok | 1828 | 86,6 tok/s | 3,25 GB | 7,54 GB | 21106 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 42015 | 2101 | 3851 tok | 1399 | 93,2 tok/s | 3,26 GB | 7,57 GB | 15007 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 20 | 75% | 53182 | 2659 | 5754 tok | 2621 | 106,3 tok/s | 3,28 GB | 7,58 GB | 24659 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 50,0% | 15,0% | 48,0 | `Over-Calling` | 26 | 60% | 123189 | 4738 | 8357 tok | 3290 | 88,1 tok/s | 3,26 GB | 7,53 GB | 37351 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20034 | 20034 | 10229 tok | 689 | 86,8 tok/s | 3,26 GB | 7,50 GB | 7940 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38603 | 38603 | 19855 tok | 2102 | 93,5 tok/s | 3,25 GB | 7,56 GB | 22480 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Simple Function Calling (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 16: Expected tool call 'calculate_grade_average' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for BFCL - Multiple Tools Selection (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 9: Expected tool call 'realestate_find_properties' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 8: AST evaluation failed for 'patch_deployment_resource': Argument 'resource_type' value mismatch: expected 'memory_mb' (String), got 'memory_limit' (String)

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 30,0%, 8/8 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: django__django-11099 (DiffExplosion) (Score: 21,4%, 5/7 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Over-Calling`
> - Turn 5: Expected tool call 'check_git_diff' was not invoked by model.
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
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 4: Expected tool call 'run_tests' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 15,0%, 3/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Over-Calling`
> - Turn 3: Expected tool call 'run_tests' was not invoked by model.
> - Turn 5: Expected tool call 'run_tests' was not invoked by model.
> - Turn 6: Model invoked 5 tool call(s) when 0 were expected.

### Model: `Qwen 3.5 4B (Q4_K_XL) [Lenient Dispatch & Guardrails]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-lenient-dispatch.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 11729 | 586 | 575 tok | 6273 | 133,7 tok/s | 3,24 GB | 7,64 GB | 46914 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Bad Arguments` | 20 | 100% | 15265 | 763 | 561 tok | 5640 | 134,0 tok/s | 3,21 GB | 7,85 GB | 42091 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 9478 | 306 | 525 tok | 4869 | 134,8 tok/s | 3,21 GB | 7,94 GB | 36109 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 5931 | 148,3 tok/s | 3,21 GB | 7,77 GB | 39992 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Missing Tool Call` | 7 | 88% | 16462 | 2352 | 1640 tok | 2124 | 126,4 tok/s | 3,21 GB | 7,86 GB | 16808 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2734 | 911 | 517 tok | 613 | 125,9 tok/s | 3,21 GB | 7,83 GB | 4867 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Patch Rejected` | 26 | 100% | 150723 | 5797 | 10318 tok | 5012 | 98,8 tok/s | 3,22 GB | 7,83 GB | 50745 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 42,0 | `—` | 22 | 100% | 137474 | 6249 | 9626 tok | 3880 | 92,4 tok/s | 3,12 GB | 7,69 GB | 42011 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 17 | 75% | 36766 | 2163 | 3628 tok | 1302 | 102,9 tok/s | 3,15 GB | 7,65 GB | 12658 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 40,0 | `—` | 19 | 100% | 80652 | 4245 | 8963 tok | 5270 | 117,9 tok/s | 3,17 GB | 7,72 GB | 44701 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 47133 | 2357 | 4347 tok | 1758 | 107,3 tok/s | 3,16 GB | 7,72 GB | 16378 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 17 | 75% | 37922 | 2231 | 4358 tok | 3403 | 129,7 tok/s | 3,17 GB | 7,72 GB | 26235 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 48,0 | `Patch Rejected` | 18 | 100% | 111672 | 6204 | 8843 tok | 3386 | 98,5 tok/s | 3,17 GB | 7,75 GB | 34370 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20545 | 20545 | 10740 tok | 1741 | 119,7 tok/s | 3,16 GB | 7,75 GB | 14542 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38630 | 38630 | 19882 tok | 2100 | 103,1 tok/s | 3,15 GB | 7,75 GB | 20370 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Multiple Tools Selection (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 20: AST evaluation failed for 'calculate_effective_population_size': Argument 'species' value mismatch: expected 'wild tiger' (String), got 'tiger' (String)

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 8: Expected tool call 'patch_deployment_resource' was not invoked by model.

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
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 30,0%, 6/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

### Model: `Qwen 3.5 4B (Q4_K_XL) [Healing + Lenient Dispatch]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 11809 | 590 | 474 tok | 5737 | 143,2 tok/s | 3,16 GB | 7,75 GB | 40052 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Bad Arguments` | 20 | 100% | 15276 | 764 | 585 tok | 5661 | 132,1 tok/s | 3,17 GB | 7,75 GB | 42844 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 9480 | 306 | 522 tok | 4653 | 140,0 tok/s | 3,18 GB | 7,70 GB | 33227 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 6077 | 148,2 tok/s | 3,17 GB | 7,79 GB | 41002 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 17189 | 2149 | 1778 tok | 2015 | 126,5 tok/s | 3,17 GB | 7,76 GB | 15930 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2734 | 911 | 517 tok | 643 | 135,1 tok/s | 3,17 GB | 7,76 GB | 4759 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 87,5% | 26,2% | 40,0 | `Tool Loop` | 31 | 86% | 191269 | 6170 | 10612 tok | 3900 | 89,8 tok/s | 3,12 GB | 7,73 GB | 43411 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ✅ PASS | 85,7% | 95,7% | 42,0 | `—` | 15 | 100% | 106714 | 7114 | 8794 tok | 3176 | 97,3 tok/s | 3,14 GB | 7,70 GB | 32650 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 15 | 75% | 33287 | 2219 | 3629 tok | 1396 | 110,1 tok/s | 3,18 GB | 7,71 GB | 12684 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 60,0% | 18,0% | 40,0 | `Missing Tool Call` | 20 | 60% | 56873 | 2844 | 4972 tok | 1693 | 102,2 tok/s | 3,15 GB | 7,73 GB | 16563 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 42478 | 2124 | 3976 tok | 1475 | 104,3 tok/s | 3,16 GB | 7,73 GB | 14145 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Patch Rejected` | 12 | 100% | 28703 | 2392 | 4066 tok | 1291 | 113,7 tok/s | 3,15 GB | 7,73 GB | 11353 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 48,0 | `Patch Rejected` | 18 | 100% | 131432 | 7302 | 11469 tok | 4670 | 104,8 tok/s | 3,17 GB | 7,74 GB | 44580 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20640 | 20640 | 10835 tok | 2084 | 122,7 tok/s | 3,18 GB | 7,79 GB | 16979 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38611 | 38611 | 19863 tok | 1573 | 95,2 tok/s | 3,20 GB | 7,79 GB | 16520 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Multiple Tools Selection (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 12: AST evaluation failed for 'get_battle_details': Argument 'battle' value mismatch: expected 'Battle of Stalingrad' (String), got 'Stalingrad' (String)

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 8: AST evaluation failed for 'patch_deployment_resource': Argument 'resource_type' value mismatch: expected 'memory_mb' (String), got 'memory_limit' (String)

> ⚡ **Circuit Breaker Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon):** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 26,2%, 7/8 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Tool Loop`
> - Turn 5: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: django__django-11099 (DiffExplosion) (Score: 95,7%, 6/7 turns passed):** 
> - Turn 7: Model invoked 4 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose) (Score: 18,0%, 3/5 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 1: Expected tool call 'read_file' was not invoked by model.
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: Circuit breaker did not trip to break out of repetitive tool loop.
> - Failure Taxonomy: `Over-Calling`
> - Turn 4: Model invoked 5 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 30,0%, 4/4 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 30,0%, 6/6 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Patch Rejected`

### Model: `Qwen 3.5 4B (Q4_K_XL) [Healing + Tool Compaction]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-tool-compaction.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 10,0 | `Missing Tool Call` | 19 | 95% | 11486 | 604 | 505 tok | 5921 | 144,4 tok/s | 3,23 GB | 7,71 GB | 40996 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Bad Arguments` | 20 | 100% | 15067 | 753 | 572 tok | 5200 | 141,2 tok/s | 3,24 GB | 7,72 GB | 36817 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 8933 | 288 | 412 tok | 4656 | 144,4 tok/s | 3,23 GB | 7,74 GB | 32252 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 93,3% | 28,0% | 15,0 | `Validation Failure` | 0 | 100% | 3121 | — | 240 tok | 5359 | 150,8 tok/s | 3,22 GB | 7,79 GB | 35532 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 19869 | 2484 | 2519 tok | 2532 | 130,3 tok/s | 3,23 GB | 7,79 GB | 19434 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2905 | 968 | 671 tok | 739 | 134,7 tok/s | 3,24 GB | 7,79 GB | 5487 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Patch Rejected` | 23 | 100% | 121730 | 5293 | 8392 tok | 3880 | 101,4 tok/s | 3,23 GB | 7,79 GB | 38277 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 42,0 | `—` | 7 | 100% | 43221 | 6174 | 5405 tok | 2647 | 114,7 tok/s | 3,23 GB | 7,76 GB | 23069 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 3 | 75% | 6697 | 2232 | 1444 tok | 845 | 123,1 tok/s | 3,23 GB | 7,76 GB | 6862 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ✅ PASS | 80,0% | 94,0% | 40,0 | `—` | 21 | 80% | 66825 | 3182 | 5972 tok | 2461 | 104,6 tok/s | 3,23 GB | 7,74 GB | 23524 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Tool Loop` | 15 | 100% | 31457 | 2097 | 4134 tok | 1876 | 114,7 tok/s | 3,23 GB | 7,77 GB | 16363 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 16 | 75% | 38754 | 2422 | 4728 tok | 1497 | 105,6 tok/s | 3,24 GB | 7,75 GB | 14172 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 83,3% | 25,0% | 48,0 | `Over-Calling` | 26 | 100% | 159892 | 6150 | 12195 tok | 2889 | 73,7 tok/s | 3,28 GB | 7,79 GB | 39176 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20128 | 20128 | 10323 tok | 881 | 95,8 tok/s | 3,24 GB | 7,80 GB | 9201 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38497 | 38497 | 19749 tok | 1349 | 85,7 tok/s | 3,24 GB | 7,79 GB | 15744 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Simple Function Calling (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 6: Expected tool call 'query_database' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for BFCL - Multiple Tools Selection (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 20: AST evaluation failed for 'calculate_effective_population_size': Argument 'species' value mismatch: expected 'wild tiger' (String), got 'wild tigers' (String)

> [!WARNING]
> **Partial Outcome Details for BFCL - Irrelevance (Negative Control) (Score: 28,0%, 14/15 turns passed):** 
> - End-to-end task failed: 1 of 15 turns failed validation.
> - Failure Taxonomy: `Validation Failure`
> - Turn 11: Expected direct conversational text response, but assistant returned empty output.

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 8: AST evaluation failed for 'patch_deployment_resource': Argument 'resource_type' value mismatch: expected 'memory_mb' (String), got 'memory_limit' (String)

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
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose) (Score: 94,0%, 4/5 turns passed):** 
> - Turn 5: Expected tool call 'check_git_diff' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: Circuit breaker did not trip to break out of repetitive tool loop.
> - Failure Taxonomy: `Tool Loop`
> - Turn 4: Unassisted model failed to break out of repetitive tool loop; circuit breaker was required.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 4: Expected tool call 'run_tests' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 25,0%, 5/6 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Over-Calling`
> - Turn 6: Model invoked 5 tool call(s) when 0 were expected.

### Model: `Qwen 3.5 4B (Q4_K_XL) [Recommended SLM Pipeline]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 10,0 | `Missing Tool Call` | 19 | 95% | 11161 | 587 | 453 tok | 5336 | 133,9 tok/s | 3,26 GB | 7,74 GB | 39842 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Bad Arguments` | 20 | 100% | 15082 | 754 | 607 tok | 4791 | 134,8 tok/s | 3,27 GB | 7,79 GB | 35553 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 94% | 9291 | 300 | 517 tok | 5211 | 140,3 tok/s | 3,28 GB | 7,79 GB | 37134 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 7947 | 147,2 tok/s | 3,30 GB | 7,76 GB | 53997 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 18999 | 2375 | 2483 tok | 2172 | 124,0 tok/s | 3,28 GB | 7,72 GB | 17513 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2734 | 911 | 517 tok | 597 | 129,9 tok/s | 3,28 GB | 7,72 GB | 4596 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Patch Rejected` | 17 | 100% | 104554 | 6150 | 8151 tok | 4430 | 108,4 tok/s | 3,25 GB | 7,72 GB | 40866 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 71,4% | 21,4% | 42,0 | `Missing Tool Call` | 9 | 67% | 47303 | 5256 | 5212 tok | 1358 | 100,4 tok/s | 3,25 GB | 7,72 GB | 13526 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 20 | 75% | 41770 | 2088 | 3932 tok | 1354 | 98,3 tok/s | 3,24 GB | 7,72 GB | 13768 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Missing Tool Call` | 25 | 80% | 71148 | 2846 | 5109 tok | 1511 | 88,2 tok/s | 3,27 GB | 7,69 GB | 17139 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 41954 | 2098 | 3764 tok | 1331 | 97,5 tok/s | 3,27 GB | 7,69 GB | 13645 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Tool Loop` | 17 | 75% | 41757 | 2456 | 5185 tok | 1547 | 104,1 tok/s | 3,27 GB | 7,72 GB | 14855 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 83,3% | 25,0% | 48,0 | `Missing Tool Call` | 16 | 80% | 77985 | 4874 | 6885 tok | 3028 | 105,0 tok/s | 3,27 GB | 7,72 GB | 28830 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20111 | 20111 | 10306 tok | 712 | 93,0 tok/s | 3,27 GB | 7,72 GB | 7656 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38588 | 38588 | 19840 tok | 2534 | 103,7 tok/s | 3,27 GB | 7,72 GB | 24446 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Simple Function Calling (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 15: Expected tool call 'find_real_estate' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for BFCL - Multiple Tools Selection (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 20: AST evaluation failed for 'calculate_effective_population_size': Argument 'species' value mismatch: expected 'wild tiger' (String), got 'wild tigers' (String)

> ⚡ **Circuit Breaker Details for BFCL - Parallel Function Calling:** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

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
> **Partial Outcome Details for SWE-bench Replay: django__django-11099 (DiffExplosion) (Score: 21,4%, 5/7 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
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
> - Turn 4: Model invoked 5 tool call(s) when 0 were expected.

> ⚡ **Circuit Breaker Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl):** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 30,0%, 4/4 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Tool Loop`

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 25,0%, 5/6 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 4: Expected tool call 'apply_patch' was not invoked by model.

### Model: `Qwen 3.5 4B (Q4_K_XL) [All Local LLM without SLM Summarizer]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 10,0 | `Missing Tool Call` | 19 | 95% | 11278 | 594 | 432 tok | 5635 | 138,7 tok/s | 3,27 GB | 7,72 GB | 40622 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 25,0 | `—` | 20 | 100% | 15042 | 752 | 542 tok | 4997 | 135,3 tok/s | 3,28 GB | 7,72 GB | 36923 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 94% | 9164 | 296 | 470 tok | 4977 | 140,2 tok/s | 3,27 GB | 7,72 GB | 35501 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 7704 | 147,5 tok/s | 3,24 GB | 7,72 GB | 52219 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Missing Tool Call` | 7 | 88% | 16818 | 2403 | 1727 tok | 2201 | 125,3 tok/s | 3,24 GB | 7,72 GB | 17565 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2881 | 960 | 646 tok | 687 | 131,9 tok/s | 3,25 GB | 7,72 GB | 5207 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 87,5% | 26,2% | 40,0 | `Over-Calling` | 19 | 100% | 107425 | 5654 | 7642 tok | 2895 | 97,1 tok/s | 3,27 GB | 7,72 GB | 29805 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 42,0 | `—` | 17 | 100% | 96559 | 5680 | 7415 tok | 3173 | 98,6 tok/s | 3,29 GB | 7,72 GB | 32191 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 7 | 75% | 12110 | 1730 | 2541 tok | 901 | 114,2 tok/s | 3,32 GB | 7,72 GB | 7890 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 60,0% | 18,0% | 40,0 | `Missing Tool Call` | 25 | 60% | 82826 | 3313 | 6346 tok | 2570 | 100,3 tok/s | 3,34 GB | 7,72 GB | 25610 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 40657 | 2033 | 3852 tok | 1433 | 101,2 tok/s | 3,37 GB | 7,72 GB | 14154 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Patch Rejected` | 20 | 100% | 54128 | 2706 | 5939 tok | 2032 | 105,2 tok/s | 3,36 GB | 7,72 GB | 19310 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 50,0% | 15,0% | 48,0 | `Over-Calling` | 16 | 60% | 88353 | 5522 | 7009 tok | 3879 | 107,9 tok/s | 3,31 GB | 7,72 GB | 35944 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20176 | 20176 | 10371 tok | 726 | 93,5 tok/s | 3,32 GB | 7,72 GB | 7768 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 37936 | 37936 | 19188 tok | 666 | 65,6 tok/s | 3,34 GB | 7,72 GB | 10160 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Simple Function Calling (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 15: Expected tool call 'find_real_estate' was not invoked by model.

> ⚡ **Circuit Breaker Details for BFCL - Parallel Function Calling:** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 8: Expected tool call 'patch_deployment_resource' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 26,2%, 7/8 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Over-Calling`
> - Turn 8: Model invoked 1 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose) (Score: 18,0%, 3/5 turns passed):** 
> - End-to-end task failed: Verification tests were not run or failed after applying patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.
> - Turn 5: Expected tool call 'check_git_diff' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: Circuit breaker did not trip to break out of repetitive tool loop.
> - Failure Taxonomy: `Over-Calling`
> - Turn 4: Model invoked 5 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 30,0%, 4/4 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

> ✂️ **Compaction Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion):** 2,790 chars pruned (~698 tokens), 6 thinking traces collapsed, 0 tool results truncated, 0 summarized (Speculative Hit Rate: 0.0%).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 15,0%, 3/6 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Over-Calling`
> - Turn 4: Expected tool call 'apply_patch' was not invoked by model.
> - Turn 5: Expected tool call 'run_tests' was not invoked by model.
> - Turn 6: Model invoked 1 tool call(s) when 0 were expected.

### Model: `Qwen 3.5 4B (Q4_K_XL) [All Local LLM with SLM Summarizer]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 12049 | 602 | 783 tok | 6686 | 140,5 tok/s | 3,36 GB | 7,72 GB | 47582 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Bad Arguments` | 20 | 100% | 14996 | 750 | 501 tok | 5256 | 135,9 tok/s | 3,37 GB | 7,72 GB | 38671 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 94% | 9208 | 297 | 458 tok | 4722 | 139,5 tok/s | 3,34 GB | 7,72 GB | 33840 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 7227 | 147,6 tok/s | 3,32 GB | 7,72 GB | 48958 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 18576 | 2322 | 2364 tok | 2214 | 124,0 tok/s | 3,23 GB | 7,72 GB | 17851 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2734 | 911 | 517 tok | 632 | 130,3 tok/s | 3,23 GB | 7,72 GB | 4849 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 87,5% | 26,2% | 40,0 | `Tool Loop` | 25 | 71% | 150979 | 6039 | 10855 tok | 4580 | 98,5 tok/s | 3,24 GB | 7,72 GB | 46505 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 85,7% | 25,7% | 42,0 | `Tool Loop` | 21 | 83% | 127203 | 6057 | 9941 tok | 5506 | 105,1 tok/s | 3,22 GB | 7,72 GB | 52367 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 16 | 75% | 36892 | 2306 | 3943 tok | 1630 | 108,4 tok/s | 3,24 GB | 7,72 GB | 15040 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Missing Tool Call` | 21 | 80% | 64358 | 3065 | 5284 tok | 1662 | 95,4 tok/s | 3,24 GB | 7,71 GB | 17425 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 41965 | 2098 | 3897 tok | 1450 | 99,8 tok/s | 3,22 GB | 7,69 GB | 14532 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Patch Rejected` | 20 | 100% | 52116 | 2606 | 4875 tok | 1660 | 100,3 tok/s | 3,23 GB | 7,71 GB | 16548 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 83,3% | 25,0% | 48,0 | `Missing Tool Call` | 21 | 80% | 116737 | 5559 | 8981 tok | 3530 | 96,7 tok/s | 3,24 GB | 7,72 GB | 36488 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20476 | 20476 | 10671 tok | 1314 | 109,8 tok/s | 3,25 GB | 7,72 GB | 11968 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38229 | 38229 | 19481 tok | 1556 | 91,7 tok/s | 3,25 GB | 7,72 GB | 16971 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Multiple Tools Selection (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 20: AST evaluation failed for 'calculate_effective_population_size': Argument 'species' value mismatch: expected 'wild tiger' (String), got 'wild tigers' (String)

> ⚡ **Circuit Breaker Details for BFCL - Parallel Function Calling:** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 8: AST evaluation failed for 'patch_deployment_resource': Argument 'resource_type' value mismatch: expected 'memory_mb' (String), got 'memory' (String)

> ✂️ **Compaction Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon):** 1,908 chars pruned (~477 tokens), 6 thinking traces collapsed, 0 tool results truncated, 0 summarized (Speculative Hit Rate: 0.0%).

> ⚡ **Circuit Breaker Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon):** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 26,2%, 7/8 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Tool Loop`
> - Turn 6: Expected tool call 'run_tests' was not invoked by model.

> ⚡ **Circuit Breaker Details for SWE-bench Replay: django__django-11099 (DiffExplosion):** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: django__django-11099 (DiffExplosion) (Score: 25,7%, 6/7 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Tool Loop`
> - Turn 5: Expected tool call 'check_git_diff' was not invoked by model.

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
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 30,0%, 4/4 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 25,0%, 5/6 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 5: Expected tool call 'run_tests' was not invoked by model.

### Model: `Qwen 3.5 4B (Q4_K_XL) [All Optimizations Combined]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 10,0 | `Missing Tool Call` | 19 | 95% | 11439 | 602 | 598 tok | 6122 | 139,2 tok/s | 3,25 GB | 7,72 GB | 43966 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 25,0 | `—` | 20 | 100% | 15190 | 760 | 649 tok | 5114 | 135,4 tok/s | 3,26 GB | 7,72 GB | 37759 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 94% | 9269 | 299 | 525 tok | 4988 | 140,7 tok/s | 3,29 GB | 7,72 GB | 35456 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 7110 | 147,0 tok/s | 3,32 GB | 7,72 GB | 48363 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Missing Tool Call` | 7 | 88% | 16240 | 2320 | 1614 tok | 2063 | 124,6 tok/s | 3,33 GB | 7,72 GB | 16557 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2867 | 956 | 632 tok | 801 | 134,0 tok/s | 3,28 GB | 7,72 GB | 5977 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 87,5% | 26,2% | 40,0 | `Missing Tool Call` | 27 | 86% | 133258 | 4936 | 7804 tok | 3233 | 93,3 tok/s | 3,28 GB | 7,72 GB | 34650 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 42,0 | `Tool Loop` | 18 | 83% | 106182 | 5899 | 8768 tok | 4980 | 106,8 tok/s | 3,27 GB | 7,72 GB | 46631 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 16 | 75% | 34212 | 2138 | 3645 tok | 1392 | 105,0 tok/s | 3,31 GB | 7,72 GB | 13256 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Tool Loop` | 22 | 60% | 56554 | 2571 | 4623 tok | 1602 | 96,2 tok/s | 3,33 GB | 7,72 GB | 16659 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 42570 | 2128 | 3962 tok | 1473 | 100,7 tok/s | 3,37 GB | 7,72 GB | 14634 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 20 | 75% | 49621 | 2481 | 5286 tok | 2137 | 108,2 tok/s | 3,40 GB | 7,72 GB | 19750 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 66,7% | 20,0% | 48,0 | `Over-Calling` | 17 | 80% | 94813 | 5577 | 9338 tok | 3118 | 99,5 tok/s | 3,43 GB | 7,72 GB | 31345 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20923 | 20923 | 11118 tok | 2287 | 120,6 tok/s | 3,45 GB | 7,71 GB | 18962 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38703 | 38703 | 19955 tok | 2161 | 100,3 tok/s | 3,40 GB | 7,71 GB | 21548 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Simple Function Calling (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 15: Expected tool call 'find_real_estate' was not invoked by model.

> ⚡ **Circuit Breaker Details for BFCL - Parallel Function Calling:** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 8: Expected tool call 'patch_deployment_resource' was not invoked by model.

> ✂️ **Compaction Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon):** 10 chars pruned (~2 tokens), 10 thinking traces collapsed, 0 tool results truncated, 0 summarized (Speculative Hit Rate: 0.0%).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 26,2%, 7/8 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 5: Expected tool call 'apply_patch' was not invoked by model.

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

> ⚡ **Circuit Breaker Details for SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose):** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose) (Score: 24,0%, 4/5 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Tool Loop`
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
> - Turn 2: Expected tool call 'run_tests' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 20,0%, 4/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Over-Calling`
> - Turn 4: Expected tool call 'apply_patch' was not invoked by model.
> - Turn 6: Model invoked 1 tool call(s) when 0 were expected.

