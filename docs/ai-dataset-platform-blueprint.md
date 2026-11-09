# Playwright Intelligence Model (PIM): Data Platform Architecture & Dataset Blueprint

> **Role & Positioning:** Principal AI Architect, Staff ML Engineer, and Data Platform Engineer  
> **Target Repository:** `Autonomous-Bug-Hunter-Playwright`  
> **Core Strategic Principle:** *The dataset is the product. The model is a consumer of the dataset. The dataset is the defensible system-of-record and long-term moat of the platform.*

---

## 1. End-State Data Platform Architecture

The Data Platform captures live browser telemetry from Playwright, normalizes noisy execution artifacts, orchestrates human-in-the-loop (HITL) curation, generates stratified ML-ready splits, and exports versioned datasets to Hugging Face Hub.

```mermaid
flowchart TB
    subgraph Execution["Playwright Test Execution Surface"]
        PWT[Playwright Runner / Browser Worker]
        APP[(Application Under Test)]
        PWT -->|CDP Session| APP
        PWT -->|Raw Telemetry Stream| OBS[Observer: Traces, HAR, DOM, Screenshots, Logs]
    end

    subgraph Ingestion["Ingestion & Normalization Layer"]
        OBS -->|Raw Failure Event| ING[Telemetry Ingestion Pipeline<br/>/api/failure-intelligence/ingest]
        ING -->|Raw JSON Storage| RAW[(Raw Telemetry Lake<br/>ai/datasets/raw/*.json)]
        ING -->|Signature Sanitizer| NORM[Normalizer<br/>Regex Stripping: GUIDs, Timestamps, Memory]
    end

    subgraph Curation["Human-In-The-Loop Curation (HITL)"]
        NORM -->|AI Classification Proposal| PEND[(Pending Review Queue<br/>VerificationStatus: ai_proposed)]
        PEND -->|Review Request| CUR_API[/api/dataset/pending-review]
        QE[Senior Quality Engineer / Reviewer] -->|Curate: Approve/Reclassify + Notes| CUR_ACT[/api/dataset/curate]
        CUR_ACT -->|State Transition: human_verified| CURATED[(Curated Store<br/>ai/datasets/curated/*.jsonl)]
    end

    subgraph Generation["Dataset Transformation & Versioning"]
        CURATED --> SPLIT[Stratified Split Generator<br/>dataset_manager.py]
        SPLIT -->|80/10/10 Stratified| SPLITS[(Versioned Splits<br/>ai/datasets/splits/PFD-v1.0/)]
        SPLITS --> MAN[Manifest & Checksum Validator]
    end

    subgraph SecondaryDatasets["Derived Cognitive Datasets"]
        CURATED --> TRIP[Hard Negative Triplet Miner]
        TRIP --> FSD[(Failure Similarity Dataset<br/>FSD-v1.0 Triplet Records)]
        PWT --> AX_EXT[AXTree State Extractor]
        AX_EXT --> AID[(Action Intent Dataset<br/>AID-v1.0 Intent Records)]
    end

    subgraph Distribution["Benchmarking & Hugging Face Publishing"]
        SPLITS --> PIB[Playwright Intelligence Benchmark<br/>pib_evaluator.py (Macro-F1, MRR@10)]
        SPLITS --> EXPORT[Hugging Face Exporter<br/>export_to_hf.py]
        EXPORT --> HF[(Hugging Face Hub<br/>abul-lab/playwright-failure-dataset)]
    end
```

---

## 2. Canonical Playwright Failure Dataset (PFD-v1.0) Schema

The canonical schema is formally defined in [`ai/schemas/playwright-failure.schema.json`](../ai/schemas/playwright-failure.schema.json)). It provides a complete, structured snapshot of failure context for downstream encoder and generative model consumers.

