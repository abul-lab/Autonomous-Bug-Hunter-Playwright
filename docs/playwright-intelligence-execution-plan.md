# Playwright Intelligence Platform (PIP): Execution Plan, Dataset Milestones & Model Training Strategy

> **Role & Positioning:** Distinguished AI Architect, Principal ML Engineer, Staff Data Platform Engineer, and Quality Engineering AI Specialist  
> **Repository:** `Autonomous-Bug-Hunter-Playwright`  
> **Core Axiom:** *"AI proposes. The engine verifies."*  
> **Mission:** Shift from architecture design to concrete asset production: telemetry collection, production-grade dataset curation, Hugging Face asset publishing, and domain-specific model fine-tuning.

---

## Executive Challenge to Current Assumptions

The proposed 5-phase sequential cascade (*Collect all 3 datasets $\rightarrow$ Publish datasets $\rightarrow$ Train Model 1 $\rightarrow$ Train Model 2 $\rightarrow$ Train Model 3*) contains two critical strategic flaws:

1. **Do NOT wait to collect all 3 datasets (PFD, FSD, AID) before training.**
   - *Flaw:* Attempting to collect failure telemetry and accessibility-tree intent in parallel stalls model shipping for months.
   - *Correction:* **PFD alone is fully sufficient to train and ship Model 1 (PIM-RC-v1).** Begin collecting PFD immediately. FSD is directly derived from PFD via hard-negative pairing, requiring zero separate telemetry collection.
2. **Do NOT publish the Model before the Benchmark.**
   - *Flaw:* Publishing a model without a recognized evaluation standard looks like an unverified hobby project.
   - *Correction:* **The Playwright Intelligence Benchmark (PIB) must be published FIRST or CONCURRENTLY with the dataset.** Establishing the benchmark first defines the rules of the game: *"Here is the industry benchmark for AI in QE, here is our dataset, and here is our model beating baseline classifiers."*
3. **Merge Model 1 (Root Cause) & Model 2 (Failure Similarity) Architecture:**
   - Both tasks process identical text sequences (`[ERROR]`, `[LOCATOR]`, `[DOM]`, `[TRAIL]`).
   - Use `answerdotai/ModernBERT-base` as the shared encoder backbone. Train the classification head first (PIM-RC), then append the metric learning projection head (PIM-FE).

---

## PART 1: Implementation Readiness Review

| Platform Component | Status | Readiness Level | Action Required Before 10k Collection |
|---|---|:---:|---|
| **PFD-v2.0 Schema & Telemetry** | Implemented | **CRITICAL** | Capture `precedingActionTrail` and `domFragmentAtFailure` in Playwright worker listeners. |
| **Sanitizer & Secret Scanner** | Implemented | **CRITICAL** | Automated regex + Shannon entropy scanning blocking secrets (`ai/pipelines/sanitizer.py`). |
| **Split Leakage Deduplicator** | Implemented | **CRITICAL** | Group-based splitting preventing identical error signatures across train/test (`ai/pipelines/deduplicator.py`). |
| **HITL Curation APIs** | Implemented | **IMPORTANT** | Connect Next.js UI to `/api/dataset/pending-review` and `/api/dataset/curate`. |
| **PIB Benchmark Evaluator** | Implemented | **IMPORTANT** | Add Expected Calibration Error (ECE) and Self-Healing double-replay pass rate (`pib_evaluator.py`). |
| **SigLIP Image Embeddings** | Designed | **OPTIONAL / DEFERRED** | Defer multimodal embedding generation until text-based ModernBERT reaches $\ge 88\%$ F1. |
| **Neo4j / GraphSON Export** | Designed | **OPTIONAL / DEFERRED** | In-memory .NET `DigitalTwinGraph` is sufficient for up to 100k nodes; defer Neo4j. |

---

## PART 2: PFD Collection Plan & Data Milestones

```
+----------------------------------------------------------------------------------------------------+
|                                    PFD MILESTONE ACCUMULATION CURVE                                |
|                                                                                                    |
|    5000 ┼                                                                      [Enterprise Ready]  |
|         │                                                                   PIM-RC v2 (Production) |
|    1000 ┼                                              [HF Model Release]                          |
|         │                                           PIM-RC v1 (ModernBERT)                         |
|     500 ┼                           [HF Dataset Release]                                           |
|         │                        PFD-v2.0 (Public OpenCore)                                        |
|     100 ┼       [Internal POC]                                                                     |
|         │    Baseline Classifier                                                                   |
|       0 ┴───────────┼───────────────────────┼───────────────────────┼───────────────────────┼─────►|
|                   Day 15                  Day 30                  Day 60                  Day 90   |
+----------------------------------------------------------------------------------------------------+
```

