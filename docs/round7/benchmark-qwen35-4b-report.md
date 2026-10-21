# Pmad.AI Tool Calling Benchmark Report

Generated on: 2026-10-19 20:00:05 UTC

## Environment

- **CPU:** Intel(R) Core(TM) i7-14700KF (28 logical cores)
- **GPU:** NVIDIA GeForce RTX 4080 SUPER, 15,99 GB VRAM, Driver: 610.88
- **OS:** Microsoft Windows 10.0.26200

> [!NOTE]
> 📄 **Execution Transcripts:** Individual turn-by-turn verbatim transcripts and failure analyses for each configuration are saved in the `transcripts/` directory and linked directly in the tables below.

## Summary Comparison

| Model | Backend | Reasoning | Scenarios Passed | Pass@1 (Wtd) | Pass@1 (Unwtd) | Step Acc | Pass Rate | Tool Calls | Gen Tokens | Avg Speed (tok/s) | Sys RAM | GPU VRAM | Total Time |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| [`Qwen 3.5 4B (Q4_K_XL) [Baseline (Raw)]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_baseline.transcript.md) | Vulkan | Default | 7/15 | 35,1% | 46,7% | 87,6% | 50,9% | 237 | 51087 | 113,6 | 3,10 GB | 8,05 GB | 449,88s |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Call Healing]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing.transcript.md) | Vulkan | Default | 5/15 | 39,7% | 46,7% | 89,5% | 54,6% | 228 | 47809 | 113,6 | 3,13 GB | 8,31 GB | 420,98s |
| [`Qwen 3.5 4B (Q4_K_XL) [Thinking Trace Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-thinking-compaction.transcript.md) | Vulkan | Default | 6/15 | 32,4% | 46,7% | 88,0% | 49,1% | 239 | 48086 | 114,1 | 3,16 GB | 8,15 GB | 421,57s |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Result Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-tool-compaction.transcript.md) | Vulkan | Default | 6/15 | 27,7% | 40,0% | 89,0% | 46,1% | 232 | 47216 | 110,2 | 3,18 GB | 8,19 GB | 428,44s |
| [`Qwen 3.5 4B (Q4_K_XL) [Speculative SLM Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-speculative-slm.transcript.md) | Vulkan | Default | 7/15 | 29,0% | 46,7% | 82,4% | 45,0% | 246 | 54587 | 109,7 | 3,20 GB | 8,23 GB | 497,50s |
| [`Qwen 3.5 4B (Q4_K_XL) [Lenient Dispatch & Guardrails]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-lenient-dispatch.transcript.md) | Vulkan | Default | 7/15 | 34,7% | 46,7% | 86,7% | 50,3% | 223 | 48949 | 114,1 | 3,22 GB | 8,19 GB | 428,82s |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Lenient Dispatch]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md) | Vulkan | Default | 6/15 | 32,8% | 46,7% | 85,7% | 48,7% | 223 | 42369 | 114,9 | 3,22 GB | 8,08 GB | 368,83s |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Tool Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-tool-compaction.transcript.md) | Vulkan | Default | 5/15 | 21,0% | 33,3% | 80,1% | 38,7% | 212 | 45474 | 116,6 | 3,27 GB | 8,11 GB | 389,94s |
| [`Qwen 3.5 4B (Q4_K_XL) [Recommended SLM Pipeline]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md) | Vulkan | Default | 6/15 | 40,2% | 46,7% | 90,9% | 55,4% | 212 | 54419 | 120,2 | 3,35 GB | 8,23 GB | 452,83s |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM without SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md) | Vulkan | Default | 8/15 | 45,3% | 53,3% | 91,4% | 59,2% | 224 | 52467 | 117,7 | 3,37 GB | 8,29 GB | 445,93s |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM with SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | Vulkan | Default | 7/15 | 32,8% | 46,7% | 88,4% | 49,5% | 201 | 48173 | 116,9 | 3,31 GB | 8,15 GB | 412,01s |
| [`Qwen 3.5 4B (Q4_K_XL) [All Optimizations Combined]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md) | Vulkan | Default | 7/15 | 29,0% | 46,7% | 87,8% | 46,6% | 203 | 42307 | 117,5 | 3,35 GB | 8,07 GB | 359,97s |

## Benchmark Suite Breakdown (BFCL / SWE-bench / RULER)

> Evaluates performance separated by benchmark domain: schema adherence and tool call syntax (BFCL), multi-turn agentic software engineering (SWE-bench), and needle-in-a-haystack retrieval (RULER).

