# Development docs

Current developer-facing notes live here. Historical phase-by-phase implementation notes are archived under [../archive/phases](../archive/phases/).

- [Core packaging](core-packaging.md)
- [Core UI smoke checklist](core-ui-smoke.md)
- [Gemma 4 12B review model compatibility](gemma-4-12b-review-compatibility.md)
- [LLM profile / task settings roadmap](llm-profile-task-settings-roadmap.md)
- [Review candidate confirmation flow](review-candidate-confirmation-flow.md)
- [Standard/detail layout manual checks](standard-detail-layout-manual-checks.md)

Gemma 4 12B review release checks can be repeated with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\development\Test-Gemma4ReviewSmoke.ps1
```

Release and installer operations live under [../release](../release/).
