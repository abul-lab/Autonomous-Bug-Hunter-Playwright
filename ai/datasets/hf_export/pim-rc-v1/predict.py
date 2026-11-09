#!/usr/bin/env python3
"""
Playwright Intelligence Model (PIM-RC) Inference Engine.
Predicts root cause category from Playwright test failure records.
"""

import argparse
import json
import os
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional, Union

os.environ["HF_HUB_DISABLE_SYMLINKS_WARNING"] = "1"
os.environ["HF_HUB_DISABLE_XET"] = "1"
os.environ["TOKENIZERS_PARALLELISM"] = "false"

TAXONOMY = [
    "AssertionFailure",
    "AuthenticationFailure",
    "BackendApiFailure",
    "EnvironmentFailure",
    "LocatorDrift",
    "TestDataFailure",
    "TimingIssue",
    "VisualRegression"
]
LABEL2ID = {cat: i for i, cat in enumerate(TAXONOMY)}
ID2LABEL = {i: cat for i, cat in enumerate(TAXONOMY)}

REMEDIATION_GUIDES = {
    "AssertionFailure": "Verify application state and expectations. Add custom error messaging or check payload contract.",
    "AuthenticationFailure": "Refresh test auth session, verify authentication cookies/tokens, or renew credentials in test setup.",
    "BackendApiFailure": "Inspect mock servers, network intercepts, and upstream microservice availability.",
    "EnvironmentFailure": "Verify browser binaries, display server, container memory, and base URL connectivity.",
    "LocatorDrift": "Update target selector to use semantic locators (getByRole, getByTestId, getByText) instead of fragile DOM paths.",
    "TestDataFailure": "Ensure test database seeding succeeded and unique identifiers/fixtures are not stale or colliding.",
    "TimingIssue": "Replace static sleeps with dynamic web-first assertions (expect(locator).toBeVisible()) or increase action timeout.",
    "VisualRegression": "Review visual snapshot diff threshold, mask animated/dynamic regions, and ensure font antialiasing consistency."
}

BASE_DIR = Path(__file__).resolve().parent.parent
MODELS_DIR = BASE_DIR / "models" / "pim-rc-v1"


def format_action_trail(actions: Union[List[Any], str, None]) -> str:
    """Formats preceding action trail into a clean textual trace."""
    if not actions:
        return "None"
    if isinstance(actions, str):
        return actions
    trail_parts = []
    for a in actions:
        if isinstance(a, dict):
            atype = a.get("actionType") or a.get("type", "action")
            sel = a.get("selector") or a.get("target", "")
            payload = a.get("payload") or a.get("value")
            delta = a.get("deltaMs")
            desc = atype
            if sel:
                desc += f" on '{sel}'"
            if payload is not None and payload != "":
                desc += f" with '{payload}'"
            if delta is not None:
                desc += f" [+{delta}ms]"
            trail_parts.append(desc)
        else:
            trail_parts.append(str(a))
    return " -> ".join(trail_parts) if trail_parts else "None"


def format_framework_state(state: Union[Dict[str, Any], str, None]) -> str:
    """Formats framework state metadata into structured text."""
    if not state:
        return "None"
    if isinstance(state, dict):
        parts = [f"{k}={v}" for k, v in state.items()]
        return ", ".join(parts) if parts else "None"
    return str(state)


def build_training_text(record: Dict[str, Any]) -> str:
    """
    Constructs sequence input from failure record using:
    - errorMessage
    - normalizedError.cleanedMessage
    - precedingActionTrail
    - frameworkState
    """
    error_msg = record.get("errorMessage") or ""
    norm_error = record.get("normalizedError") or {}
    if isinstance(norm_error, dict):
        cleaned_msg = norm_error.get("cleanedMessage") or ""
    else:
        cleaned_msg = str(norm_error)

    trail = format_action_trail(record.get("precedingActionTrail", []))
    fw_state = format_framework_state(record.get("frameworkState"))

    text = (
        f"[ERROR_MESSAGE]\n{str(error_msg).strip()}\n\n"
        f"[CLEANED_MESSAGE]\n{str(cleaned_msg).strip()}\n\n"
        f"[ACTION_TRAIL]\n{trail}\n\n"
        f"[FRAMEWORK_STATE]\n{fw_state}"
    )
    return text


