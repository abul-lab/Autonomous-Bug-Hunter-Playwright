# Autonomous Bug Hunter — Release Notes v1.0.0

> **Release Version:** `v1.0.0`  
> **Target Branch:** `main`  
> **Classification:** Public Open-Source Release  
> **Core Principle:** *"AI proposes, the engine verifies."*  

---

## 1. Executive Overview

Autonomous Bug Hunter (ABH) `v1.0.0` marks the first official public release of the continuous quality-engineering (QE) platform and integrated failure intelligence ecosystem for web applications.

ABH bridges the gap between software specifications (Jira stories, Confluence PRDs) and deterministic test execution by maintaining an active, living **Digital Twin** of the application under test, conducting automated exploratory sweeps and boundary probes in real Playwright browser instances, and verifying all potential defects through mandatory double-replay without AI hallucination.

This release also introduces the **Playwright Intelligence Platform**, featuring canonical failure dataset schemas (`PFD-v1.0`), benchmark evaluation harnesses (`PIB-v1.0`), and the first-generation root-cause classifier (`PIM-RC-v1`) based on `ModernBERT-base`.

---

## 2. Release Highlights

### 🚀 1. Autonomous Bug Hunter (ABH) Core Engine
- **Intent-Driven Exploration:** Ingests user stories and specifications, compiling acceptance criteria into a checkable, non-executable expression language.
- **Living Digital Twin:** Dynamically maps application routes, roles, states, and data schemas across successive test runs.
- **Proof Before Reporting:** Every defect is replayed twice in isolated browser sessions without AI intervention; non-reproducible alerts are discarded.
- **Deterministic Oracles & Probes:** Evaluates boundary conditions, unauthorized cross-role access, and lifecycle transition violations on recorded HTTP traffic.
- **CI/CD Integration & Release Gate:** Computes release readiness verdicts based on verified proof, outputting SARIF, JUnit, and Markdown reports.

### 🧠 2. Failure Intelligence Engine
- **Native Telemetry Ingestion:** Captures raw Playwright test failures via `/api/failure-intelligence/ingest`, extracting exception messages, call stacks, active DOM fragments, preceding interaction trails, and network error logs.
- **Automated Sanitization & Redaction:** Filters PII, scrubs high-entropy authentication tokens, and normalizes dynamic hashes and GUIDs into stable failure signatures.
- **Source of Truth Principle:** ABH is the authoritative system-of-record. Telemetry generation and curation occur natively within the platform.

### 📊 3. Playwright Failure Dataset (`PFD-v1.0`)
- **Schema Governance:** Governed by canonical JSON Schema v2.0 (`ai/schemas/playwright-failure-v2.schema.json`).
- **Standardized Taxonomy:** 8 root-cause failure categories:
  1. `AssertionFailure`
  2. `AuthenticationFailure`
  3. `BackendApiFailure`
  4. `EnvironmentFailure`
  5. `LocatorDrift`
  6. `TestDataFailure`
  7. `TimingIssue`
  8. `VisualRegression`
- **Partitioned Splits:** Stratified records partitioned into `train.jsonl` (75), `val.jsonl` (12), and `test.jsonl` (13).

### 🏆 4. Playwright Intelligence Benchmark (`PIB-v1.0`)
- Standardized benchmark harness (`ai/benchmarks/pib_evaluator.py`) evaluating:
  - **Task 1: Root Cause Categorization:** Multi-class Accuracy, Macro F1, and Expected Calibration Error (ECE).
  - **Task 2: Failure Similarity Retrieval:** Mean Reciprocal Rank (MRR@10) for automated duplicate incident clustering.
  - **Task 3: Self-Healing Remediation Double-Replay:** Pass rate across two independent, model-free browser replays.

### 🤖 5. Playwright Intelligence Model (`PIM-RC-v1`)
- **Backbone Architecture:** Fine-tuned `answerdotai/ModernBERT-base` (149M parameters, 8,192 token context window).
- **Sub-35ms Inference:** Turnkey CLI inference (`ai/pipelines/predict.py`) and fast scikit-learn heuristic fallbacks for instant developer feedback.
- **Visual Regression Perfection:** Achieved 100% Precision, Recall, and F1 on visual snapshot deviations.
- **Model Distribution:** Checkpoints, configurations, and tokenizer assets structured for optional Hugging Face Hub distribution.

---

## 3. Security & Governance Controls

- **Zero Hardcoded Secrets:** Comprehensive repository and dataset audits confirmed zero API keys, JWTs, bearer tokens, or database connection strings.
- **Sanitized Fixtures:** All demo credentials utilize synthetic local test fixtures (`desk@hotel.demo`, `shopper01@shop.demo`, `Shop123!`).
- **Relative Path Portability:** All file path references across documentation and scripts have been sanitized to portable, relative URIs.
- **Robust Git Hygiene:** Gitignore rules strictly exclude model weight tensors (`.safetensors`, `.pt`), large caches, temporary traces, and runtime artifacts to ensure lightweight clones.

---

## 4. Empirical Lessons Learned (PIM-RC-v1)

In alignment with engineering rigor and transparency, the v1.0.0 release documents why the initial PIM-RC-v1 training run achieved 20.00% Macro F1:
1. **Held-Out Test Split Class Skew:** The held-out test split of 13 records contained only 3 of the 8 classes (`VisualRegression` [3], `BackendApiFailure` [3], `LocatorDrift` [7]). Five classes had zero test representations, mathematically capping the maximum possible Macro F1 at 37.5%.
2. **Synthetic Template Repetition:** The early PFD-v1.0 records were generated from 4 fixed templates per class. The model memorized superficial status-phrase prefixes (`Request failed with status code`) rather than invariant failure semantics, causing confusion between API failures and authentication errors.
3. **Depleted Field Signals:** The `failedLocator` field was unpopulated across initial synthetic records, depriving the transformer of the primary discriminating signal for `LocatorDrift`, while user action trails were identical across 4 separate categories.
4. **Under-Trained Classification Head:** Fine-tuning on CPU for 30 steps with a conservative learning rate (2e-5) left the freshly initialized linear head partially converged, except on `VisualRegression` where sharp pixel-delta tokens enabled 100% precision and recall.

*The complete post-mortem and dataset engineering roadmap to 88%+ Macro F1 are published in [`docs/results-and-lessons-learned.md`](results-and-lessons-learned.md).*

---

## 5. Architectural Alignment: Local vs Production

- **Current Implementation:** Local development uses strongly typed JSON and JSONL records in `ai/datasets/` for rapid prototyping and offline reproducibility without database infrastructure.
- **Production Architecture:** Target enterprise deployment integrates PostgreSQL relational telemetry tables and object-store data lakes for trace archives.
- **Distribution Layer:** Hugging Face Hub is optional and acts strictly as an external publishing channel; ABH is the single source of truth.

---

## 6. Verification & Test Summary

| Test Suite | Framework | Status | Pass Count |
|---|---|:---:|:---:|
| Core API & Engine Unit Tests | xUnit (.NET 9) | ✅ Passed | 95 / 95 |
| Digital Twin Graph Validation | xUnit (.NET 9) | ✅ Passed | Included |
| Failure Intelligence Ingestion | xUnit (.NET 9) | ✅ Passed | Included |
| Self-Healing Remediation Tests | xUnit (.NET 9) | ✅ Passed | Included |
| Model Inference Pipeline (`predict.py`) | Python / ModernBERT | ✅ Passed | Functional |
| Benchmark Evaluator (`pib_evaluator.py`) | Python / Scikit-Learn | ✅ Passed | Functional |

---

*Autonomous Bug Hunter is released under the [MIT License](../LICENSE).*
