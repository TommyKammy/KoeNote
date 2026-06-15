# Review candidate decision change flow

## Scope

The review candidate confirmation dialog allows changing a processed candidate decision while the dialog is open:

- accepted -> rejected
- accepted -> manual edit
- edited -> accepted
- edited -> rejected
- edited -> manual edit
- rejected -> accepted
- rejected -> manual edit

The existing accept, reject, and manual edit controls are reused. For pending candidates they create the first decision. For processed candidates they replace the latest decision for that draft.

## Consistency rules

- `correction_drafts.status` is updated to the new decision status.
- A new row is appended to `review_decisions`; previous decision rows are preserved as audit history.
- `review_operation_history` records the before/after state, so the operation follows the same undo/history path as normal review decisions.
- `jobs.unreviewed_draft_count` is refreshed from pending drafts and does not increase when a processed decision is changed.
- `transcript_segments.final_text` is rebuilt from the baseline that existed before the draft's first decision.

## Same-segment safety

Decision changes are allowed only when the selected draft is the latest operation for its transcript segment.

This prevents an older decision from rewriting text after a newer decision or manual segment edit has already changed the same segment. In that case the UI reports the service error and keeps the existing state.

## Non-goals

- Editing an older processed decision after a newer same-segment operation.
- Reordering or deleting previous `review_decisions` audit rows.
- Replaying correction memory side effects for historical decisions beyond the current dialog operation.
