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

Full Phase 2 completion still requires:

- placing `crispasr.exe`
- placing `vibevoice-asr-q4_k.gguf`
- running a 30-second normalized WAV through the worker
- confirming real output parses into `TranscriptSegment`
