# Pmad.AI Tool Calling Benchmark Report

Generated on: 2026-10-12 17:35:50 UTC

## Environment

- **CPU:** Intel(R) Core(TM) i7-14700KF (28 logical cores)
- **GPU:** NVIDIA GeForce RTX 4080 SUPER, 15,99 GB VRAM, Driver: 610.88
- **OS:** Microsoft Windows 10.0.26200

> [!NOTE]
> 📄 **Execution Transcript & Failure Analysis:** Detailed turn-by-turn prompts, reasoning traces, tool inputs/outputs, and failure analysis are recorded in [transcript.md](transcript.md).

## Summary Comparison

| Model | Backend | Reasoning | Scenarios Passed | Pass Rate | Tool Calls | Gen Tokens | Avg Speed (tok/s) | Sys RAM | GPU VRAM | Total Time |
|---|---|---|---|---|---|---|---|---|---|---|
| `Qwen 3.5 2B (Q4_K_XL)` | CPU | Disabled | 4/4 | 100% | 5 | 882 | 33,8 | 1,65 GB | - | 26,09s |
| `Qwen 3.5 2B (Q4_K_XL)` | CPU | Enabled | 3/4 | 75% | 9 | 2647 | 36,7 | 1,64 GB | - | 72,14s |
| `Qwen 3.5 2B (Q4_K_XL)` | Vulkan | Disabled | 4/4 | 100% | 5 | 784 | 156,7 | 1,56 GB | 4,22 GB | 5,00s |
| `Qwen 3.5 2B (Q4_K_XL)` | Vulkan | Enabled | 4/4 | 100% | 7 | 1448 | 180,6 | 1,57 GB | 4,22 GB | 8,02s |
| `Qwen 3.5 4B (Q4_K_XL)` | CPU | Disabled | 4/4 | 100% | 5 | 512 | 12,7 | 3,32 GB | - | 40,27s |
| `Qwen 3.5 4B (Q4_K_XL)` | CPU | Enabled | 4/4 | 100% | 5 | 1416 | 14,2 | 3,32 GB | - | 99,40s |
| `Qwen 3.5 4B (Q4_K_XL)` | Vulkan | Disabled | 4/4 | 100% | 5 | 382 | 71,6 | 3,01 GB | 5,86 GB | 5,33s |
| `Qwen 3.5 4B (Q4_K_XL)` | Vulkan | Enabled | 4/4 | 100% | 5 | 1491 | 115,0 | 3,02 GB | 5,86 GB | 12,96s |
| `Gemma 4 E2B (Q4_K_XL)` | CPU | Disabled | 4/4 | 100% | 5 | 695 | 25,1 | 1,96 GB | - | 27,70s |
| `Gemma 4 E2B (Q4_K_XL)` | CPU | Enabled | 3/4 | 75% | 5 | 353 | 22,1 | 1,97 GB | - | 16,00s |
| `Gemma 4 E2B (Q4_K_XL)` | Vulkan | Disabled | 4/4 | 100% | 5 | 355 | 85,8 | 1,90 GB | 4,52 GB | 4,14s |
| `Gemma 4 E2B (Q4_K_XL)` | Vulkan | Enabled | 3/4 | 75% | 5 | 352 | 85,6 | 1,91 GB | 4,52 GB | 4,11s |
| `Gemma 4 E4B (Q4_K_XL)` | CPU | Disabled | 4/4 | 100% | 5 | 359 | 12,5 | 3,77 GB | - | 28,77s |
| `Gemma 4 E4B (Q4_K_XL)` | CPU | Enabled | 4/4 | 100% | 5 | 359 | 12,5 | 3,77 GB | - | 28,71s |
| `Gemma 4 E4B (Q4_K_XL)` | Vulkan | Disabled | 4/4 | 100% | 5 | 366 | 58,6 | 3,37 GB | 6,33 GB | 6,25s |
| `Gemma 4 E4B (Q4_K_XL)` | Vulkan | Enabled | 4/4 | 100% | 5 | 379 | 59,5 | 3,38 GB | 6,33 GB | 6,37s |

## Hardware Acceleration Comparison (CPU vs Vulkan GPU)

