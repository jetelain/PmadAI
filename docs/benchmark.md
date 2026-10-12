# Pmad.AI Tool Calling Benchmark Report

Generated on: 2026-10-12 18:32:24 UTC

## Environment

- **CPU:** Intel(R) Core(TM) i7-14700KF (28 logical cores)
- **GPU:** NVIDIA GeForce RTX 4080 SUPER, 15,99 GB VRAM, Driver: 610.88
- **OS:** Microsoft Windows 10.0.26200

> [!NOTE]
> 📄 **Execution Transcript & Failure Analysis:** Detailed turn-by-turn prompts, reasoning traces, tool inputs/outputs, and failure analysis are recorded in [transcript.md](transcript.md).

## Summary Comparison

| Model | Backend | Reasoning | Scenarios Passed | Pass Rate | Tool Calls | Gen Tokens | Avg Speed (tok/s) | Sys RAM | GPU VRAM | Total Time |
|---|---|---|---|---|---|---|---|---|---|---|
| `Qwen 3.5 2B (Q4_K_XL)` | CPU | Disabled | 2/4 | 50% | 3 | 582 | 36,8 | 1,62 GB | - | 15,81s |
| `Qwen 3.5 2B (Q4_K_XL)` | CPU | Enabled | 3/4 | 75% | 9 | 3757 | 39,3 | 1,64 GB | - | 95,53s |
| `Qwen 3.5 2B (Q4_K_XL)` | Vulkan | Disabled | 3/4 | 75% | 4 | 843 | 220,8 | 1,53 GB | 4,19 GB | 3,82s |
| `Qwen 3.5 2B (Q4_K_XL)` | Vulkan | Enabled | 4/4 | 100% | 5 | 1420 | 239,1 | 1,52 GB | 4,20 GB | 5,94s |
| `Qwen 3.5 4B (Q4_K_XL)` | CPU | Disabled | 4/4 | 100% | 5 | 496 | 13,9 | 3,31 GB | - | 35,67s |
| `Qwen 3.5 4B (Q4_K_XL)` | CPU | Enabled | 4/4 | 100% | 5 | 1349 | 14,5 | 3,29 GB | - | 93,15s |
| `Qwen 3.5 4B (Q4_K_XL)` | Vulkan | Disabled | 4/4 | 100% | 5 | 497 | 119,0 | 3,01 GB | 5,65 GB | 4,18s |
| `Qwen 3.5 4B (Q4_K_XL)` | Vulkan | Enabled | 4/4 | 100% | 5 | 1532 | 134,6 | 2,99 GB | 5,65 GB | 11,38s |
| `Gemma 4 E2B (Q4_K_XL)` | CPU | Disabled | 3/4 | 75% | 5 | 357 | 23,7 | 1,92 GB | - | 15,08s |
| `Gemma 4 E2B (Q4_K_XL)` | CPU | Enabled | 4/4 | 100% | 5 | 375 | 24,6 | 1,92 GB | - | 15,27s |
| `Gemma 4 E2B (Q4_K_XL)` | Vulkan | Disabled | 4/4 | 100% | 5 | 349 | 152,2 | 1,85 GB | 4,26 GB | 2,29s |
| `Gemma 4 E2B (Q4_K_XL)` | Vulkan | Enabled | 4/4 | 100% | 5 | 369 | 154,7 | 1,80 GB | 4,42 GB | 2,39s |
| `Gemma 4 E4B (Q4_K_XL)` | CPU | Disabled | 4/4 | 100% | 5 | 349 | 14,0 | 3,72 GB | - | 24,88s |
| `Gemma 4 E4B (Q4_K_XL)` | CPU | Enabled | 4/4 | 100% | 5 | 364 | 14,3 | 3,71 GB | - | 25,42s |
| `Gemma 4 E4B (Q4_K_XL)` | Vulkan | Disabled | 4/4 | 100% | 5 | 388 | 97,1 | 3,38 GB | 6,13 GB | 4,00s |
| `Gemma 4 E4B (Q4_K_XL)` | Vulkan | Enabled | 4/4 | 100% | 5 | 387 | 99,9 | 3,32 GB | 6,10 GB | 3,88s |

