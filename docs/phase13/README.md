# Phase 13 - MSI installer and standard uninstall

Phase 13 moves KoeNote from the Phase 10 preview installer to a lightweight WiX/MSI package.

## Scope

- Install scope: per-user by default.
- Install location: `%LOCALAPPDATA%\Programs\KoeNote`.
- Windows Apps management registration is provided by MSI.
- Start Menu shortcuts:
  - `KoeNote`
  - `KoeNote Cleanup`
- Core package excludes model binaries. ASR, review, and diarization runtime payloads remain setup-time installs.

The MSI removes the application payload on uninstall. KoeNote user data is intentionally kept unless the user explicitly chooses cleanup.

## Build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\phase13\Build-KoeNoteMsi.ps1 `
  -Configuration Release `
  -RuntimeIdentifier win-x64 `
  -ProductVersion 0.13.0 `
  -Publisher "KoeNote Project"
```

The script:

1. Publishes `KoeNote.App`.
2. Publishes `KoeNoteCleanup.exe` into the same payload directory.
3. Generates `src\KoeNote.Installer\PayloadFiles.wxs` from the publish payload.
4. Builds `artifacts\msi\KoeNote.msi`.

## Uninstall policy

Removed by MSI:

- `%LOCALAPPDATA%\Programs\KoeNote`
- KoeNote Start Menu shortcuts

Kept by default:

- `%APPDATA%\KoeNote\jobs.sqlite`
- `%APPDATA%\KoeNote\jobs`
- `%APPDATA%\KoeNote\setup-state.json`
- `%APPDATA%\KoeNote\setup_report.json`
- `%APPDATA%\KoeNote\settings.json`
- `%LOCALAPPDATA%\KoeNote\models`
- `%ProgramData%\KoeNote\models`

Removed by default in quiet cleanup:

- `%LOCALAPPDATA%\KoeNote\logs`
- `%LOCALAPPDATA%\KoeNote\model-downloads`
- `%LOCALAPPDATA%\KoeNote\python-packages`

Explicit cleanup options:

```powershell
KoeNoteCleanup.exe --models
KoeNoteCleanup.exe --machine-models
KoeNoteCleanup.exe --user-data
KoeNoteCleanup.exe --quiet --logs --downloads --models --user-data
KoeNoteCleanup.exe --dry-run --quiet --logs --downloads --models --user-data
```

## Smoke test

Run on a clean Windows 11 VM when possible:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\phase13\Test-KoeNoteMsiSmoke.ps1 `
  -MsiPath artifacts\msi\KoeNote.msi
```

Expected result:

- `KoeNote` appears in Windows Apps management.
- `UninstallString` and `QuietUninstallString` use `msiexec`.
- Install, setup, run, uninstall, and reinstall complete.
- Reinstall detects existing setup state, job database, job folders, and model storage in the Setup Wizard details panel.

## Notes

The WiX project is intentionally built through `scripts\phase13\Build-KoeNoteMsi.ps1` rather than the root solution. The generated `PayloadFiles.wxs` depends on the publish output, so CI can keep normal app/test builds lightweight while installer builds stay explicit.
