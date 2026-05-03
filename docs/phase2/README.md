# Phase 2 - ASR Worker

Phase 2 adds the VibeVoice-ASR worker boundary.

## Current Scope

Implemented in the first Phase 2 slice:

- ASR run options and result models
- `crispasr.exe` command-line argument builder
- hotword and context argument support
- raw ASR JSON normalizer for common segment shapes
- raw output and normalized segment JSON file storage
- transcript segment persistence into SQLite
- ASR failure categories
- tests for command building, JSON normalization, result storage, and DB persistence

Implemented in the UI integration slice:

- the main `Run` command now continues from ffmpeg normalization into ASR
- ASR stage progress is saved in SQLite and reflected in the bottom stage strip
- generated transcript segments replace the placeholder segment list in the center pane
- ASR success updates the job to `文字起こし完了`
- ASR failures update the job list, stage strip, latest log, and `last_error_category`
- ViewModel test hooks allow audio registration and run execution without an open file dialog

## Expected Runtime Layout

Runtime files remain local and ignored by Git:

```text
src/KoeNote.App/bin/<Configuration>/net11.0-windows/
  tools/
    crispasr.exe
  models/
    asr/
      vibevoice-asr-q4_k.gguf
```

The exact `crispasr.exe` CLI is still pending real-runtime confirmation. The current command shape is:

```text
crispasr.exe --model "<model>" --audio "<normalized.wav>" --format "json" --context "<context>" --hotword "<word>"
```

If the runtime uses different option names, update `AsrCommandBuilder`.

## Verification

Run:

```powershell
dotnet build KoeNote.slnx --configuration Release --no-restore
dotnet test tests/KoeNote.App.Tests/KoeNote.App.Tests.csproj --configuration Release --no-build
```

Automated/smoke verification performed:

- fake `crispasr.exe` + 30-second audio through the same ViewModel path: segments saved to DB and displayed in UI state
- fake `crispasr.exe` + 10-minute audio through the same ViewModel path: completes with `end_seconds = 600`
- missing `crispasr.exe`: `MissingRuntime` appears in job status, ASR stage, latest log, and DB

Real-runtime confirmation still requires placing the actual runtime files in the expected layout:

- `tools/crispasr.exe`
- `models/asr/vibevoice-asr-q4_k.gguf`
