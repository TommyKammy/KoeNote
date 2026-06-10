# Targeted test commands

Use `scripts\test\Test-KoeNoteTarget.ps1` when a full `dotnet test KoeNote.slnx` run is too slow for refactoring feedback.

List available targets:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\test\Test-KoeNoteTarget.ps1 -Target list
```

Fast smoke set:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\test\Test-KoeNoteTarget.ps1 -Target quick -NoRestore
```

Run one area:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\test\Test-KoeNoteTarget.ps1 -Target transcript -NoRestore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\test\Test-KoeNoteTarget.ps1 -Target setup -NoRestore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\test\Test-KoeNoteTarget.ps1 -Target viewmodel -NoRestore
```

Run multiple areas in sequence:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\test\Test-KoeNoteTarget.ps1 -Target asr,review,runtime -NoRestore
```

Useful targets:

| Target | Scope |
| --- | --- |
| `quick` | Small smoke set for fast refactoring feedback. |
| `asr` | ASR parsing, ASR repositories, ASR runtime-related tests. |
| `review` | Review operation, correction memory, diff, review runtimes. |
| `transcript` | Transcript repositories, summary, polishing, export-adjacent transcript logic. |
| `setup` | Setup wizard and setup readiness/model selection logic. |
| `viewmodel` | `MainWindowViewModelTests` through `KoeNote.App.UiIntegrationTests`. |
| `jobs` | Job repositories, job logs, coordinator, stage progress. |
| `models` | Model catalog, install, download, verification, import. |
| `updates` | Update check/download/history/launcher services. |
| `presets` | Domain preset import and prompt context. |
| `runtime` | Runtime installation/probing helpers and external process tests. |
| `ui` | Entire UI integration test project. |

Extra `dotnet test` arguments can be appended with `-DotnetArgs`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\test\Test-KoeNoteTarget.ps1 -Target quick -NoRestore -DotnetArgs "--blame-hang-timeout","60s"
```
