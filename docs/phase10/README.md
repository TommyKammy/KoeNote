# Phase 10 - Packaging and First Run

Phase 10 prepares KoeNote for external beta distribution after the review, memory, evaluation, and export paths are proven.

## Goal

Create a local-first Windows distribution that can be installed and smoke-tested without hand-copying app files.

## Pre-Implementation Gate

Before starting packaging work, keep the UI and runtime surface beta-readable:

- main navigation, stage labels, and operation labels are not mojibake
- review/export/edit flows keep their current regression coverage green
- `origin/main` is clean and tests pass
- Phase 10 changes stay focused on packaging, first-run checks, and smoke-testability

## Scope

- choose installer shape
- support self-contained app distribution
- define model pack layout
- add license manifest for app, tools, and models
- add first-run runtime checks
- add first-run model/tool location checks
- add offline smoke test
- document preview .NET runtime implications

## Runtime Policy

Development may continue on .NET 11 preview. For external beta, prefer self-contained distribution to reduce runtime setup friction. Revisit .NET 10 LTS fallback or .NET 11 stable once release timing is clear.

## Out Of Scope

- beta feedback workflow
- quality benchmark expansion
- new model support

## Completion Criteria

- A clean Windows machine can launch the packaged app.
- Missing tools/models are reported with actionable UI.
- License manifest is included.
- Offline smoke test covers startup, sample import, review screen, and export path.

## Initial Implementation Slices

1. Packaging shape and publish profile
2. First-run runtime/tool/model checks
3. License manifest and distribution layout docs
4. Offline smoke-test command/script
5. Final UI pass for first-run messages

## Implementation Start

Added the first Phase 10 packaging and first-run slice:

- `win-x64-self-contained` publish profile
- `scripts/phase10/Publish-KoeNote.ps1`
- `scripts/phase10/Test-OfflineSmoke.ps1`
- preview distribution README
- preview license manifest
- publish output placeholders for `tools`, `models/asr`, and `models/review`
- first-run status summary in the app status bar
- required runtime asset checks for ASR/review tools and models
- optional .NET CLI check because the beta app is published self-contained

Validated with:

```powershell
dotnet test
powershell -ExecutionPolicy Bypass -File scripts\phase10\Publish-KoeNote.ps1
powershell -ExecutionPolicy Bypass -File scripts\phase10\Test-OfflineSmoke.ps1
```
