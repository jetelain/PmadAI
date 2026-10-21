# Pmad.AI SLM Pipeline Optimization & Resilient Dispatch Plan — Round 7

**Document Version:** 3.1  
**Date:** October 20, 2026  
**Status:** Active — diagnoses established from code and fixture inspections; ready for implementation (Milestones 1–4)  
**Target:** `Pmad.AI` and `Pmad.AI.Benchmark` (benchmark changes limited to wiring/telemetry)  
**Data Evaluated:** `docs/round6/benchmark-qwen35-4b-report.md` (**Previous**) → `docs/benchmark-qwen35-4b-report.md` (**Latest**, produced by the Round 6 changes; transcripts in `docs/benchmark-qwen35-4b-report.transcript.md`)  

> [!NOTE]
> Naming: "Latest" is the benchmark run *after* the Round 6 changes were applied. Round 7 is the plan for what comes next; no Round 7 change has been benchmarked yet.

---

## 1. Executive Summary & Comparative Benchmark Analysis

### 1.1 Overview of Results: Previous vs. Latest Run

Reversing the middleware inversion (Tool Call Healing nearest to inference, Lenient Dispatch above it) and dropping destructive Tool Result Compaction from the SLM preset coincided with large gains for `Qwen 3.5 4B (Q4_K_XL)` on Vulkan:

* **Recommended SLM Pipeline**
  * Pass@1 (Weighted) 21.0% → 40.2% (+19.2 points).
  * Overall Pass Rate 41.2% → 55.4% (+14.2 points).
  * SWE-bench Pass@1 0/7 → 2/7 (28.3%), weighted pass rate 46.4%.
  * `django__django-11099 (DiffExplosion)` 21.4% (FAIL) → 100.0% (PASS).
  * `sympy__sympy-13647 (MatrixTranspose)` 24.0% (FAIL) → 94.0% (PASS).
* **All Local LLM without SLM Summarizer**
  * Scenarios passed 7/15 → 8/15 (highest of all configurations).
  * Pass@1 (Weighted) 36.2% → 45.3%; Pass Rate 50.7% → 59.2%.
* **Benchmark integrity**
  * Baseline (raw) 50.9% pass rate (35.1% Pass@1); best configuration 59.2% (45.3% Pass@1).
  * All configurations stay below the 70% SLM ceiling from [`AGENTS.md`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/AGENTS.md). No fixture, scenario, metric or AST validation was relaxed.

> [!WARNING]
> **Variance caveat.** Each configuration is a single run over 15 scenarios, and several configurations moved in opposite directions for no obvious reason (e.g. *Lenient Dispatch* −7.2, *Healing + Tool Compaction* −15.2, *Tool Result Compaction* −8.1 on Pass@1). One scenario flipping is worth several points of weighted Pass@1. The headline gains are directionally encouraging but are **not** statistically established. Round 7 therefore requires repeated runs (§4, Milestone 0) before any final claim is made.

---

### 1.2 Full Configuration Matrix: Previous vs. Latest

