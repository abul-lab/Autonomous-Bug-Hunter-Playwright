#!/usr/bin/env python3
"""
Playwright Intelligence Benchmark (PIB-v1.0) Evaluator.
Standardized scoring harness for:
- Task 1: Root Cause Classification (Accuracy, Macro-F1, ECE, Confusion Breakdown)
- Task 2: Failure Similarity Retrieval (MRR@10, Precision@1, NDCG@5)
- Task 3: Self-Healing & Remediation Double-Replay Pass Rate
"""

import argparse
import json
import math
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any, Dict, List, Optional

TAXONOMY = [
    "AssertionFailure",
    "AuthenticationFailure",
    "BackendApiFailure",
    "EnvironmentFailure",
    "LocatorDrift",
    "TestDataFailure",
    "TimingIssue",
    "VisualRegression",
]

BASE_DIR = Path(__file__).resolve().parent.parent
REPO_ROOT = BASE_DIR.parent
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))
if str(BASE_DIR / "pipelines") not in sys.path:
    sys.path.insert(0, str(BASE_DIR / "pipelines"))


def compute_ece(predictions: List[Dict[str, Any]], num_bins: int = 10) -> float:
    """
    Computes Expected Calibration Error (ECE) across confidence bins:
    ECE = sum(|B_m| / N * |acc(B_m) - conf(B_m)|)
    """
    if not predictions:
        return 0.0

    bins = [[] for _ in range(num_bins)]
    for p in predictions:
        conf = float(p.get("confidence", 0.0))
        conf = min(max(conf, 0.0), 1.0)
        bin_idx = min(int(conf * num_bins), num_bins - 1)
        is_correct = 1.0 if p["groundTruth"] == p["predicted"] else 0.0
        bins[bin_idx].append((is_correct, conf))

    total_samples = len(predictions)
    ece = 0.0

    for b in bins:
        if not b:
            continue
        bin_size = len(b)
        acc = sum(x[0] for x in b) / bin_size
        avg_conf = sum(x[1] for x in b) / bin_size
        ece += (bin_size / total_samples) * abs(acc - avg_conf)

    return round(ece, 4)


def evaluate_classification(predictions: List[Dict[str, Any]]) -> Dict[str, Any]:
    """
    predictions: list of {"failureId": str, "groundTruth": str, "predicted": str, "confidence": float}
    """
    total = len(predictions)
    if total == 0:
        return {"error": "No prediction samples provided"}

    correct = sum(1 for p in predictions if p["groundTruth"] == p["predicted"])
    accuracy = correct / total

    # Per-class metrics
    tp = defaultdict(int)
    fp = defaultdict(int)
    fn = defaultdict(int)

    for p in predictions:
        gt = p["groundTruth"]
        pred = p["predicted"]
        if gt == pred:
            tp[gt] += 1
        else:
            fp[pred] += 1
            fn[gt] += 1

    f1_scores = {}
    precisions = {}
    recalls = {}

    for cat in TAXONOMY:
        prec = tp[cat] / (tp[cat] + fp[cat]) if (tp[cat] + fp[cat]) > 0 else 0.0
        rec = tp[cat] / (tp[cat] + fn[cat]) if (tp[cat] + fn[cat]) > 0 else 0.0
        f1 = (2 * prec * rec) / (prec + rec) if (prec + rec) > 0 else 0.0
        precisions[cat] = round(prec, 4)
        recalls[cat] = round(rec, 4)
        f1_scores[cat] = round(f1, 4)

    macro_f1 = sum(f1_scores.values()) / len(TAXONOMY)
    ece = compute_ece(predictions)

    return {
        "task": "RootCauseClassification",
        "totalEvaluated": total,
        "accuracy": round(accuracy, 4),
        "macroF1": round(macro_f1, 4),
        "expectedCalibrationError": ece,
        "perClassF1": f1_scores,
        "perClassPrecision": precisions,
        "perClassRecall": recalls,
        "benchmarkPassing": accuracy >= 0.88 and macro_f1 >= 0.85 and ece <= 0.12
    }


def evaluate_retrieval(retrieval_results: List[Dict[str, Any]]) -> Dict[str, Any]:
    """
    retrieval_results: list of {"queryId": str, "relevantIds": list[str], "retrievedRankedIds": list[str]}
    """
    if not retrieval_results:
        return {"error": "No retrieval samples provided"}

    reciprocal_ranks = []
    p_at_1 = []

    for item in retrieval_results:
        relevant = set(item["relevantIds"])
        ranked = item["retrievedRankedIds"]

        # Precision@1
        if ranked and ranked[0] in relevant:
            p_at_1.append(1.0)
        else:
            p_at_1.append(0.0)

        # MRR@10
        rr = 0.0
        for rank, doc_id in enumerate(ranked[:10], start=1):
            if doc_id in relevant:
                rr = 1.0 / rank
                break
        reciprocal_ranks.append(rr)

    mrr_10 = sum(reciprocal_ranks) / len(reciprocal_ranks)
    mean_p1 = sum(p_at_1) / len(p_at_1)

    return {
        "task": "FailureSimilarityRetrieval",
        "totalQueries": len(retrieval_results),
        "mrrAt10": round(mrr_10, 4),
        "precisionAt1": round(mean_p1, 4),
        "benchmarkPassing": mrr_10 >= 0.75
    }