### Milestone Specifications

#### 1. Milestone 100 (Internal Proof of Concept — Day 15)
- **Target:** 100 verified failure records across DemoShop and DemoHotel seeded defects.
- **Model Readiness:** Train baseline Logistic Regression / TF-IDF classifier to validate feature extraction and data formatting.
- **Benchmark Utility:** Confirms that `pib_evaluator.py` runs cleanly without NaN scores.
- **Curation Overhead:** 100% human-verified by author (approx. 2 hours of review).

#### 2. Milestone 500 (Hugging Face Dataset Alpha — Day 30)
- **Target:** 500 records from open-source apps (Conduit, Spree, Ghost) and synthetic test mutators.
- **Model Readiness:** First fine-tuning pass of `ModernBERT-base` (Target: Macro-F1 $\ge 0.78$).
- **Benchmark Utility:** Sufficient sample size to establish stable per-class precision/recall across all 8 taxonomy categories.
- **Public Action:** Publish `abul-lab/playwright-failure-dataset` (v0.5) to Hugging Face Hub with public Dataset Card.

#### 3. Milestone 1,000 (Hugging Face Model Release — Day 60)
- **Target:** 1,000 verified failures with balanced class distribution ($\ge 80$ samples per category).
- **Model Readiness:** Official release of `abul-lab/pim-root-cause-modernbert` (Target: Macro-F1 $\ge 0.88$).
- **Benchmark Utility:** Official release of **Playwright Intelligence Benchmark (PIB-v1.0)** with baseline comparison leaderboard.
- **Packaging:** Export merged weights and ONNX model for CPU inference (latency to be measured).

#### 4. Milestone 5,000 (Enterprise Foundation — Day 90)
- **Target:** 5,000 records including multi-role RBAC failures, flaky timing races, and live CI/CD telemetry.
- **Model Readiness:** PIM-RC-v2 with multi-task embedding head (PIM-FE) powering instant root-cause clustering.
- **Enterprise Defensibility:** The world's largest public/private repository of Playwright-specific failure artifacts.

---

## PART 3: PFD-v2.0 Implemented Telemetry & Governance

The platform codebase has been updated to support `PFD-v2.0`. The architecture includes:

1. **Canonical V2 Schema:** [`ai/schemas/playwright-failure-v2.schema.json`](../ai/schemas/playwright-failure-v2.schema.json)).
2. **C# Domain Models:** [`FailureModels.cs`](../src/BugHunter.Api/FailureIntelligence/FailureModels.cs)) updated with:
   - `PrecedingActionRecord` (ActionType, Selector, Payload, DeltaMs).
   - `DomFragmentContext` (OuterHtml, ParentTag, ComputedStyles).
   - `FrameworkStateContext` (Framework, IsHydrated, ActivePendingRequests).
   - `GovernanceMetadata` (PiiScanStatus, SecretScanStatus, ScreenshotAuditStatus, ApprovedForTraining, SignedBy).
3. **Data Platform Tooling:**
   - **Sanitizer:** [`sanitizer.py`](../ai/pipelines/sanitizer.py)) — automated regex redactor for JWTs, Bearer tokens, connection strings, emails, and IPs.
   - **Deduplicator:** [`deduplicator.py`](../ai/pipelines/deduplicator.py)) — group-based splitting ensuring **zero cross-split leakage**.
   - **Collector:** [`collector.py`](../ai/pipelines/collector.py)) — ground-truth telemetry generator for all 8 taxonomy categories.
   - **Training Harness:** [`train_pim_rc.py`](../ai/pipelines/train_pim_rc.py)) — fine-tuning and evaluation script.

---

## PART 4: Playwright Intelligence Dataset Strategy

