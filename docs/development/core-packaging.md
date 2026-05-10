# Core packaging

KoeNote Core is a self-contained `win-x64` app package that can launch without bundled ASR or review LLM model binaries.

## Scope

- self-contained `win-x64` app publish
- lightweight core setup payload
- license manifest for the core package
- first-run status summary for missing runtime/model assets
- offline layout smoke test

## Core package layout

```text
KoeNote.App.exe
README.distribution.md
licenses/license-manifest.json
tools/ffmpeg.exe
tools/python/python.exe
tools/review/llama-completion.exe
tools/review/ggml-cuda.dll
tools/asr/crispasr.exe
tools/asr/crispasr.dll
tools/asr/whisper.dll
tools/asr/ggml-cuda.dll
models/README-models-not-included.txt
models/asr/README-ASR-models-not-included.txt
models/review/README-review-models-not-included.txt
samples/README-sample-audio.txt
samples/koenote-smoke-1s.wav
```

ASR and review model binaries are intentionally excluded. KoeNote Core includes the minimal Python runtime, the standard CPU llama.cpp review runtime, and KoeNote-specific GPU runtime files needed for the GPU-ready MSI.

The MSI bundles only runtime files that are specific to KoeNote, Crisp ASR, whisper.cpp, or llama.cpp:

- `tools/review/ggml-cuda.dll`
- `tools/asr/crispasr*.exe`
- `tools/asr/crispasr*.dll`
- `tools/asr/whisper.dll`
- `tools/asr/ggml-cuda.dll`

NVIDIA redistributable DLLs are excluded from the MSI and resolved at setup time:

- Review downloads or reuses `cudart64_12.dll`, `cublas64_12.dll`, and `cublasLt64_12.dll` from the NVIDIA CUDA redist manifest.
- ASR downloads or reuses the same CUDA DLLs plus `cudnn*.dll` from the NVIDIA cuDNN redist manifest.
- The default manifests are `https://developer.download.nvidia.com/compute/cuda/redist/redistrib_12.9.0.json` and `https://developer.download.nvidia.com/compute/cudnn/redist/redistrib_9.22.0.json`.

`Publish-KoeNote.ps1` is responsible for copying runtime tools into the release payload. The app project does not copy `tools/python`, `tools/review`, or `tools/review-ternary` into publish output by default, so release packaging is protected from arbitrary local `tools` contents.

Before MSI generation, `Test-KoeNoteReleasePayloadGuard.ps1` verifies that the GPU-ready payload does not contain NVIDIA redistributable DLLs such as `cublas*`, `cudart*`, `cudnn*`, `cufft*`, `curand*`, or `cusparse*`. It also rejects host-only Python packages such as `artifact_tool_v2`, `pandas`, `numpy`, or document/image tooling, and checks that `tools/python`, `tools/review`, and `tools/asr` remain within the GPU-ready size budget.

For local developer checks that need the legacy ASR native tools copied into the publish folder, pass `-IncludeLegacyRuntimeTools` to the publish script. Release MSI builds pass `-RequireGpuReadyRuntime`, which fails if the KoeNote-specific Review or ASR GPU files are missing. The hidden Ternary review runtime is omitted from the normal package; pass `-IncludeTernaryReviewRuntime` only for explicit runtime-package checks.

## Commands

```powershell
dotnet test
powershell -ExecutionPolicy Bypass -File scripts\phase10\Publish-KoeNote.ps1
powershell -ExecutionPolicy Bypass -File scripts\phase10\Test-OfflineSmoke.ps1
powershell -ExecutionPolicy Bypass -File scripts\phase10\New-InstallerPackages.ps1
powershell -ExecutionPolicy Bypass -File scripts\phase10\Test-CoreUiSmoke.ps1
```

See [Core UI smoke checklist](core-ui-smoke.md) for the manual clean-environment UI checklist.

## Setup Wizard GPU runtime flow

Setup Wizard shows runtime install stages as `確認中`, `ダウンロード中`, `検証中`, `展開中`, and `インストール中`.

For NVIDIA GPU hosts, the wizard first checks whether the bundled KoeNote GPU files and local CUDA/cuDNN DLLs are already present. If they are not present, it downloads the NVIDIA redist package zips, verifies their SHA256 values from the manifest, extracts only the required DLLs, and copies them into `tools/review` or `tools/asr`. Failed installs roll back partial copies and keep CPU fallback available where supported.

## Runtime policy

Development may continue on .NET 11 preview. For external beta, keep the app self-contained to reduce runtime setup friction. Revisit .NET 10 LTS fallback or .NET 11 stable once release timing is clear.
