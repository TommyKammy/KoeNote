# Phase 6 - Editing, Speaker Aliases, and Undo

Phase 6 adds local editing safety around the review workflow after Phase 5 has made draft selection and decisions reliable.

## Goal

Users can correct transcript text and speaker names confidently, with a one-step undo for the most recent destructive-looking action.

## Scope

- add segment direct edit UI
- update only `final_text` for direct segment edits
- show copy that explains original ASR text is retained
- add `speaker_aliases` table
- resolve speaker aliases in transcript display and future exports
- add `review_operation_history`
- support one-step undo for:
  - accept draft
  - reject draft
  - manual correction
  - segment direct edit
  - speaker alias update

## Schema Additions

```sql
speaker_aliases (
  job_id TEXT NOT NULL,
  speaker_id TEXT NOT NULL,
  display_name TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  PRIMARY KEY (job_id, speaker_id)
);
```

```sql
review_operation_history (
  operation_id TEXT PRIMARY KEY,
  job_id TEXT NOT NULL,
  draft_id TEXT NULL,
  segment_id TEXT NULL,
  operation_type TEXT NOT NULL,
  before_json TEXT NOT NULL,
  after_json TEXT NOT NULL,
  created_at TEXT NOT NULL
);
```

## UI Copy

Use clear product language near direct edit actions:

```text
元の文字起こしは残ります。
出力には、ここで保存した文章が使われます。
```

## Out Of Scope

- multi-step history browser
- correction memory
- ASR prompt enrichment
- export file generation

## Completion Criteria

- Direct segment edits never overwrite `raw_text`.
- Speaker names are stored as aliases, not destructive mass updates to transcript rows.
- The immediately previous review/edit/speaker operation can be undone.
- Undo restores DB state and visible UI state.
- Tests cover transaction rollback and alias resolution.