| Model | Suite | Scenarios | Pass@1 (Task Success) | Step Acc | Turns Passed | Turn Acc | Weighted Pass Rate |
|---|---|---|---|---|---|---|---|
| [`Qwen 3.5 4B (Q4_K_XL) [Baseline (Raw)]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_baseline.transcript.md) | **BFCL** | 4/6 | 65,9% | 96,8% | 80/82 | 97,6% | 75,2% |
| [`Qwen 3.5 4B (Q4_K_XL) [Baseline (Raw)]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_baseline.transcript.md) | **SWE-bench** | 1/7 | 14,5% | 82,4% | 32/38 | 84,2% | 34,9% |
| [`Qwen 3.5 4B (Q4_K_XL) [Baseline (Raw)]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_baseline.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Call Healing]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing.transcript.md) | **BFCL** | 3/6 | 47,7% | 96,1% | 79/82 | 96,3% | 62,3% |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Call Healing]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing.transcript.md) | **SWE-bench** | 2/7 | 28,3% | 85,5% | 33/38 | 86,8% | 45,4% |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Call Healing]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Thinking Trace Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-thinking-compaction.transcript.md) | **BFCL** | 4/6 | 56,8% | 96,6% | 80/82 | 97,6% | 68,8% |
| [`Qwen 3.5 4B (Q4_K_XL) [Thinking Trace Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-thinking-compaction.transcript.md) | **SWE-bench** | 1/7 | 13,8% | 83,1% | 32/38 | 84,2% | 34,6% |
| [`Qwen 3.5 4B (Q4_K_XL) [Thinking Trace Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-thinking-compaction.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Result Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-tool-compaction.transcript.md) | **BFCL** | 3/6 | 36,4% | 95,2% | 79/82 | 96,3% | 54,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Result Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-tool-compaction.transcript.md) | **SWE-bench** | 1/7 | 14,5% | 85,2% | 33/38 | 86,8% | 35,7% |
| [`Qwen 3.5 4B (Q4_K_XL) [Tool Result Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-tool-compaction.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Speculative SLM Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-speculative-slm.transcript.md) | **BFCL** | 5/6 | 79,5% | 97,7% | 81/82 | 98,8% | 85,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Speculative SLM Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-speculative-slm.transcript.md) | **SWE-bench** | 0/7 | 0,0% | 74,1% | 29/38 | 76,3% | 22,2% |
| [`Qwen 3.5 4B (Q4_K_XL) [Speculative SLM Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-speculative-slm.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Lenient Dispatch & Guardrails]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-lenient-dispatch.transcript.md) | **BFCL** | 4/6 | 65,9% | 96,8% | 80/82 | 97,6% | 75,2% |
| [`Qwen 3.5 4B (Q4_K_XL) [Lenient Dispatch & Guardrails]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-lenient-dispatch.transcript.md) | **SWE-bench** | 1/7 | 13,8% | 81,0% | 31/38 | 81,6% | 34,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Lenient Dispatch & Guardrails]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-lenient-dispatch.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Lenient Dispatch]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md) | **BFCL** | 4/6 | 56,8% | 96,6% | 80/82 | 97,6% | 68,8% |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Lenient Dispatch]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md) | **SWE-bench** | 1/7 | 14,5% | 79,7% | 31/38 | 81,6% | 34,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Lenient Dispatch]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Tool Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-tool-compaction.transcript.md) | **BFCL** | 3/6 | 47,7% | 95,7% | 78/82 | 95,1% | 62,1% |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Tool Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-tool-compaction.transcript.md) | **SWE-bench** | 0/7 | 0,0% | 71,4% | 27/38 | 71,1% | 21,4% |
| [`Qwen 3.5 4B (Q4_K_XL) [Healing + Tool Compaction]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-tool-compaction.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [Recommended SLM Pipeline]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md) | **BFCL** | 3/6 | 50,0% | 93,6% | 76/82 | 92,7% | 63,1% |
| [`Qwen 3.5 4B (Q4_K_XL) [Recommended SLM Pipeline]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md) | **SWE-bench** | 2/7 | 28,3% | 88,6% | 34/38 | 89,5% | 46,4% |
| [`Qwen 3.5 4B (Q4_K_XL) [Recommended SLM Pipeline]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM without SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md) | **BFCL** | 4/6 | 70,5% | 97,3% | 80/82 | 97,6% | 78,5% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM without SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md) | **SWE-bench** | 2/7 | 28,3% | 87,9% | 33/38 | 86,8% | 46,2% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM without SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM with SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | **BFCL** | 4/6 | 56,8% | 95,5% | 79/82 | 96,3% | 68,4% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM with SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | **SWE-bench** | 1/7 | 14,5% | 84,1% | 33/38 | 86,8% | 35,4% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Local LLM with SLM Summarizer]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Optimizations Combined]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md) | **BFCL** | 5/6 | 79,5% | 97,7% | 81/82 | 98,8% | 85,0% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Optimizations Combined]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md) | **SWE-bench** | 0/7 | 0,0% | 82,4% | 32/38 | 84,2% | 24,7% |
| [`Qwen 3.5 4B (Q4_K_XL) [All Optimizations Combined]`](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md) | **RULER** | 2/2 | 100,0% | 100,0% | 4/4 | 100,0% | 100,0% |

## Pmad.AI Optimization Matrix: Empirical Utility Gains

> Quantifies prompt token reduction, context ceiling capping, execution time savings, and accuracy improvements achieved by Pmad.AI utilities vs. unassisted baseline model execution.

| Model | Configuration | Pass Rate | Prompt Tokens | Token Savings % | Peak Context | Context Reduction % | Gen Speed | Duration | Time Savings % |
|---|---|---|---|---|---|---|---|---|---|
| `Qwen 3.5 4B (Q4_K_XL)` | [**Baseline (Raw)**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_baseline.transcript.md) | 50,9% | 796997 | — | 19161 tok | — | 113,6 tok/s | 449,88s | — |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Tool Call Healing**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing.transcript.md) | 54,6% | 705061 | **-11,5%** | 19887 tok | +3,8% | 113,6 tok/s | 420,98s | **-6,4%** |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Thinking Trace Compaction**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-thinking-compaction.transcript.md) | 49,1% | 780693 | **-2,0%** | 19278 tok | +0,6% | 114,1 tok/s | 421,57s | **-6,3%** |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Tool Result Compaction**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-tool-compaction.transcript.md) | 46,1% | 786915 | **-1,3%** | 19940 tok | +4,1% | 110,2 tok/s | 428,44s | **-4,8%** |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Speculative SLM Compaction**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-speculative-slm.transcript.md) | 45,0% | 812054 | +1,9% | 19464 tok | +1,6% | 109,7 tok/s | 497,50s | +10,6% |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Lenient Dispatch & Guardrails**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-lenient-dispatch.transcript.md) | 50,3% | 712356 | **-10,6%** | 19822 tok | +3,4% | 114,1 tok/s | 428,82s | **-4,7%** |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Healing + Lenient Dispatch**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md) | 48,7% | 665704 | **-16,5%** | 19162 tok | +0,0% | 114,9 tok/s | 368,83s | **-18,0%** |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Healing + Tool Compaction**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-tool-compaction.transcript.md) | 38,7% | 631356 | **-20,8%** | 19397 tok | +1,2% | 116,6 tok/s | 389,94s | **-13,3%** |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Recommended SLM Pipeline**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md) | 55,4% | 638274 | **-19,9%** | 19848 tok | +3,6% | 120,2 tok/s | 452,83s | +0,7% |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Local LLM without SLM Summarizer**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md) | 59,2% | 709230 | **-11,0%** | 19668 tok | +2,6% | 117,7 tok/s | 445,93s | **-0,9%** |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Local LLM with SLM Summarizer**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | 49,5% | 571617 | **-28,3%** | 20147 tok | +5,1% | 116,9 tok/s | 412,01s | **-8,4%** |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Optimizations Combined**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md) | 46,6% | 577783 | **-27,5%** | 18931 tok | **-1,2%** | 117,5 tok/s | 359,97s | **-20,0%** |

## Compaction Efficiency & Telemetry Matrix

> Reports characters and tokens pruned, speculative SLM hit rate, and breakdown of collapsed reasoning traces vs. truncated/summarized tool results.

| Model | Configuration | Pruned Chars | Pruned Tokens | Speculative Hit Rate | Thinking Traces Collapsed | Tool Results Truncated | Tool Results Summarized | Total Events |
|---|---|---|---|---|---|---|---|---|
| `Qwen 3.5 4B (Q4_K_XL)` | [**Speculative SLM Compaction**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-speculative-slm.transcript.md) | 26,352 | 6,588 | 0.0% (0/9) | 9 | 0 | 0 | 9 |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Recommended SLM Pipeline**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md) | 0 | 0 | 0.0% | 0 | 0 | 0 | 0 |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Local LLM without SLM Summarizer**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md) | 0 | 0 | 0.0% | 0 | 0 | 0 | 0 |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Local LLM with SLM Summarizer**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | 1,620 | 405 | 0.0% (0/3) | 3 | 0 | 0 | 3 |

## Pmad.AI Utility Interventions & Telemetry Matrix

> Quantifies runtime interventions by Pmad.AI components: Tool Call Healing (malformed JSON, pseudo-tag extractions, fence strippings), Circuit Breaker (loop and ping-pong interceptions), and Lenient Dispatch (tool/parameter normalization).

| Model | Configuration | Healing Total | Pseudo-Tags | JSON Repairs | Breaker Total | Breaker Loops | Repetitions | Dispatch Total | Alias Hits | Param Hits | Casing/Norm |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `Qwen 3.5 4B (Q4_K_XL)` | [**Lenient Dispatch & Guardrails**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-lenient-dispatch.transcript.md) | 0 | 0 | 0 | 1 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Healing + Lenient Dispatch**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md) | 0 | 0 | 0 | 1 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Qwen 3.5 4B (Q4_K_XL)` | [**Recommended SLM Pipeline**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md) | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Local LLM without SLM Summarizer**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md) | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Local LLM with SLM Summarizer**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md) | 0 | 0 | 0 | 1 | 1 | 0 | 0 | 0 | 0 | 0 |
| `Qwen 3.5 4B (Q4_K_XL)` | [**All Optimizations Combined**](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md) | 0 | 0 | 0 | 2 | 2 | 0 | 0 | 0 | 0 | 0 |

