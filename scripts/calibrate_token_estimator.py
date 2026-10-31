#!/usr/bin/env python3
"""
Statistical calibration script for Pmad.AI HeuristicTokenEstimator.
Fits non-negative linear regression factors and safety floor across tokenizer families,
incorporating both synthetic benchmark fixtures and real-world OllamaRouter token telemetry.
Can automatically update the presets in src/Pmad.AI/Tokens/HeuristicTokenEstimator.cs
and tests/Pmad.AI.Tests/HeuristicTokenEstimatorTests.cs (--apply).
"""

import argparse
import csv
import os
import re
import sys
from pathlib import Path
import numpy as np
from scipy.optimize import lsq_linear

SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parent

DEFAULT_SYNTHETIC_CSV = REPO_ROOT / "benchmarks" / "Pmad.AI.Benchmark" / "Fixtures" / "token_calibration_data.csv"
DEFAULT_CS_FILE = REPO_ROOT / "src" / "Pmad.AI" / "Tokens" / "HeuristicTokenEstimator.cs"
DEFAULT_TEST_FILE = REPO_ROOT / "tests" / "Pmad.AI.Tests" / "HeuristicTokenEstimatorTests.cs"


def resolve_default_router_csv() -> Path:
    """Finds the default OllamaRouter token-metrics.csv across Windows and WSL environments."""
    local_app_data = os.environ.get("LOCALAPPDATA")
    if local_app_data:
        candidate = Path(local_app_data) / "OllamaRouter" / "token-metrics" / "token-metrics.csv"
        if candidate.exists():
            return candidate

    # Common WSL mount path or user home fallback
    wsl_candidates = [
        Path("/mnt/c/Users/Julien/AppData/Local/OllamaRouter/token-metrics/token-metrics.csv"),
        Path.home() / "AppData" / "Local" / "OllamaRouter" / "token-metrics" / "token-metrics.csv",
    ]
    if Path("/mnt/c/Users").exists():
        for user_dir in Path("/mnt/c/Users").iterdir():
            cand = user_dir / "AppData" / "Local" / "OllamaRouter" / "token-metrics" / "token-metrics.csv"
            if cand.exists():
                return cand

    for cand in wsl_candidates:
        if cand.exists():
            return cand

    return wsl_candidates[0]


def to_local_path(p: str | Path) -> Path:
    """Converts a Windows path (e.g. C:\\Users\\...) to a WSL path (/mnt/c/Users/...) when running on Linux/WSL."""
    s = str(p)
    if os.name != "nt" and len(s) >= 3 and s[1] == ":" and s[2] in ("\\", "/"):
        drive = s[0].lower()
        rest = s[3:].replace("\\", "/")
        return Path(f"/mnt/{drive}/{rest}")
    return Path(s)


def load_synthetic_data(csv_path: Path):
    rows = []
    if not csv_path.exists():
        return rows
    with open(csv_path, "r", encoding="utf-8-sig") as f:
        reader = csv.DictReader(f)
        for r in reader:
            rows.append({
                "source": "synthetic",
                "model_family": r["model_family"],
                "model_name": r["model_name"],
                "category": r["category"],
                "sample_id": str(r["sample_id"]),
                "words": int(r["words"]),
                "symbols": int(r["symbols"]),
                "non_ascii": int(r["non_ascii"]),
                "chars": int(r["chars"]),
                "actual_tokens": int(r["actual_tokens"]),
                "estimated_logged": None,
            })
    return rows


def classify_router_model(model_name: str) -> str:
    m = (model_name or "").lower()
    if "qwen" in m:
        return "Qwen"
    if "gemma" in m:
        return "Gemma"
    return "Other"


