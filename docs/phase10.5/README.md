# Phase 10.5 - ASR Adapter Architecture

Phase 10.5 starts the ASR re-evaluation work by separating ASR execution from a single fixed implementation.

## Initial Slice

- Add `IAsrEngine` and adapter contracts.
- Add `AsrEngineRegistry`.
- Add `asr_runs` persistence and `transcript_segments.asr_run_id`.
- Wrap the current VibeVoice/CrispASR path as `vibevoice-crispasr`.
- Keep existing ASR worker behavior while routing job execution through the registry.

## Current Engine IDs

```text
vibevoice-crispasr
faster-whisper-large-v3-turbo
reazonspeech-k2-v3
```

Planned IDs:

```text
reazonspeech-nemo-v3
reazonspeech-espnet-v3
whisper-cpp
```

## Engine Selection

The main window exposes an ASR engine selector in the header toolbar. The selected engine is persisted in
`asr_settings.engine_id` and passed to `JobRunCoordinator` for the next run against the same normalized audio path.

## PoC Script Adapters

Two Python script adapters are packaged with the app:

```text
scripts/asr/faster_whisper_transcribe.py
scripts/asr/reazonspeech_k2_transcribe.py
```

Both scripts emit a JSON payload with `segments`, which is normalized through the shared `AsrJsonNormalizer` into
KoeNote `TranscriptSegment` rows. The app expects local model directories under:

```text
models/asr/faster-whisper-large-v3-turbo
models/asr/reazonspeech-k2-v3
```

## ASR Comparison Manifest

`KoeNote.EvalBench` can compare engine outputs after PoC runs:

```powershell
dotnet run --project src/KoeNote.EvalBench -- --output experiments/phase10.5 --asr-manifest experiments/phase10.5/asr-manifest.json
```

Manifest shape:

```json
{
  "cases": [
    {
      "caseId": "meeting-30s",
      "durationBucket": "30s",
      "audioDurationSeconds": 30,
      "referenceText": "reference transcript",
      "results": [
        {
          "engineId": "faster-whisper-large-v3-turbo",
          "outputJsonPath": "experiments/phase10.5/faster-whisper-30s.json",
          "processingSeconds": 12.3,
          "succeeded": true
        }
      ]
    }
  ]
}
```

Use duration buckets `30s`, `5m`, and `10m` to produce CER, processing time, failure rate, RTF, and default ASR
recommendations for v0.1 / v0.2.

## Validation

```powershell
dotnet test
```

This phase is not complete until multiple ASR engines can be compared on the same audio, but the first slice establishes the storage and adapter seam for that work.

## Completion Status

- Multiple engine selection: implemented in settings and UI.
- ReazonSpeech v3 k2 PoC path: adapter and packaged script implemented; requires local Python package/model to execute.
- faster-whisper large-v3-turbo path: adapter and packaged script implemented; requires local Python package/model to execute.
- TranscriptSegment normalization: shared normalizer path implemented for all adapters.
- 30s / 5m / 10m comparison: manifest-based EvalBench reporting implemented.
- v0.1 / v0.2 default ASR reselection: EvalBench emits a recommendation from CER, failure rate, and RTF.
