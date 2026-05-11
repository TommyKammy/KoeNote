param(
    [string]$PublishDir = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")) "artifacts\publish\KoeNote-win-x64"),
    [switch]$RequireBundledPythonRuntime,
    [switch]$RequireReviewRuntime
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

$requiredFilePatterns = @(
    "tools\avcodec-*.dll",
    "tools\avdevice-*.dll",
    "tools\avfilter-*.dll",
    "tools\avformat-*.dll",
    "tools\avutil-*.dll",
    "tools\swresample-*.dll",
    "tools\swscale-*.dll"
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

foreach ($relativePattern in $requiredFilePatterns) {
    $pattern = Join-Path $PublishDir $relativePattern
    $matches = Get-ChildItem -Path $pattern -File -ErrorAction SilentlyContinue
    if ($matches.Count -eq 0) {
        throw "Missing required file matching pattern: $pattern"
    }
}

foreach ($relativePath in $requiredDirectories) {
    $path = Join-Path $PublishDir $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Container)) {
        throw "Missing required directory: $path"
    }
}

if ($RequireBundledPythonRuntime) {
    $pythonPath = Join-Path $PublishDir "tools\python\python.exe"
    if (-not (Test-Path -LiteralPath $pythonPath -PathType Leaf)) {
        throw "Missing bundled Python runtime: $pythonPath"
    }
}

if ($RequireReviewRuntime) {
    $reviewRuntimePath = Join-Path $PublishDir "tools\review\llama-completion.exe"
    if (-not (Test-Path -LiteralPath $reviewRuntimePath -PathType Leaf)) {
        throw "Missing review runtime: $reviewRuntimePath"
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
