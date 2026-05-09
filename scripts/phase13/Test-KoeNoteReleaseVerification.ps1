param(
    [Parameter(Mandatory = $true)][string]$MsiPath,
    [string]$ManifestPath = "",
    [switch]$SkipVersioningTests
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$artifactIntegrityScript = Join-Path $repoRoot "scripts\phase13\Test-KoeNoteArtifactIntegrity.ps1"
$payloadGuardScript = Join-Path $repoRoot "scripts\phase13\Test-KoeNoteReleasePayloadGuard.ps1"
$testProject = Join-Path $repoRoot "tests\KoeNote.App.Tests\KoeNote.App.Tests.csproj"
$expectedTernaryReviewRuntimeTag = "prism-b8846-d104cf1"
$expectedTernaryReviewRuntimeSourceUrl = "https://github.com/PrismML-Eng/llama.cpp/releases/download/$expectedTernaryReviewRuntimeTag/llama-bin-win-cpu-x64.zip"
$expectedCudaReleaseAssets = @(
    "https://github.com/TommyKammy/KoeNote/releases/latest/download/koenote-cuda-asr-runtime.zip",
    "https://github.com/TommyKammy/KoeNote/releases/latest/download/koenote-cuda-review-runtime.zip"
)

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

$publishDir = Join-Path $repoRoot "artifacts\publish\KoeNote-$($manifest.runtime_identifier)"
$payloadGuardResult = & $payloadGuardScript -PayloadDir $publishDir
$pythonPath = Join-Path $publishDir "tools\python\python.exe"
if (-not (Test-Path -LiteralPath $pythonPath -PathType Leaf)) {
    throw "Bundled Python runtime is required but missing from publish output: $pythonPath"
}

$reviewRuntimePath = Join-Path $publishDir "tools\review\llama-completion.exe"
if (-not (Test-Path -LiteralPath $reviewRuntimePath -PathType Leaf)) {
    throw "Review runtime is required but missing from publish output: $reviewRuntimePath"
}

$ternaryReviewRuntimePath = Join-Path $publishDir "tools\review-ternary\llama-completion.exe"
$ternaryReviewRuntimePresent = Test-Path -LiteralPath $ternaryReviewRuntimePath -PathType Leaf

if (-not ($manifest.PSObject.Properties.Name -contains "bundled_python_runtime")) {
    throw "Release manifest is missing bundled_python_runtime metadata."
}

if (-not [bool]$manifest.bundled_python_runtime.required) {
    throw "Release manifest must mark bundled_python_runtime.required as true."
}

if ($manifest.bundled_python_runtime.path -ne $pythonPath) {
    throw "Release manifest bundled Python path does not match. Expected $pythonPath but got $($manifest.bundled_python_runtime.path)."
}

if (-not ($manifest.PSObject.Properties.Name -contains "review_runtime")) {
    throw "Release manifest is missing review_runtime metadata."
}

if (-not [bool]$manifest.review_runtime.required) {
    throw "Release manifest must mark review_runtime.required as true."
}

if (-not ($manifest.PSObject.Properties.Name -contains "ternary_review_runtime")) {
    throw "Release manifest is missing ternary_review_runtime metadata."
}

if ([bool]$manifest.ternary_review_runtime.required) {
    throw "Release manifest must mark ternary_review_runtime.required as false because Ternary Bonsai is hidden."
}

if ([bool]$manifest.ternary_review_runtime.present -ne [bool]$ternaryReviewRuntimePresent) {
    throw "Release manifest ternary review runtime present flag does not match publish output."
}

if ($manifest.ternary_review_runtime.tag -ne $expectedTernaryReviewRuntimeTag) {
    throw "Release manifest ternary review runtime tag does not match. Expected $expectedTernaryReviewRuntimeTag but got $($manifest.ternary_review_runtime.tag)."
}

if ($manifest.ternary_review_runtime.source_url -ne $expectedTernaryReviewRuntimeSourceUrl) {
    throw "Release manifest ternary review runtime source_url does not match. Expected $expectedTernaryReviewRuntimeSourceUrl but got $($manifest.ternary_review_runtime.source_url)."
}

if ($manifest.review_runtime.path -ne $reviewRuntimePath) {
    throw "Release manifest review runtime path does not match. Expected $reviewRuntimePath but got $($manifest.review_runtime.path)."
}

if ($manifest.ternary_review_runtime.path -ne $ternaryReviewRuntimePath) {
    throw "Release manifest ternary review runtime path does not match. Expected $ternaryReviewRuntimePath but got $($manifest.ternary_review_runtime.path)."
}

[pscustomobject]@{
    MsiPath = $msi.Path
    Sha256 = $sidecarSha256
    ManifestPath = (Resolve-Path $ManifestPath).Path
    ProductVersion = $manifest.product_version
    RuntimeIdentifier = $manifest.runtime_identifier
    SigningRequired = [bool]$manifest.signing.required
    SigningStatus = $manifest.signing.status
    ReviewRuntimeMB = $payloadGuardResult.ReviewRuntimeMB
    BundledPythonMB = $payloadGuardResult.BundledPythonMB
}

foreach ($assetUrl in $expectedCudaReleaseAssets) {
    try {
        $response = Invoke-WebRequest -Uri $assetUrl -Method Head -MaximumRedirection 5 -TimeoutSec 30
        if ([int]$response.StatusCode -lt 200 -or [int]$response.StatusCode -ge 400) {
            throw "HTTP $($response.StatusCode)"
        }
    }
    catch {
        throw "Required CUDA release asset is missing or unreachable: $assetUrl. Details: $($_.Exception.Message)"
    }
}