def load_router_data(csv_path: Path):
    rows = []
    if not csv_path or not csv_path.exists():
        return rows
    with open(csv_path, "r", encoding="utf-8-sig") as f:
        reader = csv.DictReader(f)
        for idx, r in enumerate(reader, start=1):
            if r.get("success", "").strip().lower() != "true":
                continue
            act_str = r.get("actual_prompt_tokens", "").strip()
            if not act_str:
                continue
            try:
                act = int(act_str)
                words = int(r["words"])
                symbols = int(r["symbols"])
                non_ascii = int(r["non_ascii"])
                chars = int(r["char_count"])
                est_logged = int(r["estimated_prompt_tokens"]) if r.get("estimated_prompt_tokens") else None
            except (ValueError, KeyError):
                continue

            if act <= 0 or chars <= 0:
                continue
            if words == 0 and symbols == 0 and non_ascii == 0:
                continue

            model_name = r.get("model", "unknown").strip()
            rows.append({
                "source": "router",
                "model_family": classify_router_model(model_name),
                "model_name": model_name,
                "category": r.get("endpoint", "/api/chat"),
                "sample_id": r.get("request_id", str(idx))[:8],
                "words": words,
                "symbols": symbols,
                "non_ascii": non_ascii,
                "chars": chars,
                "actual_tokens": act,
                "estimated_logged": est_logged,
            })
    return rows


def evaluate(w_word: float, w_sym: float, w_non_ascii: float, floor: float, dataset: list[dict]):
    if not dataset:
        return None

    words = np.array([d["words"] for d in dataset], dtype=float)
    syms = np.array([d["symbols"] for d in dataset], dtype=float)
    non_ascii = np.array([d["non_ascii"] for d in dataset], dtype=float)
    chars = np.array([d["chars"] for d in dataset], dtype=float)
    act = np.array([d["actual_tokens"] for d in dataset], dtype=float)

    est = np.ceil(words * w_word + syms * w_sym + non_ascii * w_non_ascii)
    if floor > 0:
        char_floor = np.ceil(chars / floor)
        est = np.maximum(est, char_floor)
    est = np.maximum(1.0, est)

    errors = est - act
    pct_errors = (errors / act) * 100.0
    under_mask = errors < 0

    worst_samples = []
    for i, d in enumerate(dataset):
        if errors[i] < 0:
            under_pct = -pct_errors[i]
            worst_samples.append((
                under_pct,
                d["sample_id"],
                d["category"],
                d["chars"],
                d["words"],
                d["symbols"],
                d["non_ascii"],
                int(act[i]),
                int(est[i]),
            ))
    worst_samples.sort(key=lambda x: x[0], reverse=True)

    return {
        "mae": float(np.mean(np.abs(errors))),
        "mape": float(np.mean(np.abs(pct_errors))),
        "rmse": float(np.sqrt(np.mean(errors ** 2))),
        "under_rate": float(np.mean(under_mask) * 100.0),
        "max_under_pct": float(np.max(-pct_errors[under_mask])) if np.any(under_mask) else 0.0,
        "mean_error": float(np.mean(errors)),
        "mean_pct_bias": float(np.mean(pct_errors)),
        "worst_samples": worst_samples[:3],
    }


