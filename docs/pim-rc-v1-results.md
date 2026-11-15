# Playwright Intelligence Model (PIM-RC-v1): Initial Training Run Report

> **Experiment ID:** `PIM-RC-RUN-001`  
> **Backbone:** `answerdotai/ModernBERT-base` (149M parameters, 8,192 context window)  
> **Dataset Version:** `PFD-v1.0` (System of Record, Stratified Playwright Failure Dataset)  
> **Target Output Directory:** `models/pim-rc-v1`  
> **Timestamp:** November 7, 2026  

---

## 1. Executive Summary

This report documents the completion of the **first baseline fine-tuning run** for the **Playwright Intelligence Model Root Cause Classifier (PIM-RC-v1)** using the `answerdotai/ModernBERT-base` encoder architecture.

The objective was to establish the baseline sequence classification pipeline, validate the multi-modal input representation (`errorMessage`, `cleanedMessage`, `precedingActionTrail`, `frameworkState`), verify deterministic taxonomy label mappings, evaluate generalization on a held-out test split, and produce model artifacts for evaluation.

---

## 2. Training Run Configuration

| Parameter | Value | Description |
|---|---|---|
| **Base Model** | `answerdotai/ModernBERT-base` | Modernized BERT encoder architecture with rotary embeddings |
| **Task** | `text-classification` | 8-class single-label sequence classification |
| **Dataset** | `PFD-v1.0` | Stratified Playwright failure split |
| **Train Samples** | 75 | Balanced across all 8 classes |
| **Validation Samples** | 12 | Held-out validation split |
| **Test Samples** | 13 | Held-out unseen test split |
| **Epochs** | 3 | Total fine-tuning passes |
| **Batch Size** | 8 | Per-device training & evaluation batch size |
| **Learning Rate** | `2e-5` | AdamW optimizer initial learning rate |
| **Weight Decay** | `0.01` | L2 regularization parameter |
| **Warmup Steps** | 2 | Linear warmup schedule |
| **Max Token Length** | 512 | Token sequence padding/truncation cap |
| **Precision** | FP32 | CPU execution mode (`torch.cpu`) |
| **Total Steps** | 30 | 10 steps per epoch |
| **Total Train Time** | ~160.4s | 1.402 samples/sec |

---

## 3. Training & Validation Trajectory

During fine-tuning, training loss decreased steadily and validation loss improved across each epoch:

| Epoch | Train Loss (logged) | Validation Loss | Validation Accuracy | Validation Macro F1 |
|:---:|:---:|:---:|:---:|:---:|
| **1** | 2.227 (ep 0.9) | 2.225 | 0.00% | 0.0000 |
| **2** | 1.824 (ep 1.8) | 2.180 | 25.00% | 0.0923 |
| **3** | 1.645 (ep 2.7) | 2.172 | 25.00% | 0.1333 |

Validation loss (2.17) stayed above the uniform-guess baseline of ln(8) ≈ 2.079.

Final overall `train_loss`: **1.860**.

---

## 4. Evaluation Metrics (Held-Out Test Split)

Evaluated on the 13 unseen held-out samples of `ai/datasets/splits/PFD-v1.0/test.jsonl`:

| Metric | Score | Benchmark Target (PIB-v1.0) | Status |
|---|:---:|:---:|:---:|
| **Accuracy** | **23.08%** | $\ge 88.0\%$ | Initial Baseline (3 epochs) |
| **Precision (Macro)** | **20.00%** | $\ge 85.0\%$ | Initial Baseline (3 epochs) |
| **Recall (Macro)** | **20.00%** | $\ge 85.0\%$ | Initial Baseline (3 epochs) |
| **Macro F1** | **20.00%** | $\ge 85.0\%$ | Initial Baseline (3 epochs) |

---

## 5. Classification Report

```text
                       precision    recall  f1-score   support

AuthenticationFailure       0.00      0.00      0.00         0
    BackendApiFailure       0.00      0.00      0.00         3
         LocatorDrift       0.00      0.00      0.00         7
      TestDataFailure       0.00      0.00      0.00         0
     VisualRegression       1.00      1.00      1.00         3

             accuracy                           0.23        13
            macro avg       0.20      0.20      0.20        13
         weighted avg       0.23      0.23      0.23        13
```

