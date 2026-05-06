param(
    [Parameter(Mandatory = $true)][string]$MsiPath,
    [string]$ManifestPath = "",
    [switch]$SkipVersioningTests
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$artifactIntegrityScript = Join-Path $repoRoot "scripts\phase13\Test-KoeNoteArtifactIntegrity.ps1"
$testProject = Join-Path $repoRoot "tests\KoeNote.App.Tests\KoeNote.App.Tests.csproj"

if (-not $SkipVersioningTests) {
    dotnet test $testProject --configuration Debug --filter "FullyQualifiedName~VersioningTests" --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "VersioningTests failed with exit code $LASTEXITCODE."
    }
}

$null = & powershell -NoProfile -ExecutionPolicy Bypass -File $artifactIntegrityScript -MsiPath $MsiPath
if ($LASTEXITCODE -ne 0) {
    throw "Artifact integrity verification failed with exit code $LASTEXITCODE."
}

$msi = Resolve-Path $MsiPath
$sha256Path = "$($msi.Path).sha256"
$sidecarLine = (Get-Content -LiteralPath $sha256Path -Raw).Trim()
if ($sidecarLine -notmatch '^(?<hash>[0-9a-fA-F]{64})\s+') {
    throw "SHA256 sidecar has an invalid format: $sha256Path"
}

$sidecarSha256 = $Matches["hash"].ToLowerInvariant()
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = [System.IO.Path]::ChangeExtension($msi.Path, ".release-manifest.json")
}

if (-not (Test-Path -LiteralPath $ManifestPath)) {
    throw "Release manifest is missing: $ManifestPath"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.schema_version -ne 1) {
    throw "Unsupported release manifest schema_version: $($manifest.schema_version)"
}

if ($manifest.artifacts.msi_path -ne $msi.Path) {
    throw "Manifest MSI path does not match. Expected $($msi.Path) but got $($manifest.artifacts.msi_path)."
}

if ($manifest.artifacts.sha256_path -ne $sha256Path) {
    throw "Manifest SHA256 path does not match. Expected $sha256Path but got $($manifest.artifacts.sha256_path)."
}

if (-not (Test-Path -LiteralPath $manifest.artifacts.update_log_path)) {
    throw "Manifest update log path is missing or does not exist: $($manifest.artifacts.update_log_path)"
}

if ($manifest.sha256 -ne $sidecarSha256) {
    throw "Manifest SHA256 does not match sidecar. Expected $sidecarSha256 but got $($manifest.sha256)."
}

if ([string]::IsNullOrWhiteSpace($manifest.product_version)) {
    throw "Manifest product_version is missing."
}

if ([string]::IsNullOrWhiteSpace($manifest.runtime_identifier)) {
    throw "Manifest runtime_identifier is missing."
}

if ([string]::IsNullOrWhiteSpace($manifest.signing.status)) {
    throw "Manifest signing.status is missing."
}

if ($manifest.signing.status -notin @("signed", "skipped", "not_configured")) {
    throw "Manifest signing.status is invalid: $($manifest.signing.status)"
}

if ([bool]$manifest.signing.required -and $manifest.signing.status -ne "signed") {
    throw "Manifest requires signing, but signing.status is $($manifest.signing.status)."
}

[pscustomobject]@{
    MsiPath = $msi.Path
    Sha256 = $sidecarSha256
    ManifestPath = (Resolve-Path $ManifestPath).Path
    ProductVersion = $manifest.product_version
    RuntimeIdentifier = $manifest.runtime_identifier
    SigningRequired = [bool]$manifest.signing.required
    SigningStatus = $manifest.signing.status
}