### 2.1 Complete JSON Schema (Summary Specification)
```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://antigravity.qe/schemas/pfd-v1.0.json",
  "title": "PlaywrightFailureRecord",
  "type": "object",
  "required": [
    "failureId", "timestamp", "browser", "page", "route",
    "errorMessage", "normalizedError", "rootCauseCategory", "verificationStatus"
  ],
  "properties": {
    "failureId": { "type": "string", "pattern": "^fail_[a-zA-Z0-9_-]{8,32}$" },
    "timestamp": { "type": "string", "format": "date-time" },
    "browser": {
      "type": "object",
      "required": ["name", "version", "viewport", "os"],
      "properties": {
        "name": { "type": "string", "enum": ["chromium", "firefox", "webkit"] },
        "version": { "type": "string" },
        "viewport": { "type": "object", "properties": { "width": { "type": "integer" }, "height": { "type": "integer" } } },
        "os": { "type": "string" }
      }
    },
    "page": {
      "type": "object",
      "required": ["url", "title"],
      "properties": {
        "url": { "type": "string", "format": "uri" },
        "title": { "type": "string" },
        "routePattern": { "type": "string" }
      }
    },
    "route": { "type": "string" },
    "workflow": {
      "type": "object",
      "properties": {
        "journeyId": { "type": "string" },
        "name": { "type": "string" },
        "role": { "type": "string" },
        "stepIndex": { "type": "integer" }
      }
    },
    "errorMessage": { "type": "string" },
    "normalizedError": {
      "type": "object",
      "required": ["signature", "cleanedMessage", "salientTokens"],
      "properties": {
        "signature": { "type": "string" },
        "cleanedMessage": { "type": "string" },
        "salientTokens": { "type": "array", "items": { "type": "string" } }
      }
    },
    "stackTrace": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "file": { "type": "string" },
          "line": { "type": "integer" },
          "column": { "type": "integer" },
          "method": { "type": "string" }
        }
      }
    },
    "screenshotMetadata": {
      "type": "object",
      "properties": {
        "sha256": { "type": "string" },
        "storagePath": { "type": "string" },
        "dimensions": { "type": "string" },
        "mimeType": { "type": "string" }
      }
    },
    "traceMetadata": {
      "type": "object",
      "properties": {
        "traceZipPath": { "type": "string" },
        "actionsCount": { "type": "integer" },
        "networkRequestsCount": { "type": "integer" }
      }
    },
    "consoleLogs": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "level": { "type": "string", "enum": ["error", "warning", "info", "log"] },
          "text": { "type": "string" },
          "timestamp": { "type": "string" }
        }
      }
    },
    "networkSummary": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "method": { "type": "string" },
          "url": { "type": "string" },
          "statusCode": { "type": "integer" },
          "durationMs": { "type": "number" },
          "errorText": { "type": "string" }
        }
      }
    },
    "environmentInfo": {
      "type": "object",
      "properties": {
        "ciProvider": { "type": "string" },
        "nodeVersion": { "type": "string" },
        "playwrightVersion": { "type": "string" },
        "targetHost": { "type": "string" },
        "gitCommit": { "type": "string" }
      }
    },
    "rootCauseCategory": {
      "type": "string",
      "enum": [
        "LocatorDrift", "TimingIssue", "AuthenticationFailure",
        "EnvironmentFailure", "BackendApiFailure", "TestDataFailure",
        "VisualRegression", "AssertionFailure", "Unknown"
      ]
    },
    "remediationRecommendation": { "type": "string" },
    "confidence": { "type": "number", "minimum": 0.0, "maximum": 1.0 },
    "verificationStatus": {
      "type": "string",
      "enum": ["unverified", "ai_proposed", "human_verified", "rejected", "quarantined"]
    },
    "curationMetadata": {
      "type": "object",
      "properties": {
        "reviewedBy": { "type": "string" },
        "reviewedAt": { "type": "string", "format": "date-time" },
        "notes": { "type": "string" },
        "tags": { "type": "array", "items": { "type": "string" } }
      }
    }
  }
}
```

