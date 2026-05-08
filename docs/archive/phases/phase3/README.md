# Phase 3 - Review Worker

Phase 3 adds the local LLM review worker boundary for generating `CorrectionDraft` records from ASR transcript segments.

## Current Scope

Implemented in this slice:

- `llama-completion.exe` command-line argument builder
- JSON-only review prompt builder
- one-shot JSON repair retry when the first review output is not parseable JSON
- review raw output and normalized draft JSON storage
- correction draft normalization with confidence threshold
- conservative filtering for candidates that do not match the source segment or appear to add unsupported content
- `correction_drafts` persistence
- `transcript_segments.review_state` update for segments with drafts
- `jobs.unreviewed_draft_count` update
- main run flow connection: ffmpeg -> ASR -> review

## Expected Runtime Layout

Runtime files remain local and ignored by Git:

```text
src/KoeNote.App/bin/<Configuration>/net11.0-windows/
  tools/
    llama-completion.exe
  models/
    review/
      llm-jp-4-8B-thinking-Q4_K_M.gguf
```

The exact `llama-completion.exe` CLI still needs real-runtime confirmation. The current command shape is:

```text
llama-completion.exe --model "<model>" --prompt "<prompt>" --ctx-size 4096 --n-predict 1024 --temp 0.1
```

If the runtime uses different option names, update `ReviewCommandBuilder`.

## Verification

Run:

```powershell
dotnet build KoeNote.slnx --configuration Release --no-restore
dotnet test tests/KoeNote.App.Tests/KoeNote.App.Tests.csproj --configuration Release --no-build
```

Smoke verification performed with fake runtimes:

- UI-equivalent ViewModel path generated a correction draft from ASR text
- job status became `レビュー待ち`
- review stage became `成功`
- right pane review fields reflected the generated draft
- 20 segment JSON normalization produced 19 usable drafts, matching the 95% parse gate shape

Real-runtime confirmation still requires placing the actual runtime files and measuring real output quality.