## Hardware Acceleration Comparison (CPU vs Vulkan GPU)

| Model | Reasoning | CPU Speed (tok/s) | Vulkan Speed (tok/s) | Vulkan Speedup | CPU RAM | Vulkan VRAM | Duration (CPU -> Vulkan) |
|---|---|---|---|---|---|---|---|
| `Qwen 3.5 2B (Q4_K_XL)` | Disabled | 36,8 | 220,8 | **6,0x** | 1,62 GB | 4,19 GB | 15,8s -> 3,8s |
| `Qwen 3.5 2B (Q4_K_XL)` | Enabled | 39,3 | 239,1 | **6,1x** | 1,64 GB | 4,20 GB | 95,5s -> 5,9s |
| `Qwen 3.5 4B (Q4_K_XL)` | Disabled | 13,9 | 119,0 | **8,6x** | 3,31 GB | 5,65 GB | 35,7s -> 4,2s |
| `Qwen 3.5 4B (Q4_K_XL)` | Enabled | 14,5 | 134,6 | **9,3x** | 3,29 GB | 5,65 GB | 93,2s -> 11,4s |
| `Gemma 4 E2B (Q4_K_XL)` | Disabled | 23,7 | 152,2 | **6,4x** | 1,92 GB | 4,26 GB | 15,1s -> 2,3s |
| `Gemma 4 E2B (Q4_K_XL)` | Enabled | 24,6 | 154,7 | **6,3x** | 1,92 GB | 4,42 GB | 15,3s -> 2,4s |
| `Gemma 4 E4B (Q4_K_XL)` | Disabled | 14,0 | 97,1 | **6,9x** | 3,72 GB | 6,13 GB | 24,9s -> 4,0s |
| `Gemma 4 E4B (Q4_K_XL)` | Enabled | 14,3 | 99,9 | **7,0x** | 3,71 GB | 6,10 GB | 25,4s -> 3,9s |

## Reasoning Impact Comparison (With vs Without Reasoning)

| Model | Backend | No-Reasoning Speed | Reasoning Speed | Speed Ratio | Generated Tokens (Off -> On) | Duration (Off -> On) |
|---|---|---|---|---|---|---|
| `Qwen 3.5 2B (Q4_K_XL)` | CPU | 36,8 tok/s | 39,3 tok/s | 1,07x | 582 -> 3757 (+546%) | 15,8s -> 95,5s (+79,7s) |
| `Qwen 3.5 2B (Q4_K_XL)` | Vulkan | 220,8 tok/s | 239,1 tok/s | 1,08x | 843 -> 1420 (+68%) | 3,8s -> 5,9s (+2,1s) |
| `Qwen 3.5 4B (Q4_K_XL)` | CPU | 13,9 tok/s | 14,5 tok/s | 1,04x | 496 -> 1349 (+172%) | 35,7s -> 93,2s (+57,5s) |
| `Qwen 3.5 4B (Q4_K_XL)` | Vulkan | 119,0 tok/s | 134,6 tok/s | 1,13x | 497 -> 1532 (+208%) | 4,2s -> 11,4s (+7,2s) |
| `Gemma 4 E2B (Q4_K_XL)` | CPU | 23,7 tok/s | 24,6 tok/s | 1,04x | 357 -> 375 (+5%) | 15,1s -> 15,3s (+0,2s) |
| `Gemma 4 E2B (Q4_K_XL)` | Vulkan | 152,2 tok/s | 154,7 tok/s | 1,02x | 349 -> 369 (+6%) | 2,3s -> 2,4s (+0,1s) |
| `Gemma 4 E4B (Q4_K_XL)` | CPU | 14,0 tok/s | 14,3 tok/s | 1,02x | 349 -> 364 (+4%) | 24,9s -> 25,4s (+0,5s) |
| `Gemma 4 E4B (Q4_K_XL)` | Vulkan | 97,1 tok/s | 99,9 tok/s | 1,03x | 388 -> 387 (+-0%) | 4,0s -> 3,9s (+-0,1s) |

## Detailed Scenario Breakdown

