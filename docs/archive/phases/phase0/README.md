# Phase 0 - Preflight Validation

Phase 0 validates whether KoeNote's local-first pipeline is viable before building the desktop application.

## Host Context

Development host:

- OS: Windows 11
- GPU: GeForce RTX 3090
- .NET SDK: 11.0.100-preview.3

Target reference machine:

- OS: Windows 11
- GPU: GeForce RTX 3060 12GB

Results from the RTX 3090 development host are useful for development speed, but they do not prove RTX 3060 viability. Any GPU, VRAM, or throughput result should identify the machine profile used for the run.

## Validation Goals

- Confirm `ffmpeg.exe` can normalize common audio formats to 24 kHz mono WAV.
- Confirm the ASR runtime can execute `vibevoice-asr-q4_k.gguf` on Windows.
- Confirm the review LLM can execute `llm-jp-4-8B-thinking-Q4_K_M.gguf` through `llama-completion.exe`.
- Confirm the pipeline can produce parseable ASR JSON and review JSON.
- Confirm ASR and LLM can run sequentially without leaving GPU memory resident.

## Repository Rules

Do not commit:

- GGUF model files
- private audio
- generated normalized WAV files
- raw transcripts from private audio
- local job databases
- worker logs with transcript content

Small synthetic fixtures may be committed later under a dedicated public fixtures directory after privacy and license review.

## Suggested Output Structure

Keep local experiment output outside Git or in ignored folders:

```text
experiments/
  phase0/
    runs/
      <date>-<machine>/
        notes.md
        metrics.json
        logs/
```

If a result should be preserved in Git, summarize it in Markdown without embedding private audio, full transcripts, or large raw outputs.

## Phase 0 Tools

- `scripts/phase0/Collect-HostInfo.ps1` captures the current Windows development host profile.
- `scripts/phase0/Test-Phase0Prereqs.ps1` checks required and optional local tools/models.
- `docs/archive/phases/phase0/RUNBOOK.md` describes the Phase 0 command sequence.

## Phase 0 Gate

Phase 0 is complete when the following command-line path works on Windows:

```text
audio file
  -> ffmpeg normalized WAV
  -> ASR JSON
  -> review JSON
```

The first pass may be completed on the RTX 3090 host. A follow-up RTX 3060 12GB run remains required before treating the target machine as validated.
