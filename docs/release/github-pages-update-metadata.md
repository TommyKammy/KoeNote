# GitHub Pages update metadata

KoeNote uses GitHub Releases for versioned MSI artifacts and GitHub Pages for the stable app-facing update metadata URL.

## Stable URL

```text
https://tommykammy.github.io/KoeNote/latest.json
```

The app checks this URL by default. Use `KOENOTE_UPDATE_LATEST_URL` only for tests or alternate distribution channels.

## Required release assets

Each GitHub Release must include exactly one set of:

- `KoeNote-v<version>-<rid>.msi`
- `KoeNote-v<version>-<rid>.msi.sha256`
- `KoeNote-v<version>-<rid>.release-manifest.json`

Create the release as a draft, upload all three assets, and then publish it. This ensures the `release.published` workflow sees a complete asset set.

## Workflow

`.github/workflows/publish-update-metadata.yml` listens for a published GitHub Release and can also be run manually with `workflow_dispatch`.

The workflow resolves the release tag, requires exactly one `*.release-manifest.json` asset, downloads the manifest, runs `scripts\phase13\New-KoeNoteLatestJson.ps1`, writes `public\latest.json` and `public\.nojekyll`, and deploys `public` to GitHub Pages.

## latest.json contents

The generated metadata includes schema version, product name, product version, runtime identifier, MSI download URL, SHA256 value, SHA256 sidecar URL, release notes URL, mandatory update flag, and published timestamp.

Example:

```json
{
  "schema_version": 1,
  "product_name": "KoeNote",
  "version": "0.15.0",
  "runtime_identifier": "win-x64",
  "msi_url": "https://github.com/TommyKammy/KoeNote/releases/download/v0.15.0/KoeNote-v0.15.0-win-x64.msi",
  "sha256": "9e9d50e86028dce13f090e80466f62e253e05684ec028b5af1a9fa3e52a7bd45",
  "sha256_url": "https://github.com/TommyKammy/KoeNote/releases/download/v0.15.0/KoeNote-v0.15.0-win-x64.msi.sha256",
  "release_notes_url": "https://github.com/TommyKammy/KoeNote/releases/tag/v0.15.0",
  "mandatory": false
}
```

## App-side update behavior

When an update is available, KoeNote downloads the MSI into `%LOCALAPPDATA%\KoeNote\updates`, verifies it against `latest.json` SHA256, and promotes it from `.download` to `.msi` only after verification succeeds.

The app records update check, download, verification, and install handoff events in `%LOCALAPPDATA%\KoeNote\updates\history.jsonl`.

KoeNote also removes stale verified MSI downloads older than 30 days and stale `.download` files older than 1 day.

## Operational notes

- Configure GitHub Pages Source to GitHub Actions.
- If a release was published before all assets were attached, rerun the workflow manually after fixing the assets.
- Public Pages content may take a short time to refresh. Verify with:

```powershell
curl.exe -L -H "Cache-Control: no-cache" https://tommykammy.github.io/KoeNote/latest.json
```

## v0.15.0 reference

- Release: https://github.com/TommyKammy/KoeNote/releases/tag/v0.15.0
- MSI: `KoeNote-v0.15.0-win-x64.msi`
- SHA256: `9e9d50e86028dce13f090e80466f62e253e05684ec028b5af1a9fa3e52a7bd45`
- Pages workflow: success
- Published `latest.json`: `version = 0.15.0`
