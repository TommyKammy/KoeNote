# Phase 12 - First-run setup wizard

Phase 12 introduces a guided setup surface on top of the Phase 11 model catalog. The app still starts without models, but transcription runs remain disabled until setup is completed.

## Implemented slice

- Setup state is stored at `%APPDATA%\KoeNote\setup-state.json`.
- Smoke reports are written to `%APPDATA%\KoeNote\setup_report.json`.
- The setup flow tracks Welcome, environment check, setup mode, ASR model, review LLM, storage, license, install/import, smoke test, and complete steps.
- ASR choices come from the model catalog and include ReazonSpeech v3 k2 and faster-whisper families.
- Review LLM choices come from the same catalog.
- The setup tab exposes recommended selection, license acceptance, online download, local registration, offline pack import, smoke check, and completion actions.
- Smoke checks verify ffmpeg, selected installed ASR model, selected installed review model, license acceptance, and storage root.
- Installed model audit displays checksum, manifest, and license status for selected models.
- Run is disabled until setup is completed and runtime assets are ready.

## Install path handoff

The wizard is wired to Phase 11 model services:

- Online download: `ModelDownloadService`
- Local model registration: `ModelInstallService`
- Offline model pack import: `ModelPackImportService`

The current UI accepts paths directly for local files and `.kmodelpack` imports. A native file picker can be layered onto the same commands later without changing the setup contract.

## Verification

Run:

```powershell
dotnet test
```

The Phase 12 tests cover default incomplete state, catalog choices, failure report generation, local registration audit, offline model pack import, download failure safety, and completion after verified model installation.
