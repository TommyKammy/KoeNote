param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$IncludeLegacyRuntimeTools,
    [switch]$RequireBundledPythonRuntime,
    [switch]$RequireReviewRuntime
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$project = Join-Path $repoRoot "src\KoeNote.App\KoeNote.App.csproj"
$publishDir = Join-Path $repoRoot "artifacts\publish\KoeNote-$RuntimeIdentifier"

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

dotnet publish $project `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishDir="$publishDir\" `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:PublishTrimmed=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

New-Item -ItemType Directory -Force -Path (Join-Path $publishDir "tools") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $publishDir "models\asr") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $publishDir "models\review") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $publishDir "samples") | Out-Null

$toolsSource = Join-Path $repoRoot "tools"
$ffmpegSource = Join-Path $toolsSource "ffmpeg.exe"
if (Test-Path -LiteralPath $ffmpegSource) {
    Copy-Item -LiteralPath $ffmpegSource -Destination (Join-Path $publishDir "tools\ffmpeg.exe") -Force
}
else {
    throw "Missing required Phase 10 core runtime: $ffmpegSource. Place ffmpeg.exe there before publishing KoeNote Core."
}

$pythonRuntimeSource = Join-Path $toolsSource "python"
$pythonRuntimeDestination = Join-Path $publishDir "tools\python"
if (Test-Path -LiteralPath $pythonRuntimeSource -PathType Container) {
    New-Item -ItemType Directory -Force -Path $pythonRuntimeDestination | Out-Null
    Get-ChildItem -LiteralPath $pythonRuntimeSource -Force |
        Copy-Item -Destination $pythonRuntimeDestination -Recurse -Force
}
elseif ($RequireBundledPythonRuntime) {
    throw "Missing bundled Python runtime: $pythonRuntimeSource. Place Python 3.12 x64 runtime there before publishing with -RequireBundledPythonRuntime."
}

$reviewRuntimeSource = Join-Path $toolsSource "review"
$reviewRuntimeDestination = Join-Path $publishDir "tools\review"
if (Test-Path -LiteralPath $reviewRuntimeSource -PathType Container) {
    New-Item -ItemType Directory -Force -Path $reviewRuntimeDestination | Out-Null
    Get-ChildItem -LiteralPath $reviewRuntimeSource -Force |
        Copy-Item -Destination $reviewRuntimeDestination -Recurse -Force
}
elseif ($RequireReviewRuntime) {
    throw "Missing review runtime: $reviewRuntimeSource. Place llama.cpp CPU x64 runtime files there before publishing with -RequireReviewRuntime."
}

$ternaryReviewRuntimeSource = Join-Path $toolsSource "review-ternary"
$ternaryReviewRuntimeDestination = Join-Path $publishDir "tools\review-ternary"
if (Test-Path -LiteralPath $ternaryReviewRuntimeSource -PathType Container) {
    New-Item -ItemType Directory -Force -Path $ternaryReviewRuntimeDestination | Out-Null
    Get-ChildItem -LiteralPath $ternaryReviewRuntimeSource -Force |
        Copy-Item -Destination $ternaryReviewRuntimeDestination -Recurse -Force
}
elseif ($RequireReviewRuntime) {
    throw "Missing ternary review runtime: $ternaryReviewRuntimeSource. Place Prism llama.cpp CPU x64 runtime files there before publishing with -RequireReviewRuntime."
}

$legacyRuntimeReadme = @"
ASR runtime tools are intentionally not included in the Phase 10 core package.

ASR uses the bundled Python runtime and installs faster-whisper into a managed environment during first-run setup.
For local developer smoke tests, rerun Publish-KoeNote.ps1 with -IncludeLegacyRuntimeTools to copy the current tools/asr folder.
"@

if ($IncludeLegacyRuntimeTools -and (Test-Path -LiteralPath $toolsSource)) {
    $asrTools = Join-Path $toolsSource "asr"
    if (Test-Path -LiteralPath $asrTools) {
        Copy-Item -LiteralPath $asrTools -Destination (Join-Path $publishDir "tools\asr") -Recurse -Force
    }
}
elseif (-not (Test-Path -LiteralPath $reviewRuntimeDestination -PathType Container)) {
    Set-Content -LiteralPath (Join-Path $publishDir "tools\README-runtime-tools-not-included.txt") -Value $legacyRuntimeReadme -Encoding UTF8
}

Set-Content -LiteralPath (Join-Path $publishDir "models\README-models-not-included.txt") -Value @"
ASR and review LLM model binaries are intentionally not included in KoeNote Core.

Install models later through the Phase 11 Model Catalog / Download Manager, the Phase 12 Setup Wizard, a local model registration flow, or an offline model pack.
"@ -Encoding UTF8

Set-Content -LiteralPath (Join-Path $publishDir "models\asr\README-ASR-models-not-included.txt") -Value "ASR models are installed after KoeNote Core is installed." -Encoding UTF8
Set-Content -LiteralPath (Join-Path $publishDir "models\review\README-review-models-not-included.txt") -Value "Review LLM models are installed after KoeNote Core is installed." -Encoding UTF8
Set-Content -LiteralPath (Join-Path $publishDir "samples\README-sample-audio.txt") -Value "Small sample audio for first-run smoke checks. Keep private or large evaluation audio outside the core package." -Encoding UTF8

$samplePath = Join-Path $publishDir "samples\koenote-smoke-1s.wav"
$sampleRate = 16000
$channels = [int16]1
$bitsPerSample = [int16]16
$durationSeconds = 1
$sampleCount = $sampleRate * $durationSeconds
$blockAlign = [int16]($channels * $bitsPerSample / 8)
$byteRate = $sampleRate * $blockAlign
$dataSize = $sampleCount * $blockAlign

$writer = [IO.BinaryWriter]::new([IO.File]::Create($samplePath))
try {
    $writer.Write([Text.Encoding]::ASCII.GetBytes("RIFF"))
    $writer.Write([int32](36 + $dataSize))
    $writer.Write([Text.Encoding]::ASCII.GetBytes("WAVE"))
    $writer.Write([Text.Encoding]::ASCII.GetBytes("fmt "))
    $writer.Write([int32]16)
    $writer.Write([int16]1)
    $writer.Write($channels)
    $writer.Write([int32]$sampleRate)
    $writer.Write([int32]$byteRate)
    $writer.Write($blockAlign)
    $writer.Write($bitsPerSample)
    $writer.Write([Text.Encoding]::ASCII.GetBytes("data"))
    $writer.Write([int32]$dataSize)

    for ($i = 0; $i -lt $sampleCount; $i++) {
        $sample = [int16]([Math]::Sin(2 * [Math]::PI * 440 * $i / $sampleRate) * [int16]::MaxValue * 0.08)
        $writer.Write($sample)
    }
}
finally {
    $writer.Dispose()
}

Write-Host "Published KoeNote to $publishDir"
