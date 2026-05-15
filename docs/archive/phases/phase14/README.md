# Phase 14 - Polishing, summarization, and minutes specification

Phase 14 extends KoeNote beyond raw transcription. The goal is to keep the ASR
transcript as the source record while generating useful derived documents:
polished transcript, summary, and meeting minutes.

This document is the Phase S0 specification. It fixes product language,
generation boundaries, safety rules, and implementation contracts before data
model or UI work begins.

## Product terms

- Raw transcript: the direct ASR output plus user edits applied to transcript
  segments. This is the source record shown to the user and must remain
  available.
- ASR baseline: the first transcript produced by ASR before user edits. It is
  useful for audit and recovery when available, but Phase 14 derived documents
  should be generated from the current raw transcript.
- Polished transcript: a readable version of the raw transcript. It may remove
  filler, repeated words, and obvious speech disfluency, but it must not add
  new facts or change the speaker's intent.
- Summary: a compressed, structured view of the transcript. It extracts the
  important points from the source and may omit detail.
- Minutes: a practical meeting document built from the transcript and summary.
  It contains overview, topics, decisions, action items, open questions, and
  notes.
- Derived document: any generated artifact that depends on transcript content.
  Polished transcript, summary, and minutes are derived documents.

## Non-goals

- KoeNote will not replace the raw transcript with generated text.
- KoeNote will not treat generated text as a source of truth for later factual
  extraction without keeping a link back to the raw transcript.
- KoeNote will not silently invent facts, participants, owners, or deadlines.
- KoeNote will not require cloud LLM services for Phase 14.
- KoeNote will not make generated minutes the only export format.
- KoeNote will not implement collaborative editing in this phase.

## Output modes

### Polished transcript

Purpose:

- Make speech easier to read as prose.
- Preserve meaning and speaker intent.
- Keep enough structure to trace back to the source.

Allowed transformations:

- Add punctuation and paragraph breaks.
- Remove fillers such as "um" and repeated self-corrections when they do not
  affect meaning.
- Normalize light spoken-language redundancy.
- Keep speaker labels when speaker data exists.
- Keep timestamps when the selected display/export mode asks for them.

Disallowed transformations:

- Adding claims not present in the transcript.
- Replacing uncertain terms with confident guesses.
- Changing decisions, numbers, names, dates, prices, or deadlines.
- Merging different speakers into one speaker unless the input lacks speaker
  data.

Default output shape:

```text
[00:00:12] Speaker 1: Polished utterance...
[00:00:25] Speaker 2: Polished utterance...
```

### Summary

Purpose:

- Let the user understand the content quickly.
- Provide structured extraction from the transcript.

Default sections:

- Overview
- Key points
- Decisions
- Action items
- Open questions
- Keywords

Rules:

- Prefer polished transcript as the reading input when available, but keep raw
  transcript references for decisions and action items.
- Fall back to raw transcript when polished text does not exist.
- Mark missing owners, due dates, or participants as "Unspecified".
- Do not include action items unless the source implies an action.
- Do not include decisions unless the source states or clearly implies a
  decision.

### Minutes

Purpose:

- Produce a practical document the user can copy or export.

Default Markdown shape:

```markdown
# Minutes

## Overview

## Topics

## Decisions

## Action Items

| Item | Owner | Due | Source |
| --- | --- | --- | --- |

## Open Questions

## Notes
```

Rules:

- Use summary data when available.
- Keep access to the raw transcript or source references when generating
  decisions and action items.
- Use transcript references for important decisions and action items when
  timestamps exist.
- Keep generated text editable after creation.
- Use "Unspecified" instead of guessing owner or due date.

## Generation profiles

Phase 14 uses existing model quality labels and maps them to generation
behavior.

| Label | Intended behavior |
| --- | --- |
| Lightweight | Short chunks, conservative polishing, short summaries. |
| Recommended | Standard chunks, balanced polishing, normal summaries. |
| High accuracy | Larger context, detailed summaries, stronger consistency checks. |
| Experimental | More flexible formatting and minutes generation. |

The first implementation should expose only safe defaults. Advanced knobs can
remain internal until the workflow is stable.

## Default settings

Recommended defaults:

