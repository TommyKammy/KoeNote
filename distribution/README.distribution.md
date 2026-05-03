# KoeNote Preview Distribution

This folder is produced for Phase 10 packaging checks.

## Expected Layout

```text
KoeNote.App.exe
README.distribution.md
licenses/license-manifest.json
tools/crispasr.exe
tools/llama-completion.exe
models/asr/vibevoice-asr-q4_k.gguf
models/review/llm-jp-4-8B-thinking-Q4_K_M.gguf
```

The app can start without the tools and models, but first-run checks will report them as missing until they are placed in the expected paths.

## Preview Warning

KoeNote is still in preview. Confirm all tool and model redistribution terms before sharing an external beta package.
