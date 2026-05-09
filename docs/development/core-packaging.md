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
models/README-models-not-included.txt
models/asr/README-ASR-models-not-included.txt
models/review/README-review-models-not-included.txt
samples/README-sample-audio.txt
samples/koenote-smoke-1s.wav
```

ASR and review model binaries are intentionally excluded. KoeNote Core includes only the minimal Python runtime and the standard CPU llama.cpp review runtime. CUDA review runtime files are installed later through Setup Wizard or an additional runtime install flow.

`Publish-KoeNote.ps1` is responsible for copying runtime tools into the release payload. The app project does not copy `tools/python`, `tools/review`, or `tools/review-ternary` into publish output by default, so release packaging is protected from arbitrary local `tools` contents.

Before MSI generation, `Test-KoeNoteReleasePayloadGuard.ps1` verifies that the normal payload does not contain CUDA review DLLs, host-only Python packages such as `artifact_tool_v2`, `pandas`, `numpy`, or document/image tooling, and that `tools/python` and `tools/review` remain within the normal size budget.

For local developer checks that need the legacy ASR native tools copied into the publish folder, pass `-IncludeLegacyRuntimeTools` to the publish script. The hidden Ternary review runtime is also omitted from the normal package; pass `-IncludeTernaryReviewRuntime` only for explicit runtime-package checks.

## Commands

```powershell
dotnet test
powershell -ExecutionPolicy Bypass -File scripts\phase10\Publish-KoeNote.ps1
powershell -ExecutionPolicy Bypass -File scripts\phase10\Test-OfflineSmoke.ps1
powershell -ExecutionPolicy Bypass -File scripts\phase10\New-InstallerPackages.ps1
powershell -ExecutionPolicy Bypass -File scripts\phase10\Test-CoreUiSmoke.ps1
```

See [Core UI smoke checklist](core-ui-smoke.md) for the manual clean-environment UI checklist.

## Runtime policy

Development may continue on .NET 11 preview. For external beta, keep the app self-contained to reduce runtime setup friction. Revisit .NET 10 LTS fallback or .NET 11 stable once release timing is clear.