- Polishing strength: Standard
- Summary length: Medium
- Preserve speaker labels: On
- Preserve timestamps: On for transcript views, optional for exports
- Extract action items: On
- Extract decisions: On
- Extract open questions: On
- Minutes template: Standard meeting minutes
- Input priority: polished transcript, then raw transcript

Polishing strength:

- Light: punctuation and paragraphs only.
- Standard: punctuation, paragraphs, filler removal, repeated-word cleanup.
- Strong: more prose-like cleanup, still no meaning changes.

The default is Standard. Strong should be opt-in because it has a higher risk
of changing nuance.

## Chunking contract

Long transcripts must be processed in chunks.

Chunking requirements:

- Keep segment order stable.
- Keep speaker and timestamp metadata with each chunk.
- Keep source segment ids or an equivalent source range for each chunk.
- Use overlap only when needed for continuity.
- Store prompt version and model id for every generated result.
- Never overwrite raw transcript segments.
- Store enough chunk metadata to retry a failed chunk without regenerating the
  whole job.

Recommended pipeline:

1. Build source chunks from transcript segments.
2. Generate polished chunks.
3. Merge polished chunks into a polished transcript.
4. Generate chunk summaries from polished text when available.
5. Merge chunk summaries into a final summary.
6. Generate minutes from the final summary and source references.

## Safety and validation

Before saving a derived document:

- Reject empty output.
- Reject output that is much shorter than expected unless the source is short.
- Reject malformed structured output when a JSON contract is used.
- Record model id, prompt version, source transcript hash, created time, and
  generation status.
- Store error details for retry and support.

Hallucination controls:

- Prompts must say: "Do not add facts that are not present in the transcript."
- Prompts must say: "Use Unspecified when owner, date, or participant is not
  present."
- Decisions and action items should include timestamp/source references when
  available.

## Source and invalidation policy

Derived documents are valid only for the transcript version they were generated
from.

Requirements:

- Compute a stable source transcript hash from segment id, start time, end time,
  speaker label, and text.
- Store the source transcript hash on every derived document.
- If the raw transcript is edited after generation, mark existing derived
  documents as stale instead of deleting them.
- If the selected source kind changes, for example raw transcript to polished
  transcript, store that source kind with the derived document.
- The UI may show stale derived documents, but it must indicate that they were
  generated from an older transcript version before allowing export.

## Data contract for later phases

Phase S1 should add storage for derived documents with at least:

- job id
- kind: polished, summary, minutes
- content format: plain_text, markdown, json
- content
- source kind: raw, polished, summary
- source transcript hash
- source segment range or source chunk ids
- model id
- prompt version
- generation profile
- created at
- updated at
- status
- error message

The app should allow multiple generations per job over time, but the UI can
start by showing the latest successful result for each kind.

For chunked generation, S1 may use either a separate chunk table or a structured
metadata field. The important contract is that each generated chunk can be
matched back to source segment ids and retried independently.

## Phase S1 implemented slice

Phase S1 adds the storage foundation only. It does not generate polished text,
summaries, or minutes yet.

Implemented database objects:

- `transcript_derivatives`
- `transcript_derivative_chunks`
- indexes for latest result lookup and chunk ordering

Implemented service contract:

- Save and read derived documents.
- Save and read generated chunks.
- Read the latest successful derived document for a job and kind.
- Compute a stable raw transcript hash from current transcript segments.
- Detect whether a raw-source derived document is stale.
- Mark outdated raw-source derived documents as stale for a job.

The implementation entry point is `TranscriptDerivativeRepository`.

## Phase S2 implemented slice

Phase S2 adds the first polishing generation service. It still does not expose
the feature in the UI.

Implemented service objects:

- `TranscriptPolishingPromptBuilder`
- `TranscriptPolishingService`
- `LlamaTranscriptPolishingRuntime`

Implemented behavior:

- Read current transcript segments for a job.
- Build stable source transcript hash before generation.
- Split long transcripts into segment-count chunks.
- Generate polished plain text per chunk through a runtime abstraction.
- Save the merged polished transcript as a `polished` derivative.
- Save each polished chunk with source segment ids and source time range.
- Store failed empty-output generations as failed derivatives.