| Configuration | Prev Scenarios | Latest Scenarios | Prev Pass@1 (Wtd) | Latest Pass@1 (Wtd) | Delta Pass@1 | Prev Pass Rate | Latest Pass Rate | Delta Pass Rate | Prev Time | Latest Time |
|---|---|---|---|---|---|---|---|---|---|---|
| **Baseline (Raw)** | 6/15 | 7/15 | 32.8% | 35.1% | **+2.3%** | 50.4% | 50.9% | **+0.5%** | 388.67s | 449.88s |
| **Tool Call Healing** | 6/15 | 5/15 | 32.8% | 39.7% | **+6.9%** | 47.5% | 54.6% | **+7.1%** | 346.59s | 420.98s |
| **Thinking Trace Compaction** | 7/15 | 6/15 | 32.8% | 32.4% | **-0.4%** | 49.8% | 49.1% | **-0.7%** | 399.40s | 421.57s |
| **Tool Result Compaction** | 6/15 | 6/15 | 35.8% | 27.7% | **-8.1%** | 49.9% | 46.1% | **-3.8%** | 472.47s | 428.44s |
| **Speculative SLM Compaction** | 5/15 | 7/15 | 21.0% | 29.0% | **+8.0%** | 39.4% | 45.0% | **+5.6%** | 436.66s | 497.50s |
| **Lenient Dispatch & Guardrails** | 8/15 | 7/15 | 41.9% | 34.7% | **-7.2%** | 57.1% | 50.3% | **-6.8%** | 448.79s | 428.82s |
| **Healing + Lenient Dispatch** | 6/15 | 6/15 | 32.8% | 32.8% | **+0.0%** | 49.5% | 48.7% | **-0.8%** | 386.70s | 368.83s |
| **Healing + Tool Compaction** | 5/15 | 5/15 | 36.2% | 21.0% | **-15.2%** | 51.9% | 38.7% | **-13.2%** | 356.91s | 389.94s |
| **Recommended SLM Pipeline** | 5/15 | 6/15 | 21.0% | 40.2% | **+19.2%** | 41.2% | 55.4% | **+14.2%** | 363.37s | 452.83s |
| **All Local LLM (no SLM Summarizer)** | 7/15 | 8/15 | 36.2% | 45.3% | **+9.1%** | 50.7% | 59.2% | **+8.5%** | 370.87s | 445.93s |
| **All Local LLM (with SLM Summarizer)** | 6/15 | 7/15 | 23.3% | 32.8% | **+9.5%** | 42.8% | 49.5% | **+6.7%** | 419.60s | 412.01s |
| **All Optimizations Combined** | 6/15 | 7/15 | 26.7% | 29.0% | **+2.3%** | 44.5% | 46.6% | **+2.1%** | 405.51s | 359.97s |

---

### 1.3 Suite Breakdown: BFCL and SWE-bench

```
SWE-bench Pass@1 (% Task Success):
Previous Recommended SLM:  [                    ]  0.0% (0/7)
Latest   Recommended SLM:  [======              ] 28.3% (2/7)
Latest   All Local LLM:    [======              ] 28.3% (2/7)

Overall Pass Rate (% Weighted Score):
Previous Recommended SLM:  [========            ] 41.2%
Latest   Recommended SLM:  [===========         ] 55.4%
Latest   All Local LLM:    [============        ] 59.2%
```

---

## 2. Root Cause Analysis of Remaining Failures

All findings below have been verified against the codebase ([`src/Pmad.AI`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/src/Pmad.AI), [`benchmarks/Pmad.AI.Benchmark`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/benchmarks/Pmad.AI.Benchmark)) and the execution logs in [`docs/benchmark-qwen35-4b-report.transcript.md`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/docs/benchmark-qwen35-4b-report.transcript.md).