| Dataset | Moat Rating | Industry Value | Uniqueness | Release Strategy | Rationale |
|---|:---:|:---:|:---:|---|---|
| **PFD (Failure Dataset)** | **10 / 10** | **Highest** | **High** | **Dual (Open Core + Vault)** | Solves the #1 enterprise testing pain point (triage fatigue). Open-source 1,000 public samples; keep enterprise proprietary failures in private vault. |
| **FSD (Similarity Dataset)** | **8.5 / 10** | **High** | **Medium** | **Public** | Derived directly from PFD via hard-negative mining. Publishing FSD establishes your platform as the standard for test clustering. |
| **AID (Action Intent Dataset)** | **9.0 / 10** | **Transformational** | **Highest** | **Platform-Owned (Proprietary)** | Autonomous navigation is the holy grail. Keep the rich 20-class intent dataset platform-owned to power proprietary autonomous testing agents. |

---

## PART 5: Hugging Face Execution Plan

### Release Sequence: Benchmark $\rightarrow$ Dataset $\rightarrow$ Model
```
Step 1: Publish PIB Benchmark (Day 45)    ── Define evaluation standard & open leaderboard
Step 2: Publish PFD Dataset (Day 50)      ── Release 500+ curated samples with Dataset Card
Step 3: Publish PIM-RC Model (Day 60)     ── Release fine-tuned ModernBERT beating baselines
```

- **HF Dataset Entry Point:** Release `PFD-v2.0` upon reaching **500 human-verified records**.
- **HF Model Entry Point:** Train and release `PIM-RC-v1` upon reaching **1,000 verified records**.
- **Why Release Benchmark First?** Releasing the model first makes you look like a tool vendor; releasing the benchmark first positions you as an **industry authority establishing scientific rigor**.

---

## PART 6: First Model Training Plan (PIM-RC-v1)

### Backbone Evaluation

| Model Candidate | Parameter Count | Context Window | Inference Latency (CPU) | Verdict |
|---|:---:|:---:|:---:|---|
| **answerdotai/ModernBERT-base** | **149M** | **8,192 tokens** | **not measured** | **RECOMMENDED:** Unmatched encoder performance for long logs and stack traces. Zero generative hallucinations. |
| **Qwen2.5-Coder-1.5B** | 1.54B | 32,768 tokens | not measured | Overkill for classification; reserved for future Action Intent code generation (PIM-AI). |
| **Phi-3.5-mini** | 3.8B | 128k tokens | not measured | Too heavy for CI/CD CPU runners. |
| **SmolLM2-360M** | 360M | 8,192 tokens | not measured | Good runner-up, but inferior representation quality on structured error traces compared to ModernBERT. |

### Training Specifications
- **Recommended Model:** `answerdotai/ModernBERT-base` with 8-class classification head.
- **Hardware:** 1x NVIDIA RTX 3090 / 4090 (or AWS `g5.xlarge` / Lambda Labs A10G) for 2 hours. Local CPU export via `onnxruntime`.
- **Dataset Size:** 1,000 verified PFD-v2.0 records (800 train, 100 val, 100 test).
- **Hyperparameters:** Learning Rate: $2 \times 10^{-5}$, Warmup Ratio: 0.1, Batch Size: 16, Epochs: 4, Weight Decay: 0.01.
- **Input Formatting:**
  `[ERROR] {cleanedMessage} [LOCATOR] {failedLocator} [DOM] {domFragment} [TRAIL] {precedingActions}`
- **Expected Performance:** $\text{Accuracy} \ge 90\%$, $\text{Macro-F1} \ge 0.88$.

---

## PART 7: Model Family Roadmap

```
PIM-RC (Root Cause Classifier)          ── Ingests trace logs; predicts 1 of 8 taxonomy classes
        │
        ▼ (Shared ModernBERT Encoder Backbone)
PIM-FE (Failure Similarity Embeddings)  ── Metric learning head; outputs 768-dim vector for cluster retrieval
        │
        ▼ (Sequence-to-Sequence Generation)
PIM-AI (Action Intent & Healing Model)  ── Qwen2.5-Coder-1.5B; generates Playwright AST patches from AXTree
```

---

## PART 8: Mandatory Dataset Quality Gates

A model **MUST NOT** be trained or published until all 6 gates pass:

1. **Volume Gate:** Minimum 1,000 verified records for training splits.
2. **Class Balance Gate:** No single taxonomy class may exceed 30% of total records; no class may fall below 8%.
3. **Zero-Leakage Gate:** Deduplicator confirms 0 cross-split Jaccard overlap ($\ge 0.85$) between train and test.
4. **Sanitization Gate:** 100% of records have `piiScan.status == "PASSED"` and `secretScan.status == "PASSED"`.
5. **Screenshot Gate:** 100% of attached images have OCR sweep completed and sensitive inputs redacted.
6. **Provenance Gate:** Every sample contains a valid `curationMetadata.reviewedBy` handle and timestamp.

