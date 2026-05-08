# LLM Profile / Task Settings Roadmap

This note records the Phase 1 inventory and implementation plan for separating
model-wide runtime settings from task-specific LLM settings.

## Goals

- Keep runtime choices that belong to a model in one profile.
- Keep review, summary, and polishing behavior in per-task settings.
- Preserve the current setup flow and installed model records while adding a
  migration path toward explicit profiles.
- Make Bonsai, Gemma, and fallback behavior inspectable and testable without
  spreading model-specific conditionals across stage runners.

## Current Inventory

### Model Selection

- Built-in model metadata is loaded from
  `src/KoeNote.App/catalog/model-catalog.json`.
- Installed model state is stored in the `installed_models` table and accessed
  through `InstalledModelRepository`.
- Setup selection state is stored as JSON by `SetupStateService`.
- Presets currently select one ASR model and one review model. There is no
  separate summary or polishing model selection.
- Review model display in the header is derived from selected setup state and
  installed model/catalog entries.

### Review Runtime

- `ReviewStageRunner` resolves the selected review model id and model path.
- `ReviewRuntimeTuningProfiles.ForReviewModel(modelId)` returns runtime and
  task tuning in one object:
  - timeout
  - context size
  - GPU layers
  - max tokens
  - chunk segment count
  - threads / threads-batch
  - JSON schema usage
  - repair enablement
  - prompt profile
- `ReviewRunOptions` carries both runtime settings and review-specific settings.
- `ReviewCommandBuilder` builds llama.cpp arguments for review. Temperature is
  currently hard-coded to `0.1`.
- Review-specific validation and repair live in `ReviewWorker`.

### Summary Runtime

- `SummaryStageRunner` currently resolves the same review model id used by
  Review.
- Summary also uses `ReviewRuntimeTuningProfiles.ForReviewModel(modelId)`.
- `TranscriptSummaryOptions` contains runtime fields, chunk sizing, prompt
  version, generation profile, and sanitizer profile.
- `LlamaTranscriptSummaryRuntime` builds llama.cpp arguments independently from
  review. Temperature is currently hard-coded to `0.1`.
- `SummaryStageRunner.ResolveSummaryMaxTokens` clamps review max tokens down for
  summary instead of using summary-specific task settings.
- Bonsai-specific summary prompt context limits are chosen inside
  `LlamaTranscriptSummaryRuntime` by checking the model id.

### Polishing Runtime

- `TranscriptPolishingOptions` mirrors the summary option shape but has its own
  defaults.
- `LlamaTranscriptPolishingRuntime` builds llama.cpp arguments independently
  from review and summary. Temperature is currently hard-coded to `0.1`.
- Polishing is not currently wired through a separate stage runner in the same
  way as Review/Summary model selection, but it already needs the same profile
  and task-settings split.

### Runtime Resolution

- llama.cpp executable resolution is centralized in `ReviewRuntimeResolver`.
- Runtime package selection is still inferred from model/catalog compatibility
  and model id checks.
- `--no-conversation` is now used by Review, Summary, and Polishing to avoid
  chat-template conversation mode issues.

### Persistence

- Current DB migrations end at version 16.
- Existing durable LLM-related state is split across:
  - `installed_models`
  - `installed_runtimes`
  - `model_download_jobs`
  - `transcript_derivatives`
  - setup-state JSON
- There are no `llm_profiles` or `llm_task_settings` tables yet.
- Transcript derivatives already store model id, prompt version, and generation
  profile, which can serve as the first execution snapshot surface.

## Design Direction

### Runtime Profile

Introduce an internal model-wide profile type in Phase 2. It should describe
runtime behavior that is shared across tasks for a selected model.

Initial shape:

```text
LlmRuntimeProfile
  ProfileId
  ModelId
  DisplayName
  RuntimeKind
  RuntimePackageId
  ModelPath
  LlamaCompletionPath
  ContextSize
  GpuLayers
  Threads
  ThreadsBatch
  NoConversation
  OutputSanitizerProfile
  Timeout
```

Notes:

- `ModelPath` and `LlamaCompletionPath` may be resolved values rather than
  persisted values in early phases.
- `NoConversation` should default to true for llama.cpp completion usage.
- `OutputSanitizerProfile` is currently catalog/model-derived and can move into
  the resolved profile.

### Task Settings