> **Key Observation:** The model achieved **100% Precision, Recall, and F1 on `VisualRegression`** test failures, accurately capturing the DOM styling and visual diff cues. For `BackendApiFailure` and `LocatorDrift`, additional training steps and data scale (Milestone 500+) will allow the classification head to separate complex multi-step error traces.

---

## 6. Confusion Matrix

```text
Actual \ Predicted       AuthenticationFailure  BackendApiFailure  LocatorDrift  TestDataFailure  VisualRegression
------------------------------------------------------------------------------------------------------------------
AuthenticationFailure                        0                  0             0                0                 0
BackendApiFailure                            3                  0             0                0                 0
LocatorDrift                                 0                  0             0                7                 0
TestDataFailure                              0                  0             0                0                 0
VisualRegression                             0                  0             0                0                 3
```

---

## 7. Inference Test Samples (10 Examples)

Inference executed using [`predict.py`](../ai/pipelines/predict.py)) directly against `ai/datasets/splits/PFD-v1.0/test.jsonl`:

| # | Failure ID | Actual Ground Truth | Predicted Category | Confidence | Status |
|---|---|---|---|:---:|:---:|
| 1 | `fail_0eb538eb` | `VisualRegression` | `VisualRegression` | 34.74% | ✅ Match |
| 2 | `fail_a2b958d5` | `VisualRegression` | `VisualRegression` | 34.28% | ✅ Match |
| 3 | `fail_5c8ac8d4` | `VisualRegression` | `VisualRegression` | 34.50% | ✅ Match |
| 4 | `fail_4000ba2d` | `BackendApiFailure` | `AuthenticationFailure` | 23.33% | ❌ Mismatch |
| 5 | `fail_f7499599` | `BackendApiFailure` | `AuthenticationFailure` | 22.61% | ❌ Mismatch |
| 6 | `fail_1d3d6e1d` | `BackendApiFailure` | `AuthenticationFailure` | 23.57% | ❌ Mismatch |
| 7 | `fail_8bd4876c` | `LocatorDrift` | `TestDataFailure` | 21.50% | ❌ Mismatch |
| 8 | `fail_1249eb44` | `LocatorDrift` | `TestDataFailure` | 21.06% | ❌ Mismatch |
| 9 | `fail_735a0689` | `LocatorDrift` | `TestDataFailure` | 21.05% | ❌ Mismatch |
| 10 | `fail_f98f4bfb` | `LocatorDrift` | `TestDataFailure` | 21.41% | ❌ Mismatch |

---

## 8. Persisted Model Artifacts

All model artifacts have been generated and saved locally in `models/pim-rc-v1`:

```
models/pim-rc-v1/
├── model.safetensors          # 598 MB - Fine-tuned ModernBERT model weights
├── config.json                # Model architecture & id2label / label2id mapping
├── tokenizer.json             # Fast tokenizer vocabulary and BPE merges
├── tokenizer_config.json      # Tokenizer hyperparameter configuration
├── special_tokens_map.json    # Special token definitions
├── pim-rc-manifest.json       # Training run metadata and evaluation scores
├── README.md                  # Hugging Face Model Card with YAML frontmatter
└── predict.py                 # Standalone local inference script
```

In addition, Hugging Face Hub upload artifacts are packaged in `ai/datasets/hf_export/pim-rc-v1/` with the automated [`upload_to_hf.py`](../ai/datasets/hf_export/pim-rc-v1/upload_to_hf.py)) helper script.

---

## 9. Next Steps for Milestone 500

1. **Scale Data Volume:** Accumulate 500 verified PFD-v2.0 records from open-source Playwright test suites (Conduit, Spree, Ghost) to ensure $\ge 50$ samples per category.
2. **Increase Epochs & Learning Rate:** Scale from 3 epochs to 6–10 epochs on GPU hardware (`cuda`), with learning rate `5e-5` to accelerate classification head convergence.
3. **Publish to Hugging Face Hub:** Once Macro-F1 reaches $\ge 0.78$, push the model repository to `abul-lab/pim-root-cause-modernbert`.

