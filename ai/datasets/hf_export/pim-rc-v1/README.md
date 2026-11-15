---
language:
- en
license: mit
library_name: transformers
tags:
- modernbert
- text-classification
- playwright
- automated-testing
- bug-hunting
- root-cause-analysis
- failure-intelligence
- devops
base_model: answerdotai/ModernBERT-base
metrics:
- accuracy
- precision
- recall
- f1
pipeline_tag: text-classification
model-index:
- name: PIM-RC-v1
  results:
  - task:
      type: text-classification
      name: Playwright Failure Root Cause Analysis
    dataset:
      name: Playwright Failure Dataset (PFD-v1.0)
      type: jsonl
    metrics:
    - name: Accuracy
      type: accuracy
      value: 0.2308
    - name: Precision
      type: precision
      value: 0.2000
    - name: Recall
      type: recall
      value: 0.2000
    - name: Macro F1
      type: f1
      value: 0.2000
---

# Playwright Intelligence Model (PIM-RC-v1)

Fine-tuned sequence classification model based on `answerdotai/ModernBERT-base` for automated root-cause classification of Playwright and browser automation test failures.

Developed as part of **Autonomous Bug Hunter**.

## Model Details
- **Architecture**: Sequence Classification Encoder (`answerdotai/ModernBERT-base`)
- **Dataset**: Playwright Failure Dataset (`PFD-v1.0`)
- **Splits**: 75 Train | 12 Validation | 13 Test
- **Number of Classes**: 8

## Evaluation Results
Evaluated on the held-out test split:

| Metric | Score |
|---|---|
| **Accuracy** | `23.08%` |
| **Precision (Macro)** | `20.00%` |
| **Recall (Macro)** | `20.00%` |
| **Macro F1** | `20.00%` |

## Target Taxonomy Classes
- `AssertionFailure`
- `AuthenticationFailure`
- `BackendApiFailure`
- `EnvironmentFailure`
- `LocatorDrift`
- `TestDataFailure`
- `TimingIssue`
- `VisualRegression`

## Multi-Modal Input Representation
Each failure record is compiled into a multi-attribute sequence:
1. `errorMessage`: Raw browser/test runner exception
2. `normalizedError.cleanedMessage`: Stripped and canonicalized error message
3. `precedingActionTrail`: Prior Playwright action sequence with selectors and values
4. `frameworkState`: Hydration status, pending requests, and framework runtime metadata

```
[ERROR_MESSAGE]
AssertionError: expect(cartTotal).toBe('$89.00')

[CLEANED_MESSAGE]
AssertionError: expect(cartTotal).toBe('$89.00')

[ACTION_TRAIL]
fill on 'input#coupon' with 'SUMMER10' -> click on 'button#apply-coupon'

[FRAMEWORK_STATE]
framework=nextjs, isHydrated=True, activePendingRequests=0
```

## How to Use

### Using `transformers` Pipeline
```python
from transformers import AutoTokenizer, AutoModelForSequenceClassification, pipeline

model_path = "pim-rc-v1"
tokenizer = AutoTokenizer.from_pretrained(model_path)
model = AutoModelForSequenceClassification.from_pretrained(model_path)

classifier = pipeline("text-classification", model=model, tokenizer=tokenizer)

input_text = """[ERROR_MESSAGE]
TimeoutError: locator.click: Timeout 5000ms exceeded waiting for locator('#checkout')

[CLEANED_MESSAGE]
Timeout 5000ms exceeded waiting for locator('#checkout')

[ACTION_TRAIL]
goto on '/cart' -> click on '#terms'

[FRAMEWORK_STATE]
framework=react, isHydrated=True
"""

result = classifier(input_text)
print(result)
```

### Using Local Inference Script
```bash
python predict.py --json '{"errorMessage": "Timeout 5000ms exceeded"}'
```
