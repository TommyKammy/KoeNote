# Phase 5 - Safe Review Decisions

Phase 5 turns review candidates into a safe, explicit decision workflow. It deliberately does not include long-term learning, export, or packaging work.

## Goal

Every accept, reject, and manual correction operation must target exactly one `CorrectionDraft`, update the transcript predictably, and move the reviewer to the next pending candidate without losing the original ASR text.

## Scope

- introduce `SelectedCorrectionDraftId` as the only target for review actions
- build a pending `ReviewQueue` ordered by segment time, issue severity, and confidence
- synchronize center transcript selection and right-pane draft selection
- disable review actions when no draft is selected
- add previous/next draft navigation
- add inline diff display for Japanese text
- wire accept/reject/manual correction buttons to `ReviewOperationService`
- prevent double-submit while a review operation is running
- refresh unreviewed counts after each operation
- advance to the next pending draft after success
- preserve `raw_text` and `normalized_text` on every operation

## Final Text Rules

| Operation | Draft status | Segment `final_text` | Segment review state |
| --- | --- | --- | --- |
| Accept | `accepted` | `suggested_text` | `reviewed` when all segment drafts are resolved |
| Reject | `rejected` | unchanged | `reviewed` when all segment drafts are resolved |
| Manual correction | `edited` | user-entered text | `reviewed` when all segment drafts are resolved |

Rejecting a draft must not force `final_text`. Export fallback should continue to use `normalized_text`, then `raw_text`.

## Diff Display

Start with an inline diff, not a side-by-side diff, because the right panel is narrow.

```text
この仕様はサーバーの [ミギワ -> 右側] で処理します。
```

Implementation notes:

- add a small `TextDiffService`
- begin with character-level LCS
- fold long unchanged runs when needed
- keep diff tokens renderable by WPF without model-specific UI code

Suggested model:

```csharp
public sealed record DiffToken(string Text, DiffKind Kind);

public enum DiffKind
{
    Equal,
    Deleted,
    Inserted,
    Replaced
}
```

## Out Of Scope

- one-step undo
- segment direct edit outside a selected draft
- speaker alias management
- correction memory / learned terms
- export command implementation
- installer or model packaging

## Completion Criteria

- `SelectedCorrectionDraftId` is always the only operation target.
- Central segment selection and right-pane draft selection stay synchronized.
- Accept/reject/manual correction are transaction-backed and refresh UI state.
- Reject leaves `final_text` unchanged.
- Manual correction updates `final_text` while preserving original transcript text.
- Inline diff makes Japanese changes visible.
- Commands are disabled during execution and when no draft is selected.
- Tests cover multiple drafts per segment, filtered queues, reject fallback, and next-draft navigation.
