# MSI installer and cleanup policy

KoeNote is distributed as a lightweight per-user MSI. The core package installs the app payload and shortcuts, while ASR/review/diarization model assets remain setup-time downloads or offline imports.

## Scope

- Install scope: per-user by default.
- Install location: `%LOCALAPPDATA%\Programs\KoeNote`.
- Windows Apps management registration is provided by MSI.
- Start Menu shortcuts:
  - `KoeNote`
  - `KoeNote Cleanup`
- Model binaries are not bundled in the core MSI.
- The MSI is GPU-ready: KoeNote-specific GPU runtime files are bundled, while NVIDIA redistributable CUDA/cuDNN DLLs are downloaded or reused by Setup Wizard.

The MSI removes the application payload on uninstall. KoeNote user data is intentionally kept unless the user explicitly chooses cleanup.

## Build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\phase13\Build-KoeNoteMsi.ps1 `
  -Configuration Release `
  -RuntimeIdentifier win-x64 `
  -Publisher "KoeNote Project"
```

The script publishes the app and cleanup tool, generates `src\KoeNote.Installer\PayloadFiles.wxs`, builds `artifacts\msi\KoeNote-v<version>-<rid>.msi`, writes a SHA256 sidecar, writes an update/release JSONL log, and writes a release manifest.

The default product version comes from `Directory.Build.props` `VersionPrefix`. Pass `-ProductVersion` only for an intentional one-off override.

Release MSI builds call `Publish-KoeNote.ps1` with `-RequireGpuReadyRuntime`. The publish step must find:

- `tools\review\llama-completion.exe`
- `tools\review\ggml-cuda.dll`
- `tools\asr\crispasr.exe`
- `tools\asr\crispasr.dll`
- `tools\asr\whisper.dll`
- `tools\asr\ggml-cuda.dll`

The publish step excludes NVIDIA redistributable DLLs from the MSI payload, including `cublas*`, `cudart*`, `cudnn*`, `cufft*`, `curand*`, and `cusparse*`.

## Code signing

Code signing is optional and configured by environment variables:

```powershell
$env:KOENOTE_SIGNTOOL_PATH = "C:\Program Files (x86)\Windows Kits\10\bin\<version>\x64\signtool.exe"
$env:KOENOTE_SIGN_CERT_SHA1 = "<certificate thumbprint>"
$env:KOENOTE_SIGN_TIMESTAMP_URL = "http://timestamp.digicert.com"
```

Alternatively use `KOENOTE_SIGN_CERT_PATH` and `KOENOTE_SIGN_CERT_PASSWORD` for a PFX file.

If signing variables are not set, signing is skipped and the release log records the reason. Pass `-RequireCodeSigning` only for builds that must fail instead of producing unsigned artifacts.

## Verification

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\phase13\Test-KoeNoteReleaseVerification.ps1 `
  -MsiPath artifacts\msi\KoeNote-v0.15.0-win-x64.msi
```

The verification command runs versioning/release tests, verifies the SHA256 sidecar, and checks that the release manifest matches the MSI, SHA sidecar, update log, version, runtime identifier, signing state, bundled Python runtime, Review runtime, and GPU-ready runtime metadata.

`Test-KoeNoteReleasePayloadGuard.ps1` also checks that NVIDIA redistributable DLLs are not present in `tools\review` or `tools\asr`. The release manifest records `gpu_ready_runtime.nvidia_redistributables_included = false` and the NVIDIA manifest URLs used by Setup Wizard:

- CUDA: `https://developer.download.nvidia.com/compute/cuda/redist/redistrib_12.9.0.json`
- cuDNN: `https://developer.download.nvidia.com/compute/cudnn/redist/redistrib_9.22.0.json`

## GPU runtime setup troubleshooting

On NVIDIA GPU hosts, Setup Wizard may download or reuse NVIDIA redistributable DLLs after installation. The progress UI uses these stages:

- `確認中`: bundled KoeNote GPU files and local CUDA/cuDNN DLLs are checked.
- `ダウンロード中`: NVIDIA redist manifests or package zips are being downloaded.
- `検証中`: manifest/package hashes and final install layout are being verified.
- `展開中`: required DLLs are being extracted from NVIDIA packages.
- `インストール中`: verified DLLs are being copied into `tools\review` or `tools\asr`.

If this fails, check network/proxy access to `developer.download.nvidia.com`, ensure there is enough disk space, and retry Setup Wizard. Review can continue with the CPU runtime. ASR CPU fallback depends on the selected ASR model and installed Python packages.

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
  -MsiPath artifacts\msi\KoeNote-v0.15.0-win-x64.msi
```

To test an MSI-to-MSI upgrade:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\phase13\Test-KoeNoteMsiSmoke.ps1 `
  -UpgradeFromMsiPath artifacts\msi\KoeNote-v0.14.0-win-x64.msi `
  -MsiPath artifacts\msi\KoeNote-v0.15.0-win-x64.msi
```

The upgrade smoke refuses to run when existing KoeNote user data is present unless `-AllowExistingUserData` is passed.
