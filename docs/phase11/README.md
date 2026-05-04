# Phase 11 - Model Catalog and Deferred Model Downloads

Phase 11 starts the model-management layer that lets KoeNote remain a lightweight app while ASR and review models are selected, installed, verified, and removed later.

## Implemented Slice

- Added built-in `catalog/model-catalog.json`.
- Added catalog records and `ModelCatalogService`.
- Added database migration version 6:
  - `installed_models`
  - `installed_runtimes`
  - `model_download_jobs`
- Added local model registration and verification services.
- Added HTTPS-only download service with `.partial` temp files, progress persistence, checksum verification, and install registration.
- Added download cancellation, pause state persistence, and resume from partial files when the server supports ranged responses.
- Added offline model pack import skeleton for `.kmodelpack` ZIP files with `modelpack.json`.
- Added built-in, HTTPS remote, and local catalog loading paths.
- Added a compact Models tab in the log panel with scan, use, license, requirement, size, and forget actions.
- ASR run readiness now follows the selected ASR engine/model instead of always requiring the legacy VibeVoice model.

## Built-In Catalog Entries

```text
reazonspeech-k2-v3-ja
faster-whisper-large-v3-turbo
faster-whisper-large-v3
vibevoice-asr-q4-k
llm-jp-4-8b-thinking-q4-k-m
```

The catalog intentionally allows manual or later-confirmed downloads when a model is not safely represented by a single direct file URL.

## Validation

```powershell
dotnet test
powershell -ExecutionPolicy Bypass -File scripts/phase10/Publish-KoeNote.ps1
```

## Remaining Work

- Direct download recipes for each model source.
- Full model-management settings page.
- Local file picker and model pack picker UI.
- Runtime installation and verification beyond model registration.
- Phase 12 setup wizard integration.
