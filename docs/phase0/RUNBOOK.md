# Phase 0 Runbook

This runbook defines the first command-line validation steps for KoeNote.

## 1. Capture Host Information

Run:

```powershell
.\scripts\phase0\Collect-HostInfo.ps1
```

This writes an ignored local run directory under:

```text
experiments/phase0/runs/<timestamp>-<machine>/
```

The capture includes OS, CPU, memory, GPU, driver, CUDA reported by `nvidia-smi`, `.NET` version, and `ffmpeg` availability. It must not include private audio, transcripts, or model files.

## 2. Check Prerequisites

Run:

```powershell
.\scripts\phase0\Test-Phase0Prereqs.ps1
```

Required for the first host check:

- `dotnet`
- `nvidia-smi`
- `ffmpeg`

Expected later for full Phase 0:

- `tools/crispasr.exe`
- `tools/llama-completion.exe`
- `models/asr/vibevoice-asr-q4_k.gguf`
- `models/review/llm-jp-4-8B-thinking-Q4_K_M.gguf`

Use strict mode only when all local runtime and model files are expected to exist:

```powershell
.\scripts\phase0\Test-Phase0Prereqs.ps1 -Strict
```

## 3. Audio Normalization Smoke Test

After a public or synthetic sample exists locally, normalize it to 24 kHz mono WAV:

```powershell
ffmpeg -y -i input.m4a -ar 24000 -ac 1 normalized.wav
```

Do not commit input audio or generated WAV files unless they are explicitly public fixtures.

## 4. ASR Smoke Test

Pending final `crispasr.exe` command-line confirmation.

Expected shape:

```text
normalized.wav
  -> crispasr.exe
  -> raw ASR JSON
  -> normalized TranscriptSegment JSON
```

Record command, machine profile, runtime version, model version, elapsed time, and peak VRAM.

## 5. Review LLM Smoke Test

Pending final `llama-completion.exe` prompt and command-line confirmation.

Expected shape:

```text
TranscriptSegment JSON
  -> prompt
  -> llama-completion.exe
  -> CorrectionDraft JSON
```

Record JSON parse success, retry count, elapsed time, and peak VRAM.

## 6. Phase 0 Completion Gate

Phase 0 is complete when this full local pipeline runs on Windows:

```text
audio file
  -> ffmpeg normalized WAV
  -> ASR JSON
  -> review JSON
```

The RTX 3090 development host can complete the first pass. A separate RTX 3060 12GB run remains required before the target environment is considered validated.