def fit_family(synthetic_rows: list[dict], router_rows: list[dict]):
    """
    Fits (WordFactor, SymbolFactor, NonAsciiFactor, CharFloorFactor).
    - Starts from synthetic natural/code/multilingual samples to establish structural separation
      (especially for NonAscii and prose vs. symbol-heavy code).
    - Incorporates router telemetry via sample-size-invariant relative WLS with Tikhonov regularization
      toward the synthetic structural prior, preventing collinearity drift as telemetry grows.
    - Selects CharFloorFactor across [3.8, 3.6, 3.5, 3.4, 3.2, 3.0] to minimize asymmetric context-safety loss.
    """
    natural_syn = [r for r in synthetic_rows if r["category"] != "dense_adversarial"]

    if natural_syn:
        X_nat = np.array([[r["words"], r["symbols"], r["non_ascii"]] for r in natural_syn], dtype=float)
        y_nat = np.array([r["actual_tokens"] for r in natural_syn], dtype=float)
        res_syn = lsq_linear(X_nat, y_nat, bounds=([0.5, 0.2, 0.2], [2.5, 2.0, 2.0]), method="bvls")
        prior_w = res_syn.x
    else:
        prior_w = np.array([1.25, 0.80, 1.00], dtype=float)

    if router_rows:
        X_r = np.array([[r["words"], r["symbols"], r["non_ascii"]] for r in router_rows], dtype=float)
        y_r = np.array([r["actual_tokens"] for r in router_rows], dtype=float)

        # Normalize each router observation by actual_tokens to minimize relative error across all prompt sizes
        X_rel = X_r / y_r[:, None]
        y_rel = np.ones_like(y_r)
        n_router = len(router_rows)

        # Regularization toward structural prior: stronger on NonAscii when router data is predominantly ASCII
        alpha = 0.002
        reg_diag = np.diag([1.0, 1.0, 5.0])
        A = np.vstack([X_rel / np.sqrt(n_router), np.sqrt(alpha) * reg_diag])
        b = np.concatenate([y_rel / np.sqrt(n_router), np.sqrt(alpha) * (reg_diag @ prior_w)])

        res_lsq = lsq_linear(A, b, bounds=([0.5, 0.2, 0.2], [2.5, 2.0, 2.0]), method="bvls")
        raw_w = res_lsq.x
    else:
        raw_w = prior_w

    safe_w = np.round(raw_w, 2)

    # Select floor factor
    candidate_floors = [3.8, 3.6, 3.5, 3.4, 3.2, 3.0]
    best_floor = 3.6
    best_loss = 1e18

    if router_rows:
        r_X = np.array([[r["words"], r["symbols"], r["non_ascii"]] for r in router_rows], dtype=float)
        r_y = np.array([r["actual_tokens"] for r in router_rows], dtype=float)
        r_chars = np.array([r["chars"] for r in router_rows], dtype=float)

        for candidate_floor in candidate_floors:
            preds = np.ceil(r_X @ safe_w)
            floors = np.ceil(r_chars / candidate_floor)
            est = np.maximum(preds, floors)
            diff_abs = est - r_y
            # Asymmetric absolute token error loss (1.5x penalty on underestimation)
            loss = float(np.mean(np.where(diff_abs < 0, -1.5 * diff_abs, 1.0 * diff_abs)))
            if loss < best_loss:
                best_loss = loss
                best_floor = candidate_floor
    elif synthetic_rows:
        s_X = np.array([[r["words"], r["symbols"], r["non_ascii"]] for r in synthetic_rows], dtype=float)
        s_y = np.array([r["actual_tokens"] for r in synthetic_rows], dtype=float)
        s_chars = np.array([r["chars"] for r in synthetic_rows], dtype=float)

        for candidate_floor in candidate_floors:
            preds = np.ceil(s_X @ raw_w)
            floors = np.ceil(s_chars / candidate_floor)
            est = np.maximum(preds, floors)
            diff = est - s_y
            loss = float(np.mean(np.where(diff < 0, -2.5 * diff, 1.0 * diff)))
            if loss < best_loss:
                best_loss = loss
                best_floor = candidate_floor

    return {
        "raw_weights": np.round(raw_w, 3),
        "safe_weights": safe_w,
        "floor": best_floor,
    }


def read_current_csharp_preset(cs_path: Path, preset_name: str):
    if not cs_path.exists():
        return None
    content = cs_path.read_text(encoding="utf-8")
    pattern = re.compile(
        rf"public\s+static\s+HeuristicTokenEstimator\s+{preset_name}\s*\{{[^}}]+\}}\s*=\s*new\(\s*"
        r"wordFactor:\s*([0-9.]+)\s*,\s*"
        r"symbolFactor:\s*([0-9.]+)\s*,\s*"
        r"nonAsciiFactor:\s*([0-9.]+)\s*,\s*"
        r"charFloorFactor:\s*([0-9.]+)\s*\);",
        re.MULTILINE,
    )
    m = pattern.search(content)
    if not m:
        return None
    return (float(m.group(1)), float(m.group(2)), float(m.group(3)), float(m.group(4)))


def read_text_preserve_eol(path: Path) -> tuple[str, str]:
    raw = path.read_bytes()
    eol = "\r\n" if b"\r\n" in raw else "\n"
    return raw.decode("utf-8"), eol