### 2.2 Canonical Sample Record
```json
{
  "failureId": "fail_c84a19b2",
  "timestamp": "2026-11-07T12:00:00Z",
  "browser": {
    "name": "chromium",
    "version": "131.0.6778.85",
    "viewport": { "width": 1280, "height": 720 },
    "os": "Windows_NT"
  },
  "page": {
    "url": "http://localhost:3000/shop/checkout",
    "title": "Checkout - DemoShop",
    "routePattern": "/shop/checkout"
  },
  "route": "/shop/checkout",
  "workflow": {
    "journeyId": "journey_guest_checkout",
    "name": "Guest Checkout Journey",
    "role": "guest",
    "stepIndex": 3
  },
  "errorMessage": "Error: page.waitForSelector: Timeout 30000ms exceeded.\nwaiting for locator('#checkout-submit-btn') to be visible",
  "normalizedError": {
    "signature": "LocatorDrift_4f89a1b2",
    "cleanedMessage": "page.waitForSelector: Timeout exceeded waiting for locator('#checkout-submit-btn')",
    "salientTokens": ["checkout", "submit", "btn", "timeout", "visible"]
  },
  "stackTrace": [
    { "file": "tests/checkout.spec.ts", "line": 24, "column": 12, "method": "CartPage.submitOrder" }
  ],
  "screenshotMetadata": {
    "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
    "storagePath": "media/screenshots/fail_c84a19b2.png",
    "dimensions": "1280x720",
    "mimeType": "image/png"
  },
  "traceMetadata": {
    "traceZipPath": "media/traces/fail_c84a19b2.trace.zip",
    "actionsCount": 14,
    "networkRequestsCount": 22
  },
  "consoleLogs": [
    { "level": "log", "text": "Navigated to checkout page", "timestamp": "2026-11-07T11:59:58Z" }
  ],
  "networkSummary": [
    { "method": "POST", "url": "http://localhost:3000/api/cart/validate", "statusCode": 200, "durationMs": 42.5 }
  ],
  "environmentInfo": {
    "ciProvider": "GitHub Actions",
    "nodeVersion": "v22.11.0",
    "playwrightVersion": "1.49.0",
    "targetHost": "localhost:3000",
    "gitCommit": "9bf3a1c"
  },
  "rootCauseCategory": "LocatorDrift",
  "remediationRecommendation": "Button id '#checkout-submit-btn' was renamed to '[data-testid=\"checkout-action\"]'. Update test selector to page.getByRole('button', { name: 'Complete Purchase' }).",
  "confidence": 0.96,
  "verificationStatus": "human_verified",
  "curationMetadata": {
    "reviewedBy": "senior-qe-lead",
    "reviewedAt": "2026-11-07T12:05:00Z",
    "notes": "Confirmed UI button redesign in PR #104. Deterministic replay verified intent.",
    "tags": ["checkout", "redesign", "pfd-v1"]
  }
}
```

---

## 3. Root Cause Taxonomy & Labeling Handbook

To eliminate subjective labeling ambiguity and ensure dataset consistency, labelers must adhere to the following decision rubric:

