# Phase 1 - Windows Runtime Foundation

Phase 1 builds the desktop application foundation for KoeNote.

## Current Scope

Implemented in the first Phase 1 slice:

- .NET 11 preview WPF application project
- `.slnx` solution
- NuGet source configuration for package restore
- SQLite dependency through `Microsoft.Data.Sqlite`
- `%APPDATA%/KoeNote` directory initialization
- `%LOCALAPPDATA%/KoeNote/logs` directory initialization
- initial `jobs.sqlite` schema
- environment/tool status checks
- external process runner abstraction
- first shell UI matching the target layout:
  - top offline/model/tool status bar
  - left job list
  - center transcript segment table
  - right review and environment panes
  - bottom stage/status strip

## Verification

Run:

```powershell
dotnet restore KoeNote.slnx
dotnet build KoeNote.slnx --configuration Release --no-restore
```

Expected result:

- build succeeds on Windows with .NET `11.0.100-preview.3`
- no warnings or errors, apart from the informational .NET preview support message

## Remaining Phase 1 Work

- wire real audio file selection into job creation
- persist real jobs instead of sample view-model data
- add ffmpeg version check through the shared process runner
- save worker stdout/stderr into job logs
- implement cancel as process-tree termination
- add simple GPU detail parsing instead of only command presence
- add first app smoke test once the UI shell is testable without launching WPF