def write_text_preserve_eol(path: Path, text: str, eol: str):
    normalized = text.replace("\r\n", "\n")
    if eol == "\r\n":
        normalized = normalized.replace("\n", "\r\n")
    path.write_bytes(normalized.encode("utf-8"))


def update_csharp_files(cs_path: Path, test_path: Path, preset_name: str, w_word: float, w_sym: float, w_na: float, floor: float):
    w_str = f"{w_word:.2f}"
    s_str = f"{w_sym:.2f}"
    na_str = f"{w_na:.2f}"
    flr_str = f"{floor:.1f}"

    updated_files = []

    if cs_path.exists():
        content, eol = read_text_preserve_eol(cs_path)
        # Update XML doc comment and property initializer for the preset
        doc_and_prop_pattern = re.compile(
            rf"(///[^\n]*\(WordFactor=)[0-9.]+(,\s*SymbolFactor=)[0-9.]+(,\s*NonAsciiFactor=)[0-9.]+(,\s*CharFloorFactor=)[0-9.]+(\)\.\r?\n"
            rf"\s*///\s*</summary>\r?\n"
            rf"\s*public\s+static\s+HeuristicTokenEstimator\s+{preset_name}\s*\{{[^}}]+\}}\s*=\s*new\(\r?\n"
            rf"\s*wordFactor:\s*)[0-9.]+(,\r?\n"
            rf"\s*symbolFactor:\s*)[0-9.]+(,\r?\n"
            rf"\s*nonAsciiFactor:\s*)[0-9.]+(,\r?\n"
            rf"\s*charFloorFactor:\s*)[0-9.]+(\);)",
            re.MULTILINE,
        )
        new_content, count = doc_and_prop_pattern.subn(
            rf"\g<1>{w_str}\g<2>{s_str}\g<3>{na_str}\g<4>{flr_str}\g<5>{w_str}\g<6>{s_str}\g<7>{na_str}\g<8>{flr_str}\g<9>",
            content,
        )
        if count > 0 and new_content != content:
            write_text_preserve_eol(cs_path, new_content, eol)
            updated_files.append(str(cs_path))
        elif count > 0:
            print(f"  [OK] {cs_path.name} ({preset_name}) already up to date.")
        else:
            print(f"  [WARN] Could not match {preset_name} block in {cs_path}", file=sys.stderr)

    if test_path.exists():
        test_content, test_eol = read_text_preserve_eol(test_path)
        test_pattern = re.compile(
            rf"(public\s+void\s+{preset_name}_HasExpectedCalibratedFactors\(\)\r?\n"
            rf"\s*\{{\r?\n"
            rf"\s*var\s+\w+\s*=\s*HeuristicTokenEstimator\.{preset_name};\r?\n"
            rf"\s*Assert\.NotNull\(\w+\);\r?\n"
            rf"\s*Assert\.Equal\()[0-9.]+(,\s*\w+\.WordFactor\);\r?\n"
            rf"\s*Assert\.Equal\()[0-9.]+(,\s*\w+\.SymbolFactor\);\r?\n"
            rf"\s*Assert\.Equal\()[0-9.]+(,\s*\w+\.NonAsciiFactor\);\r?\n"
            rf"\s*Assert\.Equal\()[0-9.]+(,\s*\w+\.CharFloorFactor\);)",
            re.MULTILINE,
        )
        new_test_content, count = test_pattern.subn(
            rf"\g<1>{w_str}\g<2>{s_str}\g<3>{na_str}\g<4>{flr_str}\g<5>",
            test_content,
        )
        if count > 0 and new_test_content != test_content:
            write_text_preserve_eol(test_path, new_test_content, test_eol)
            updated_files.append(str(test_path))
        elif count > 0:
            print(f"  [OK] {test_path.name} ({preset_name}) already up to date.")
        else:
            print(f"  [WARN] Could not match {preset_name}_HasExpectedCalibratedFactors in {test_path}", file=sys.stderr)

    return updated_files


