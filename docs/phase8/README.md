# Phase 8 - Evaluation Bench

Phase 8 creates repeatable measurement for ASR quality, review quality, memory usefulness, and runtime cost.

## Goal

Make quality and performance changes measurable before export and packaging harden the product surface.

## Scope

- build a local evaluation runner
- define a three-layer Japanese audio dataset plan
- measure ASR character error rate and word-like error rate where references exist
- measure review draft JSON parse failures
- measure LLM candidate acceptance rate
- measure memory-derived candidate acceptance rate
- measure runtime duration and peak memory/VRAM where available
- record results under `experiments/phase8`

## Dataset Layers

```text
A. Public or redistributable short audio set
B. Self-recorded / consented meeting-like set
C. Artificial proper-noun and recurring misrecognition set
```

Layer C is required before judging Correction Memory because it tests whether repeated mistakes become useful candidates later.

## Metrics

- ASR CER
- review JSON parse failure rate
- review candidate acceptance rate
- memory candidate acceptance rate
- memory suggestion rejection rate
- processing time by stage
- missing-runtime and failure category coverage

## Out Of Scope

- installer
- model distribution
- UI redesign
- new ASR engine integration

## Completion Criteria

- A repeatable command can run the bench locally.
- Results are written with host/runtime metadata.
- At least one short fixture exercises ASR, review, and memory matching.
- Evaluation output identifies regressions clearly enough to block a release candidate.

## Implementation Start

Added a local evaluation CLI:

```powershell
dotnet run --project src\KoeNote.EvalBench\KoeNote.EvalBench.csproj
```

Current fixture coverage:

- Layer C style proper-noun recurring correction fixture
- ASR CER using character edit distance
- review draft JSON normalization and parse-failure metric
- correction memory seed, deterministic memory suggestion generation, and memory suggestion count
- host/runtime metadata
- regression list with non-zero process exit when thresholds fail

Output:

- `experiments/phase8/<timestamp>/report.json`
- `experiments/phase8/latest.json`