The runtime abstraction allows tests and future engines to supply polished
chunks without starting `llama-completion`.

## Phase S3 readiness decisions

Phase S3 will implement the summary engine before adding UI controls or minutes
generation. The implementation should keep Summary separate from Minutes so
that the first summary slice can be tested and tuned independently.

Input selection:

- Prefer the latest successful `polished` derivative when it exists and is not
  stale.
- Fall back to the current raw transcript when no usable polished derivative
  exists.
- Keep raw transcript segment ids, speaker labels, and timestamps available for
  decisions and action items even when the readable input is polished text.

Output format:

- Store the final summary as `kind = summary`.
- Use `content_format = markdown` for the first implementation.
- Use these default sections in order:
  - Overview
  - Key points
  - Decisions
  - Action items
  - Open questions
  - Keywords

Generation shape:

- Use a two-step pipeline for long transcripts: chunk summaries first, then a
  final merged summary.
- Store each chunk summary in `transcript_derivative_chunks` with source segment
  ids and source time range.
- Store the final merged summary in `transcript_derivatives`.
- Use `source_kind = polished` when the source is a polished derivative; use
  `source_kind = raw` when falling back to raw transcript.
- Store the source transcript hash for both chunk and final records.

Safety rules:

- The summary prompt must say not to add facts that are not present in the
  transcript.
- Owners, dates, participants, and deadlines must be `Unspecified` when the
  transcript does not provide them.
- Decisions and action items must not be invented. Include them only when the
  source states or clearly implies them.
- Prefer source references for decisions and action items when timestamps are
  available.

Validation and failure handling:

- Reject empty output.
- Reject malformed structured output when a structured contract is used.
- Reject extremely short summaries for non-short transcripts unless the runtime
  explicitly reports that there was little content to summarize.
- Save failed generations as failed summary derivatives with error details so
  the user can retry later.

Phase S3 planned service slice:

- `TranscriptSummaryPromptBuilder`
- `TranscriptSummaryService`
- `ITranscriptSummaryRuntime`
- `LlamaTranscriptSummaryRuntime`
- unit tests for input selection, chunking, final merge, stale source behavior,
  and failed-output persistence

Phase S3 will not add the main UI tabs, copy/export buttons, or minutes
generation. Those remain later-phase work after the summary service contract is
stable.

## Phase S3 implemented slice

Phase S3 adds the summary generation service. It still does not expose the
feature in the UI and it does not generate minutes.

Implemented service objects:

- `TranscriptSummaryPromptBuilder`
- `TranscriptSummaryService`
- `LlamaTranscriptSummaryRuntime`

Implemented behavior:

- Read current transcript segments for a job.
- Prefer the latest successful non-stale `polished` derivative as summary
  input.
- Fall back to raw transcript segments when no usable polished derivative
  exists.
- Split summary input into chunks while preserving source segment ids and time
  ranges.
- Generate Markdown chunk summaries through a runtime abstraction.
- Merge chunk summaries into a final Markdown summary.
- Save the final result as a `summary` derivative.
- Save each chunk summary in `transcript_derivative_chunks`.
- Store failed empty or invalid generations as failed summary derivatives.

The runtime abstraction allows tests and future engines to supply summary
chunks without starting `llama-completion`.

## UI contract for later phases

Initial UI should avoid a crowded tool surface.

Recommended first slice:

- Add view tabs: Raw, Polished, Summary, Minutes.
- Add one primary action for the selected missing derived document.
- Show generation status and errors inline.
- Keep Copy and Export actions close to the generated output.
- Keep raw transcript editing unchanged.

## Export contract

Exports should support:

- Raw transcript
- Polished transcript
- Summary
- Minutes

Priority:

1. Markdown
2. TXT
3. DOCX
4. JSON

Markdown should be the first structured output for summary and minutes.

## Phase S0 acceptance criteria

Phase S0 is complete when:

- The product terms in this document are accepted as the implementation
  vocabulary.
- Polished transcript, summary, and minutes have separate boundaries.
- Default settings and safety rules are fixed.
- Source hashing and stale-result behavior are defined.
- Chunk metadata is sufficient for source traceability and retry.
- Later phases can implement storage, generation, UI, and export without
  redefining the feature.