### 2.1 Root Cause 1: Dynamic tools are never registered with `LenientToolDispatcher` — **[Confirmed in Code & Fixtures]**
* **Symptoms:** `BFCL - Executable Multi-Turn Workflows` Turn 8 fails in 10 of 12 configurations (`resource_type` expected `memory_mb`, got `memory_limit` / `memory`). The interventions table shows `Dispatch Total: 0, Alias Hits: 0, Param Hits: 0` for every lenient configuration.
* **Investigation & Verification:**
  * [`BenchmarkPipelineBuilder.CreateConfiguredDispatcher()`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/benchmarks/Pmad.AI.Benchmark/Engine/BenchmarkPipelineBuilder.cs#L325-L344) statically configures only 9 tools, while the suite supplies ~125 fixture tools dynamically via `ChatOptions.Tools`.
  * [`LenientToolDispatchChatClient`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/src/Pmad.AI/Tools/LenientToolDispatcher.cs#L1926-L1956) never inspects `options.Tools`. As a result, `_toolRootSchemas` and `_toolSchemaProperties` are never populated for these tools.
  * Schema-driven parameter normalization (snake/camel/Pascal matching, type coercion, and deep-schema normalization) is completely bypassed.
* **Fixture Schema Inspection Result:**
  * Fixtures [`benchmarks/Pmad.AI.Benchmark/Fixtures/bfcl/multiturn.jsonl`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/benchmarks/Pmad.AI.Benchmark/Fixtures/bfcl/multiturn.jsonl) (line 8) and [`multiple.jsonl`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/benchmarks/Pmad.AI.Benchmark/Fixtures/bfcl/multiple.jsonl) (lines 12, 20) were inspected.
  * None of `resource_type`, `species`, or `battle` declare an `enum` or `const` constraint; all three are declared as bare `{"type": "string"}`.
  * **Conclusion:** Registering dynamic tools will restore property-name casing normalization and type coercion across all dynamic tools. However, schema-driven enum snapping (Recommendation 2) **cannot** fix those three value mismatches (`memory_limit` → `memory_mb`, `wild tigers` → `wild tiger`, `Stalingrad` → `Battle of Stalingrad`). Those remain genuine model value errors; per [`AGENTS.md`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/AGENTS.md), hardcoded aliases must not be introduced.

### 2.2 Root Cause 2: Circuit breaker never trips on the repetitive-loop scenario — **[Confirmed in Code & Transcript]**
The earlier hypothesis (`ResetOnUserPrompt = true` resets the counter on each conversational retry) was disproved:
* [`IsContinuationPrompt`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/src/Pmad.AI/Guardrails/ToolLoopCircuitBreaker.cs#L164-L183) already matches `again`, `one more time`, `retry`, `try again`, and `repeat`. `RecordUserPrompt` returns early for all three retry prompts of `sympy-20590`.

Transcript and implementation evidence ([`docs/benchmark-qwen35-4b-report.transcript.md:L72688`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/docs/benchmark-qwen35-4b-report.transcript.md#L72688)):
* The scenario fails identically in **all 12 configurations**.
* In each turn, the model emits 5 tool calls whose arguments jitter slightly (`sympy/core/symbol.py` vs `sympy/sympy/core/symbol.py`, `end_line` 50/100/200/500, `query` `__slots__`/`Symbol`/`class Symbol`/`slots`, `path: null`).
* In Turn 4, the transcript reports:
  ```
  2. Tool Call: search_code(...)
     Circuit Breaker Tripped: Repetitive invocation detected.
  ```
  **Correction:** This text was **not** produced by [`ToolLoopCircuitBreaker`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/src/Pmad.AI/Guardrails/ToolLoopCircuitBreaker.cs). It was the static mock observation loaded from [`archetype_repetitive_loop.json:L40`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/benchmarks/Pmad.AI.Benchmark/Fixtures/swebench/archetype_repetitive_loop.json#L40) and returned by [`StateAwareSweBenchMockExecutor.ExecuteSearchCode()`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/benchmarks/Pmad.AI.Benchmark/Scenarios/SweBench/ISweBenchExecutor.cs#L305-L310).
* The real middleware breaker **did not trip at all**. If it had tripped, [`ToolLoopCircuitBreakerChatClient`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/src/Pmad.AI/Guardrails/ToolLoopCircuitBreakerChatClient.cs#L77-L87) would have cleared the tool calls from the response and substituted `[Circuit Breaker Tripped]` as final text, resulting in 0 tool calls. Because the middleware breaker remained passive, 5 tool calls reached the environment and the turn failed with:
  `Model invoked 5 tool call(s) when 0 were expected.`
* **Why the breaker remained passive:**
  1. `ToolLoopCircuitBreaker.EvaluateToolInvocation` checks exact hashes of `name:canonicalArgs`. Argument jitter resets `_consecutiveIdenticalCount` back to 1 on almost every call.
  2. Multi-tool cycle detection requires exact identical hashes across cycle periods ($L \ge 2$), which jitter also defeats.
  3. `RecordObservation` resets ping-pong tracking whenever the observation hash changes. Interleaved `read_file` errors and `search_code` outputs alternate between different hashes, resetting the counter.

**Conclusion:** The breaker is blind to semantic loops (varying arguments with identical failure outcomes). Semantic loop detection (Recommendation 3) is required.

*Related inconsistency:* [`RecordUserPrompt`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/src/Pmad.AI/Guardrails/ToolLoopCircuitBreaker.cs#L189) XML doc claims the default of `ResetOnUserPrompt` is `false`, while [`ToolLoopCircuitBreakerOptions.ResetOnUserPrompt`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/src/Pmad.AI/Guardrails/ToolLoopCircuitBreaker.cs#L57) defaults to `true`. This documentation bug must be corrected.

### 2.3 Root Cause 3: Identical duplicate calls inside a single response — **[Confirmed]**
* **Symptoms:** `BFCL - Simple Function Calling` Turn 5 (`compute_hash` emitted twice in the exact same turn with identical arguments `{"algorithm":"sha256","payload":"HelloWorld2026"}`, failing with `expected 1 call(s), but model generated 2`).
* **Scope boundaries:** Other call count mismatches are **not** exact duplicates: e.g. Turn 6 (two `query_database` calls with *different* filters: `status="active"` vs `role="admin"`), Turn 12 (missing call), and Parallel Function Calling Turn 5 (6 calls vs 2 expected). Deduplication cannot and must not be expected to fix calls with distinct arguments.
* Deduplication is only safe for pure/idempotent tools.

### 2.4 Root Cause 4: `apply_patch` rejected with `Hunk context mismatch` — **[Investigated in Code]**
* Inspecting rejected calls across SWE-bench scenarios shows no fenced patches and no CRLF formatting issues.
* Inspecting [`ISweBenchExecutor.TryApplyUnifiedDiff()`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/benchmarks/Pmad.AI.Benchmark/Scenarios/SweBench/ISweBenchExecutor.cs#L440-L496) reveals how patches are evaluated in mock execution:
  ```csharp
  foreach (var line in patchLines)
  {
      if (line.StartsWith("---") || line.StartsWith("+++") || line.StartsWith("@@")) continue;
      if (line.StartsWith('-')) removedLines.Add(line[1..].TrimEnd('\r'));
      else if (line.StartsWith('+')) addedLines.Add(line[1..].TrimEnd('\r'));
  }
  ```
* The mock executor **already ignores `@@` headers and bare context lines completely**. The error `Hunk context mismatch: removed lines were not found in original file` occurs strictly because the model generated `-` lines that do not exist in the virtual file.
* **Conclusion:** Mechanical patch repairs (recalculating `@@` counts or prepending spaces to bare lines) would have **zero impact** on mock benchmark execution. Recommendation 5 is therefore dropped from Round 7.

### 2.5 Root Cause 5: No feedback signal on repeated tool errors — **[Confirmed]**
The same failing call pattern repeats many times with identical error messages (`Hunk context mismatch…`, `File … not found`). Neither the breaker nor the dispatcher provides feedback on consecutive tool failures.

---

## 3. Architecture & Recommendations for Round 7

Pipeline order is unchanged (outermost → innermost), matching [`ChatClientBuilderExtensions`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/src/Pmad.AI/Extensions/ChatClientBuilderExtensions.cs#L43-L44):

```
Request → ThinkingTraceCompaction → ToolLoopCircuitBreaker → LenientToolDispatch → ToolCallHealing → Raw IChatClient
```

Changes touch only the breaker and the dispatch layers.

---

### Recommendation 1: Register `options.Tools` with the dispatcher (High Impact, Low Risk)

#### Proposed Implementation
In [`LenientToolDispatchChatClient`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/src/Pmad.AI/Tools/LenientToolDispatcher.cs#L1926-L1956), register the executable functions from `ChatOptions.Tools` on every request (both `GetResponseAsync` and `GetStreamingResponseAsync`):

```csharp
private void RegisterRequestTools(ChatOptions options)
{
    if (options.Tools is { Count: > 0 })
    {
        _dispatcher.RegisterTools(options.Tools.OfType<AIFunction>());
    }
}
```

Requirements:
* **Idempotent registration:** In [`LenientToolDispatcher.RegisterTool()`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/src/Pmad.AI/Tools/LenientToolDispatcher.cs#L577-L582), check if `_registeredTools.TryGetValue(tool.Name, out var existing)` and skip schema re-parsing if the instance or schema is unchanged. Avoid re-allocating property dictionaries on every turn.
* **Casing & Canonical Resolution:** Verify `NormalizeCall` resolves canonical names for tools that exist in `_registeredTools`. Ensure `_canonicalToolNames` is kept in sync.
* Cover both non-streaming and streaming execution paths.
* Verify telemetry coverage so `Dispatch Total` reflects normalized calls.

---

### Recommendation 2: Schema-driven enum value snapping (Generic Library Utility)

Add a generic normalization step to [`LenientToolDispatcher`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/src/Pmad.AI/Tools/LenientToolDispatcher.cs): when a tool's JSON schema property declares `enum` (or `const`), and the supplied string value is not an exact member, snap it to the closest member using deterministic rules only:
1. Ordinal case-insensitive match.
2. Normalization (trim, collapse whitespace/underscore/hyphen, naive plural stripping).
3. Unique prefix/suffix/substring containment (e.g. `Stalingrad` in `Battle of Stalingrad`) — only when exactly one member matches.
4. Bounded edit distance (e.g. $\le 2$ and $\le 25\%$ of length) with a unique best candidate.
5. Otherwise leave the value untouched (never guess among ties) and record a telemetry event.

> [!NOTE]
> As verified in §2.1, BFCL benchmark fixtures do not declare `enum` on the failing parameters. This recommendation is implemented as a general-purpose library improvement for consumer schemas, but will **not** alter the BFCL benchmark score.

---

### Recommendation 3: Argument-agnostic semantic loop detection in the circuit breaker (High Impact)

#### Problem Solved
`sympy-20590` (Root Cause 2) and repeated-error patterns evade exact-hash ping-pong detection because arguments jitter while outcomes do not.

#### Proposed Implementation (opt-in options on `ToolLoopCircuitBreakerOptions`, enabled in recommended presets)

1. **Same-outcome streak** — `MaxIdenticalObservationStreak` (suggested default 4 in presets, `0` = disabled). Track the hash of each tool observation. If the last $N$ tool observations (across any arguments or a set of tools) are identical, trip with `LoopType.ToolPingPong` or a new `LoopType.NoProgress`. Equal hashes increment the streak counter.
2. **Per-tool failure streak** — `MaxConsecutiveToolErrors` (suggested 5). An observation is treated as an error when the tool result represents an exception or begins with `Error:` / `File ... not found`. Trip after $N$ consecutive errors from the **same tool name**, regardless of argument variations.
3. **Observation reset semantics** — a changed observation hash only resets the *exact-hash* ping-pong counter. Streak counters reset only upon receiving a **successful, non-error, non-repeated** observation, ensuring alternating failed tools do not reset the streak.
4. **Fix documentation inconsistency** on `ResetOnUserPrompt` (default is `true`; update the XML doc of `RecordUserPrompt`).
5. Keep `IsContinuationPrompt` intact.

#### Guardrail
Any trip in a scenario that previously passed is a regression; thresholds must be tuned conservatively.

---

### Recommendation 4: Opt-in deduplication of identical calls in one response (Targeted)

#### Scope
Addresses `BFCL - Simple Function Calling` Turn 5-style duplicate calls (identical name + canonical arguments in a single assistant message).

#### Proposed Implementation
* New option `LenientToolDispatcherOptions.DeduplicateIdenticalCallsInTurn` (**default `false`**). When `true`, repeated identical calls inside a single assistant message are dropped, retaining the first invocation.
* Safety: restrict to tools flagged idempotent (`ToolMapping.Idempotent` or a predicate).
* **Reflection-free hashing:** Build the deduplication key from canonicalized arguments using a deterministic, reflection-free serializer (reusing or extending `ToolLoopCircuitBreaker.SerializeArguments`), ensuring full Native AOT compatibility.
* Telemetry: Record `LenientDispatchEventType.DuplicateCallDeduplicated`.
* Benchmark: Enable across all benchmark configurations alike (benchmark fixtures are mock/read-only tools).

---

### Recommendation 5: Lenient unified-diff normalization — **[Out of Scope for Round 7]**

As established in §2.4, [`ISweBenchExecutor.TryApplyUnifiedDiff()`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/benchmarks/Pmad.AI.Benchmark/Scenarios/SweBench/ISweBenchExecutor.cs#L440-L496) already ignores hunk headers (`@@`) and bare context lines during mock execution. The failures are caused by hallucinated removed lines. Mechanical diff normalizers will have zero impact on mock benchmark scores. This recommendation is therefore **dropped from Round 7** to preserve engineering focus on high-yield items.

---

### Recommendation 6: Tool error feedback hints for retry storms (Medium Value)

Complements Recommendation 3 without blocking execution early. After $N$ consecutive identical errors for a tool, append a short, deterministic, generic hint to the tool message before passing history to the model (e.g., `"The previous 3 attempts failed with the same error. Re-read the target and verify paths/context before retrying."`).
* Opt-in, off by default, enabled in presets.
* Implemented by non-destructively cloning `ChatMessage` instances in middleware, preserving caller history immutability.

---

## 4. Implementation Roadmap for Round 7

| Milestone | Key Deliverables | Target Files | Verification Method |
| :--- | :--- | :--- | :--- |
| **0. Diagnosis & Baseline Variance** | (a) Verify `sympy-20590` mock executor finding vs breaker; (b) confirm fixture schemas lack enums [DONE]; (c) confirm `TryApplyUnifiedDiff` ignores headers [DONE]; (d) run the **unchanged** baseline pipeline $\ge 3$ times to establish per-configuration variance. | `benchmarks/Pmad.AI.Benchmark`, `docs/` | Recorded variance metrics appended to documentation. |
| **1. Dynamic Tool Registration** | Rec 1: register `options.Tools` in `LenientToolDispatchChatClient` (streaming + non-streaming), idempotent caching in `RegisterTool`. | [`LenientToolDispatcher.cs`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/src/Pmad.AI/Tools/LenientToolDispatcher.cs) | Unit tests with dynamic `AIFunction` instances: property-name aliasing and type coercion functional; static config preserved; AOT smoke test passes. |
| **2. Semantic Loop Detection** | Rec 3 options (`MaxIdenticalObservationStreak`, `MaxConsecutiveToolErrors`), `LoopType.NoProgress`, doc fix for `ResetOnUserPrompt`; Rec 6 hint. | [`ToolLoopCircuitBreaker.cs`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/src/Pmad.AI/Guardrails/ToolLoopCircuitBreaker.cs), [`ToolLoopCircuitBreakerChatClient.cs`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/src/Pmad.AI/Guardrails/ToolLoopCircuitBreakerChatClient.cs), [`ChatClientBuilderExtensions.cs`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/src/Pmad.AI/Extensions/ChatClientBuilderExtensions.cs) | Unit tests: jittered-argument loop trips on Turn 4; varied calls do not false-positive; interleaved errors do not reset streak counters. |
| **3. Enum Snapping (Library Utility)** | Rec 2 deterministic snapping with telemetry (generic feature). | [`LenientToolDispatcher.cs`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/src/Pmad.AI/Tools/LenientToolDispatcher.cs) | Unit tests with enum schemas: case/plural/substring snapping; ambiguous ties untouched; AOT build passes. |
| **4. Opt-in Single-Turn Dedup** | Rec 4 (`DeduplicateIdenticalCallsInTurn`, reflection-free key serializer, default `false`). | [`LenientToolDispatcher.cs`](file:///c:/Users/Julien/source/repos/jetelain/PmadAI/src/Pmad.AI/Tools/LenientToolDispatcher.cs) | Unit tests for identical duplicate calls dropped; distinct calls preserved; telemetry recorded. |
| **5. Benchmark Replay** | Re-run full suite with $\ge 3$ repetitions per configuration; report mean $\pm$ range, breaker trips per scenario, and isolated effect of each recommendation. | `benchmarks/Pmad.AI.Benchmark` | SLM pass rate $\le 70\%$; zero regressions on previously passing scenarios; no fixture or metric tampering. |

---

## 5. Non-Negotiable Benchmark Integrity Checklist

To be ticked only after Milestone 5 has been verified, not in advance:

- [ ] **No Scenario Modification:** no benchmark prompt, turn sequence, or fixture file is altered.
- [ ] **No Metric Relaxation:** expected AST values, regexes, and scoring weights remain identical.
- [ ] **Difficulty Ceiling Guard:** SLM (4B class) pass rate stays $\le 70\%$.
- [ ] **No Fixture-Specific Tuning:** no benchmark dispatcher mapping, alias, or hint references an expected answer, scenario name, or fixture tool; all mechanisms are generic and schema/behavior driven.
- [ ] **Zero Model Favoritism:** all optimizations are generic `IChatClient` middleware applied equally to every model and configuration.
- [ ] **Statistical Honesty:** results reported from repeated runs; single-run deltas are not presented as improvements.
- [ ] **AOT-Ready:** no runtime reflection (notably no `JsonSerializer.Serialize` on untyped dictionaries); builds under Native AOT.
