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
  -Publisher "KoeNote Project"
```

The script:

1. Publishes `KoeNote.App`.
2. Publishes `KoeNoteCleanup.exe` into the same payload directory.
3. Generates `src\KoeNote.Installer\PayloadFiles.wxs` from the publish payload.
4. Builds `artifacts\msi\KoeNote-v<version>-<rid>.msi`.
5. Writes a SHA256 sidecar file next to the MSI.
6. Writes a JSON Lines update/release log under `artifacts\logs\updates`.
7. Writes `artifacts\msi\KoeNote-v<version>-<rid>.release-manifest.json`.

The default product version comes from the repository-level `Directory.Build.props` `VersionPrefix`.
Pass `-ProductVersion` only when intentionally overriding that single source for a one-off build.
When `-OutputName` is omitted, the MSI name is fixed as `KoeNote-v<version>-<rid>.msi`,
for example `KoeNote-v0.13.0-win-x64.msi`.

Optional code signing is enabled by environment variables:

```powershell
$env:KOENOTE_SIGNTOOL_PATH = "C:\Program Files (x86)\Windows Kits\10\bin\<version>\x64\signtool.exe"
$env:KOENOTE_SIGN_CERT_SHA1 = "<certificate thumbprint>"
$env:KOENOTE_SIGN_TIMESTAMP_URL = "http://timestamp.digicert.com"
```

Alternatively use `KOENOTE_SIGN_CERT_PATH` and `KOENOTE_SIGN_CERT_PASSWORD` for a PFX file.
If signing variables are not set, signing is skipped and the release log records the reason.
Pass `-RequireCodeSigning` for release/CI builds that must fail instead of producing unsigned artifacts.

Release manifest:

- MSI path
- SHA256 sidecar path
- update/release log path
- product version
- runtime identifier
- signing requirement and status

Verify the MSI sidecar before publishing:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\phase13\Test-KoeNoteArtifactIntegrity.ps1 `
  -MsiPath artifacts\msi\KoeNote-v0.13.0-win-x64.msi
```

CI-oriented release verification:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\phase13\Test-KoeNoteReleaseVerification.ps1 `
  -MsiPath artifacts\msi\KoeNote-v0.13.0-win-x64.msi
```

The CI verification command runs the versioning/release tests, verifies the SHA256 sidecar,
and checks that the release manifest matches the MSI.

Create the distribution `latest.json` used by the app's update check:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\phase13\New-KoeNoteLatestJson.ps1 `
  -ReleaseManifestPath artifacts\msi\KoeNote-v0.13.0-win-x64.release-manifest.json `
  -BaseDownloadUrl https://example.com/koenote/releases/0.13.0/ `
  -ReleaseNotesUrl https://example.com/koenote/releases/0.13.0
```

The generated `latest.json` contains the version, runtime identifier, MSI URL, SHA256,
SHA256 sidecar URL, optional release notes URL, and whether the update is mandatory.
KoeNote checks `https://tommykammy.github.io/KoeNote/latest.json` by default.
Set `KOENOTE_UPDATE_LATEST_URL` only when overriding the update metadata URL for
test or alternate distribution channels.
When an update is available, the app can download the MSI into
`%LOCALAPPDATA%\KoeNote\updates`, verify it against the `latest.json` SHA256, and then
show that the installer is ready. Partially downloaded files use a `.download` suffix
and are not promoted to `.msi` unless verification succeeds.
After verification, the app exposes an install action that launches the MSI through
`msiexec /i`. The install action is disabled while a KoeNote job is running, so users
can finish active transcription/review work before handing off to the installer.
Before launching `msiexec`, KoeNote verifies the downloaded MSI's Authenticode
signature with Windows trust. Unsigned or untrusted MSI files are not launched.
Set `KOENOTE_UPDATE_SIGNER_SUBJECT_CONTAINS` to require the trusted signing
certificate subject to contain an expected publisher string.
Update check, download, verification, and install handoff events are appended to
`%LOCALAPPDATA%\KoeNote\updates\history.jsonl` for local troubleshooting. On each
update download, KoeNote also removes stale verified MSI downloads older than 30 days
and stale `.download` files older than 1 day from `%LOCALAPPDATA%\KoeNote\updates`.

For GitHub-hosted OSS releases, publish MSI artifacts to GitHub Releases and keep
`latest.json` on GitHub Pages. The repository workflow
`.github/workflows/publish-update-metadata.yml` listens for a published GitHub
Release, downloads the `*.release-manifest.json` release asset, generates
`public/latest.json`, and deploys it to Pages. Configure GitHub Pages to deploy
from GitHub Actions, then set:

```powershell
$env:KOENOTE_UPDATE_LATEST_URL = "https://<owner>.github.io/<repo>/latest.json"
```

Each GitHub Release must include:

- `KoeNote-v<version>-<rid>.msi`
- `KoeNote-v<version>-<rid>.msi.sha256`
- `KoeNote-v<version>-<rid>.release-manifest.json`

The generated `latest.json` points MSI and SHA256 URLs at the versioned GitHub
Release download path while keeping the app-facing metadata URL stable.
Create the release as a draft, upload all three assets, and then publish it so the
`release.published` workflow sees the complete asset set. If assets are corrected
after publishing, run the workflow manually with the release tag.

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

Update recovery options:

```powershell
KoeNoteCleanup.exe --list-update-backups
KoeNoteCleanup.exe --restore-update-backup latest --dry-run
KoeNoteCleanup.exe --restore-update-backup latest
KoeNoteCleanup.exe --restore-update-backup schema-1-to-14-20260506-160000
```

Restore saves the current job database, settings, setup state, setup report, and job files under a `pre-restore-*` update backup directory before overwriting them.

## Smoke test

Run on a clean Windows 11 VM when possible:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\phase13\Test-KoeNoteMsiSmoke.ps1 `
  -MsiPath artifacts\msi\KoeNote-v0.13.0-win-x64.msi
```

To test an MSI-to-MSI upgrade on a clean VM:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\phase13\Test-KoeNoteMsiSmoke.ps1 `
  -UpgradeFromMsiPath artifacts\msi\KoeNote-v0.13.0-win-x64.msi `
  -MsiPath artifacts\msi\KoeNote-v0.14.0-win-x64.msi
```

The upgrade smoke refuses to run when existing KoeNote user data is present unless `-AllowExistingUserData` is passed.

Expected result:

- `KoeNote` appears in Windows Apps management.
- `UninstallString` and `QuietUninstallString` use `msiexec`.
- Install, setup, run, uninstall, and reinstall complete.
- Reinstall detects existing setup state, job database, job folders, and model storage in the Setup Wizard details panel.
- Upgrade prompts to close `KoeNote.App.exe` when the app is running during install.

## Notes

The WiX project is intentionally built through `scripts\phase13\Build-KoeNoteMsi.ps1` rather than the root solution. The generated `PayloadFiles.wxs` depends on the publish output, so CI can keep normal app/test builds lightweight while installer builds stay explicit.
