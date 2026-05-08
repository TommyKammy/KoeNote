# Phase 4 - Desktop UI

Phase 4 moves the WPF shell closer to the target KoeNote UI.

## Implemented In This Slice

- two-level top area:
  - status row for Offline/GPU/ASR/review model
  - toolbar row for import, run, cancel placeholder, exports, settings
- drag-and-drop audio registration on the main window
- persisted job restore on startup
- left job list with live search, progress, updated time, and unreviewed count
- center transcript segment grid with live text search and speaker filter
- cancel button wired to the active preprocess/ASR/review worker cancellation token
- context and hotword inputs persisted in SQLite, restored on startup, and passed to ASR runs
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
- repository tests cover persisted job restore, selected job log restore, and cancelled job state
- external process tests cover cancellation-token process termination
- ASR settings tests cover DB persistence, hotword parsing, and ViewModel restore

## Remaining Work

- export command implementations
- persisted selected UI state
- job delete/rename/retry/open export actions
- speaker rename UI
