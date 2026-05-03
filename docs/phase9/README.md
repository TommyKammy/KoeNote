# Phase 9 - Export Workflow

Phase 9 completes the user's output path. It is intentionally separate from packaging so export correctness can be tested on its own.

## Goal

Users can export reviewed transcripts with the right text, speaker labels, timestamps, and warnings.

## Scope

- implement TXT export
- implement JSON export
- implement SRT export
- implement VTT export
- implement DOCX export if the runtime dependency is available
- use `final_text` first, then `normalized_text`, then `raw_text`
- resolve speaker aliases
- warn when unresolved drafts remain
- allow the user to choose an output folder
- add "open export folder" action
- record export log events

## Export Text Fallback

```text
display_text =
  final_text
  ?? normalized_text
  ?? raw_text
```

Rejecting a draft should not create `final_text`; this fallback preserves the meaning of "left unchanged."

## Out Of Scope

- installer
- model pack
- cloud sharing
- automatic publishing

## Completion Criteria

- Export buttons produce files with expected text and timestamps.
- Unreviewed drafts trigger a clear warning before export.
- Speaker aliases appear in exported files.
- Export logs include file paths and failure categories.
- Tests cover fallback order and unresolved-review warnings.