| Model | Reasoning | CPU Speed (tok/s) | Vulkan Speed (tok/s) | Vulkan Speedup | CPU RAM | Vulkan VRAM | Duration (CPU -> Vulkan) |
|---|---|---|---|---|---|---|---|
| `Qwen 3.5 2B (Q4_K_XL)` | Disabled | 33,8 | 156,7 | **4,6x** | 1,65 GB | 4,22 GB | 26,1s -> 5,0s |
| `Qwen 3.5 2B (Q4_K_XL)` | Enabled | 36,7 | 180,6 | **4,9x** | 1,64 GB | 4,22 GB | 72,1s -> 8,0s |
| `Qwen 3.5 4B (Q4_K_XL)` | Disabled | 12,7 | 71,6 | **5,6x** | 3,32 GB | 5,86 GB | 40,3s -> 5,3s |
| `Qwen 3.5 4B (Q4_K_XL)` | Enabled | 14,2 | 115,0 | **8,1x** | 3,32 GB | 5,86 GB | 99,4s -> 13,0s |
| `Gemma 4 E2B (Q4_K_XL)` | Disabled | 25,1 | 85,8 | **3,4x** | 1,96 GB | 4,52 GB | 27,7s -> 4,1s |
| `Gemma 4 E2B (Q4_K_XL)` | Enabled | 22,1 | 85,6 | **3,9x** | 1,97 GB | 4,52 GB | 16,0s -> 4,1s |
| `Gemma 4 E4B (Q4_K_XL)` | Disabled | 12,5 | 58,6 | **4,7x** | 3,77 GB | 6,33 GB | 28,8s -> 6,2s |
| `Gemma 4 E4B (Q4_K_XL)` | Enabled | 12,5 | 59,5 | **4,8x** | 3,77 GB | 6,33 GB | 28,7s -> 6,4s |

## Reasoning Impact Comparison (With vs Without Reasoning)

| Model | Backend | No-Reasoning Speed | Reasoning Speed | Speed Ratio | Generated Tokens (Off -> On) | Duration (Off -> On) |
|---|---|---|---|---|---|---|
| `Qwen 3.5 2B (Q4_K_XL)` | CPU | 33,8 tok/s | 36,7 tok/s | 1,09x | 882 -> 2647 (+200%) | 26,1s -> 72,1s (+46,1s) |
| `Qwen 3.5 2B (Q4_K_XL)` | Vulkan | 156,7 tok/s | 180,6 tok/s | 1,15x | 784 -> 1448 (+85%) | 5,0s -> 8,0s (+3,0s) |
| `Qwen 3.5 4B (Q4_K_XL)` | CPU | 12,7 tok/s | 14,2 tok/s | 1,12x | 512 -> 1416 (+177%) | 40,3s -> 99,4s (+59,1s) |
| `Qwen 3.5 4B (Q4_K_XL)` | Vulkan | 71,6 tok/s | 115,0 tok/s | 1,61x | 382 -> 1491 (+290%) | 5,3s -> 13,0s (+7,6s) |
| `Gemma 4 E2B (Q4_K_XL)` | CPU | 25,1 tok/s | 22,1 tok/s | 0,88x | 695 -> 353 (+-49%) | 27,7s -> 16,0s (+-11,7s) |
| `Gemma 4 E2B (Q4_K_XL)` | Vulkan | 85,8 tok/s | 85,6 tok/s | 1,00x | 355 -> 352 (+-1%) | 4,1s -> 4,1s (+-0,0s) |
| `Gemma 4 E4B (Q4_K_XL)` | CPU | 12,5 tok/s | 12,5 tok/s | 1,00x | 359 -> 359 (+0%) | 28,8s -> 28,7s (+-0,1s) |
| `Gemma 4 E4B (Q4_K_XL)` | Vulkan | 58,6 tok/s | 59,5 tok/s | 1,02x | 366 -> 379 (+4%) | 6,2s -> 6,4s (+0,1s) |

## Detailed Scenario Breakdown

### Model: `Qwen 3.5 2B (Q4_K_XL) [CPU | No Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1151 | 196 | 30,6 tok/s | 1,63 GB | - | 6403 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 923 | 423 | 38,6 tok/s | 1,63 GB | - | 10945 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1962 | 256 | 31,5 tok/s | 1,64 GB | - | 8116 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 198 | 7 | 11,2 tok/s | 1,65 GB | - | 624 ms |

