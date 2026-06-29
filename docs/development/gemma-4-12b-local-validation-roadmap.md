# Gemma 4 12B QAT local validation roadmap

This document records the local-only path for re-evaluating
`gemma-4-12b-it-qat-q4-0` as a future high-accuracy KoeNote review model.

## Current decision

Keep Gemma 4 12B QAT hidden from normal user selection.

The model may be tested locally only when this environment variable is set:

```powershell
$env:KOENOTE_ENABLE_GEMMA12B_LOCAL_VALIDATION = "1"
```

Without the flag, KoeNote keeps repairing the hidden 12B selection back to the
stable default review model, Gemma 4 E4B.

## Why a guarded path is required

Previous local probes showed that Gemma 4 12B QAT can run slowly because it
keeps generating until the max-token limit after entering abnormal output modes:

- long `000000...` runs
- visible `<|channel>` / `<|channel>thought` output
- visible thinking output despite `--reasoning off`
- missing or unbalanced `BEGIN_BLOCK` / `END_BLOCK` markers

Those are treated as model/runtime anomalies, not merely cosmetic text.

## Runtime profile

The local validation profile intentionally differs from the E4B profile:

- generation profile: `gemma12b-polishing-local-validation`
- chunk segment count: `8`
- max tokens: `768`
- temperature: `0`
- repeat penalty: `1.15`
- validation mode: `gemma12b_guarded_blocks`

The goal is to avoid expensive max-token runs while checking whether the model
can produce stable block-shaped output.

## Fallback behavior

When the primary model is Gemma 4 12B QAT, readable polishing enables
chunk-level fallback to the stable default Gemma 4 E4B model when E4B is
installed.

Fallback order:

1. Run the 12B chunk.
2. Reject critical anomalies immediately.
3. Retry the same chunk with E4B.
4. If E4B output is usable, save that chunk and mark its generation profile with
   the model fallback reason.
5. If E4B is unavailable or also fails, use the existing source-transcript
   fallback for that chunk.

This keeps long jobs recoverable while 12B behavior is being measured.

## Local smoke command

Run the MTP `llama-server` smoke without starting the app:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File scripts\development\Test-Gemma412BPolishingValidation.ps1 `
  -ModelPath "$env:LOCALAPPDATA\KoeNote\models\review\gemma-4-12b-it-qat-q4-0\gemma-4-12b-it-qat-q4_0.gguf" `
  -MtpDraftModelPath "$env:LOCALAPPDATA\KoeNote\models\review_aux\gemma-4-12b-it-qat-assistant-mtp-q8-0\gemma-4-12b-it-qat-assistant-mtp-q8_0.gguf" `
  -E4BModelPath "$env:LOCALAPPDATA\KoeNote\models\review\gemma-4-e4b-it-q4-k-m\gemma-4-E4B-it-Q4_K_M.gguf"
```

The report is written to:

```text
artifacts\smoke\gemma12b-polishing-validation.json
```

## Promotion gate

Do not promote 12B back to high accuracy until repeated local jobs satisfy all
of these:

- 0 occurrences of `000000...`
- 0 occurrences of `<|channel>`, `<|channel>thought`, or visible thinking text
- 0 unbalanced block marker failures
- 0 unrecovered chunk failures across a 30-minute audio job
- E4B fallback rate below 1 percent
- 12B total polishing time no more than 2x the E4B baseline on RTX 3090
- exported readable document remains complete and manually editable

If any gate fails, keep E4B as the high-accuracy model and keep 12B hidden.
