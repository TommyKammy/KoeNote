# Core UI smoke checklist

This checklist verifies KoeNote Core after installing the lightweight core package on a clean or clean-like Windows environment.

## Environment

- Use a clean Windows 11 VM when available.
- Network can be disabled after the installer is available locally.
- Do not preinstall ASR or review model binaries.
- Use `KOENOTE_INSTALL_TARGET` for local smoke installs when you do not want to touch the normal user install directory.

## Prepare

```powershell
dotnet test
powershell -ExecutionPolicy Bypass -File scripts\phase10\Publish-KoeNote.ps1
powershell -ExecutionPolicy Bypass -File scripts\phase10\Test-OfflineSmoke.ps1
powershell -ExecutionPolicy Bypass -File scripts\phase10\New-InstallerPackages.ps1
powershell -ExecutionPolicy Bypass -File scripts\phase10\Test-CoreUiSmoke.ps1 -InstallDir artifacts\installer-smoke-core
```

## Manual UI checklist

1. Launch `KoeNote.App.exe` from the installed Core folder.
2. Confirm the main window opens without ASR or review model binaries.
3. Confirm the status/setup UI shows missing asset guidance.
4. Confirm the run command is disabled before required models/runtimes are installed.
5. Open setup/model management from the app.
6. Confirm model setup guidance is reachable.
7. Import `samples\koenote-smoke-1s.wav`.
8. Confirm the job appears in the job list.
9. Confirm the review panel is visible and the app remains responsive.
10. Confirm export controls and export path UI are visible.

## Pass criteria

- KoeNote Core launches offline.
- Missing ASR/review assets are visible and actionable.
- Model setup is reachable in the app.
- The sample audio can be registered as a job.
- The app does not crash when models are absent.
