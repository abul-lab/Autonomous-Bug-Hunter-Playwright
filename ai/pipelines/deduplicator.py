#!/usr/bin/env python3
"""
Dataset Deduplication & Split Leakage Guard.
Computes deterministic error signatures and token similarity to prevent
identical or near-identical failure artifacts from contaminating both train and test splits.
"""

import json
from pathlib import Path


def extract_normalized_tokens(record: dict) -> set[str]:
    norm = record.get("normalizedError", {})
    tokens = set(norm.get("salientTokens", []))
    if not tokens:
        raw = record.get("errorMessage", "").lower()
        tokens = set(w for w in raw.split() if len(w) > 3)
    return tokens


def check_split_leakage(train_path: Path, test_path: Path, max_jaccard: float = 0.85) -> list[dict]:
    train_records = []
    test_records = []

    with open(train_path, "r", encoding="utf-8") as f:
        train_records = [json.loads(line) for line in f if line.strip()]
    with open(test_path, "r", encoding="utf-8") as f:
        test_records = [json.loads(line) for line in f if line.strip()]

    leaks = []
    train_tokens = [(r["failureId"], extract_normalized_tokens(r), r.get("rootCauseCategory")) for r in train_records]

    for test_r in test_records:
        test_id = test_r["failureId"]
        t_tokens = extract_normalized_tokens(test_r)
        if not t_tokens:
            continue

        for tr_id, tr_toks, tr_cat in train_tokens:
            if not tr_toks:
                continue
            intersection = len(t_tokens.intersection(tr_toks))
            union = len(t_tokens.union(tr_toks))
            jaccard = intersection / union if union > 0 else 0.0

            if jaccard >= max_jaccard:
                leaks.append({
                    "testFailureId": test_id,
                    "trainFailureId": tr_id,
                    "jaccardSimilarity": round(jaccard, 3),
                    "testCategory": test_r.get("rootCauseCategory"),
                    "trainCategory": tr_cat
                })

    return leaks


def main():
    import sys
    if len(sys.argv) < 3:
        print("Usage: python deduplicator.py <train.jsonl> <test.jsonl>")
        sys.exit(1)

    train_p = Path(sys.argv[1])
    test_p = Path(sys.argv[2])

    leaks = check_split_leakage(train_p, test_p)
    if leaks:
        print(f"[LEAKAGE DETECTED] Found {len(leaks)} highly similar cross-split pairs!")
        for l in leaks[:5]:
            print(f"  Train: {l['trainFailureId']} <-> Test: {l['testFailureId']} (Sim: {l['jaccardSimilarity']})")
        sys.exit(1)
    else:
        print("[PASS] Zero cross-split leakage detected between train and test.")


if __name__ == "__main__":
    main()

