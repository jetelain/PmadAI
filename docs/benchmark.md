# Pmad.AI Tool Calling Benchmark Report

Generated on: 2026-10-12 10:28:29 UTC

## Summary Comparison

| Model | Scenarios Passed | Pass Rate | Tool Calls | Gen Tokens | Avg Speed (tok/s) | Peak RAM | Total Time |
|---|---|---|---|---|---|---|---|
| `Qwen 3.5 2B (Q4_K_XL)` | 3/4 | 75% | 7 | 3777 | 34,6 | 1,79 GB | 109,09s |
| `Qwen 3.5 4B (Q4_K_XL)` | 4/4 | 100% | 5 | 1237 | 12,8 | 3,98 GB | 96,35s |
| `Gemma 4 E2B (Q4_K_XL)` | 4/4 | 100% | 5 | 357 | 13,5 | 2,49 GB | 26,48s |
| `Gemma 4 E4B (Q4_K_XL)` | 4/4 | 100% | 5 | 376 | 7,0 | 5,07 GB | 53,36s |

## Detailed Scenario Breakdown

### Model: `Qwen 3.5 2B (Q4_K_XL)`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | Peak RAM | Duration |
|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1165 | 386 | 27,4 tok/s | 1,77 GB | 14072 ms |
| **Calculator Math Expression** | ❌ FAIL | 3 | 100% | 1874 | 2934 | 38,4 tok/s | 1,78 GB | 76352 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1895 | 446 | 25,3 tok/s | 1,78 GB | 17637 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 194 | 11 | 10,7 tok/s | 1,79 GB | 1028 ms |

> [!WARNING]
> **Failure Details for Calculator Math Expression:** 
> - Turn 1: Expected final answer to state approximately $152.12. Model answered: 'The final total price is **$154.32**.

Here's the breakdown:
- Cost of 3 jackets at $49.99 each: $149.97
- Cost of 2 scarves at $14.50 each: $29.00
- Subtotal: $178.97
- 15% discount: $26.8455
- Final price: $154.32'

### Model: `Qwen 3.5 4B (Q4_K_XL)`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | Peak RAM | Duration |
|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1151 | 460 | 14,0 tok/s | 3,97 GB | 32770 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 682 | 366 | 15,0 tok/s | 3,97 GB | 24477 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1889 | 400 | 10,9 tok/s | 3,97 GB | 36777 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 194 | 11 | 4,7 tok/s | 3,98 GB | 2321 ms |

### Model: `Gemma 4 E2B (Q4_K_XL)`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | Peak RAM | Duration |
|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1118 | 109 | 14,0 tok/s | 2,48 GB | 7761 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 710 | 68 | 14,3 tok/s | 2,48 GB | 4743 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1912 | 173 | 13,4 tok/s | 2,49 GB | 12932 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 201 | 7 | 6,7 tok/s | 2,48 GB | 1039 ms |

### Model: `Gemma 4 E4B (Q4_K_XL)`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | Peak RAM | Duration |
|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1125 | 133 | 8,1 tok/s | 5,06 GB | 16471 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 710 | 76 | 7,6 tok/s | 5,07 GB | 9944 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1884 | 160 | 6,5 tok/s | 5,07 GB | 24783 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 201 | 7 | 3,2 tok/s | 5,06 GB | 2165 ms |

