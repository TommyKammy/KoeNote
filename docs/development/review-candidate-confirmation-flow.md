# Review Candidate Confirmation Flow

This note defines the UX and state-transition contract for inserting a review
candidate confirmation dialog before readable polishing. It covers GitHub issue
#72 and is intended to guide the UI and runner work in #73 through #75.

## Problem

The current run flow can generate review candidates and show them in the main
window, then continue into readable polishing without requiring the user to
accept, reject, or edit those candidates.

Readable polishing reads transcript segments through `TranscriptReadRepository`,
which uses `final_text` when present and otherwise falls back to
`normalized_text` or `raw_text`. Pending review candidates are therefore not
reflected in the final readable output until they are explicitly decided through
the review operation path.

## Target Flow

```text
Run button
  -> preprocess
  -> ASR
  -> review candidate generation
  -> review candidate confirmation
  -> speaker name confirmation
  -> readable polishing
```

The confirmation dialog belongs after `ReviewStageRunner` succeeds and before
`ConfirmSpeakerNamesBeforeReadablePolishing` is called. This keeps text edits
ahead of speaker-label edits, and ensures readable polishing sees the same
transcript state the user just approved.

## Terms

- Review candidate: a pending `CorrectionDraft`.
- Decided candidate: a candidate whose status is accepted, rejected, or edited.
- Accepted candidate: the suggested text is written to the segment's
  `final_text`.
- Edited candidate: a user-edited text is written to `final_text`.
- Rejected candidate: the candidate is dismissed and no `final_text` is written
  by that decision.
- Confirmation gate: the modal step that can either allow the run to continue to
  speaker confirmation or stop before readable polishing.

## Dialog Outcomes

The dialog should return one of these outcomes to the runner.

| Outcome | Meaning | Runner action |
| --- | --- | --- |
| `Continue` | The user confirmed candidate handling and wants to proceed. | Run speaker name confirmation next. |
| `Defer` | The user wants to handle candidates later. | Do not run readable polishing. Leave pending candidates visible. |
| `Cancel` | The user cancelled the run at the confirmation gate. | Do not run readable polishing. Keep run state non-running. |

`Defer` and `Cancel` both stop before readable polishing. They differ only in
user-facing copy and logging.

## Candidate Count Rules

- If there are no pending candidates for the job, skip the dialog and continue
  to speaker name confirmation.
- If pending candidates exist, show the dialog before readable polishing.
- The dialog should display pending, accepted, rejected, and edited counts for
  the current confirmation session.
- `Continue` should be enabled only when no pending candidates remain, unless a
  future setting explicitly allows continuing with pending candidates.
- If a future setting allows continuing with pending candidates, the runner must
  log that pending candidates were intentionally left unapplied.

## Candidate Decision Rules

The dialog must use the same durable review operation path as the existing
Review panel:

- Accept -> `ReviewOperationService.AcceptDraft`
- Reject -> `ReviewOperationService.RejectDraft`
- Manual edit -> `ReviewOperationService.ApplyManualEdit`

This is important because those operations update the correction draft status,
segment `final_text`, `review_state`, pending counts, and edit history
consistently.

The dialog must not keep a separate in-memory-only decision list. Once a
candidate is accepted, rejected, or edited in the dialog, that decision should be
persisted immediately.

## State Transitions

### No Candidates

```text
Review stage succeeded
  -> pending candidate count is 0
  -> skip review candidate confirmation
  -> speaker name confirmation
```

### Candidates Fully Decided

```text
Review stage succeeded
  -> pending candidate count > 0
  -> show review candidate confirmation
  -> user accepts/rejects/edits all candidates
  -> Continue
  -> speaker name confirmation
```

### Deferred Confirmation

```text
Review stage succeeded
  -> pending candidate count > 0
  -> show review candidate confirmation
  -> user chooses Defer
  -> stop before speaker name confirmation
  -> stop before readable polishing
  -> leave pending candidates in the main Review panel
```

### Cancelled Confirmation

```text
Review stage succeeded
  -> pending candidate count > 0
  -> show review candidate confirmation
  -> user chooses Cancel or closes the dialog
  -> stop before speaker name confirmation
  -> stop before readable polishing
  -> leave already persisted candidate decisions intact
```

### Runtime Cancellation

If the run cancellation token is cancelled before or during the dialog handoff,
the runner must not show the next confirmation dialog and must not start
readable polishing.

## Speaker Name Confirmation Ordering

Speaker name confirmation should stay after review candidate confirmation.

Rationale:

- Review candidates change transcript text.
- Speaker confirmation changes display names for speaker ids.
- Readable polishing should receive text and speaker names after both have been
  reviewed.

If review candidate confirmation returns `Defer` or `Cancel`, speaker name
confirmation should not be shown.

## Re-run Behavior

On a later run or manual resume:

- Pending candidates should be loaded from `CorrectionDraftRepository`.
- Already decided candidates should remain decided and should not reappear as
  pending dialog items.
- Accepted or edited candidates should already be reflected through
  `final_text`.
- Re-running the review stage may replace draft sets according to existing
  review-stage behavior; the confirmation dialog should consume whatever is
  pending after that stage completes.

## Logging

The runner should log these events in the job log or latest status surface:

- Confirmation skipped because there are no pending candidates.
- Confirmation opened with the pending count.
- Confirmation completed with accepted, rejected, edited, and remaining counts.
- Confirmation deferred by the user.
- Confirmation cancelled by the user.

## Implementation Surface

Expected implementation touch points for follow-up issues:

- `MainWindowViewModel.Runner.cs`
  - Add a `ConfirmReviewCandidatesBeforeReadablePolishing(job)` gate before
    `ConfirmSpeakerNamesBeforeReadablePolishing(job)`.
- `MainWindowViewModel.Jobs.cs`
  - Add a dialog delegate similar to `ConfirmSpeakerNamesDialog`.
- New dialog request/result types under `Services.Dialogs`.
- New WPF dialog under `Dialogs`.
- Existing `ReviewOperationService` for all candidate decisions.
- Existing `CorrectionDraftRepository` for loading pending candidates.

## Acceptance Criteria for #72

- The dialog outcomes are defined.
- The continue/defer/cancel runner behavior is defined.
- The no-candidate auto-skip behavior is defined.
- The ordering relative to speaker name confirmation is defined.
- Candidate decisions are required to use the same persistence path as the
  existing Review panel.