Introduce task settings that describe behavior specific to review, summary, and
polishing.

Initial shape:

```text
LlmTaskSettings
  TaskKind
  PromptTemplateId
  PromptVersion
  GenerationProfile
  Temperature
  TopP
  TopK
  RepeatPenalty
  MaxTokens
  ChunkSegmentCount
  ChunkOverlap
  UseJsonSchema
  EnableRepair
  ValidationMode
```

Notes:

- Review should own JSON schema and repair settings.
- Summary should own chunk sizing, merge behavior, and summary validation.
- Polishing should own chunk sizing and markdown/text validation.
- Unsupported fields can remain null until a runtime uses them.

### Resolution Flow

Target flow after Phase 3:

```text
StageRunner
  -> LlmProfileResolver.Resolve(modelId)
  -> LlmTaskSettingsResolver.Resolve(profile, taskKind)
  -> Task options
  -> llama.cpp argument builder
```

This keeps stage runners responsible for orchestration and makes model/task
policy testable outside the stages.

## Proposed Phase Breakdown

### Phase 2: Internal Setting Models

- Add internal records/enums for runtime profiles and task settings.
- Add resolver interfaces or services with current behavior as defaults.
- Keep existing DB and UI unchanged.
- Add unit tests for model id to profile/task-settings resolution.

### Phase 3: Runtime Arguments via Task Settings

- Refactor Review/Summary/Polishing option creation to use resolved profile and
  task settings.
- Replace duplicated llama.cpp argument construction with a shared helper where
  practical.
- Keep output paths and existing result formats unchanged.
- Add tests that Review and Summary can differ for the same model.

### Phase 4: Presets

- Add Gemma, Bonsai, ternary Bonsai, llm-jp, and fallback presets.
- Move Bonsai checks out of runtime classes where possible.
- Add snapshot-style tests for resolved profile/task settings.

### Phase 5: Logging

- Add profile id, task kind, model id, prompt version, generation profile, and
  key runtime values to job logs.
- Prefer sanitized argument summaries over raw command strings.

### Phase 6: Persistence

- Add DB migrations for `llm_profiles` and `llm_task_settings`.
- Migrate existing setup-state selection into a default active profile.
- Preserve fallback behavior for users without migrated settings.

### Phase 7: UI

- Add a settings surface for active LLM profile and per-task settings.
- Keep advanced controls collapsed by default.
- Display Review and Summary profile/task status separately in the header.

### Phase 8: Prompt / Retry / Validation

- Add prompt template variants per model family/task.
- Add task-specific retry policies.
- Add summary validation for empty/fallback/low-structure output.

## Phase 1 Self Review

Findings:

- No production behavior changed in this phase.
- The current summary path is tightly coupled to review model selection and
  review tuning, so Phase 2 should start by introducing read-only internal
  profile/task records before changing any stage behavior.
- The same llama.cpp flags are built in three places. Phase 3 should remove this
  duplication only after Phase 2 tests pin the current behavior.
- DB persistence should wait until after runtime behavior is proven through
  internal resolvers; adding migrations before the shape settles would create
  unnecessary compatibility weight.

Residual risks:

- Some catalog display strings are currently mojibake in the checked-in JSON.
  This roadmap does not address catalog localization or encoding cleanup.
- Real-world Bonsai/Gemma values still need empirical tuning. Phase 4 should
  begin with conservative presets and log enough detail to compare hosts.

## Phase 2 Implementation Notes

Implemented internal-only setting records and resolvers. No DB, UI, or runtime
stage behavior has been switched over yet.

Added records:

- `LlmRuntimeProfile`
- `LlmTaskSettings`
- `LlmTaskKind`

Added resolvers:

- `LlmProfileResolver`
- `LlmTaskSettingsResolver`

Current behavior pinned by tests:

- Gemma resolves to the standard llama.cpp runtime package and current review
  defaults.
- Ternary Bonsai resolves to the ternary llama.cpp runtime package and bounded
  CPU defaults.
- Installed review model paths are preferred when the registered file exists.
- Review task settings keep JSON schema / repair behavior.
- Summary task settings keep the current max-token clamp.
- Polishing task settings keep the current review-derived max-token and chunk
  defaults.

Phase 2 self-review findings:

- The new resolvers intentionally mirror current behavior, including Summary's
  dependency on review-derived tuning. This is temporary and will be loosened in
  Phase 3 and Phase 4.
