#!/usr/bin/env python3
"""
Statistical calibration script for Pmad.AI HeuristicTokenEstimator.
Fits non-negative linear regression factors and safety floor across tokenizer families.
"""

import csv
import sys
import numpy as np
from scipy.optimize import lsq_linear, minimize

CSV_PATH = "/mnt/c/Users/Julien/source/repos/jetelain/PmadAI/benchmarks/Pmad.AI.Benchmark/Fixtures/token_calibration_data.csv"

def load_data(csv_path):
    rows = []
    with open(csv_path, "r", encoding="utf-8-sig") as f:
        reader = csv.DictReader(f)
        for r in reader:
            rows.append({
                "model_family": r["model_family"],
                "model_name": r["model_name"],
                "category": r["category"],
                "sample_id": int(r["sample_id"]),
                "words": int(r["words"]),
                "symbols": int(r["symbols"]),
                "non_ascii": int(r["non_ascii"]),
                "chars": int(r["chars"]),
                "actual_tokens": int(r["actual_tokens"])
            })
    return rows

def evaluate(w_word, w_sym, w_non_ascii, floor, dataset):
    errors = []
    underestimates = 0
    max_underestimate_pct = 0.0
    worst_samples = []
    
    for d in dataset:
        est = np.ceil(d["words"] * w_word + d["symbols"] * w_sym + d["non_ascii"] * w_non_ascii)
        if floor > 0:
            char_floor = np.ceil(d["chars"] / floor)
            est = max(est, char_floor)
        est = max(1, int(est))
        
        act = d["actual_tokens"]
        err = est - act
        errors.append(err)
        if est < act:
            underestimates += 1
            under_pct = ((act - est) / act) * 100.0
            if under_pct > max_underestimate_pct:
                max_underestimate_pct = under_pct
            worst_samples.append((under_pct, d["sample_id"], d["category"], d["chars"], d["words"], d["symbols"], d["non_ascii"], act, est))
                
    worst_samples.sort(key=lambda x: x[0], reverse=True)
    errors = np.array(errors)
    mae = np.mean(np.abs(errors))
    rmse = np.sqrt(np.mean(errors ** 2))
    under_rate = (underestimates / len(dataset)) * 100.0
    return {
        "mae": mae,
        "rmse": rmse,
        "under_rate": under_rate,
        "max_under_pct": max_underestimate_pct,
        "mean_error": np.mean(errors),
        "worst_samples": worst_samples[:3]
    }

def fit_family(rows, family_name):
    # Separate natural/code/dialogue text from dense adversarial text
    # so factors represent true tokenization of language/code structures
    natural_rows = [r for r in rows if r["category"] != "dense_adversarial"]
    dense_rows = [r for r in rows if r["category"] == "dense_adversarial"]
    
    X_nat = np.array([[r["words"], r["symbols"], r["non_ascii"]] for r in natural_rows], dtype=float)
    y_nat = np.array([r["actual_tokens"] for r in natural_rows], dtype=float)
    
    # 1. Bounded Least Squares on structured language/code
    res_lsq = lsq_linear(X_nat, y_nat, bounds=([0.5, 0.2, 0.2], [2.5, 2.0, 2.0]), method="bvls")
    raw_w = res_lsq.x
    
    # 2. Evaluate floor factors on adversarial & dense samples
    # For dense samples, we want floor = chars / actual_tokens to not underestimate
    dense_chars = np.array([r["chars"] for r in dense_rows], dtype=float)
    dense_y = np.array([r["actual_tokens"] for r in dense_rows], dtype=float)
    
    # Best floor factor: find floor between 2.5 and 4.0 that minimizes global loss
    all_X = np.array([[r["words"], r["symbols"], r["non_ascii"]] for r in rows], dtype=float)
    all_y = np.array([r["actual_tokens"] for r in rows], dtype=float)
    all_chars = np.array([r["chars"] for r in rows], dtype=float)
    
    best_floor = 3.6
    best_loss = 1e9
    
    for candidate_floor in [3.8, 3.6, 3.5, 3.4, 3.2, 3.0]:
        preds = np.ceil(all_X @ raw_w)
        floors = np.ceil(all_chars / candidate_floor)
        est = np.maximum(preds, floors)
        diff = est - all_y
        # Penalize underestimation 2.5x more than overestimation for LLM context safety
        loss = np.mean(np.where(diff < 0, -2.5 * diff, 1.0 * diff))
        if loss < best_loss:
            best_loss = loss
            best_floor = candidate_floor
            
    # Round weights to 2 decimal places
    safe_w = np.round(raw_w, 2)
    
    return {
        "raw_weights": np.round(raw_w, 3),
        "safe_weights": safe_w,
        "floor": best_floor
    }

