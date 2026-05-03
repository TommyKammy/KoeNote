param(
    [string]$PublishDir = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")) "artifacts\publish\KoeNote-win-x64")
)

$ErrorActionPreference = "Stop"

$requiredFiles = @(
    "KoeNote.App.exe",
    "README.distribution.md",
    "licenses\license-manifest.json"
)

$requiredDirectories = @(
    "tools",
    "models\asr",
    "models\review"
)

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $PublishDir $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing required file: $path"
    }
}

foreach ($relativePath in $requiredDirectories) {
    $path = Join-Path $PublishDir $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Container)) {
        throw "Missing required directory: $path"
    }
}

Write-Host "Offline smoke layout check passed: $PublishDir"
