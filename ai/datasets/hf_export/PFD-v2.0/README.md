---
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