---

## PART 9: Career Positioning Hierarchy

To position yourself as an **AI Solutions Builder specializing in Quality Engineering**, rank your assets by defensibility:

```
1. Playwright Intelligence Benchmark (PIB) ── Defines the industry standard; cited by others
2. Playwright Failure Dataset (PFD)        ── The proprietary data moat; impossible to replicate overnight
3. Domain-Fine-Tuned Model (PIM)           ── Tangible ML engineering artifact on Hugging Face
4. Deterministic Self-Healing AST Engine   ── "AI proposes, engine verifies"; no-unverified-findings rule
5. Digital Twin Graph Core                 ── Systems architecture; bridges code, APIs, and state
6. Failure Intelligence Clustering         ── High-utility diagnostic tool solving triage overhead
```

*Key Takeaway:* Test automation engineers write test scripts. AI Solutions Builders build **scientific benchmarks, proprietary datasets, and verified cognitive systems**.

---

## PART 10: 90-Day Practical Execution Plan

### Days 1–30: The Collection Engine (Target: 500 PFD Records)
- **Week 1:** Hook Playwright worker (`src/browser-worker/server.mjs`) to auto-post failures to `/api/failure-intelligence/ingest` with preceding actions and DOM fragments.
- **Week 2:** Run automated test hunts on DemoShop and DemoHotel with all 16 seeded defects enabled; accumulate first 200 raw failure records.
- **Week 3:** Ingest real-world open-source web suites (Conduit RealWorld app); reach 500 records.
- **Week 4:** Run `sanitizer.py` and `dataset_manager.py split --version PFD-v2.0`. Verify zero leakage with `deduplicator.py`.

### Days 31–60: The Benchmark & Model Launch (Target: 1,000 Records)
- **Week 5:** Publish **Playwright Intelligence Benchmark (PIB-v1.0)** on GitHub and Hugging Face with evaluation dataset.
- **Week 6:** Reach 1,000 curated failures; publish `PFD-v2.0` on Hugging Face Hub under `abul-lab/playwright-failure-dataset`.
- **Week 7:** Fine-tune `answerdotai/ModernBERT-base` using `train_pim_rc.py`; achieve $\ge 88\%$ Macro-F1.
- **Week 8:** Export model to ONNX; publish `abul-lab/pim-root-cause-modernbert` to Hugging Face Hub with interactive Model Card and inference widget.

### Days 61–90: Enterprise Closed-Loop Integration & Moat Hardening
- **Week 9:** Connect fine-tuned PIM-RC model into the .NET API via `Microsoft.ML.OnnxRuntime` for local inference in CI/CD (latency to be measured).
- **Week 10:** Derive `FSD-v1.0` (Failure Similarity Dataset) using hard-negative mining; fine-tune contrastive embedding head.
- **Week 11:** Launch Closed-Loop Self-Healing action in GitHub Actions; auto-open PRs on test locator drift using verified AST patches.
- **Week 12:** Author comprehensive engineering post & paper: *"Defeating the Test-Oracle Problem with Domain-Specific SLMs and Deterministic Verification."*

---

## Top 10 Actions to Take Next

```
 1. [CODE] Connect browser-worker failure hook in server.mjs to send precedingActionTrail and domFragmentAtFailure.
 2. [DATA] Run 50 automated defect hunts on DemoShop & DemoHotel to populate ai/datasets/raw/ with initial 100 failures.
 3. [CURATION] Use /api/dataset/pending-review and dataset_manager.py to human-verify the first 100 failure records.
 4. [PIPELINE] Execute sanitizer.py over all collected raw records to confirm zero PII or secret exposure.
 5. [SPLITS] Run dataset_manager.py split --version PFD-v2.0 to generate verified train, val, and test splits.
 6. [LEAKAGE] Run deduplicator.py to confirm 0 cross-split contamination.
 7. [BENCHMARK] Execute pib_evaluator.py to establish baseline classification metrics.
 8. [HUGGING FACE] Initialize abul-lab/playwright-intelligence-benchmark on Hugging Face Spaces.
 9. [TRAINING] Set up GPU training environment (Lambda Labs / local RTX) and run train_pim_rc.py on ModernBERT.
10. [PORTFOLIO] Document the training loss curves and confusion matrix for your career portfolio and GitHub README.
```