def print_comparison_table(title: str, current_label: str, cur_eval: dict, new_label: str, new_eval: dict):
    print(f"\n  Performance Comparison on {title}:")
    print(f"    {'Metric':<25} | {current_label:<28} | {new_label:<24}")
    print(f"    {'-' * 25}-|-{'-' * 28}-|-{'-' * 24}")
    print(f"    {'MAE (mean abs error)':<25} | {cur_eval['mae']:<28.2f} | {new_eval['mae']:<24.2f}")
    print(f"    {'MAPE (mean abs % error)':<25} | {cur_eval['mape']:<27.2f}% | {new_eval['mape']:<23.2f}%")
    print(f"    {'RMSE':<25} | {cur_eval['rmse']:<28.2f} | {new_eval['rmse']:<24.2f}")
    print(f"    {'Underestimate Rate':<25} | {cur_eval['under_rate']:<27.1f}% | {new_eval['under_rate']:<23.1f}%")
    print(f"    {'Max Underestimate %':<25} | {cur_eval['max_under_pct']:<27.2f}% | {new_eval['max_under_pct']:<23.2f}%")
    print(f"    {'Mean % Bias (Est - Act)':<25} | {cur_eval['mean_pct_bias']:<+27.2f}% | {new_eval['mean_pct_bias']:<+23.2f}%")
    if new_eval["worst_samples"]:
        print(f"    Top Underestimated Outliers ({new_label}):")
        for under_pct, sid, cat, chars, words, sym, non_ascii, act, est in new_eval["worst_samples"]:
            print(
                f"      • Sample {sid} ({cat}): Act={act}, Est={est}, Under={under_pct:.1f}% "
                f"(chars={chars}, words={words}, sym={sym}, nonAscii={non_ascii})"
            )