def evaluate_self_healing(healing_results: List[Dict[str, Any]]) -> Dict[str, Any]:
    """
    Evaluates self-healing patches by requiring double-replay execution pass rate:
    healing_results: list of {
        "failureId": str,
        "initialReplayPassed": bool,
        "secondReplayPassed": bool,
        "synthesizedPatchValid": bool
    }
    """
    if not healing_results:
        return {"error": "No self-healing samples provided"}

    total = len(healing_results)
    syntax_valid = sum(1 for r in healing_results if r.get("synthesizedPatchValid", False))
    pass_replay_1 = sum(1 for r in healing_results if r.get("initialReplayPassed", False))
    pass_double_replay = sum(
        1 for r in healing_results
        if r.get("initialReplayPassed", False) and r.get("secondReplayPassed", False)
    )

    pass_rate_1 = pass_replay_1 / total
    pass_rate_double = pass_double_replay / total
    syntax_rate = syntax_valid / total

    return {
        "task": "SelfHealingDoubleReplay",
        "totalEvaluated": total,
        "syntaxValidRate": round(syntax_rate, 4),
        "singleReplayPassRate": round(pass_rate_1, 4),
        "doubleReplayPassRate": round(pass_rate_double, 4),
        "benchmarkPassing": pass_rate_double >= 0.80 and syntax_rate >= 0.95
    }


def evaluate_model_on_split(split_file: Path, model_dir: Optional[Path] = None) -> Dict[str, Any]:
    """Runs PIM predictor directly on a dataset split and evaluates benchmark score."""
    from ai.pipelines.predict import PIMPredictor

    predictor = PIMPredictor(model_dir=model_dir)
    predictions = []

    with open(split_file, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            rec = json.loads(line)
            gt = rec.get("rootCauseCategory")
            if not gt:
                continue
            res = predictor.predict(rec)
            predictions.append({
                "failureId": rec.get("failureId", "unknown"),
                "groundTruth": gt,
                "predicted": res["predictedCategory"],
                "confidence": res.get("confidence", 0.0)
            })

    return evaluate_classification(predictions)


def main():
    parser = argparse.ArgumentParser(description="Playwright Intelligence Benchmark (PIB) Evaluator")
    parser.add_argument("--split", type=Path, help="Evaluate model directly on test JSONL split")
    parser.add_argument("--model_dir", type=Path, help="Path to PIM model directory")
    parser.add_argument("--predictions", type=Path, help="Path to predictions JSON file")
    args = parser.parse_args()

    print("=== Playwright Intelligence Benchmark (PIB-v1.0) Evaluator ===")

    if args.split:
        print(f"Evaluating model on split: {args.split}")
        report = evaluate_model_on_split(args.split, model_dir=args.model_dir)
        print(json.dumps(report, indent=2))
        return

    if args.predictions:
        with open(args.predictions, "r", encoding="utf-8") as f:
            preds = json.load(f)
        report = evaluate_classification(preds)
        print(json.dumps(report, indent=2))
        return

    # Mock classification demonstration
    mock_classification = [
        {"failureId": "f1", "groundTruth": "LocatorDrift", "predicted": "LocatorDrift", "confidence": 0.95},
        {"failureId": "f2", "groundTruth": "BackendApiFailure", "predicted": "BackendApiFailure", "confidence": 0.98},
        {"failureId": "f3", "groundTruth": "AuthenticationFailure", "predicted": "AuthenticationFailure", "confidence": 0.90},
        {"failureId": "f4", "groundTruth": "TimingIssue", "predicted": "TimingIssue", "confidence": 0.88},
        {"failureId": "f5", "groundTruth": "AssertionFailure", "predicted": "AssertionFailure", "confidence": 0.92},
        {"failureId": "f6", "groundTruth": "TestDataFailure", "predicted": "TestDataFailure", "confidence": 0.89},
        {"failureId": "f7", "groundTruth": "VisualRegression", "predicted": "VisualRegression", "confidence": 0.91},
        {"failureId": "f8", "groundTruth": "EnvironmentFailure", "predicted": "EnvironmentFailure", "confidence": 0.87},
    ]
    report = evaluate_classification(mock_classification)
    print("Demo Classification Evaluation:")
    print(json.dumps(report, indent=2))

    mock_healing = [
        {"failureId": "h1", "synthesizedPatchValid": True, "initialReplayPassed": True, "secondReplayPassed": True},
        {"failureId": "h2", "synthesizedPatchValid": True, "initialReplayPassed": True, "secondReplayPassed": True},
        {"failureId": "h3", "synthesizedPatchValid": True, "initialReplayPassed": True, "secondReplayPassed": False},
        {"failureId": "h4", "synthesizedPatchValid": True, "initialReplayPassed": True, "secondReplayPassed": True},
    ]
    healing_report = evaluate_self_healing(mock_healing)
    print("\nDemo Self-Healing Evaluation:")
    print(json.dumps(healing_report, indent=2))


if __name__ == "__main__":
    main()