### Model: `Qwen 3.5 2B (Q4_K_XL) [CPU | No Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1148 | 194 | 33,3 tok/s | 1,60 GB | - | 5832 ms |
| **Calculator Math Expression** | ❌ FAIL | 0 | 0% | 282 | 203 | 41,7 tok/s | 1,60 GB | - | 4868 ms |
| **Multi-Turn Customer Support Pipeline** | ❌ FAIL | 1 | 50% | 1287 | 176 | 37,1 tok/s | 1,62 GB | - | 4745 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 198 | 9 | 24,7 tok/s | 1,62 GB | - | 364 ms |

> [!WARNING]
> **Failure Details for Calculator Math Expression:** 
> - Turn 1: Expected tool call 'calculate' was not invoked by model.

> [!WARNING]
> **Failure Details for Multi-Turn Customer Support Pipeline:** 
> - Turn 2: Expected tool call 'track_package' was not invoked by model.

### Model: `Qwen 3.5 2B (Q4_K_XL) [CPU | Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1295 | 354 | 37,2 tok/s | 1,61 GB | - | 9522 ms |
| **Calculator Math Expression** | ❌ FAIL | 5 | 100% | 7103 | 2898 | 40,3 tok/s | 1,63 GB | - | 71997 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 2322 | 459 | 36,2 tok/s | 1,64 GB | - | 12685 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 196 | 46 | 34,6 tok/s | 1,64 GB | - | 1328 ms |

> [!WARNING]
> **Failure Details for Calculator Math Expression:** 
> - Turn 1: Expected final answer to state approximately $152.12. Model answered: ''

### Model: `Qwen 3.5 2B (Q4_K_XL) [Vulkan | No Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1145 | 190 | 174,6 tok/s | 1,51 GB | 4,19 GB | 1088 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 897 | 409 | 253,3 tok/s | 1,52 GB | 4,19 GB | 1615 ms |
| **Multi-Turn Customer Support Pipeline** | ❌ FAIL | 1 | 50% | 1295 | 235 | 230,3 tok/s | 1,53 GB | 4,13 GB | 1021 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 198 | 9 | 95,9 tok/s | 1,51 GB | 4,13 GB | 94 ms |

> [!WARNING]
> **Failure Details for Multi-Turn Customer Support Pipeline:** 
> - Turn 2: Expected tool call 'track_package' was not invoked by model.

### Model: `Qwen 3.5 2B (Q4_K_XL) [Vulkan | Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1348 | 388 | 239,7 tok/s | 1,52 GB | 4,13 GB | 1619 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 954 | 488 | 250,8 tok/s | 1,52 GB | 4,19 GB | 1946 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 2396 | 500 | 233,6 tok/s | 1,52 GB | 4,20 GB | 2141 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 196 | 44 | 188,3 tok/s | 1,52 GB | 4,20 GB | 234 ms |

### Model: `Qwen 3.5 4B (Q4_K_XL) [CPU | No Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1124 | 157 | 13,5 tok/s | 3,29 GB | - | 11596 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 690 | 164 | 15,0 tok/s | 3,30 GB | - | 10916 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1865 | 166 | 13,7 tok/s | 3,30 GB | - | 12159 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 198 | 9 | 9,0 tok/s | 3,31 GB | - | 999 ms |

### Model: `Qwen 3.5 4B (Q4_K_XL) [CPU | Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1309 | 360 | 14,4 tok/s | 3,29 GB | - | 24978 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 927 | 430 | 14,4 tok/s | 3,29 GB | - | 29850 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 2396 | 498 | 14,6 tok/s | 3,29 GB | - | 34089 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 196 | 61 | 14,4 tok/s | 3,29 GB | - | 4237 ms |

### Model: `Qwen 3.5 4B (Q4_K_XL) [Vulkan | No Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1127 | 156 | 118,3 tok/s | 2,99 GB | 5,64 GB | 1319 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 690 | 142 | 129,9 tok/s | 2,99 GB | 5,64 GB | 1093 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1895 | 190 | 117,0 tok/s | 3,00 GB | 5,65 GB | 1624 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 198 | 9 | 64,1 tok/s | 3,01 GB | 5,65 GB | 140 ms |