class PIMPredictor:
    """Inference wrapper for trained Playwright Intelligence Model (PIM-RC)."""

    def __init__(self, model_dir: Optional[Union[str, Path]] = None):
        if model_dir:
            self.model_dir = Path(model_dir)
        elif Path("models/pim-rc-v1").exists():
            self.model_dir = Path("models/pim-rc-v1").resolve()
        else:
            self.model_dir = MODELS_DIR

        self.device = "cpu"
        self.model = None
        self.tokenizer = None
        self.baseline_clf = None
        self.baseline_vec = None
        self.id2label = ID2LABEL
        self.label2id = LABEL2ID
        self._load_model()

    def _load_model(self):
        # 1. Try loading PyTorch / Hugging Face model
        if self.model_dir.exists():
            # Load metadata manifest if present
            manifest_file = self.model_dir / "pim-rc-manifest.json"
            if manifest_file.exists():
                try:
                    with open(manifest_file, "r", encoding="utf-8") as f:
                        meta = json.load(f)
                        if "classes" in meta:
                            self.id2label = {i: c for i, c in enumerate(meta["classes"])}
                            self.label2id = {c: i for i, c in enumerate(meta["classes"])}
                except Exception:
                    pass

            try:
                import torch
                from transformers import AutoModelForSequenceClassification, AutoTokenizer

                self.device = "cuda" if torch.cuda.is_available() else "cpu"
                self.tokenizer = AutoTokenizer.from_pretrained(str(self.model_dir))
                self.model = AutoModelForSequenceClassification.from_pretrained(str(self.model_dir))
                self.model.to(self.device)
                self.model.eval()

                if hasattr(self.model.config, "id2label") and self.model.config.id2label:
                    self.id2label = {int(k): v for k, v in self.model.config.id2label.items()}
                    self.label2id = {v: int(k) for k, v in self.id2label.items()}
                return
            except Exception as e:
                # Fallback to check baseline or print notice
                pass

        # 2. Check for baseline model file (joblib/pickle)
        baseline_file = self.model_dir / "baseline_model.joblib"
        if baseline_file.exists():
            try:
                import joblib
                bundle = joblib.load(baseline_file)
                self.baseline_vec = bundle["vectorizer"]
                self.baseline_clf = bundle["classifier"]
                if "classes" in bundle:
                    self.id2label = {i: c for i, c in enumerate(bundle["classes"])}
                    self.label2id = {c: i for i, c in enumerate(bundle["classes"])}
                return
            except Exception:
                pass

    def predict(self, record_or_text: Union[Dict[str, Any], str]) -> Dict[str, Any]:
        """Predict root cause category for a single failure record or formatted text."""
        if isinstance(record_or_text, dict):
            input_text = build_training_text(record_or_text)
            orig_record = record_or_text
        else:
            input_text = str(record_or_text)
            orig_record = {}

        # 1. Inference via Transformers
        if self.model is not None and self.tokenizer is not None:
            import torch
            inputs = self.tokenizer(
                input_text,
                return_tensors="pt",
                truncation=True,
                max_length=512,
                padding=True
            ).to(self.device)

            with torch.no_grad():
                outputs = self.model(**inputs)
                logits = outputs.logits
                probs = torch.softmax(logits, dim=-1).squeeze(0).cpu().tolist()

            max_idx = int(torch.argmax(logits, dim=-1).item())
            predicted_label = self.id2label.get(max_idx, "Unknown")
            confidence = float(probs[max_idx])
            scores = {self.id2label.get(i, f"Class_{i}"): round(float(p), 4) for i, p in enumerate(probs)}

            return {
                "predictedCategory": predicted_label,
                "confidence": round(confidence, 4),
                "scores": scores,
                "remediationRecommendation": REMEDIATION_GUIDES.get(predicted_label, "Review test execution logs.")
            }

        # 2. Inference via Baseline Classifier
        if self.baseline_clf is not None and self.baseline_vec is not None:
            import numpy as np
            X = self.baseline_vec.transform([input_text])
            probs = self.baseline_clf.predict_proba(X)[0]
            max_idx = int(np.argmax(probs))
            predicted_label = self.id2label.get(max_idx, "Unknown")
            confidence = float(probs[max_idx])
            scores = {self.id2label.get(i, f"Class_{i}"): round(float(p), 4) for i, p in enumerate(probs)}

            return {
                "predictedCategory": predicted_label,
                "confidence": round(confidence, 4),
                "scores": scores,
                "remediationRecommendation": REMEDIATION_GUIDES.get(predicted_label, "Review test execution logs.")
            }

        # Heuristic fallback if model not loaded
        return self._heuristic_prediction(input_text)

    def _heuristic_prediction(self, text: str) -> Dict[str, Any]:
        """Rule-based heuristic predictor for environments where model is not loaded."""
        lower = text.lower()
        if "timed out" in lower or "timeout" in lower or "waitfor" in lower:
            cat = "TimingIssue"
        elif "expect(" in lower or "assertionerror" in lower or "received:" in lower:
            cat = "AssertionFailure"
        elif "locator" in lower or "strict mode violation" in lower or "no element found" in lower:
            cat = "LocatorDrift"
        elif "401" in lower or "403" in lower or "unauthorized" in lower or "auth" in lower:
            cat = "AuthenticationFailure"
        elif "500" in lower or "502" in lower or "503" in lower or "networkerror" in lower or "fetch" in lower:
            cat = "BackendApiFailure"
        elif "visual" in lower or "pixel" in lower or "snapshot" in lower:
            cat = "VisualRegression"
        elif "database" in lower or "foreign key" in lower or "not found in test data" in lower:
            cat = "TestDataFailure"
        else:
            cat = "EnvironmentFailure"

        scores = {c: (0.85 if c == cat else 0.02) for c in self.id2label.values()}
        return {
            "predictedCategory": cat,
            "confidence": 0.85,
            "scores": scores,
            "remediationRecommendation": REMEDIATION_GUIDES.get(cat, "Review test execution logs.")
        }


