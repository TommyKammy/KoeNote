# Phase 10 - Lightweight Core Packaging

Phase 10 prepares KoeNote for external beta distribution as a lightweight Windows desktop app.

## Goal

Create a self-contained KoeNote Core package that can launch without bundled ASR or review LLM model binaries.

## Scope

- self-contained `win-x64` app publish
- lightweight core setup payload
- distribution layout documentation
- license manifest for the core package
- first-run status summary for missing runtime/model assets
- offline layout smoke test

## Out Of Scope

- ASR adapter architecture
- Model Catalog / Download Manager
- first-run setup wizard
- MSI / Windows Apps uninstaller
- beta feedback workflow
- full offline model bundle

Those move to Phase 10.5 through Phase 14.

## Core Package Layout

```text
KoeNote.App.exe
README.distribution.md
licenses/license-manifest.json
tools/ffmpeg.exe
tools/README-runtime-tools-not-included.txt
models/README-models-not-included.txt
models/asr/README-ASR-models-not-included.txt
models/review/README-review-models-not-included.txt
samples/README-sample-audio.txt
samples/koenote-smoke-1s.wav
```

ASR and review model binaries are intentionally excluded. ASR/review runtimes are also excluded by default, except for `ffmpeg.exe` when it is available on the build machine.

For local developer checks that need the legacy ASR/review native tools copied into the publish folder, pass `-IncludeLegacyRuntimeTools` to the publish script.

## Commands

```powershell
dotnet test
powershell -ExecutionPolicy Bypass -File scripts\phase10\Publish-KoeNote.ps1
powershell -ExecutionPolicy Bypass -File scripts\phase10\Test-OfflineSmoke.ps1
powershell -ExecutionPolicy Bypass -File scripts\phase10\New-InstallerPackages.ps1
powershell -ExecutionPolicy Bypass -File scripts\phase10\Test-CoreUiSmoke.ps1
```

See `docs/phase10/UI_SMOKE.md` for the manual clean-environment UI checklist.

## Runtime Policy

Development may continue on .NET 11 preview. For external beta, keep the app self-contained to reduce runtime setup friction. Revisit .NET 10 LTS fallback or .NET 11 stable once release timing is clear.

## Completion Criteria

- A clean Windows machine can launch the packaged app.
- Missing ASR/review runtimes and models are reported with actionable status.
- The run command is disabled until required runtime/model assets exist.
- License manifest is included.
- Core setup does not include ASR or review model binaries.
- Offline smoke test validates the core package layout.