### Model: `Qwen 3.5 2B (Q4_K_XL) [CPU | Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1331 | 389 | 34,2 tok/s | 1,64 GB | - | 11383 ms |
| **Calculator Math Expression** | ❌ FAIL | 5 | 100% | 6422 | 1745 | 38,1 tok/s | 1,64 GB | - | 45742 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 2309 | 471 | 35,4 tok/s | 1,64 GB | - | 13306 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 196 | 42 | 24,5 tok/s | 1,64 GB | - | 1714 ms |

> [!WARNING]
> **Failure Details for Calculator Math Expression:** 
> - Turn 1: Expected final answer to state approximately $152.12. Model answered: ''

### Model: `Qwen 3.5 2B (Q4_K_XL) [Vulkan | No Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1129 | 167 | 133,1 tok/s | 1,53 GB | 4,20 GB | 1255 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 882 | 342 | 204,3 tok/s | 1,55 GB | 4,21 GB | 1674 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1953 | 268 | 145,7 tok/s | 1,55 GB | 4,22 GB | 1840 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 198 | 7 | 29,8 tok/s | 1,56 GB | 4,21 GB | 235 ms |

### Model: `Qwen 3.5 2B (Q4_K_XL) [Vulkan | Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1321 | 361 | 175,0 tok/s | 1,56 GB | 4,22 GB | 2063 ms |
| **Calculator Math Expression** | ✅ PASS | 3 | 100% | 2590 | 585 | 194,6 tok/s | 1,56 GB | 4,22 GB | 3006 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 2328 | 457 | 178,5 tok/s | 1,57 GB | 4,22 GB | 2560 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 196 | 45 | 115,1 tok/s | 1,56 GB | 4,21 GB | 391 ms |

### Model: `Qwen 3.5 4B (Q4_K_XL) [CPU | No Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1127 | 148 | 12,4 tok/s | 3,32 GB | - | 11907 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 690 | 193 | 14,3 tok/s | 3,32 GB | - | 13533 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1869 | 164 | 12,3 tok/s | 3,32 GB | - | 13313 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 198 | 7 | 4,6 tok/s | 3,32 GB | - | 1518 ms |

### Model: `Qwen 3.5 4B (Q4_K_XL) [CPU | Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1388 | 384 | 14,1 tok/s | 3,32 GB | - | 27298 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 938 | 538 | 14,8 tok/s | 3,32 GB | - | 36237 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 2332 | 448 | 14,1 tok/s | 3,32 GB | - | 31861 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 196 | 46 | 11,5 tok/s | 3,32 GB | - | 4002 ms |

### Model: `Qwen 3.5 4B (Q4_K_XL) [Vulkan | No Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1124 | 149 | 80,0 tok/s | 3,00 GB | 5,86 GB | 1863 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 690 | 65 | 67,1 tok/s | 3,01 GB | 5,86 GB | 968 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1869 | 161 | 73,7 tok/s | 3,01 GB | 5,86 GB | 2185 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 198 | 7 | 22,0 tok/s | 3,00 GB | 5,86 GB | 318 ms |

### Model: `Qwen 3.5 4B (Q4_K_XL) [Vulkan | Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1304 | 345 | 106,7 tok/s | 3,00 GB | 5,86 GB | 3233 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 1041 | 586 | 129,9 tok/s | 3,00 GB | 5,86 GB | 4512 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 2422 | 502 | 110,1 tok/s | 3,01 GB | 5,86 GB | 4558 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 196 | 58 | 88,2 tok/s | 3,02 GB | 5,85 GB | 657 ms |

### Model: `Gemma 4 E2B (Q4_K_XL) [CPU | No Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1119 | 103 | 20,8 tok/s | 1,96 GB | - | 4956 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 764 | 375 | 29,3 tok/s | 1,96 GB | - | 12787 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1948 | 210 | 22,7 tok/s | 1,96 GB | - | 9234 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 201 | 7 | 9,6 tok/s | 1,96 GB | - | 726 ms |