| Taxonomy Category | Formal Definition | Canonical Symptoms / Indicators | Labeling Guidance | Disqualifying Edge Cases |
|---|---|---|---|---|
| **`LocatorDrift`** | The target DOM node exists semantically in the application state, but can no longer be resolved using the test's authored selector. | `page.waitForSelector: Timeout exceeded`, `waiting for locator(...)`, strict mode violation (`resolved to 2 elements`). | Check the live DOM / accessibility tree: if the intended element is present under a new class, role, or test ID, label as `LocatorDrift`. | If the element was intentionally deleted or is gated behind an ungranted permission, do **NOT** label as LocatorDrift; label as `AssertionFailure` or `AuthenticationFailure`. |
| **`TimingIssue`** | Execution desynchronization between Playwright driver commands and browser DOM / event loop hydration. | `Target closed`, `Element is detached from DOM`, navigation timeout, animation race condition. | Passes on rerun without code changes; passes in headed mode but fails in headless; passes when adding explicit `waitForLoadState('networkidle')`. | If the timeout is caused by a hanging backend API request (HTTP 504), label as `BackendApiFailure` or `EnvironmentFailure`. |
| **`AuthenticationFailure`** | Rejection of credentials, expired JWT bearer tokens, or unauthorized RBAC boundary violation. | HTTP 401 Unauthorized, HTTP 403 Forbidden, unexpected redirection to `/login` or `/auth/session-expired`. | Inspect `networkSummary`: 401/403 status on protected API endpoints or login session cookie expiry in storage. | If the test is explicitly asserting that an unauthorized user *should* be blocked, and the assertion failed, label as `AssertionFailure`. |
| **`EnvironmentFailure`** | Host, network, infrastructure, or container downtime where no application code was executed. | `net::ERR_CONNECTION_REFUSED`, `ECONNREFUSED`, 502 Bad Gateway from reverse proxy, DNS resolution failure. | Application server returned zero response; container crashed; port unreachable. | If the reverse proxy returns 502 because the backend application process threw an unhandled exception, label as `BackendApiFailure`. |
| **`BackendApiFailure`** | Unhandled server-side application exception, 500 status code, or database transaction deadlock. | HTTP 500 Internal Server Error, `NullReferenceException`, database lock timeout in API response payload. | Network response body contains server stack trace or HTTP status $\ge 500$. | If the 500 was caused by bad test data injected by the harness (e.g. duplicate key violation), label as `TestDataFailure`. |
| **`TestDataFailure`** | Conflict in database or harness state caused by collision of fixtures, stale seeds, or dirty teardowns. | `User already exists: admin@test.com`, duplicate key constraint, resource not found because prior run deleted it. | Failure disappears immediately after calling application reset hook (`/api/test-reset`). | If test data is invalid because backend schema migration changed the required fields, label as `BackendApiFailure`. |
| **`VisualRegression`** | Discrepancy between rendered pixel/layout state and baseline snapshot exceeding tolerance threshold. | `Screenshot comparison failed`, `pixel difference count > threshold`, element overlap / clipping. | Functional behavior and API calls succeed, but visual screenshot oracle rejects snapshot. | If layout shifted because a font or image failed to load due to 404/500, label as `BackendApiFailure`. |
| **`AssertionFailure`** | Logical violation of business requirements, acceptance criteria, or invariant contracts. | `expect(received).toBe(expected)`, invariant rule breach (e.g., checkout charge differs from listed price). | All elements resolved and API answered 200, but application state violates business expectation. | If assertion failed because previous action timed out before DOM updated, label as `TimingIssue`. |

---

## 4. Dataset Collection Pipeline

The pipeline is implemented across the .NET API and Python dataset automation scripts:

```
[Playwright Runner]
        │
        ▼ (POST /api/failure-intelligence/ingest)
[Ingestion API] ──────────────────────────► Write raw JSON to /ai/datasets/raw/{failureId}.json
        │
        ▼
[Normalizer]    ──────────────────────────► Strip GUIDs, Timestamps, Memory Addresses
        │
        ▼
[HITL Curation] ──────────────────────────► Verification Queue (status: ai_proposed)
        │
        ▼ (POST /api/dataset/curate)
[Curated Store] ──────────────────────────► Append to /ai/datasets/curated/verified_failures.jsonl
        │
        ▼ (python dataset_manager.py split)
[Stratified Splits] ──────────────────────► /ai/datasets/splits/PFD-v1.0/{train,val,test}.jsonl
```

### 4.1 Ingestion API Contracts
- **Endpoint:** `POST /api/failure-intelligence/ingest`
- **Payload:** `IngestedFailure` (contains raw stack trace, console logs, network events, and screenshot metadata).
- **Behavior:**
  1. Computes deterministic MD5 signature from sanitized tokens.
  2. Persists raw artifact in `ai/datasets/raw/` for auditing and replay.
  3. Assigns initial `VerificationStatus = "ai_proposed"`.

---

## 5. Dataset Curation Tooling (HITL Workflow)

To ensure that models are trained only on ground-truth quality data, a complete Human-in-the-Loop curation interface is exposed via the API and CLI:

