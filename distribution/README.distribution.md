# KoeNote Core Preview Distribution

This folder is produced for the new Phase 10 lightweight core packaging checks.

## Expected Layout

```text
KoeNote.App.exe
README.distribution.md
licenses/license-manifest.json
tools/ffmpeg.exe
tools/avcodec-*.dll
tools/avdevice-*.dll
tools/avfilter-*.dll
tools/avformat-*.dll
tools/avutil-*.dll
tools/swresample-*.dll
tools/swscale-*.dll
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

KoeNote Core includes FFmpeg under `tools/` for local audio normalization. Shared FFmpeg builds require companion DLLs such as `avformat-*.dll`, `avcodec-*.dll`, and `avutil-*.dll`; these DLLs must stay next to `ffmpeg.exe` so KoeNote works on machines that do not have FFmpeg installed globally.

KoeNote Core includes a minimal Python 3.12 x64 runtime under `tools/python/` so ASR and speaker diarization setup do not depend on host Python. It also includes the llama.cpp CPU x64 review runtime under `tools/review/` so Review can run after the selected Review model is installed.

KoeNote uses a GPU-ready MSI layout. KoeNote-specific GPU runtime files are bundled so NVIDIA GPU users do not need to download a large KoeNote runtime zip from GitHub:

- Review GPU bridge: `tools/review/ggml-cuda.dll`
- ASR GPU runtime files: `tools/asr/crispasr.exe`, `tools/asr/crispasr.dll`, `tools/asr/whisper.dll`, and `tools/asr/ggml-cuda.dll`

NVIDIA redistributable DLLs are intentionally not bundled in the MSI. During Setup Wizard, KoeNote downloads or reuses the required NVIDIA CUDA/cuDNN DLLs:

- Review: `cudart64_12.dll`, `cublas64_12.dll`, and `cublasLt64_12.dll` from the NVIDIA CUDA redistributable manifest.
- ASR: the same CUDA DLLs plus `cudnn*.dll` from the NVIDIA cuDNN redistributable manifest.

If the required NVIDIA DLLs already exist in a local CUDA/cuDNN installation, Setup Wizard can reuse them. If the NVIDIA download or verification fails, KoeNote keeps the copied runtime state clean and continues to offer CPU fallback where supported.

ASR and review model binaries are intentionally not included in KoeNote Core. ASR and speaker diarization Python packages are also not preinstalled in the MSI. During first-run setup, KoeNote creates managed virtual environments and downloads `faster-whisper==1.2.1` for ASR plus `diarize==0.1.2` and their Python package dependencies with pip.

The app can start without ASR/review models. First-run checks report missing assets and keep execution guidance visible until those assets are installed.

## Preview Warning

KoeNote application source code is licensed under the Apache License 2.0.
Third-party libraries, runtime tools, and models are governed by their respective licenses. See `licenses/LICENSE` and `licenses/license-manifest.json` in the distribution package for details.