def main():
    parser = argparse.ArgumentParser(
        description="Calibrate Pmad.AI HeuristicTokenEstimator presets from OllamaRouter token-metrics.csv and benchmark fixtures."
    )
    parser.add_argument(
        "--router-csv",
        type=str,
        default=None,
        help="Path to OllamaRouter token-metrics.csv (defaults to %%LOCALAPPDATA%%\\OllamaRouter\\token-metrics\\token-metrics.csv).",
    )
    parser.add_argument(
        "--synthetic-csv",
        type=str,
        default=str(DEFAULT_SYNTHETIC_CSV),
        help="Path to synthetic benchmark token_calibration_data.csv.",
    )
    parser.add_argument(
        "--apply",
        action="store_true",
        help="Automatically update HeuristicTokenEstimator.cs and HeuristicTokenEstimatorTests.cs with the calibrated presets.",
    )
    args = parser.parse_args()

    router_csv_path = to_local_path(args.router_csv) if args.router_csv else resolve_default_router_csv()
    synthetic_csv_path = to_local_path(args.synthetic_csv)

    syn_rows = load_synthetic_data(synthetic_csv_path)
    router_rows = load_router_data(router_csv_path)

    print(f"Loaded {len(syn_rows)} synthetic rows from: {synthetic_csv_path}")
    if router_rows:
        print(f"Loaded {len(router_rows)} valid telemetry rows from: {router_csv_path}\n")
    else:
        print(f"No router telemetry rows found at: {router_csv_path} (using synthetic data only)\n")

    families = [
        (
            "Qwen",
            "Qwen (Unified 3.5 / 3.6 / 3.8)",
            [r for r in syn_rows if r["model_family"].startswith("Qwen")],
            [r for r in router_rows if r["model_family"] == "Qwen"],
        ),
        (
            "Gemma",
            "Gemma 4",
            [r for r in syn_rows if r["model_family"].startswith("Gemma")],
            [r for r in router_rows if r["model_family"] == "Gemma"],
        ),
    ]

    results = {}
    print("=" * 88)
    print("STATISTICAL PARAMETER CALIBRATION RESULTS")
    print("=" * 88)

    for preset_name, display_name, fam_syn, fam_router in families:
        fit = fit_family(fam_syn, fam_router)
        results[preset_name] = fit

        raw_w = fit["raw_weights"]
        safe_w = fit["safe_weights"]
        floor = fit["floor"]

        current_code = read_current_csharp_preset(DEFAULT_CS_FILE, preset_name) or (1.25, 0.80, 1.00, 3.8)
        cur_label = f"Current ({current_code[0]:.2f}/{current_code[1]:.2f}/{current_code[2]:.2f}/{current_code[3]:.1f})"
        new_label = f"Calibrated ({safe_w[0]:.2f}/{safe_w[1]:.2f}/{safe_w[2]:.2f}/{floor:.1f})"

        print(f"\n--- {display_name} (Router: {len(fam_router)} rows, Synthetic: {len(fam_syn)} rows) ---")
        print(f"  Current Code Preset:  Word={current_code[0]:.2f}, Symbol={current_code[1]:.2f}, NonAscii={current_code[2]:.2f}, Floor={current_code[3]:.1f}")
        print(f"  Raw Fitted Weights:   Word={raw_w[0]:.3f}, Symbol={raw_w[1]:.3f}, NonAscii={raw_w[2]:.3f}, Floor={floor:.1f}")
        print(f"  Calibrated Preset:    Word={safe_w[0]:.2f}, Symbol={safe_w[1]:.2f}, NonAscii={safe_w[2]:.2f}, Floor={floor:.1f}")

        if fam_router:
            cur_eval_r = evaluate(*current_code, fam_router)
            new_eval_r = evaluate(safe_w[0], safe_w[1], safe_w[2], floor, fam_router)
            print_comparison_table(f"{display_name} Router Telemetry (n={len(fam_router)})", cur_label, cur_eval_r, new_label, new_eval_r)

        if fam_syn:
            cur_eval_s = evaluate(*current_code, fam_syn)
            new_eval_s = evaluate(safe_w[0], safe_w[1], safe_w[2], floor, fam_syn)
            print_comparison_table(f"{display_name} Synthetic Fixtures (n={len(fam_syn)})", cur_label, cur_eval_s, new_label, new_eval_s)

    qwen = results["Qwen"]["safe_weights"]
    qwen_floor = results["Qwen"]["floor"]
    gemma = results["Gemma"]["safe_weights"]
    gemma_floor = results["Gemma"]["floor"]

    print("\n" + "=" * 88)
    print("RECOMMENDED C# FACTOR DEFINITIONS:")
    print("=" * 88)
    print(f"""
// Qwen Family Preset (Qwen 3.5 / Qwen 3.6 35B A3B / Qwen 3.8 27B)
public static HeuristicTokenEstimator Qwen {{ get; }} = new(
    wordFactor: {qwen[0]:.2f},
    symbolFactor: {qwen[1]:.2f},
    nonAsciiFactor: {qwen[2]:.2f},
    charFloorFactor: {qwen_floor:.1f});

// Gemma 4 Family Preset (Gemma 4 E2B, E4B, 12B, 26B, 31B)
public static HeuristicTokenEstimator Gemma {{ get; }} = new(
    wordFactor: {gemma[0]:.2f},
    symbolFactor: {gemma[1]:.2f},
    nonAsciiFactor: {gemma[2]:.2f},
    charFloorFactor: {gemma_floor:.1f});
""")

    if args.apply:
        print("=" * 88)
        print("APPLYING UPDATES TO CODEBASE:")
        print("=" * 88)
        updated = []
        for preset_name in ["Qwen", "Gemma"]:
            w_s = results[preset_name]["safe_weights"]
            flr = results[preset_name]["floor"]
            updated.extend(update_csharp_files(DEFAULT_CS_FILE, DEFAULT_TEST_FILE, preset_name, w_s[0], w_s[1], w_s[2], flr))
        if updated:
            for f in dict.fromkeys(updated):
                print(f"  [UPDATED] {f}")
        else:
            print("  No file changes were needed.")
    else:
        print("Tip: Run with --apply to automatically update HeuristicTokenEstimator.cs and HeuristicTokenEstimatorTests.cs.")


if __name__ == "__main__":
    main()
