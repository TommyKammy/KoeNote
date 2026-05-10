param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$IncludeLegacyRuntimeTools,
    [switch]$IncludeTernaryReviewRuntime,
    [switch]$RequireBundledPythonRuntime,
    [switch]$RequireReviewRuntime,
    [switch]$RequireGpuReadyRuntime
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$project = Join-Path $repoRoot "src\KoeNote.App\KoeNote.App.csproj"
$publishDir = Join-Path $repoRoot "artifacts\publish\KoeNote-$RuntimeIdentifier"

function Get-PayloadRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $rootFull = [IO.Path]::GetFullPath($Root)
    if (-not $rootFull.EndsWith([IO.Path]::DirectorySeparatorChar)) {
        $rootFull += [IO.Path]::DirectorySeparatorChar
    }

    $rootUri = [Uri]$rootFull
    $pathUri = [Uri]([IO.Path]::GetFullPath($Path))
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace('/', '\')
}

function Test-AnyFile {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$Pattern
    )

    return (Test-Path -LiteralPath $Directory -PathType Container) -and
        $null -ne (Get-ChildItem -LiteralPath $Directory -File -Filter $Pattern -ErrorAction SilentlyContinue | Select-Object -First 1)
}

function Assert-RequiredRuntimeFiles {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string[]]$Patterns,
        [Parameter(Mandatory = $true)][string]$RuntimeName
    )

    foreach ($pattern in $Patterns) {
        if (-not (Test-AnyFile -Directory $Directory -Pattern $pattern)) {
            throw "Missing $RuntimeName GPU-ready runtime file matching '$pattern' in $Directory."
        }
    }
}

function Copy-FilteredDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string]$DestinationDir,
        [string[]]$ExcludePatterns = @()
    )

    New-Item -ItemType Directory -Force -Path $DestinationDir | Out-Null
    $sourceRoot = [IO.Path]::GetFullPath($SourceDir)
    $files = Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Force

    foreach ($file in $files) {
        $relative = Get-PayloadRelativePath -Root $sourceRoot -Path $file.FullName
        $normalized = $relative.Replace('/', '\')
        $shouldExclude = $false
        foreach ($pattern in $ExcludePatterns) {
            if ($normalized -like $pattern) {
                $shouldExclude = $true
                break
            }
        }

        if ($shouldExclude) {
            continue
        }

        $destinationPath = Join-Path $DestinationDir $relative
        $destinationParent = Split-Path -Parent $destinationPath
        if (-not (Test-Path -LiteralPath $destinationParent -PathType Container)) {
            New-Item -ItemType Directory -Force -Path $destinationParent | Out-Null
        }

        Copy-Item -LiteralPath $file.FullName -Destination $destinationPath -Force
    }
}

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
    $pythonRuntimeExcludes = @(
        "Lib\site-packages\artifact_tool_v2*",
        "Lib\site-packages\pandas*",
        "Lib\site-packages\numpy*",
        "Lib\site-packages\lxml*",
        "Lib\site-packages\PIL",
        "Lib\site-packages\PIL\*",
        "Lib\site-packages\pillow*",
        "Lib\site-packages\openpyxl*",
        "Lib\site-packages\pdf2image*",
        "Lib\site-packages\pypdf*",
        "Lib\site-packages\docx",
        "Lib\site-packages\docx\*",
        "Lib\site-packages\python_docx*",
        "Lib\site-packages\reportlab*",
        "**\__pycache__\*",
        "*.pyc",
        "*.pyo"
    )
    Copy-FilteredDirectory -SourceDir $pythonRuntimeSource -DestinationDir $pythonRuntimeDestination -ExcludePatterns $pythonRuntimeExcludes
}
elseif ($RequireBundledPythonRuntime) {
    throw "Missing bundled Python runtime: $pythonRuntimeSource. Place Python 3.12 x64 runtime there before publishing with -RequireBundledPythonRuntime."
}

$reviewRuntimeSource = Join-Path $toolsSource "review"
$reviewRuntimeDestination = Join-Path $publishDir "tools\review"
if (Test-Path -LiteralPath $reviewRuntimeSource -PathType Container) {
    $reviewRuntimeExcludes = @(
        "*cublas*.dll",
        "*cudart*.dll",
        "*cudnn*.dll",
        "*cufft*.dll",
        "*curand*.dll",
        "*cusparse*.dll"
    )
    Copy-FilteredDirectory -SourceDir $reviewRuntimeSource -DestinationDir $reviewRuntimeDestination -ExcludePatterns $reviewRuntimeExcludes
    if ($RequireGpuReadyRuntime) {
        Assert-RequiredRuntimeFiles -Directory $reviewRuntimeDestination -Patterns @("llama-completion.exe", "ggml-cuda*.dll") -RuntimeName "review"
    }
}
elseif ($RequireReviewRuntime) {
    throw "Missing review runtime: $reviewRuntimeSource. Place llama.cpp CPU x64 runtime files there before publishing with -RequireReviewRuntime."
}

$ternaryReviewRuntimeSource = Join-Path $toolsSource "review-ternary"
$ternaryReviewRuntimeDestination = Join-Path $publishDir "tools\review-ternary"
if ($IncludeTernaryReviewRuntime -and (Test-Path -LiteralPath $ternaryReviewRuntimeSource -PathType Container)) {
    New-Item -ItemType Directory -Force -Path $ternaryReviewRuntimeDestination | Out-Null
    Get-ChildItem -LiteralPath $ternaryReviewRuntimeSource -Force |
        Copy-Item -Destination $ternaryReviewRuntimeDestination -Recurse -Force
}

$legacyRuntimeReadme = @"
ASR runtime tools are intentionally not included in the Phase 10 core package.

ASR uses the bundled Python runtime and installs faster-whisper into a managed environment during first-run setup.
For local developer smoke tests, rerun Publish-KoeNote.ps1 with -IncludeLegacyRuntimeTools to copy the current tools/asr folder.
"@

$asrRuntimeSource = Join-Path $toolsSource "asr"
$asrRuntimeDestination = Join-Path $publishDir "tools\asr"
if (($IncludeLegacyRuntimeTools -or $RequireGpuReadyRuntime) -and (Test-Path -LiteralPath $asrRuntimeSource -PathType Container)) {
    $asrRuntimeExcludes = @(
        "*cublas*.dll",
        "*cudart*.dll",
        "*cudnn*.dll",
        "*cufft*.dll",
        "*curand*.dll",
        "*cusparse*.dll"
    )
    Copy-FilteredDirectory -SourceDir $asrRuntimeSource -DestinationDir $asrRuntimeDestination -ExcludePatterns $asrRuntimeExcludes
    if ($RequireGpuReadyRuntime) {
        Assert-RequiredRuntimeFiles -Directory $asrRuntimeDestination -Patterns @("crispasr*.exe", "crispasr*.dll", "whisper.dll", "ggml-cuda.dll") -RuntimeName "ASR"
    }
}
elseif ($RequireGpuReadyRuntime) {
    throw "Missing ASR GPU-ready runtime: $asrRuntimeSource. Place KoeNote ASR GPU runtime files there before publishing with -RequireGpuReadyRuntime."
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