## Detailed Scenario Breakdown

### Model: `Qwen 3.5 4B (Q4_K_XL) [Baseline (Raw)]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_baseline.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 11549 | 577 | 412 tok | 6123 | 134,9 tok/s | 3,09 GB | 7,97 GB | 45374 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 25,0 | `—` | 20 | 100% | 14975 | 749 | 504 tok | 5024 | 130,7 tok/s | 3,10 GB | 7,96 GB | 38433 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 9144 | 295 | 486 tok | 4961 | 128,7 tok/s | 3,09 GB | 8,05 GB | 38560 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 93,3% | 28,0% | 15,0 | `Validation Failure` | 0 | 100% | 3121 | — | 240 tok | 7571 | 143,7 tok/s | 3,10 GB | 8,02 GB | 52689 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 19016 | 2377 | 2403 tok | 2320 | 116,7 tok/s | 3,09 GB | 8,00 GB | 19874 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2734 | 911 | 517 tok | 635 | 122,6 tok/s | 3,09 GB | 7,98 GB | 5179 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 87,5% | 26,2% | 40,0 | `Missing Tool Call` | 26 | 86% | 174733 | 6720 | 10975 tok | 5976 | 99,6 tok/s | 3,09 GB | 8,02 GB | 59972 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 42,0 | `—` | 20 | 100% | 141791 | 7090 | 10498 tok | 4219 | 92,6 tok/s | 3,09 GB | 8,02 GB | 45544 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 17 | 75% | 38092 | 2241 | 3906 tok | 1513 | 104,9 tok/s | 3,09 GB | 8,04 GB | 14426 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Missing Tool Call` | 25 | 80% | 69782 | 2791 | 4902 tok | 1472 | 86,2 tok/s | 3,09 GB | 7,98 GB | 17072 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 41573 | 2079 | 3759 tok | 1325 | 97,1 tok/s | 3,09 GB | 8,04 GB | 13647 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 19 | 75% | 63894 | 3363 | 6549 tok | 3511 | 115,1 tok/s | 3,09 GB | 7,99 GB | 30501 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 83,3% | 25,0% | 48,0 | `Over-Calling` | 26 | 100% | 147967 | 5691 | 9701 tok | 3947 | 92,2 tok/s | 3,10 GB | 7,96 GB | 42786 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20717 | 20717 | 10912 tok | 1921 | 117,3 tok/s | 3,09 GB | 7,96 GB | 16379 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 37909 | 37909 | 19161 tok | 569 | 60,2 tok/s | 3,10 GB | 8,03 GB | 9444 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Irrelevance (Negative Control) (Score: 28,0%, 14/15 turns passed):** 
> - End-to-end task failed: 1 of 15 turns failed validation.
> - Failure Taxonomy: `Validation Failure`
> - Turn 12: Expected direct conversational text response, but assistant returned empty output.

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 8: AST evaluation failed for 'patch_deployment_resource': Argument 'resource_type' value mismatch: expected 'memory_mb' (String), got 'memory_limit' (String)

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 26,2%, 7/8 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
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
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 4: Expected tool call 'run_tests' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 25,0%, 5/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Over-Calling`
> - Turn 6: Model invoked 5 tool call(s) when 0 were expected.

### Model: `Qwen 3.5 4B (Q4_K_XL) [Tool Call Healing]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 10,0 | `Missing Tool Call` | 19 | 95% | 11190 | 589 | 408 tok | 5076 | 136,0 tok/s | 3,13 GB | 8,04 GB | 37332 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Bad Arguments` | 20 | 100% | 15126 | 756 | 563 tok | 5744 | 133,3 tok/s | 3,10 GB | 8,17 GB | 43090 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 10068 | 325 | 960 tok | 5847 | 125,5 tok/s | 3,10 GB | 8,31 GB | 46597 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 8383 | 141,4 tok/s | 3,10 GB | 8,18 GB | 59281 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 18754 | 2344 | 2295 tok | 2295 | 117,9 tok/s | 3,11 GB | 8,18 GB | 19463 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2734 | 911 | 517 tok | 605 | 130,8 tok/s | 3,10 GB | 8,15 GB | 4626 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Patch Rejected` | 24 | 100% | 126768 | 5282 | 7945 tok | 3673 | 89,5 tok/s | 3,11 GB | 8,18 GB | 41045 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ✅ PASS | 85,7% | 95,7% | 42,0 | `—` | 18 | 83% | 90362 | 5020 | 7294 tok | 2490 | 90,1 tok/s | 3,12 GB | 8,04 GB | 27626 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 16 | 75% | 31437 | 1965 | 3490 tok | 1237 | 99,7 tok/s | 3,12 GB | 8,08 GB | 12410 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ✅ PASS | 80,0% | 94,0% | 40,0 | `—` | 21 | 80% | 61521 | 2930 | 5809 tok | 2069 | 103,0 tok/s | 3,13 GB | 8,01 GB | 20078 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 39436 | 1972 | 3770 tok | 1350 | 99,0 tok/s | 3,11 GB | 7,98 GB | 13638 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Patch Rejected` | 20 | 100% | 50291 | 2515 | 5018 tok | 1790 | 103,3 tok/s | 3,13 GB | 8,00 GB | 17325 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 83,3% | 25,0% | 48,0 | `Over-Calling` | 26 | 100% | 185602 | 7138 | 12961 tok | 4360 | 88,7 tok/s | 3,13 GB | 8,00 GB | 49163 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20016 | 20016 | 10211 tok | 591 | 86,9 tok/s | 3,13 GB | 7,97 GB | 6797 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38635 | 38635 | 19887 tok | 2299 | 102,2 tok/s | 3,12 GB | 7,97 GB | 22506 ms |

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
> **Partial Outcome Details for SWE-bench Replay: django__django-11099 (DiffExplosion) (Score: 95,7%, 6/7 turns passed):** 
> - Turn 4: Expected tool call 'apply_patch' was not invoked by model.

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
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 30,0%, 4/4 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 25,0%, 5/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Over-Calling`
> - Turn 6: Model invoked 5 tool call(s) when 0 were expected.