### Model: `Gemma 4 E2B (Q4_K_XL) [CPU | Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1119 | 110 | 22,0 tok/s | 1,96 GB | - | 4999 ms |
| **Calculator Math Expression** | ❌ FAIL | 1 | 100% | 712 | 69 | 23,3 tok/s | 1,96 GB | - | 2955 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1914 | 167 | 23,5 tok/s | 1,97 GB | - | 7097 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 201 | 7 | 7,4 tok/s | 1,96 GB | - | 951 ms |

> [!WARNING]
> **Failure Details for Calculator Math Expression:** 
> - Turn 1: Expected final answer to state approximately $152.12. Model answered: 'The final total price is $174.62.'

### Model: `Gemma 4 E2B (Q4_K_XL) [Vulkan | No Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1119 | 103 | 83,3 tok/s | 1,89 GB | 4,52 GB | 1237 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 710 | 68 | 86,7 tok/s | 1,90 GB | 4,52 GB | 784 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1908 | 177 | 99,1 tok/s | 1,90 GB | 4,52 GB | 1786 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 201 | 7 | 21,1 tok/s | 1,89 GB | 4,52 GB | 331 ms |

### Model: `Gemma 4 E2B (Q4_K_XL) [Vulkan | Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1119 | 110 | 84,7 tok/s | 1,89 GB | 4,52 GB | 1299 ms |
| **Calculator Math Expression** | ❌ FAIL | 1 | 100% | 712 | 69 | 88,4 tok/s | 1,90 GB | 4,52 GB | 780 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1910 | 166 | 94,8 tok/s | 1,91 GB | 4,52 GB | 1752 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 201 | 7 | 25,1 tok/s | 1,89 GB | 4,52 GB | 279 ms |

> [!WARNING]
> **Failure Details for Calculator Math Expression:** 
> - Turn 1: Expected final answer to state approximately $152.12. Model answered: 'The final total price is $174.62.'

### Model: `Gemma 4 E4B (Q4_K_XL) [CPU | No Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1125 | 117 | 12,2 tok/s | 3,76 GB | - | 9563 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 710 | 75 | 13,0 tok/s | 3,77 GB | - | 5767 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1876 | 160 | 13,0 tok/s | 3,77 GB | - | 12263 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 201 | 7 | 6,0 tok/s | 3,75 GB | - | 1174 ms |

### Model: `Gemma 4 E4B (Q4_K_XL) [CPU | Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1124 | 127 | 12,4 tok/s | 3,76 GB | - | 10234 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 710 | 75 | 13,0 tok/s | 3,76 GB | - | 5786 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1874 | 150 | 13,0 tok/s | 3,77 GB | - | 11528 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 201 | 7 | 6,0 tok/s | 3,75 GB | - | 1164 ms |

### Model: `Gemma 4 E4B (Q4_K_XL) [Vulkan | No Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1125 | 131 | 61,9 tok/s | 3,36 GB | 6,33 GB | 2116 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 710 | 75 | 62,3 tok/s | 3,37 GB | 6,33 GB | 1203 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1870 | 153 | 60,2 tok/s | 3,37 GB | 6,33 GB | 2541 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 201 | 7 | 17,9 tok/s | 3,36 GB | 6,33 GB | 390 ms |

### Model: `Gemma 4 E4B (Q4_K_XL) [Vulkan | Reasoning]`

| Scenario | Status | Tool Calls | Tool Acc. | Prompt Tokens | Gen Tokens | Gen Speed | RAM | GPU VRAM | Duration |
|---|---|---|---|---|---|---|---|---|---|
| **Weather Multi-City & Comparison** | ✅ PASS | 2 | 100% | 1124 | 116 | 59,1 tok/s | 3,36 GB | 6,33 GB | 1963 ms |
| **Calculator Math Expression** | ✅ PASS | 1 | 100% | 710 | 75 | 61,4 tok/s | 3,37 GB | 6,33 GB | 1222 ms |
| **Multi-Turn Customer Support Pipeline** | ✅ PASS | 2 | 100% | 1910 | 181 | 64,8 tok/s | 3,38 GB | 6,33 GB | 2792 ms |
| **Direct Knowledge (Negative Tool Test)** | ✅ PASS | 0 | 100% | 201 | 7 | 17,8 tok/s | 3,36 GB | 6,33 GB | 394 ms |

