param(
    [string]$PublishDir = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")) "artifacts\publish\KoeNote-win-x64")
)

$ErrorActionPreference = "Stop"

$requiredFiles = @(
    "KoeNote.App.exe",
    "README.distribution.md",
    "licenses\license-manifest.json",
    "tools\ffmpeg.exe",
    "models\README-models-not-included.txt",
    "samples\README-sample-audio.txt",
    "samples\koenote-smoke-1s.wav"
)

$requiredDirectories = @(
    "tools",
    "models\asr",
    "models\review",
    "samples"
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

$forbiddenModelFiles = @(
    "models\asr\vibevoice-asr-q4_k.gguf",
    "models\review\llm-jp-4-8B-thinking-Q4_K_M.gguf"
)

foreach ($relativePath in $forbiddenModelFiles) {
    $path = Join-Path $PublishDir $relativePath
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        throw "Core package must not include model binary: $path"
    }
}

Write-Host "Offline smoke layout check passed: $PublishDir"