### Model: `Qwen 3.5 4B (Q4_K_XL) [Thinking Trace Compaction]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-thinking-compaction.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 11660 | 583 | 408 tok | 5773 | 139,5 tok/s | 3,13 GB | 7,98 GB | 41385 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Bad Arguments` | 20 | 100% | 15009 | 750 | 598 tok | 5429 | 134,9 tok/s | 3,14 GB | 8,03 GB | 40259 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 9178 | 296 | 468 tok | 5467 | 141,4 tok/s | 3,16 GB | 8,01 GB | 38663 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 5451 | 146,1 tok/s | 3,16 GB | 8,03 GB | 37317 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 17700 | 2212 | 2297 tok | 2145 | 124,7 tok/s | 3,14 GB | 8,00 GB | 17199 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2958 | 986 | 741 tok | 777 | 132,6 tok/s | 3,13 GB | 8,00 GB | 5858 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 87,5% | 26,2% | 40,0 | `Missing Tool Call` | 27 | 86% | 155022 | 5742 | 10704 tok | 4292 | 93,9 tok/s | 3,14 GB | 8,05 GB | 45703 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 85,7% | 25,7% | 42,0 | `Over-Calling` | 31 | 100% | 206318 | 6655 | 13193 tok | 4313 | 81,2 tok/s | 3,13 GB | 8,06 GB | 53121 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 11 | 75% | 26079 | 2371 | 3726 tok | 1656 | 113,7 tok/s | 3,12 GB | 8,10 GB | 14560 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ✅ PASS | 80,0% | 94,0% | 40,0 | `—` | 25 | 80% | 84638 | 3386 | 7200 tok | 2218 | 91,7 tok/s | 3,13 GB | 8,04 GB | 24194 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 44637 | 2232 | 4252 tok | 1788 | 102,6 tok/s | 3,14 GB | 8,05 GB | 17424 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 20 | 75% | 47403 | 2370 | 5473 tok | 3049 | 114,3 tok/s | 3,15 GB | 8,15 GB | 26682 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 48,0 | `Patch Rejected` | 21 | 100% | 98390 | 4685 | 8083 tok | 3132 | 97,2 tok/s | 3,16 GB | 8,05 GB | 32228 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20554 | 20554 | 10749 tok | 1819 | 115,7 tok/s | 3,12 GB | 7,99 GB | 15719 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38026 | 38026 | 19278 tok | 777 | 69,0 tok/s | 3,13 GB | 8,00 GB | 11253 ms |

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
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 5: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: django__django-11099 (DiffExplosion) (Score: 25,7%, 6/7 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Over-Calling`
> - Turn 7: Model invoked 5 tool call(s) when 0 were expected.

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
> - Failure Taxonomy: `Over-Calling`
> - Turn 4: Model invoked 5 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 30,0%, 6/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

### Model: `Qwen 3.5 4B (Q4_K_XL) [Tool Result Compaction]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-tool-compaction.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 12059 | 603 | 772 tok | 5992 | 136,8 tok/s | 3,15 GB | 7,99 GB | 43810 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Missing Tool Call` | 19 | 95% | 14671 | 772 | 582 tok | 4457 | 131,6 tok/s | 3,15 GB | 7,99 GB | 33870 ms |
| **BFCL - Parallel Function Calling** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 93,3% | 28,0% | 22,5 | `Bad Arguments` | 35 | 100% | 11301 | 323 | 827 tok | 5862 | 138,6 tok/s | 3,14 GB | 7,99 GB | 42283 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 5220 | 143,6 tok/s | 3,15 GB | 7,99 GB | 36349 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Missing Tool Call` | 7 | 88% | 16230 | 2319 | 1632 tok | 2091 | 121,7 tok/s | 3,15 GB | 7,99 GB | 17176 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2889 | 963 | 654 tok | 749 | 130,5 tok/s | 3,13 GB | 7,99 GB | 5739 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 87,5% | 26,2% | 40,0 | `Over-Calling` | 32 | 100% | 208530 | 6517 | 12714 tok | 5331 | 89,0 tok/s | 3,14 GB | 7,99 GB | 59866 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 42,0 | `—` | 23 | 100% | 145715 | 6335 | 10049 tok | 4280 | 93,6 tok/s | 3,16 GB | 7,99 GB | 45725 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 8 | 75% | 16286 | 2036 | 3031 tok | 1226 | 115,7 tok/s | 3,16 GB | 8,02 GB | 10597 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Missing Tool Call` | 25 | 80% | 73812 | 2952 | 5462 tok | 1620 | 87,6 tok/s | 3,17 GB | 8,12 GB | 18502 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 43293 | 2165 | 3922 tok | 1435 | 92,9 tok/s | 3,17 GB | 8,12 GB | 15441 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 18 | 75% | 45494 | 2527 | 4192 tok | 1452 | 94,9 tok/s | 3,18 GB | 8,09 GB | 15308 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 48,0 | `Patch Rejected` | 20 | 100% | 134232 | 6712 | 11253 tok | 3933 | 86,4 tok/s | 3,17 GB | 8,19 GB | 45530 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20594 | 20594 | 10789 tok | 1613 | 101,1 tok/s | 3,15 GB | 8,10 GB | 15949 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38688 | 38688 | 19940 tok | 1955 | 87,7 tok/s | 3,15 GB | 8,10 GB | 22299 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Multiple Tools Selection (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 9: Expected tool call 'realestate_find_properties' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for BFCL - Parallel Function Calling (Score: 28,0%, 14/15 turns passed):** 
> - End-to-end task failed: 1 of 15 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 5: Tool call count mismatch: expected 2 call(s), but model generated 6.

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 8: Expected tool call 'patch_deployment_resource' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 26,2%, 7/8 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Over-Calling`
> - Turn 8: Model invoked 5 tool call(s) when 0 were expected.

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

### Model: `Qwen 3.5 4B (Q4_K_XL) [Speculative SLM Compaction]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-speculative-slm.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 11596 | 580 | 426 tok | 5567 | 122,7 tok/s | 3,17 GB | 8,10 GB | 45383 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 25,0 | `—` | 20 | 100% | 15115 | 756 | 607 tok | 4756 | 117,4 tok/s | 3,17 GB | 8,17 GB | 40499 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 9220 | 297 | 515 tok | 5119 | 125,1 tok/s | 3,18 GB | 8,15 GB | 40916 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 7924 | 130,3 tok/s | 3,18 GB | 8,13 GB | 60832 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 19265 | 2408 | 2352 tok | 2351 | 113,4 tok/s | 3,18 GB | 8,23 GB | 20727 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2889 | 963 | 655 tok | 662 | 111,8 tok/s | 3,18 GB | 8,23 GB | 5919 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 87,5% | 26,2% | 40,0 | `Missing Tool Call` | 27 | 86% | 161466 | 5980 | 11112 tok | 4568 | 93,4 tok/s | 3,19 GB | 8,16 GB | 48931 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 85,7% | 25,7% | 42,0 | `Over-Calling` | 31 | 100% | 206444 | 6660 | 13076 tok | 7167 | 95,5 tok/s | 3,19 GB | 8,13 GB | 75079 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 20 | 75% | 44045 | 2202 | 4098 tok | 1483 | 98,7 tok/s | 3,17 GB | 8,08 GB | 15021 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Missing Tool Call` | 25 | 80% | 94155 | 3766 | 7251 tok | 3593 | 104,1 tok/s | 3,18 GB | 8,15 GB | 34524 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 41511 | 2076 | 3872 tok | 1394 | 93,8 tok/s | 3,20 GB | 8,12 GB | 14860 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 50,0% | 15,0% | 40,0 | `Missing Tool Call` | 20 | 50% | 50157 | 2508 | 5698 tok | 2657 | 106,3 tok/s | 3,19 GB | 8,17 GB | 24994 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 66,7% | 20,0% | 48,0 | `Over-Calling` | 19 | 80% | 94309 | 4964 | 7625 tok | 4543 | 110,2 tok/s | 3,19 GB | 8,16 GB | 41234 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20549 | 20549 | 10629 tok | 1479 | 111,5 tok/s | 3,19 GB | 8,15 GB | 13268 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38212 | 38212 | 19464 tok | 1324 | 86,4 tok/s | 3,18 GB | 8,15 GB | 15315 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 8: AST evaluation failed for 'patch_deployment_resource': Argument 'resource_type' value mismatch: expected 'memory_mb' (String), got 'memory' (String)

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 26,2%, 7/8 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 6: Expected tool call 'run_tests' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: django__django-11099 (DiffExplosion) (Score: 25,7%, 6/7 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Over-Calling`
> - Turn 7: Model invoked 5 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> ✂️ **Compaction Details for SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose):** 26,352 chars pruned (~6,588 tokens), 9 thinking traces collapsed, 0 tool results truncated, 0 summarized (Speculative Hit Rate: 0.0%).

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
> - Turn 4: Expected tool call 'run_tests' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 20,0%, 4/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Over-Calling`
> - Turn 4: Expected tool call 'apply_patch' was not invoked by model.
> - Turn 6: Model invoked 5 tool call(s) when 0 were expected.

### Model: `Qwen 3.5 4B (Q4_K_XL) [Lenient Dispatch & Guardrails]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-lenient-dispatch.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 11811 | 591 | 441 tok | 6185 | 136,5 tok/s | 3,20 GB | 8,15 GB | 45317 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 25,0 | `—` | 20 | 100% | 15245 | 762 | 607 tok | 5119 | 132,2 tok/s | 3,20 GB | 8,16 GB | 38734 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 10191 | 329 | 675 tok | 4917 | 136,3 tok/s | 3,19 GB | 8,19 GB | 36080 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 93,3% | 28,0% | 15,0 | `Over-Calling` | 1 | 100% | 3510 | 3510 | 389 tok | 5656 | 142,9 tok/s | 3,19 GB | 8,19 GB | 39581 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 18304 | 2288 | 2312 tok | 2172 | 121,6 tok/s | 3,18 GB | 8,15 GB | 17862 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2732 | 911 | 517 tok | 668 | 129,3 tok/s | 3,18 GB | 8,13 GB | 5168 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 87,5% | 26,2% | 40,0 | `Missing Tool Call` | 29 | 86% | 162085 | 5589 | 10365 tok | 4405 | 94,5 tok/s | 3,20 GB | 8,12 GB | 46597 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 71,4% | 21,4% | 42,0 | `Over-Calling` | 27 | 83% | 203599 | 7541 | 11527 tok | 4527 | 82,0 tok/s | 3,18 GB | 8,14 GB | 55215 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 10 | 75% | 21903 | 2190 | 2953 tok | 5030 | 126,6 tok/s | 3,19 GB | 8,17 GB | 39729 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 40,0 | `—` | 14 | 100% | 41756 | 2983 | 4849 tok | 2094 | 104,5 tok/s | 3,17 GB | 8,10 GB | 20029 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 41599 | 2080 | 3849 tok | 1397 | 95,6 tok/s | 3,19 GB | 8,08 GB | 14615 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Tool Loop` | 17 | 50% | 29847 | 1756 | 3545 tok | 1023 | 95,1 tok/s | 3,21 GB | 8,07 GB | 10756 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 83,3% | 25,0% | 48,0 | `Missing Tool Call` | 21 | 80% | 90733 | 4321 | 7160 tok | 2324 | 90,4 tok/s | 3,22 GB | 8,09 GB | 25708 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20471 | 20471 | 10666 tok | 1406 | 110,2 tok/s | 3,19 GB | 8,09 GB | 12763 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38570 | 38570 | 19822 tok | 2026 | 98,0 tok/s | 3,20 GB | 8,09 GB | 20670 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Irrelevance (Negative Control) (Score: 28,0%, 14/15 turns passed):** 
> - End-to-end task failed: 1 of 15 turns failed validation.
> - Failure Taxonomy: `Over-Calling`
> - Turn 8: Model invoked 1 tool call(s) when 0 were expected.

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 8: AST evaluation failed for 'patch_deployment_resource': Argument 'resource_type' value mismatch: expected 'memory_mb' (String), got 'memory_limit' (String)

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 26,2%, 7/8 turns passed):** 
> - End-to-end task failed: Verification tests were not run or failed after applying patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 5: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: django__django-11099 (DiffExplosion) (Score: 21,4%, 5/7 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Over-Calling`
> - Turn 5: Expected tool call 'check_git_diff' was not invoked by model.
> - Turn 7: Model invoked 5 tool call(s) when 0 were expected.

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

> ⚡ **Circuit Breaker Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl):** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 22,5%, 3/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Tool Loop`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 25,0%, 5/6 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 4: Expected tool call 'apply_patch' was not invoked by model.

### Model: `Qwen 3.5 4B (Q4_K_XL) [Healing + Lenient Dispatch]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-lenient.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 11605 | 580 | 415 tok | 5840 | 136,5 tok/s | 3,22 GB | 8,07 GB | 42776 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Bad Arguments` | 20 | 100% | 15225 | 761 | 666 tok | 5066 | 133,3 tok/s | 3,22 GB | 8,07 GB | 38016 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 9131 | 294 | 460 tok | 5324 | 138,7 tok/s | 3,21 GB | 8,08 GB | 38390 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 5618 | 144,3 tok/s | 3,19 GB | 8,04 GB | 38925 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 18424 | 2303 | 2402 tok | 2230 | 122,9 tok/s | 3,19 GB | 8,05 GB | 18141 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2732 | 911 | 517 tok | 646 | 130,0 tok/s | 3,19 GB | 8,04 GB | 4968 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 87,5% | 26,2% | 40,0 | `Missing Tool Call` | 27 | 86% | 130502 | 4833 | 7190 tok | 2913 | 89,9 tok/s | 3,20 GB | 8,04 GB | 32388 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ✅ PASS | 85,7% | 95,7% | 42,0 | `—` | 20 | 100% | 132233 | 6612 | 9527 tok | 3780 | 92,3 tok/s | 3,19 GB | 8,04 GB | 40963 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 16 | 75% | 35610 | 2226 | 3791 tok | 1448 | 104,8 tok/s | 3,20 GB | 8,04 GB | 13819 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Tool Loop` | 23 | 60% | 73238 | 3184 | 5964 tok | 1498 | 87,0 tok/s | 3,21 GB | 8,05 GB | 17226 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 41761 | 2088 | 3828 tok | 1362 | 97,4 tok/s | 3,21 GB | 8,05 GB | 13977 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 50,0% | 15,0% | 40,0 | `Missing Tool Call` | 12 | 50% | 20840 | 1737 | 3189 tok | 1531 | 116,8 tok/s | 3,21 GB | 8,05 GB | 13104 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 48,0 | `Patch Rejected` | 21 | 100% | 112806 | 5372 | 8343 tok | 2914 | 90,0 tok/s | 3,21 GB | 8,05 GB | 32374 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20566 | 20566 | 10633 tok | 1572 | 113,2 tok/s | 3,19 GB | 8,06 GB | 13882 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 37910 | 37910 | 19162 tok | 627 | 63,4 tok/s | 3,19 GB | 8,06 GB | 9885 ms |

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
> **Partial Outcome Details for SWE-bench Replay: django__django-11099 (DiffExplosion) (Score: 95,7%, 6/7 turns passed):** 
> - Turn 7: Model invoked 2 tool call(s) when 0 were expected.

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
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 15,0%, 2/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.
> - Turn 4: Expected tool call 'run_tests' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 30,0%, 6/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

### Model: `Qwen 3.5 4B (Q4_K_XL) [Healing + Tool Compaction]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-healing-tool-compaction.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 90,0% | 27,0% | 10,0 | `Missing Tool Call` | 18 | 90% | 11226 | 624 | 751 tok | 6023 | 135,7 tok/s | 3,21 GB | 8,10 GB | 44385 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 25,0 | `Bad Arguments` | 20 | 100% | 14890 | 744 | 501 tok | 4554 | 131,8 tok/s | 3,21 GB | 8,11 GB | 34542 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 9523 | 307 | 489 tok | 5161 | 138,3 tok/s | 3,21 GB | 8,05 GB | 37306 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 5850 | 144,7 tok/s | 3,21 GB | 8,04 GB | 40415 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Missing Tool Call` | 7 | 88% | 16030 | 2290 | 1730 tok | 2032 | 122,9 tok/s | 3,20 GB | 8,04 GB | 16540 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2941 | 980 | 724 tok | 807 | 132,1 tok/s | 3,22 GB | 8,04 GB | 6108 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 62,5% | 18,8% | 40,0 | `Over-Calling` | 13 | 71% | 73933 | 5687 | 6531 tok | 2933 | 105,7 tok/s | 3,23 GB | 8,04 GB | 27760 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 71,4% | 21,4% | 42,0 | `Over-Calling` | 26 | 83% | 166901 | 6419 | 10998 tok | 7183 | 103,0 tok/s | 3,22 GB | 8,04 GB | 69754 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 7 | 75% | 13746 | 1964 | 2801 tok | 1176 | 118,6 tok/s | 3,20 GB | 8,04 GB | 9913 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Missing Tool Call` | 25 | 80% | 76610 | 3064 | 5667 tok | 2027 | 94,5 tok/s | 3,23 GB | 8,04 GB | 21454 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 47284 | 2364 | 4451 tok | 1959 | 106,2 tok/s | 3,24 GB | 8,05 GB | 18451 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 50,0% | 15,0% | 40,0 | `Missing Tool Call` | 20 | 50% | 40393 | 2020 | 3799 tok | 1165 | 93,3 tok/s | 3,24 GB | 8,04 GB | 12484 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 83,3% | 25,0% | 48,0 | `Missing Tool Call` | 20 | 80% | 96743 | 4837 | 9613 tok | 2947 | 96,1 tok/s | 3,24 GB | 8,04 GB | 30678 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 19870 | 19870 | 10065 tok | 452 | 76,8 tok/s | 3,24 GB | 8,04 GB | 5885 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38145 | 38145 | 19397 tok | 1205 | 84,5 tok/s | 3,25 GB | 8,04 GB | 14260 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Simple Function Calling (Score: 27,0%, 18/20 turns passed):** 
> - End-to-end task failed: 2 of 20 turns failed validation.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 12: Expected tool call 'calculate_displacement' was not invoked by model.
> - Turn 15: Expected tool call 'find_real_estate' was not invoked by model.

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
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 18,8%, 5/8 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Over-Calling`
> - Turn 5: Expected tool call 'apply_patch' was not invoked by model.
> - Turn 6: Expected tool call 'run_tests' was not invoked by model.
> - Turn 8: Model invoked 1 tool call(s) when 0 were expected.

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
> - Turn 2: Expected tool call 'run_tests' was not invoked by model.
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 25,0%, 5/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 1: Expected tool call 'run_tests' was not invoked by model.

### Model: `Qwen 3.5 4B (Q4_K_XL) [Recommended SLM Pipeline]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-recommended-slm.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 85,0% | 25,5% | 10,0 | `Missing Tool Call` | 21 | 95% | 13489 | 642 | 1076 tok | 6834 | 137,8 tok/s | 3,30 GB | 8,04 GB | 49608 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 25,0 | `—` | 20 | 100% | 15372 | 769 | 569 tok | 5134 | 133,2 tok/s | 3,31 GB | 8,04 GB | 38550 ms |
| **BFCL - Parallel Function Calling** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 86,7% | 26,0% | 22,5 | `Tool Loop` | 27 | 81% | 8412 | 312 | 561 tok | 6576 | 141,5 tok/s | 3,29 GB | 8,04 GB | 46489 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 9591 | 146,6 tok/s | 3,29 GB | 8,04 GB | 65434 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 18541 | 2318 | 2313 tok | 2232 | 120,5 tok/s | 3,28 GB | 8,23 GB | 18525 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2906 | 969 | 674 tok | 773 | 127,3 tok/s | 3,28 GB | 8,17 GB | 6072 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 87,5% | 26,2% | 40,0 | `Missing Tool Call` | 23 | 86% | 126566 | 5503 | 8823 tok | 4207 | 97,6 tok/s | 3,29 GB | 8,12 GB | 43095 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 42,0 | `—` | 12 | 100% | 67427 | 5619 | 6560 tok | 3068 | 103,3 tok/s | 3,31 GB | 8,15 GB | 29686 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 12 | 75% | 24725 | 2060 | 3284 tok | 3047 | 125,4 tok/s | 3,32 GB | 8,14 GB | 24295 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ✅ PASS | 80,0% | 94,0% | 40,0 | `—` | 25 | 80% | 84501 | 3380 | 6368 tok | 2515 | 97,9 tok/s | 3,35 GB | 8,20 GB | 25698 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 41614 | 2081 | 3816 tok | 1361 | 97,7 tok/s | 3,32 GB | 8,20 GB | 13935 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Patch Rejected` | 20 | 100% | 53126 | 2656 | 5622 tok | 1792 | 98,5 tok/s | 3,32 GB | 8,21 GB | 18188 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 48,0 | `Patch Rejected` | 19 | 100% | 119254 | 6276 | 10404 tok | 3597 | 94,8 tok/s | 3,31 GB | 8,22 GB | 37934 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20624 | 20624 | 10819 tok | 1725 | 112,9 tok/s | 3,10 GB | 8,10 GB | 15278 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38596 | 38596 | 19848 tok | 1967 | 98,1 tok/s | 3,13 GB | 8,13 GB | 20043 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Simple Function Calling (Score: 25,5%, 17/20 turns passed):** 
> - End-to-end task failed: 3 of 20 turns failed validation.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 5: Tool call count mismatch: expected 1 call(s), but model generated 2.
> - Turn 6: Tool call count mismatch: expected 1 call(s), but model generated 2.
> - Turn 12: Expected tool call 'calculate_displacement' was not invoked by model.

> ⚡ **Circuit Breaker Details for BFCL - Parallel Function Calling:** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

> [!WARNING]
> **Partial Outcome Details for BFCL - Parallel Function Calling (Score: 26,0%, 13/15 turns passed):** 
> - End-to-end task failed: 2 of 15 turns failed validation.
> - Failure Taxonomy: `Tool Loop`
> - Turn 3: Expected tool call 'get_order' was not invoked by model.
> - Turn 12: Expected tool call 'kinematics_calculate_time' was not invoked by model.

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
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose) (Score: 94,0%, 4/5 turns passed):** 
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

> ✂️ **Compaction Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion):** 1,236 chars pruned (~309 tokens), 3 thinking traces collapsed, 0 tool results truncated, 0 summarized (Speculative Hit Rate: 0.0%).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 30,0%, 6/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

### Model: `Qwen 3.5 4B (Q4_K_XL) [All Local LLM without SLM Summarizer]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-without-slm.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 95,0% | 28,5% | 10,0 | `Missing Tool Call` | 19 | 95% | 11464 | 603 | 525 tok | 7632 | 132,4 tok/s | 3,21 GB | 8,14 GB | 57642 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 25,0 | `—` | 20 | 100% | 14843 | 742 | 501 tok | 4910 | 133,2 tok/s | 3,23 GB | 8,14 GB | 36849 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 100% | 9220 | 297 | 518 tok | 5236 | 141,0 tok/s | 3,25 GB | 8,14 GB | 37140 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 7097 | 146,9 tok/s | 3,28 GB | 8,18 GB | 48314 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 19042 | 2380 | 2539 tok | 2421 | 125,5 tok/s | 3,27 GB | 8,18 GB | 19293 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2732 | 911 | 517 tok | 704 | 132,6 tok/s | 3,28 GB | 8,15 GB | 5307 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 62,5% | 18,8% | 40,0 | `Over-Calling` | 28 | 71% | 160210 | 5722 | 10008 tok | 4896 | 99,3 tok/s | 3,28 GB | 8,20 GB | 49321 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 42,0 | `—` | 13 | 100% | 85900 | 6608 | 7734 tok | 3145 | 101,1 tok/s | 3,30 GB | 8,19 GB | 31101 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 17 | 75% | 36798 | 2165 | 3978 tok | 1563 | 101,5 tok/s | 3,31 GB | 8,16 GB | 15404 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 40,0 | `—` | 25 | 100% | 86250 | 3450 | 7009 tok | 2273 | 92,2 tok/s | 3,32 GB | 8,12 GB | 24644 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 48311 | 2416 | 4560 tok | 1939 | 106,5 tok/s | 3,36 GB | 8,13 GB | 18202 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Tool Loop` | 17 | 75% | 35949 | 2115 | 4661 tok | 2207 | 116,5 tok/s | 3,37 GB | 8,10 GB | 18936 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 48,0 | `Patch Rejected` | 21 | 100% | 136078 | 6480 | 11787 tok | 4312 | 99,2 tok/s | 3,33 GB | 8,14 GB | 43489 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20896 | 20896 | 11091 tok | 2211 | 107,2 tok/s | 3,32 GB | 8,21 GB | 20628 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38416 | 38416 | 19668 tok | 1921 | 97,7 tok/s | 3,11 GB | 8,29 GB | 19656 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Simple Function Calling (Score: 28,5%, 19/20 turns passed):** 
> - End-to-end task failed: 1 of 20 turns failed validation.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 6: Expected tool call 'query_database' was not invoked by model.

> [!WARNING]
> **Partial Outcome Details for BFCL - Executable Multi-Turn Workflows (Score: 26,7%, 8/9 turns passed):** 
> - End-to-end task failed: 1 of 9 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 8: AST evaluation failed for 'patch_deployment_resource': Argument 'resource_type' value mismatch: expected 'memory_mb' (String), got 'memory' (String)

> ✂️ **Compaction Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon):** 10,428 chars pruned (~2,607 tokens), 11 thinking traces collapsed, 0 tool results truncated, 0 summarized (Speculative Hit Rate: 0.0%).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon) (Score: 18,8%, 5/8 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Over-Calling`
> - Turn 5: Expected tool call 'apply_patch' was not invoked by model.
> - Turn 6: Expected tool call 'run_tests' was not invoked by model.
> - Turn 8: Model invoked 5 tool call(s) when 0 were expected.

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

> ⚡ **Circuit Breaker Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl):** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: requests__requests-3362 (SuccinctControl) (Score: 30,0%, 4/4 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Tool Loop`

> ✂️ **Compaction Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion):** 7,405 chars pruned (~1,851 tokens), 8 thinking traces collapsed, 0 tool results truncated, 0 summarized (Speculative Hit Rate: 0.0%).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 30,0%, 6/6 turns passed):** 
> - End-to-end task failed: Applied patch does not contain the required semantic fix for this issue.
> - Failure Taxonomy: `Patch Rejected`

### Model: `Qwen 3.5 4B (Q4_K_XL) [All Local LLM with SLM Summarizer]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_opt-all-local-llm-with-slm.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 11762 | 588 | 529 tok | 6122 | 139,9 tok/s | 3,17 GB | 8,15 GB | 43768 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 90,0% | 27,0% | 25,0 | `Bad Arguments` | 21 | 100% | 15655 | 746 | 583 tok | 5374 | 129,4 tok/s | 3,20 GB | 8,10 GB | 41539 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 94% | 9168 | 296 | 450 tok | 5396 | 136,7 tok/s | 3,21 GB | 8,07 GB | 39462 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 6332 | 141,5 tok/s | 3,19 GB | 8,09 GB | 44764 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 17660 | 2208 | 1872 tok | 2170 | 111,9 tok/s | 3,20 GB | 8,05 GB | 19386 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2732 | 911 | 517 tok | 647 | 117,8 tok/s | 3,19 GB | 8,04 GB | 5491 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 40,0 | `Patch Rejected` | 20 | 100% | 127112 | 6356 | 9383 tok | 4167 | 99,2 tok/s | 3,20 GB | 8,08 GB | 41991 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 42,0 | `—` | 13 | 100% | 82040 | 6311 | 7437 tok | 2716 | 95,6 tok/s | 3,22 GB | 8,04 GB | 28407 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 16 | 75% | 35953 | 2247 | 4119 tok | 1752 | 104,3 tok/s | 3,26 GB | 8,02 GB | 16793 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 80,0% | 24,0% | 40,0 | `Missing Tool Call` | 21 | 80% | 54880 | 2613 | 4657 tok | 1604 | 95,4 tok/s | 3,29 GB | 8,08 GB | 16812 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 45803 | 2290 | 4178 tok | 1653 | 99,7 tok/s | 3,30 GB | 8,06 GB | 16582 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 12 | 75% | 29558 | 2463 | 3879 tok | 1486 | 106,9 tok/s | 3,27 GB | 8,05 GB | 13895 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 83,3% | 25,0% | 48,0 | `Missing Tool Call` | 14 | 80% | 76446 | 5460 | 7284 tok | 3648 | 106,7 tok/s | 3,27 GB | 8,03 GB | 34175 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20832 | 20832 | 11027 tok | 2396 | 105,4 tok/s | 3,28 GB | 8,05 GB | 22724 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 38895 | 38895 | 20147 tok | 2710 | 103,3 tok/s | 3,30 GB | 8,05 GB | 26223 ms |

> [!WARNING]
> **Partial Outcome Details for BFCL - Multiple Tools Selection (Score: 27,0%, 18/20 turns passed):** 
> - End-to-end task failed: 2 of 20 turns failed validation.
> - Failure Taxonomy: `Bad Arguments`
> - Turn 12: Tool call count mismatch: expected 1 call(s), but model generated 2.
> - Turn 20: AST evaluation failed for 'calculate_effective_population_size': Argument 'species' value mismatch: expected 'wild tiger' (String), got 'wild tigers' (String)

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

> ✂️ **Compaction Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion):** 1,620 chars pruned (~405 tokens), 3 thinking traces collapsed, 0 tool results truncated, 0 summarized (Speculative Hit Rate: 0.0%).

> [!WARNING]
> **Partial Outcome Details for SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion) (Score: 25,0%, 5/6 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Missing Tool Call`
> - Turn 4: Expected tool call 'apply_patch' was not invoked by model.

### Model: `Qwen 3.5 4B (Q4_K_XL) [All Optimizations Combined]` — [📄 View Full Transcript](transcripts/qwen-3.5-4b-(q4_k_xl)_vulkan_all-optimizations.transcript.md)

| Scenario | Suite | Status | Pass@1 | Step Acc. | Score | Weight | Failure Taxonomy | Tool Calls | Tool Acc. | Prompt Tokens | Tok/Call | Peak Context | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **BFCL - Simple Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 20 | 100% | 11593 | 580 | 420 tok | 5476 | 128,4 tok/s | 3,34 GB | 8,07 GB | 42640 ms |
| **BFCL - Multiple Tools Selection** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 25,0 | `—` | 20 | 100% | 15250 | 762 | 565 tok | 4955 | 133,2 tok/s | 3,35 GB | 7,99 GB | 37197 ms |
| **BFCL - Parallel Function Calling** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 22,5 | `—` | 31 | 94% | 9067 | 292 | 481 tok | 4668 | 140,2 tok/s | 3,34 GB | 7,97 GB | 33301 ms |
| **BFCL - Irrelevance (Negative Control)** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 0 | 100% | 3121 | — | 240 tok | 5339 | 148,1 tok/s | 3,34 GB | 7,97 GB | 36041 ms |
| **BFCL - Executable Multi-Turn Workflows** | BFCL | ⚠️ PARTIAL | ❌ FAIL | 88,9% | 26,7% | 22,5 | `Bad Arguments` | 8 | 100% | 17822 | 2228 | 1857 tok | 2149 | 125,3 tok/s | 3,31 GB | 7,97 GB | 17145 ms |
| **BFCL - Strict Schema & Distractor Validation** | BFCL | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 15,0 | `—` | 3 | 100% | 2732 | 911 | 517 tok | 552 | 127,2 tok/s | 3,30 GB | 7,97 GB | 4338 ms |
| **SWE-bench Replay: scikit-learn__scikit-learn-14092 (DeepHorizon)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 87,5% | 26,2% | 40,0 | `Missing Tool Call` | 25 | 86% | 130339 | 5214 | 8370 tok | 3587 | 98,4 tok/s | 3,31 GB | 7,97 GB | 36440 ms |
| **SWE-bench Replay: django__django-11099 (DiffExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 42,0 | `Tool Loop` | 15 | 83% | 87463 | 5831 | 7827 tok | 5112 | 115,6 tok/s | 3,13 GB | 7,97 GB | 44225 ms |
| **SWE-bench Replay: sphinx-doc__sphinx-8721 (SyntaxRecovery)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 9 | 75% | 17603 | 1956 | 2929 tok | 1160 | 114,3 tok/s | 3,16 GB | 7,97 GB | 10149 ms |
| **SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 60,0% | 18,0% | 40,0 | `Tool Loop` | 18 | 40% | 41014 | 2279 | 3930 tok | 1313 | 98,9 tok/s | 3,20 GB | 7,97 GB | 13280 ms |
| **SWE-bench Replay: sympy__sympy-20590 (RepetitiveLoop)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Over-Calling` | 20 | 100% | 42451 | 2123 | 3955 tok | 1486 | 101,8 tok/s | 3,24 GB | 7,97 GB | 14599 ms |
| **SWE-bench Replay: requests__requests-3362 (SuccinctControl)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 75,0% | 22,5% | 40,0 | `Missing Tool Call` | 11 | 75% | 23609 | 2146 | 3265 tok | 1273 | 112,5 tok/s | 3,24 GB | 7,97 GB | 11321 ms |
| **SWE-bench Replay: pytest-dev__pytest-5221 (TestLogExplosion)**<br/><sub>`[Mock]`</sub> | SWE-bench | ⚠️ PARTIAL | ❌ FAIL | 100,0% | 30,0% | 48,0 | `Patch Rejected` | 21 | 100% | 117814 | 5610 | 10468 tok | 4077 | 95,0 tok/s | 3,25 GB | 8,03 GB | 42917 ms |
| **RULER Context Recall (8k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 10,0 | `—` | 1 | 100% | 20226 | 20226 | 10421 tok | 757 | 93,3 tok/s | 3,25 GB | 8,00 GB | 8116 ms |
| **RULER Context Recall (16k)** | RULER | ✅ PASS | ✅ PASS | 100,0% | 100,0% | 30,0 | `—` | 1 | 100% | 37679 | 37679 | 18931 tok | 403 | 48,7 tok/s | 3,22 GB | 8,00 GB | 8267 ms |

> ⚡ **Circuit Breaker Details for BFCL - Parallel Function Calling:** 1 trip(s), 1 ping-pong loop trip(s), 0 streaming repetition trip(s).

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
> **Partial Outcome Details for SWE-bench Replay: sympy__sympy-13647 (MatrixTranspose) (Score: 18,0%, 3/5 turns passed):** 
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Tool Loop`
> - Turn 3: Expected tool call 'apply_patch' was not invoked by model.
> - Turn 4: Expected tool call 'run_tests' was not invoked by model.

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
> - End-to-end task failed: No patch was applied to the repository via apply_patch.
> - Failure Taxonomy: `Patch Rejected`