def main():
    rows = load_data(CSV_PATH)
    print(f"Loaded {len(rows)} observation rows from {CSV_PATH}\n")
    
    # Baseline comparison with default estimator factors:
    # WordFactor = 1.25, SymbolFactor = 0.8, NonAsciiFactor = 1.0, CharFloorFactor = 3.8
    default_stats_all = evaluate(1.25, 0.8, 1.0, 3.8, rows)
    
    families = {
        "Qwen (Unified 3.5 / 3.6 / 3.8)": [r for r in rows if r["model_family"].startswith("Qwen")],
        "Gemma 4": [r for r in rows if r["model_family"].startswith("Gemma")],
        "Global / Balanced": rows
    }
    
    results = {}
    print("=" * 80)
    print("STATISTICAL PARAMETER CALIBRATION RESULTS")
    print("=" * 80)
    
    for name, fam_rows in families.items():
        fit = fit_family(fam_rows, name)
        results[name] = fit
        
        raw_w = fit["raw_weights"]
        safe_w = fit["safe_weights"]
        floor = fit["floor"]
        
        # Evaluate baseline vs tuned
        base_eval = evaluate(1.25, 0.8, 1.0, 3.8, fam_rows)
        raw_eval = evaluate(raw_w[0], raw_w[1], raw_w[2], floor, fam_rows)
        safe_eval = evaluate(safe_w[0], safe_w[1], safe_w[2], floor, fam_rows)
        
        print(f"\n--- {name} ({len(fam_rows)} observations) ---")
        print(f"  Raw OLS/NNLS Weights: Word={raw_w[0]:.3f}, Symbol={raw_w[1]:.3f}, NonAscii={raw_w[2]:.3f}, Floor={floor}")
        print(f"  Safety-Tuned Presets: Word={safe_w[0]:.2f},  Symbol={safe_w[1]:.2f},  NonAscii={safe_w[2]:.2f},  Floor={floor}")
        print(f"\n  Performance Comparison on {name}:")
        print(f"    {'Metric':<25} | {'Default (1.25/0.8/1.0/3.8)':<26} | {'Tuned Preset':<20}")
        print(f"    {'-'*25}-|-{'-'*26}-|-{'-'*20}")
        print(f"    {'MAE (mean abs error)':<25} | {base_eval['mae']:<26.2f} | {safe_eval['mae']:<20.2f}")
        print(f"    {'RMSE':<25} | {base_eval['rmse']:<26.2f} | {safe_eval['rmse']:<20.2f}")
        print(f"    {'Underestimate Rate':<25} | {base_eval['under_rate']:<25.1f}% | {safe_eval['under_rate']:<19.1f}%")
        print(f"    {'Max Underestimate %':<25} | {base_eval['max_under_pct']:<25.1f}% | {safe_eval['max_under_pct']:<19.1f}%")
        print(f"    {'Mean Bias (Est - Act)':<25} | {base_eval['mean_error']:<26.2f} | {safe_eval['mean_error']:<20.2f}")
        print(f"    Top Underestimated Outliers (Tuned):")
        for under_pct, sid, cat, chars, words, sym, non_ascii, act, est in safe_eval["worst_samples"]:
            print(f"      • Sample #{sid} ({cat}): Act={act}, Est={est}, Under={under_pct:.1f}% (chars={chars}, words={words}, sym={sym}, nonAscii={non_ascii})")

    print("\n" + "=" * 80)
    print("RECOMMENDED C# FACTOR DEFINITIONS:")
    print("=" * 80)
    qwen = results["Qwen (Unified 3.5 / 3.6 / 3.8)"]["safe_weights"]
    qwen_floor = results["Qwen (Unified 3.5 / 3.6 / 3.8)"]["floor"]
    gemma = results["Gemma 4"]["safe_weights"]
    gemma_floor = results["Gemma 4"]["floor"]
    
    print(f"""
// Qwen Family Preset (Qwen 3.5 / Qwen 3.6 35B A3B / Qwen 3.8 27B)
public static HeuristicTokenEstimator Qwen {{ get; }} = new(
    wordFactor: {qwen[0]},
    symbolFactor: {qwen[1]},
    nonAsciiFactor: {qwen[2]},
    charFloorFactor: {qwen_floor});

// Gemma 4 Family Preset (Gemma 4 E2B, E4B, 12B, 26B, 31B)
public static HeuristicTokenEstimator Gemma {{ get; }} = new(
    wordFactor: {gemma[0]},
    symbolFactor: {gemma[1]},
    nonAsciiFactor: {gemma[2]},
    charFloorFactor: {gemma_floor});
""")

if __name__ == "__main__":
    main()