```
                          ┌─────────────────────────────┐
                          │   INGESTED (Raw Storage)    │
                          └──────────────┬──────────────┘
                                         │
                                         ▼
                          ┌─────────────────────────────┐
                          │  AI_PROPOSED (Heuristic)    │
                          └──────────────┬──────────────┘
                                         │
                        ┌────────────────┴────────────────┐
                        ▼                                 ▼
         ┌─────────────────────────────┐   ┌─────────────────────────────┐
         │ HUMAN_VERIFIED (Curated)    │   │     REJECTED / QUARANTINED  │
         │ - Category confirmed        │   │ - Noisy / un-reproducible   │
         │ - Notes & Tags added        │   │ - Discarded from training   │
         └─────────────────────────────┘   └─────────────────────────────┘
```

### 5.1 Curation REST Endpoints
- `GET /api/dataset/pending-review`: Retrieves all failures waiting for human sign-off.
- `POST /api/dataset/curate`:
  ```json
  {
    "failureId": "fail_c84a19b2",
    "verifiedCategory": "LocatorDrift",
    "reviewerId": "principal-ai-architect",
    "notes": "Verified selector change against PR #104. Replay confirmed.",
    "approved": true
  }
  ```
- `GET /api/dataset/stats`: Returns real-time metrics on verification progress, pending queue size, and taxonomy distribution.

---

## 6. Failure Embedding & Similarity Dataset (FSD-v1.0)

Metric learning and contrastive similarity fine-tuning require **hard negatives** (failures with nearly identical error messages but distinct underlying root causes).

### 6.1 Triplet Schema
The schema is defined in [`ai/schemas/failure-similarity-triplet.schema.json`](../ai/schemas/failure-similarity-triplet.schema.json)):
```json
{
  "tripletId": "trip_9821af01",
  "anchorFailureId": "fail_c84a19b2",
  "positiveFailureId": "fail_d991bc23",
  "negativeFailureId": "fail_b192e8fa",
  "category": "LocatorDrift",
  "similarityScore": 0.92,
  "contrastiveRationale": "Anchor and positive are both Playwright locator timeouts caused by renamed CSS classes on checkout buttons. Negative is a backend 500 database deadlock on booking submission.",
  "hardNegativeType": "same_error_different_root_cause"
}
```

### 6.2 Hard Negative Mining Strategy
1. **Cluster Mining:** Select pairs $(A, P)$ from the same failure cluster with cosine similarity $\ge 0.85$.
2. **Hard Negative Selection:** Find failure $N$ from a *different* taxonomy category that shares identical text tokens (`Timeout 30000ms exceeded`) with $A$.
3. **Contrastive Loss Objective:** Fine-tunes the ModernBERT embedding space using Triplet Margin Loss:
   $$\mathcal{L} = \max(0, \|f(A) - f(P)\|_2^2 - \|f(A) - f(N)\|_2^2 + \alpha)$$

---

## 7. Action Intent Dataset (AID-v1.0)

Powers the future Playwright Action Intent Model, training Small Language Models to translate user intent into verified Playwright actions without brittle XPath/CSS heuristics.

### 7.1 Twelve Core Intent Classes
1. `complete_checkout`
2. `add_to_cart`
3. `search_catalog`
4. `apply_filter`
5. `authenticate_user`
6. `terminate_session`
7. `fill_form_field`
8. `toggle_navigation`
9. `paginate_results`
10. `cancel_transaction`
11. `select_date_range`
12. `confirm_modal_dialog`

### 7.2 Schema & Record Format
The schema is defined in [`ai/schemas/action-intent.schema.json`](../ai/schemas/action-intent.schema.json)):
```json
{
  "sampleId": "intent_44a19b01",
  "pageContext": {
    "url": "http://localhost:3000/shop/cart",
    "title": "Shopping Cart",
    "activeRole": "guest",
    "previousRoute": "/shop/catalog"
  },
  "axTreeCondensed": "navigation 'Main'\n  link 'Catalog'\nmain 'Cart'\n  heading 'Your Cart'\n  table 'Items'\n  button 'Complete Purchase' [data-testid='checkout-btn']",
  "targetElement": {
    "role": "button",
    "name": "Complete Purchase",
    "selector": "page.getByRole('button', { name: 'Complete Purchase' })",
    "testId": "checkout-btn"
  },
  "intentClass": "complete_checkout",
  "atomicAction": "click",
  "expectedStateTransition": "http://localhost:3000/shop/checkout",
  "verificationStatus": "human_verified"
}
```

