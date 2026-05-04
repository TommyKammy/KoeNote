param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$IncludeLegacyRuntimeTools
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

$legacyRuntimeReadme = @"
ASR and review runtime tools are intentionally not included in the Phase 10 core package.

Phase 11 will introduce Model Catalog / Download Manager support for installing ASR and review runtimes after the app is installed.
For local developer smoke tests, rerun Publish-KoeNote.ps1 with -IncludeLegacyRuntimeTools to copy the current tools/asr and tools/review folders.
"@

if ($IncludeLegacyRuntimeTools -and (Test-Path -LiteralPath $toolsSource)) {
    $asrTools = Join-Path $toolsSource "asr"
    $reviewTools = Join-Path $toolsSource "review"
    if (Test-Path -LiteralPath $asrTools) {
        Copy-Item -LiteralPath $asrTools -Destination (Join-Path $publishDir "tools\asr") -Recurse -Force
    }

    if (Test-Path -LiteralPath $reviewTools) {
        Copy-Item -LiteralPath $reviewTools -Destination (Join-Path $publishDir "tools\review") -Recurse -Force
    }
}
else {
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