- `LlmTaskSettingsResolver` still derives task defaults from
  `ReviewRuntimeTuningProfiles`; this is acceptable for Phase 2 because the goal
  is to create an internal compatibility layer before behavior changes.
- `LlmProfileResolver` uses the current fallback model path behavior for
  missing installed review models. Phase 6 persistence should revisit whether a
  missing selected model should be represented as an invalid profile instead of
  silently falling back.

## Phase 3 Implementation Notes

Connected the internal profile/task-settings layer to the runtime option flow.

Implemented:

- Added `LlamaCompletionArgumentBuilder` as the shared llama.cpp completion
  argument builder.
- Extended Review, Summary, and Polishing option records with task-level
  generation fields:
  - temperature
  - top-p
  - top-k
  - repeat penalty
  - no-conversation
- Updated Review, Summary, and Polishing runtimes to build llama.cpp arguments
  from options instead of hard-coded temperature and duplicated argument lists.
- Updated `ReviewStageRunner` to resolve `LlmRuntimeProfile` and Review
  `LlmTaskSettings`, then map those values into `ReviewRunOptions`.
- Updated `SummaryStageRunner` to resolve `LlmRuntimeProfile` and Summary
  `LlmTaskSettings`, then map those values into `TranscriptSummaryOptions`.
- Preserved the existing default review model fallback by letting
  `LlmProfileResolver` use an installed `llm-jp-4-8b-thinking-q4-k-m` path when
  the selected review model is unavailable.
- Removed obsolete private helpers from Review/Summary stage runners after the
  profile resolver took ownership of model path, runtime path, timeout, context,
  GPU/thread, sanitizer, and no-conversation values.

Current behavior intentionally preserved:

- Review and Polishing keep the existing model max-token default.
- Summary keeps the existing summary max-token clamp.
- Review keeps JSON schema / repair behavior for standard models and compact
  no-schema behavior for ternary Bonsai.
- Runtime execution still uses the selected review model for Review and Summary.

Phase 3 self-review findings:

- Summary and Review now consume separate task settings, but the settings are
  still derived from the compatibility resolver introduced in Phase 2. Phase 4
  should replace those derived values with explicit model-family presets.
- Polishing has task-aware option fields and shared argument generation, but it
  is not yet stage-runner wired because current orchestration does not expose a
  polishing stage runner equivalent to Review/Summary.

## Phase 4 Implementation Notes

Added explicit in-code presets for model families while keeping the current DB
and UI unchanged.

Implemented:

- Added `LlmPresetCatalog`.
- Added `LlmRuntimePreset`.
- Added `ModelFamily` to `LlmRuntimeProfile` so task settings can use catalog
  family metadata instead of inferring only from model id.
- Moved runtime defaults for Gemma, Bonsai, Ternary Bonsai, llm-jp, and fallback
  into explicit runtime presets.
- Moved Review, Summary, and Polishing task defaults into explicit task presets.
- Kept Ternary Bonsai CPU-bounded settings.
- Added conservative Bonsai Q1 Summary/Polishing task presets:
  - Summary uses `bonsai-summary-conservative`, 512 max tokens, 40-segment chunks.
  - Polishing uses `bonsai-polishing-conservative`, 2048 max tokens,
    40-segment chunks.
- Added snapshot-style tests for runtime preset selection and task setting
  selection.

Phase 4 self-review findings:

- The Bonsai Q1 Summary/Polishing values are intentionally conservative but not
  yet empirically optimized. Phase 5 logging should make host-to-host
  comparison easier before deeper tuning.
- `ReviewRuntimeTuningProfiles` still exists for older direct tests and as a
  compatibility reference, but the LLM profile/task resolvers no longer depend
  on it.

## Phase 5 Implementation Notes

Added LLM execution logging for Review and Summary stages.

Implemented:

- Added `LlmExecutionLogFormatter`.
- Review stage now writes a job log event before running the LLM runtime.
- Summary stage now writes a job log event before running the LLM runtime.
- The log event includes:
  - task kind
  - profile id
  - model id
  - model family
  - runtime kind/package
  - context size
  - GPU layers
  - thread settings
  - timeout
  - no-conversation mode
  - prompt template/version
  - generation profile
  - temperature/top-p/top-k/repeat penalty
  - max tokens
  - chunk segment count/overlap
  - JSON schema and repair flags
  - validation mode
  - output sanitizer profile
