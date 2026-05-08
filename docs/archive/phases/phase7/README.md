# Phase 7 - Correction Memory

Phase 7 adds the first learning-like behavior while preserving KoeNote's principle that the user makes final decisions.

## Goal

KoeNote remembers accepted terms and correction patterns locally, then uses them to improve future candidates without automatically applying changes.

## Scope

- add `user_terms`
- add `correction_memory`
- add `correction_memory_events`
- add "remember this correction" UI after accept/manual correction
- add "remember this term" UI for likely proper nouns
- feed enabled `user_terms` into ASR hotwords/context
- run a deterministic memory matcher after ASR and before LLM review
- generate memory-derived `CorrectionDraft` records
- label memory-derived drafts as "過去の修正から"
- record suggested/accepted/rejected/ignored memory events

## Schema Additions

```sql
user_terms (
  term_id TEXT PRIMARY KEY,
  surface TEXT NOT NULL,
  reading TEXT NULL,
  category TEXT NOT NULL DEFAULT 'general',
  enabled INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
```

```sql
correction_memory (
  memory_id TEXT PRIMARY KEY,
  wrong_text TEXT NOT NULL,
  correct_text TEXT NOT NULL,
  issue_type TEXT NOT NULL,
  scope TEXT NOT NULL,
  accepted_count INTEGER NOT NULL DEFAULT 1,
  rejected_count INTEGER NOT NULL DEFAULT 0,
  enabled INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
```

```sql
correction_memory_events (
  event_id TEXT PRIMARY KEY,
  memory_id TEXT NULL,
  draft_id TEXT NULL,
  job_id TEXT NOT NULL,
  segment_id TEXT NULL,
  event_type TEXT NOT NULL,
  created_at TEXT NOT NULL
);
```

`correction_drafts` also gains `source` and `source_ref_id` so memory-derived suggestions can be labelled and tracked through the same review queue.

## Matching Rules

Start deterministic and conservative:

- exact text match only
- no automatic application
- one memory suggestion per affected segment per memory pair
- de-duplicate against LLM-generated drafts
- cap ASR hotword/context enrichment:
  - max 50 terms
  - max 40 characters per term
  - prefer recently accepted terms
  - exclude terms already present in current job context

## Scoring

Use a simple score only for ordering and display labels.

```text
score =
  0.50
  + 0.10 * min(accepted_count, 3)
  - 0.15 * min(rejected_count, 3)
  + 0.10 if scope matches the current project
  + 0.05 if used recently
```

Display labels:

| Score | Label |
| --- | --- |
| 0.80+ | 過去の修正から・信頼高め |
| 0.60-0.79 | 過去の修正から・確認推奨 |
| below 0.60 | 過去の修正から・低め |

## Out Of Scope

- fuzzy matching
- model fine-tuning
- automatic application
- bulk auto-accept

## Completion Criteria

- Accepted/manual corrections can create local memory entries.
- Enabled terms are passed into future ASR context/hotwords within limits.
- Memory matches create normal `CorrectionDraft` records.
- Memory-derived drafts are reviewed through the same Phase 5 workflow.
- Nothing is auto-applied.
- Tests cover matching, de-duplication, disabled memory, scoring, and ASR context caps.

## Implementation Start

- Added migration v4 for `user_terms`, `correction_memory`, `correction_memory_events`, and draft source metadata.
- Added `CorrectionMemoryService` for ASR hotword enrichment, deterministic exact-match suggestions, memory event logging, and remembering accepted/manual corrections.
- Wired memory-derived drafts into `ReviewWorker` before draft persistence.
- Added the review-panel opt-in checkbox: `この修正を今後の候補に使う`.
- Added tests for migration, source metadata persistence, ASR enrichment, and memory draft generation.