---

## 8. Dataset Versioning & Governance

To ensure reproducible model training and full auditability, datasets follow a strict versioning protocol:

- **Format:** `PFD-v<MAJOR>.<MINOR>.<PATCH>`
  - `MAJOR`: Breaking schema change or root-cause taxonomy expansion.
  - `MINOR`: New verified batch addition ($\ge 1,000$ records).
  - `PATCH`: Label corrections, metadata fixes, or deduplication.
- **Manifest Governance:** Every version split contains `manifest.json` recording:
  - Exact SHA-256 hash of each split file (`train.jsonl`, `val.jsonl`, `test.jsonl`).
  - Class distribution counts.
  - Random seed used for stratification.
  - Generation timestamp and author signature.

---

## 9. Playwright Intelligence Benchmark (PIB)

The standardized benchmark harness is implemented in [`ai/benchmarks/pib_evaluator.py`](../ai/benchmarks/pib_evaluator.py)).

### 9.1 Benchmark Tasks & Passing Criteria
1. **Task 1: Root Cause Classification**
   - **Metrics:** Accuracy, Macro-F1 across all 8 classes, Expected Calibration Error (ECE).
   - **Passing Threshold:** $\text{Accuracy} \ge 88.0\%$, $\text{Macro-F1} \ge 85.0\%$.
2. **Task 2: Failure Similarity Retrieval**
   - **Metrics:** Mean Reciprocal Rank (MRR@10), Precision@1, NDCG@5.
   - **Passing Threshold:** $\text{MRR@10} \ge 0.75$.
3. **Task 3: Action Intent Prediction**
   - **Metrics:** Top-1 Accuracy, Top-3 Accuracy on Accessibility Tree snippets.
   - **Passing Threshold:** $\text{Top-1 Accuracy} \ge 82.0\%$, $\text{Top-3 Accuracy} \ge 94.0\%$.

---

## 10. Hugging Face Export Pipeline

The exporter is implemented in [`ai/pipelines/export_to_hf.py`](../ai/pipelines/export_to_hf.py)).

### 10.1 Export Directory Layout
```
ai/datasets/hf_export/PFD-v1.0/
├── README.md               <-- Official Dataset Card with YAML frontmatter
└── data/
    ├── train.jsonl         <-- 80% Stratified Training Split
    ├── val.jsonl           <-- 10% Validation Split
    └── test.jsonl          <-- 10% Held-Out Test Split
```

### 10.2 Publishing Command
```bash
huggingface-cli upload abul-lab/playwright-failure-dataset ai/datasets/hf_export/PFD-v1.0 . --repo-type dataset
```

---

## 11. Career Positioning: AI Solutions Builder specializing in Quality Engineering

### The Moat is the Data Asset
Commodity engineers rush to fine-tune small models on generic public datasets, creating fragile demos with zero enterprise value. By architecting this platform around the **Playwright Failure Dataset (PFD)**, you demonstrate the strategic maturity of a Principal Architect:
1. **Proprietary Data Moat:** Anyone can download Qwen2.5 or ModernBERT; nobody else possesses 25,000 human-verified, normalized Playwright failure traces with ground-truth causal links and replay proofs.
2. **Evaluation-First Discipline:** By building the **Playwright Intelligence Benchmark (PIB)** *before* training models, you establish objective mathematical standards for measuring model performance rather than relying on qualitative impressions.
3. **Defensible Enterprise Architecture:** Decoupling raw telemetry, human curation queues, and stratified dataset versioning shows that you understand data platform governance, compliance, and production ML pipelines.
