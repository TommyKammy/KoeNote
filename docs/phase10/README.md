# Phase 10 - Packaging and First Run

Phase 10 prepares KoeNote for external beta distribution after the review, memory, evaluation, and export paths are proven.

## Goal

Create a local-first Windows distribution that can be installed and smoke-tested without hand-copying app files.

## Scope

- choose installer shape
- support self-contained app distribution
- define model pack layout
- add license manifest for app, tools, and models
- add first-run runtime checks
- add first-run model/tool location checks
- add offline smoke test
- document preview .NET runtime implications

## Runtime Policy

Development may continue on .NET 11 preview. For external beta, prefer self-contained distribution to reduce runtime setup friction. Revisit .NET 10 LTS fallback or .NET 11 stable once release timing is clear.

## Out Of Scope

- beta feedback workflow
- quality benchmark expansion
- new model support

## Completion Criteria

- A clean Windows machine can launch the packaged app.
- Missing tools/models are reported with actionable UI.
- License manifest is included.
- Offline smoke test covers startup, sample import, review screen, and export path.
