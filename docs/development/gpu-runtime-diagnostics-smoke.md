# GPU runtime diagnostics smoke

Use this checklist on a Windows host with an NVIDIA GPU after changing ASR or Review GPU runtime installation logic.

## Prepare

1. Build and launch the development app.
2. Open Setup Wizard and complete the recommended GPU runtime installation.
3. Confirm the setup screen shows both items as installed:
   - ASR GPU Runtime
   - GPU acceleration / Review GPU runtime

## ASR smoke

1. Run a short transcription job with GPU enabled.
2. Export a diagnostic package for the job.
3. Open `diagnostic-report.txt` and confirm:
   - `NvidiaGpuDetected: True`
   - `ASR GPU Runtime` shows `HasPackage: True`
   - `CTranslate2RuntimeDirectory` points to the persistent ASR CTranslate2 CUDA runtime directory
   - `Runtime Backend Signals` contains `koenote_asr_diagnostic` and a CUDA device or CTranslate2 CUDA signal
4. Open `gpu-runtime-diagnostics.json` and confirm `asr.hasPackage` is `true`.

## Review smoke

Run the local Gemma 4 review smoke with an installed GGUF model:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File scripts\development\Test-Gemma4ReviewSmoke.ps1 `
  -RunLocalRuntimeSmoke `
  -ModelPath "C:\Users\tomo\AppData\Local\KoeNote\models\review\gemma-4-12b-it-qat-q4.gguf"
```

Then run a KoeNote job through Review and export its diagnostic package.

Confirm:

- `Review GPU Runtime` shows `HasPackage: True`
- `Runtime Resolver` shows `ReviewBackendMode: cuda`
- Job log events include `Review runtime backend:`
- The backend summary is `cuda-backend-loaded` when llama.cpp reports a CUDA backend load
- The run fails as `MissingRuntime` if llama.cpp explicitly reports a missing CUDA backend while GPU layers were requested

## Update preservation smoke

1. Install the released MSI over an existing install that already has GPU runtimes.
2. Launch KoeNote.
3. Confirm Setup Wizard is not forced open for missing GPU runtimes.
4. Export a diagnostic package and confirm the ASR and Review runtime directories still point to persistent KoeNote data locations, not the app installation directory.
