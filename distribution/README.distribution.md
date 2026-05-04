# KoeNote Core Preview Distribution

This folder is produced for the new Phase 10 lightweight core packaging checks.

## Expected Layout

```text
KoeNote.App.exe
README.distribution.md
licenses/license-manifest.json
tools/ffmpeg.exe
tools/README-runtime-tools-not-included.txt
models/README-models-not-included.txt
models/asr/README-ASR-models-not-included.txt
models/review/README-review-models-not-included.txt
samples/README-sample-audio.txt
samples/koenote-smoke-1s.wav
```

ASR and review model binaries are intentionally not included in KoeNote Core. Phase 11 will add the Model Catalog / Download Manager path, and Phase 12 will guide first-run setup.

The app can start without ASR/review runtimes and models. First-run checks report missing assets and keep execution guidance visible until those assets are installed.

## Preview Warning

KoeNote is still in preview. Confirm final app, tool, and model redistribution terms before sharing an external beta package.