### Model: `Qwen 3.5 4B (Q4_K_XL) [Vulkan | Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1298 | 352 | 130,6 tok/s | 2,98 GB | 5,65 GB | 2696 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 876 | 537 | 138,1 tok/s | 2,98 GB | 5,64 GB | 3888 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 2360 | 483 | 132,0 tok/s | 2,98 GB | 5,63 GB | 3659 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 196 | 160 | 140,3 tok/s | 2,99 GB | 5,61 GB | 1140 ms |

### Model: `Gemma 4 E2B (Q4_K_XL) [CPU | No Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1119 | 108 | 23,8 tok/s | 1,90 GB | - | 4547 ms |
| **Calculator Math Expression** | ❌ FAIL | 1 | 100% | 712 | 80 | 24,7 tok/s | 1,90 GB | - | 3238 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1878 | 160 | 24,0 tok/s | 1,91 GB | - | 6671 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 201 | 9 | 14,3 tok/s | 1,92 GB | - | 628 ms |

> [!WARNING]
> **Failure Details for Calculator Math Expression:** 
> - Turn 1: Expected final answer to state approximately $152.12. Model answered: 'The final total price after the 15% discount is $174.62.'

### Model: `Gemma 4 E2B (Q4_K_XL) [CPU | Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1119 | 116 | 22,4 tok/s | 1,90 GB | - | 5168 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 710 | 72 | 27,0 tok/s | 1,90 GB | - | 2671 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1914 | 178 | 25,7 tok/s | 1,92 GB | - | 6920 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 201 | 9 | 17,7 tok/s | 1,92 GB | - | 509 ms |

### Model: `Gemma 4 E2B (Q4_K_XL) [Vulkan | No Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1119 | 108 | 145,1 tok/s | 1,83 GB | 4,26 GB | 744 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 710 | 72 | 164,5 tok/s | 1,84 GB | 4,26 GB | 438 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1884 | 160 | 159,5 tok/s | 1,85 GB | 4,26 GB | 1003 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 201 | 9 | 82,8 tok/s | 1,85 GB | 4,26 GB | 109 ms |

### Model: `Gemma 4 E2B (Q4_K_XL) [Vulkan | Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1119 | 116 | 147,9 tok/s | 1,78 GB | 4,42 GB | 784 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 710 | 72 | 162,8 tok/s | 1,79 GB | 4,42 GB | 442 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1908 | 172 | 163,7 tok/s | 1,80 GB | 4,42 GB | 1051 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 201 | 9 | 83,0 tok/s | 1,80 GB | 4,37 GB | 108 ms |

### Model: `Gemma 4 E4B (Q4_K_XL) [CPU | No Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1124 | 97 | 13,2 tok/s | 3,69 GB | - | 7328 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 710 | 79 | 16,0 tok/s | 3,70 GB | - | 4928 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1876 | 164 | 14,5 tok/s | 3,71 GB | - | 11308 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 201 | 9 | 6,8 tok/s | 3,72 GB | - | 1316 ms |

### Model: `Gemma 4 E4B (Q4_K_XL) [CPU | Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1126 | 92 | 12,6 tok/s | 3,69 GB | - | 7296 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 710 | 80 | 15,9 tok/s | 3,70 GB | - | 5021 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1908 | 183 | 15,1 tok/s | 3,71 GB | - | 12096 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 201 | 9 | 8,9 tok/s | 3,71 GB | - | 1006 ms |

### Model: `Gemma 4 E4B (Q4_K_XL) [Vulkan | No Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1125 | 134 | 91,6 tok/s | 3,36 GB | 6,13 GB | 1463 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 710 | 79 | 104,8 tok/s | 3,36 GB | 6,13 GB | 754 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1880 | 166 | 101,4 tok/s | 3,38 GB | 6,13 GB | 1636 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 201 | 9 | 62,8 tok/s | 3,38 GB | 6,13 GB | 143 ms |

### Model: `Gemma 4 E4B (Q4_K_XL) [Vulkan | Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1124 | 138 | 99,7 tok/s | 3,30 GB | 6,09 GB | 1385 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 710 | 80 | 104,7 tok/s | 3,30 GB | 6,09 GB | 764 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1880 | 160 | 100,9 tok/s | 3,32 GB | 6,09 GB | 1586 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 201 | 9 | 64,2 tok/s | 3,32 GB | 6,09 GB | 140 ms |