def predict_root_cause(record: Dict[str, Any], model_dir: Optional[Union[str, Path]] = None) -> Dict[str, Any]:
    """Helper functional API to predict root cause from record."""
    predictor = PIMPredictor(model_dir=model_dir)
    return predictor.predict(record)


def main():
    parser = argparse.ArgumentParser(description="PIM Root Cause Classifier Inference")
    parser.add_argument("--model_dir", default=str(MODELS_DIR), help="Path to saved model directory")
    parser.add_argument("--input", "-i", help="Path to JSON failure file or JSONL batch file")
    parser.add_argument("--json", help="Direct JSON string of failure record")
    parser.add_argument("--error", help="Direct error message string")
    parser.add_argument("--cleaned", default="", help="Normalized cleaned error message")
    parser.add_argument("--trail", default="", help="Preceding action trail")
    parser.add_argument("--framework", default="", help="Framework state")
    args = parser.parse_args()

    predictor = PIMPredictor(model_dir=args.model_dir)

    if args.json:
        record = json.loads(args.json)
        res = predictor.predict(record)
        print(json.dumps(res, indent=2))
        return

    if args.error:
        record = {
            "errorMessage": args.error,
            "normalizedError": {"cleanedMessage": args.cleaned or args.error},
            "precedingActionTrail": args.trail,
            "frameworkState": args.framework
        }
        res = predictor.predict(record)
        print(json.dumps(res, indent=2))
        return

    if args.input:
        in_path = Path(args.input)
        if not in_path.exists():
            print(f"Error: file not found {in_path}", file=sys.stderr)
            sys.exit(1)

        if in_path.suffix == ".jsonl":
            results = []
            with open(in_path, "r", encoding="utf-8") as f:
                for line in f:
                    if line.strip():
                        rec = json.loads(line)
                        fid = rec.get("failureId", "unknown")
                        pred = predictor.predict(rec)
                        results.append({"failureId": fid, **pred})
            print(json.dumps(results, indent=2))
        else:
            with open(in_path, "r", encoding="utf-8") as f:
                rec = json.load(f)
            res = predictor.predict(rec)
            print(json.dumps(res, indent=2))
        return

    # Default interactive sample demo
    sample = {
        "failureId": "sample_demo_1",
        "errorMessage": "TimeoutError: locator.click: Timeout 5000ms exceeded waiting for locator('#checkout-btn')",
        "normalizedError": {
            "cleanedMessage": "Timeout 5000ms exceeded waiting for locator('#checkout-btn')"
        },
        "precedingActionTrail": [
            {"actionType": "goto", "selector": "/cart"},
            {"actionType": "click", "selector": "#terms-checkbox"}
        ],
        "frameworkState": {"framework": "react", "isHydrated": True}
    }
    print("No input provided. Running on sample demo failure:")
    print(json.dumps(sample, indent=2))
    print("\nPrediction Result:")
    res = predictor.predict(sample)
    print(json.dumps(res, indent=2))


if __name__ == "__main__":
    main()