- Added formatter unit coverage and StageRunner-level tests that verify the LLM
  execution summary reaches `job_log_events`.

Phase 5 self-review findings:

- The log deliberately avoids raw prompt text and raw command-line strings. It
  records sanitized settings only.
- Logging is currently wired for Review and Summary because those are the
  exposed stage runners. Polishing still needs a stage-level integration point
  before it can emit the same event from orchestration.

## Phase 6 Implementation Notes

Added DB persistence for LLM profiles and task settings.

Implemented:

- Added database migration 17.
- Added `llm_profiles`.
- Added `llm_task_settings`.
- Added indexes for profile model lookup, active profile lookup, and task
  settings by profile.
- Added `LlmSettingsRepository`.
- Added persisted wrapper records for runtime profiles and task settings.
- Added `LlmSettingsSeedService`.
- App startup now seeds an active LLM profile from setup-state when no active
  profile exists yet.
- The seed service stores Review, Summary, and Polishing task settings for the
  active profile.

Current behavior:

- Runtime execution still uses the in-code resolver path. Persisted settings are
  available for Phase 7 UI work and later execution switching.
- Seeding is non-destructive. If an active profile already exists, setup-state
  does not overwrite it.

Phase 6 self-review findings:

- Persisted profiles currently include resolved model/runtime paths. This is
  useful for execution snapshots, but Phase 7/8 should treat those paths as
  host-local values and refresh them when the selected model changes.
- No foreign keys were added yet because existing migrations do not consistently
  enforce them. Repository-level tests cover the current behavior.

## Phase 7 Implementation Notes

Added a read-only UI surface for persisted LLM profile and task settings.

Implemented:

- Added `LlmSettingsDisplayService` and `LlmSettingsDisplaySnapshot`.
- Header now shows Review and Summary task status separately.
- Header tooltips show the detailed Review/Summary task settings.
- Settings tab now shows the active LLM profile.
- Settings tab now has a collapsed `Task settings` expander for Review,
  Summary, and Polishing settings.
- Setup model changes can resync the active setup-derived LLM profile so the
  header does not remain stale after choosing a different Review model.

Phase 7 self-review findings:

- The settings surface is intentionally read-only. Editable task controls are
  deferred until the prompt/retry/validation work has settled enough to avoid
  exposing unstable knobs.
- Setup synchronization only runs for explicit setup model/preset changes.
  Plain startup still preserves an existing active profile.
- The header uses short task-generation summaries to avoid crowding the toolbar;
  the full task detail is available in the tooltip and Settings tab.

## Phase 8 Implementation Notes

Added task-aware prompt template selection, summary retry, and summary output
validation.

Implemented:

- Summary task presets now choose model-family prompt templates:
  - Gemma: `gemma-structured`
  - Bonsai / ternary Bonsai: `bonsai-compact`
  - llm-jp: `llm-jp-structured`
- Summary presets now use `markdown_summary_sections` validation.
- `TranscriptSummaryOptions` now carries prompt template id, validation mode,
  and max attempts.
- `TranscriptSummaryPromptBuilder` accepts prompt template id and adds
  model-specific output discipline for Gemma and llm-jp while preserving Bonsai
  compact no-think prompts.
- Added `TranscriptSummaryValidator`.
- Summary output validation now rejects:
  - empty output
  - `<think>` blocks
  - fenced code blocks
  - prompt echoes / instruction echoes
  - final summaries without enough Markdown sections
- Summary chunk generation and final merge now retry bounded attempts before
  falling back.
- Bonsai summaries get 3 attempts; other model families get 2 attempts.

Phase 8 self-review findings:

- Validation is stricter for final merged summaries than for intermediate
  chunks. This keeps chunk retries useful without requiring every chunk to have
  a full final-summary shape.
- Retry is bounded by `MaxAttempts`; no loop can run unbounded.
- If all retry attempts fail validation, the existing fallback derivative path
  is still used and the validation reason is included in the fallback content.
- Review JSON repair was left unchanged because it already has a separate repair
  path and schema validation.

Residual risks:

- Prompt template text is still in code. A later phase could move templates to
  files or persisted task settings once editing UI is introduced.
- The validator is intentionally conservative. Real Gemma/Bonsai outputs may
  reveal additional low-quality patterns worth adding.
