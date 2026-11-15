#!/usr/bin/env python3
"""
Hugging Face Dataset Exporter & Dataset Card Generator.
Exports versioned splits to standard Hugging Face format with official YAML frontmatter.
"""

import argparse
import json
import shutil
from pathlib import Path

BASE_DIR = Path(__file__).resolve().parent.parent
SPLITS_DIR = BASE_DIR / "datasets" / "splits"
EXPORT_DIR = BASE_DIR / "datasets" / "hf_export"


DATASET_CARD_TEMPLATE = """---
language:
- en
license: mit
task_categories:
- text-classification
- feature-extraction
- sentence-similarity
task_ids:
- root-cause-analysis
- failure-classification
tags:
- playwright
- quality-engineering
- testing
- failure-intelligence
- automated-testing
- devops
pretty_name: Playwright Failure Dataset (PFD)
size_categories:
- 1K<n<10K
configs:
- config_name: default
  data_files:
  - split: train
    path: data/train.jsonl
  - split: validation
    path: data/val.jsonl
  - split: test
    path: data/test.jsonl
---

# Playwright Failure Dataset (PFD-v1.0)

## Dataset Summary
The **Playwright Failure Dataset (PFD)** is the authoritative, system-of-record quality engineering dataset for training domain-specific models on web application testing telemetry. It contains normalized Playwright execution traces, console logs, network errors, and accessibility tree diffs mapped to an 8-class root cause taxonomy and verified human annotations.

Created by **Autonomous Bug Hunter** to eliminate test triage overhead and power the **Playwright Intelligence Model (PIM)**.

## Supported Tasks
1. **Root Cause Classification (`text-classification`):** Predict one of 8 root-cause categories from sanitized error messages, stack traces, and console logs.
2. **Failure Similarity Retrieval (`sentence-similarity`):** Contrastive embeddings to cluster similar failure incidents across CI/CD runs.
3. **Automated Remediation Synthesis:** Map normalized failure signatures to actionable Playwright test patches.

## Taxonomy Classes
- `LocatorDrift`
- `TimingIssue`
- `AuthenticationFailure`
- `EnvironmentFailure`
- `BackendApiFailure`
- `TestDataFailure`
- `VisualRegression`
- `AssertionFailure`

## Dataset Structure
Each sample contains:
- `failureId`: Unique deterministic identifier
- `timestamp`: UTC execution timestamp
- `browser`: Browser metadata (name, version, viewport, os)
- `page`: Page URL, title, and parameterized route
- `errorMessage`: Raw un-truncated error message
- `normalizedError`: Stripped memory addresses, timestamps, and extracted salient tokens
- `stackTrace`: Structured stack frames
- `consoleLogs`: Browser console logs at failure
- `networkSummary`: Failed network requests (status code, URL, duration)
- `rootCauseCategory`: Ground-truth taxonomy label
- `remediationRecommendation`: Actionable remediation guidance
- `verificationStatus`: `human_verified`

## Usage with Hugging Face Datasets
```python
from datasets import load_dataset

dataset = load_dataset("abul-lab/playwright-failure-dataset", split="train")
print(dataset[0]["rootCauseCategory"])
```

## Citation
```bibtex
@dataset{abul_pfd_2026,
  author = {Abul's Autonomous Bug Hunter Platform},
  title = {Playwright Failure Dataset (PFD): Ground Truth Telemetry for AI Quality Engineering},
  year = {2026},
  publisher = {Hugging Face},
  version = {1.0.0}
}
```
"""


def export_hf_dataset(version: str = "PFD-v1.0", repo_id: str = "abul-lab/playwright-failure-dataset"):
    version_dir = SPLITS_DIR / version
    if not version_dir.exists():
        print(f"[ERROR] Version split '{version}' does not exist in {SPLITS_DIR}")
        return False

    target_dir = EXPORT_DIR / version
    target_data_dir = target_dir / "data"
    target_data_dir.mkdir(parents=True, exist_ok=True)

    # Copy splits
    for split_name in ["train.jsonl", "val.jsonl", "test.jsonl"]:
        src = version_dir / split_name
        if src.exists():
            shutil.copy2(src, target_data_dir / split_name)

    # Write Dataset Card
    card_path = target_dir / "README.md"
    with open(card_path, "w", encoding="utf-8") as f:
        f.write(DATASET_CARD_TEMPLATE)

    print(f"[EXPORT] Successfully packaged Hugging Face dataset at: {target_dir}")
    print(f"         Dataset Card generated: {card_path}")
    print(f"         To upload run: huggingface-cli upload {repo_id} {target_dir} . --repo-type dataset")
    return True


def main():
    parser = argparse.ArgumentParser(description="Export PFD dataset to Hugging Face Hub format")
    parser.add_argument("--version", default="PFD-v1.0", help="Version split to export")
    parser.add_argument("--repo-id", default="abul-lab/playwright-failure-dataset", help="Target Hugging Face Hub repo id")
    args = parser.parse_args()

    export_hf_dataset(args.version, args.repo_id)


if __name__ == "__main__":
    main()

