# KoeNote Core Preview Distribution

This folder is produced for the new Phase 10 lightweight core packaging checks.

## Expected Layout

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

KoeNote Core includes a minimal Python 3.12 x64 runtime under `tools/python/` so ASR and speaker diarization setup do not depend on host Python. It also includes the llama.cpp CPU x64 review runtime under `tools/review/` so Review can run after the selected Review model is installed.

ASR and review model binaries are intentionally not included in KoeNote Core. CUDA review runtime files are also not included in the normal MSI; GPU acceleration runtimes are installed later through Setup Wizard or an additional runtime install flow. ASR and speaker diarization Python packages are also not preinstalled in the MSI. During first-run setup, KoeNote creates managed virtual environments and downloads `faster-whisper==1.2.1` for ASR plus `diarize==0.1.2` and their Python package dependencies with pip.

The app can start without ASR/review models. First-run checks report missing assets and keep execution guidance visible until those assets are installed.

## Preview Warning

KoeNote application source code is licensed under the Apache License 2.0.
Third-party libraries, runtime tools, and models are governed by their respective licenses. See `licenses/LICENSE` and `licenses/license-manifest.json` in the distribution package for details.
