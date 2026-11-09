#!/usr/bin/env python3
"""
Dataset Manager CLI for Playwright Intelligence Model (PIM).
Handles:
- Schema validation
- Ingestion of raw Playwright failure telemetry
- Human curation and verification
- Stratified train/val/test splits generation
"""

import argparse
import hashlib
import json
import os
import random
import sys
from datetime import datetime, timezone
from pathlib import Path

BASE_DIR = Path(__file__).resolve().parent.parent
SCHEMAS_DIR = BASE_DIR / "schemas"
DATASETS_DIR = BASE_DIR / "datasets"
RAW_DIR = DATASETS_DIR / "raw"
CURATED_DIR = DATASETS_DIR / "curated"
SPLITS_DIR = DATASETS_DIR / "splits"

TAXONOMY = [
    "LocatorDrift",
    "TimingIssue",
    "AuthenticationFailure",
    "EnvironmentFailure",
    "BackendApiFailure",
    "TestDataFailure",
    "VisualRegression",
    "AssertionFailure",
    "Unknown",
]


def load_json(path: Path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def load_jsonl(path: Path):
    records = []
    if not path.exists():
        return records
    with open(path, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                records.append(json.loads(line))
    return records


def save_jsonl(records, path: Path):
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        for r in records:
            f.write(json.dumps(r, ensure_ascii=False) + "\n")


def validate_schema(record: dict, schema_type: str = "failure") -> bool:
    required_failure = [
        "failureId",
        "timestamp",
        "browser",
        "page",
        "route",
        "errorMessage",
        "normalizedError",
        "rootCauseCategory",
        "verificationStatus",
    ]
    if schema_type == "failure":
        for field in required_failure:
            if field not in record:
                print(f"[FAIL] Missing required field: {field}")
                return False
        if record["rootCauseCategory"] not in TAXONOMY:
            print(f"[FAIL] Invalid category: {record['rootCauseCategory']}")
            return False
    return True


def ingest_failure(raw_telemetry_path: Path):
    raw_data = load_json(raw_telemetry_path)
    failure_id = raw_data.get("failureId") or f"fail_{hashlib.md5(str(raw_data).encode()).hexdigest()[:10]}"
    raw_data["failureId"] = failure_id
    raw_data["timestamp"] = raw_data.get("timestamp") or datetime.now(timezone.utc).isoformat()
    raw_data["verificationStatus"] = "unverified"

    out_file = RAW_DIR / f"{failure_id}.json"
    RAW_DIR.mkdir(parents=True, exist_ok=True)
    with open(out_file, "w", encoding="utf-8") as f:
        json.dump(raw_data, f, indent=2)
    print(f"[INGEST] Stored raw failure artifact: {out_file}")
    return failure_id


def curate_record(failure_id: str, verified_category: str, reviewer: str, notes: str):
    raw_file = RAW_DIR / f"{failure_id}.json"
    record = None
    if raw_file.exists():
        record = load_json(raw_file)
    else:
        # Search in sample / curated
        curated_file = CURATED_DIR / "sample_failures.jsonl"
        records = load_jsonl(curated_file)
        for r in records:
            if r["failureId"] == failure_id:
                record = r
                break

    if not record:
        print(f"[ERROR] Failure ID '{failure_id}' not found.")
        return False

    if verified_category not in TAXONOMY:
        print(f"[ERROR] Category '{verified_category}' not in canonical taxonomy.")
        return False

    record["rootCauseCategory"] = verified_category
    record["verificationStatus"] = "human_verified"
    record["curationMetadata"] = {
        "reviewedBy": reviewer,
        "reviewedAt": datetime.now(timezone.utc).isoformat(),
        "notes": notes,
        "tags": ["curated", "verified"]
    }

    curated_out = CURATED_DIR / "verified_failures.jsonl"
    existing = load_jsonl(curated_out)
    existing = [r for r in existing if r["failureId"] != failure_id]
    existing.append(record)
    save_jsonl(existing, curated_out)
    print(f"[CURATE] Successfully verified failure '{failure_id}' as '{verified_category}'.")
    return True


def generate_splits(version: str = "PFD-v1.0", seed: int = 42):
    source_file = CURATED_DIR / "sample_failures.jsonl"
    verified_file = CURATED_DIR / "verified_failures.jsonl"

    records = load_jsonl(source_file) + load_jsonl(verified_file)
    if not records:
        print("[WARN] No curated records found. Ingest and curate records first.")
        return

    # Deduplicate by failureId
    by_id = {r["failureId"]: r for r in records}
    unique_records = list(by_id.values())

    # Group by base error message to prevent cross-split leakage
    signature_groups = {}
    for r in unique_records:
        sig = r.get("errorMessage", "").strip()
        signature_groups.setdefault(sig, []).append(r)

    grouped_signatures = list(signature_groups.keys())
    random.seed(seed)
    random.shuffle(grouped_signatures)

    train_set, val_set, test_set = [], [], []
    for i, sig in enumerate(grouped_signatures):
        bucket = i % 10
        if bucket == 0 and len(grouped_signatures) >= 3:
            test_set.extend(signature_groups[sig])
        elif bucket == 1 and len(grouped_signatures) >= 3:
            val_set.extend(signature_groups[sig])
        else:
            train_set.extend(signature_groups[sig])

    if not test_set and train_set:
        test_set.append(train_set.pop())
    if not val_set and len(train_set) > 1:
        val_set.append(train_set.pop())

    version_dir = SPLITS_DIR / version
    version_dir.mkdir(parents=True, exist_ok=True)

    save_jsonl(train_set, version_dir / "train.jsonl")
    save_jsonl(val_set, version_dir / "val.jsonl")
    save_jsonl(test_set, version_dir / "test.jsonl")

    manifest = {
        "datasetVersion": version,
        "generatedAt": datetime.now(timezone.utc).isoformat(),
        "totalRecords": len(unique_records),
        "splits": {
            "train": len(train_set),
            "validation": len(val_set),
            "test": len(test_set)
        },
        "taxonomyDistribution": {
            cat: sum(1 for r in unique_records if r.get("rootCauseCategory") == cat)
            for cat in TAXONOMY
        }
    }
    with open(version_dir / "manifest.json", "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)

    print(f"[SPLIT] Successfully generated version {version} in {version_dir}")
    print(f"        Train: {len(train_set)} | Val: {len(val_set)} | Test: {len(test_set)}")


def main():
    parser = argparse.ArgumentParser(description="Dataset Manager for Playwright Intelligence Model")
    subparsers = parser.add_subparsers(dest="command")

    # Ingest
    ingest_p = subparsers.add_parser("ingest")
    ingest_p.add_argument("--file", required=True, help="Path to raw telemetry JSON")

    # Curate
    curate_p = subparsers.add_parser("curate")
    curate_p.add_argument("--id", required=True, help="Failure ID")
    curate_p.add_argument("--category", required=True, choices=TAXONOMY, help="Root cause category")
    curate_p.add_argument("--reviewer", default="senior-qe-lead", help="Reviewer handle")
    curate_p.add_argument("--notes", default="", help="Curation notes")

    # Splits
    split_p = subparsers.add_parser("split")
    split_p.add_argument("--version", default="PFD-v1.0", help="Split version tag")

    args = parser.parse_args()
    if args.command == "ingest":
        ingest_failure(Path(args.file))
    elif args.command == "curate":
        curate_record(args.id, args.category, args.reviewer, args.notes)
    elif args.command == "split":
        generate_splits(args.version)
    else:
        parser.print_help()


if __name__ == "__main__":
    main()

