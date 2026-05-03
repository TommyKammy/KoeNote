# Phase 4 - Desktop UI

Phase 4 moves the WPF shell closer to the target KoeNote UI.

## Implemented In This Slice

- two-level top area:
  - status row for Offline/GPU/ASR/review model
  - toolbar row for import, run, cancel placeholder, exports, settings
- drag-and-drop audio registration on the main window
- persisted job restore on startup
- left job list with search field placeholder, progress, updated time, and unreviewed count
- center transcript segment grid with speaker filter/search placeholders
- right pane tabs:
  - job info
  - review
  - logs
  - settings/environment check
- bottom stage strip with progress bars
- bottom latest log grid
- selected job log loading

## Verification

Run:

```powershell
dotnet build KoeNote.slnx --configuration Release --no-restore
dotnet test tests/KoeNote.App.Tests/KoeNote.App.Tests.csproj --configuration Release --no-build
```

Additional checks performed:

- WPF startup smoke
- Phase 4 shell screenshot generated at `artifacts/phase4-shell-screenshot-v2.png`
- repository tests cover persisted job restore and selected job log restore

## Remaining Work

- real search/filter behavior for job and segment grids
- functional cancel command
- export command implementations
- persisted selected UI state
- job delete/rename/retry/open export actions
- speaker rename UI
- real context/hotword persistence and use in ASR
