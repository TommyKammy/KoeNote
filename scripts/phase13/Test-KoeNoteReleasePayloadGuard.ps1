param(
    [Parameter(Mandatory = $true)][string]$PayloadDir,
    [int]$MaxReviewRuntimeMB = 700,
    [int]$MaxAsrRuntimeMB = 180,
    [int]$MaxBundledPythonMB = 120,
    [int]$MaxFfmpegRuntimeMB = 280
)

$ErrorActionPreference = "Stop"

$payloadRoot = [IO.Path]::GetFullPath($PayloadDir)
if (-not (Test-Path -LiteralPath $payloadRoot -PathType Container)) {
    throw "PayloadDir does not exist: $payloadRoot"
}

function Get-DirectorySizeBytes {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return 0L
    }

    $sum = (Get-ChildItem -LiteralPath $Path -Recurse -File | Measure-Object Length -Sum).Sum
    if ($null -eq $sum) {
        return 0L
    }

    return [int64]$sum
}

function Format-Size {
    param([Parameter(Mandatory = $true)][int64]$Bytes)

    return "{0:N2} MB" -f ($Bytes / 1MB)
}

function Get-RelativePath {
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

function Test-FileContainsText {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string]$Text
    )

    if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf)) {
        return $false
    }

    $bytes = [IO.File]::ReadAllBytes($FilePath)
    $needle = [Text.Encoding]::ASCII.GetBytes($Text)
    if ($needle.Length -eq 0 -or $bytes.Length -lt $needle.Length) {
        return $false
    }

    for ($index = 0; $index -le $bytes.Length - $needle.Length; $index++) {
        $matched = $true
        for ($offset = 0; $offset -lt $needle.Length; $offset++) {
            if ($bytes[$index + $offset] -ne $needle[$offset]) {
                $matched = $false
                break
            }
        }

        if ($matched) {
            return $true
        }
    }

    return $false
}

$failures = New-Object System.Collections.Generic.List[string]
$reviewRuntimeDir = Join-Path $payloadRoot "tools\review"
$asrRuntimeDir = Join-Path $payloadRoot "tools\asr"
$asrCTranslate2RuntimeDir = Join-Path $payloadRoot "tools\asr-ctranslate2-cuda"
$bundledPythonDir = Join-Path $payloadRoot "tools\python"
$ffmpegRuntimeDir = Join-Path $payloadRoot "tools"

foreach ($requiredReviewRuntime in @("llama-completion.exe", "llama-server.exe", "llama-server-impl.dll")) {
    $requiredReviewRuntimePath = Join-Path $reviewRuntimeDir $requiredReviewRuntime
    if (-not (Test-Path -LiteralPath $requiredReviewRuntimePath -PathType Leaf)) {
        $failures.Add("Review runtime payload is missing required file: tools\review\$requiredReviewRuntime")
    }
}

$reviewServerRuntimePath = Join-Path $reviewRuntimeDir "llama-server.exe"
$reviewServerImplementationPath = Join-Path $reviewRuntimeDir "llama-server-impl.dll"
if ((Test-Path -LiteralPath $reviewServerRuntimePath -PathType Leaf) -and
    (Test-Path -LiteralPath $reviewServerImplementationPath -PathType Leaf) -and
    -not (Test-FileContainsText -FilePath $reviewServerImplementationPath -Text "931eb37f8")) {
    $failures.Add("Review server runtime does not match expected llama.cpp b9848 build 931eb37f8: tools\review\llama-server-impl.dll")
}

$ffmpegRequiredPatterns = @(
    "avcodec-*.dll",
    "avdevice-*.dll",
    "avfilter-*.dll",
    "avformat-*.dll",
    "avutil-*.dll",
    "swresample-*.dll",
    "swscale-*.dll"
)

foreach ($pattern in $ffmpegRequiredPatterns) {
    $matches = Get-ChildItem -LiteralPath $ffmpegRuntimeDir -File -Filter $pattern -ErrorAction SilentlyContinue
    if ($matches.Count -eq 0) {
        $failures.Add("FFmpeg shared runtime dependency is missing from the MSI payload: tools\$pattern")
    }
}

$nvidiaRedistributablePatterns = @(
    "cublas*.dll",
    "cudart*.dll",
    "cudnn*.dll",
    "cufft*.dll",
    "curand*.dll",
    "cusparse*.dll"
)

foreach ($runtimeDir in @($reviewRuntimeDir, $asrRuntimeDir, $asrCTranslate2RuntimeDir)) {
    if (-not (Test-Path -LiteralPath $runtimeDir -PathType Container)) {
        continue
    }

    foreach ($pattern in $nvidiaRedistributablePatterns) {
        $matches = Get-ChildItem -LiteralPath $runtimeDir -Recurse -File -Filter $pattern
        foreach ($match in $matches) {
            $relative = Get-RelativePath -Root $payloadRoot -Path $match.FullName
            $failures.Add("NVIDIA redistributable runtime file is not allowed in the GPU-ready MSI payload: $relative ($(Format-Size $match.Length))")
        }
    }
}

$forbiddenPythonPackages = @(
    "artifact_tool_v2",
    "pandas",
    "pandas.libs",
    "numpy",
    "numpy.libs",
    "lxml",
    "PIL",
    "pillow",
    "pillow.libs",
    "openpyxl",
    "pdf2image",
    "pypdf",
    "docx",
    "python_docx",
    "reportlab"
)

$sitePackagesDir = Join-Path $bundledPythonDir "Lib\site-packages"
if (Test-Path -LiteralPath $sitePackagesDir -PathType Container) {
    $packageNames = Get-ChildItem -LiteralPath $sitePackagesDir -Directory | ForEach-Object { $_.Name }
    foreach ($forbiddenPackage in $forbiddenPythonPackages) {
        $packageMatches = $packageNames | Where-Object {
            $_.Equals($forbiddenPackage, [StringComparison]::OrdinalIgnoreCase) -or
            $_.StartsWith("$forbiddenPackage-", [StringComparison]::OrdinalIgnoreCase)
        }

        foreach ($packageMatch in $packageMatches) {
            $packagePath = Join-Path $sitePackagesDir $packageMatch
            $relative = Get-RelativePath -Root $payloadRoot -Path $packagePath
            $failures.Add("Forbidden Python package is not allowed in the GPU-ready MSI payload: $relative")
        }
    }
}

$reviewRuntimeBytes = Get-DirectorySizeBytes -Path $reviewRuntimeDir
$asrRuntimeBytes = Get-DirectorySizeBytes -Path $asrRuntimeDir
$bundledPythonBytes = Get-DirectorySizeBytes -Path $bundledPythonDir
$ffmpegRuntimeBytes = 0L
if (Test-Path -LiteralPath $ffmpegRuntimeDir -PathType Container) {
    $ffmpegRuntimeFiles = Get-ChildItem -LiteralPath $ffmpegRuntimeDir -File |
        Where-Object { $_.Name -eq "ffmpeg.exe" -or $_.Name -match '^(avcodec|avdevice|avfilter|avformat|avutil|swresample|swscale)-.*\.dll$' }
    $ffmpegRuntimeSum = ($ffmpegRuntimeFiles | Measure-Object Length -Sum).Sum
    if ($null -ne $ffmpegRuntimeSum) {
        $ffmpegRuntimeBytes = [int64]$ffmpegRuntimeSum
    }
}
$maxReviewRuntimeBytes = [int64]$MaxReviewRuntimeMB * 1MB
$maxAsrRuntimeBytes = [int64]$MaxAsrRuntimeMB * 1MB
$maxBundledPythonBytes = [int64]$MaxBundledPythonMB * 1MB
$maxFfmpegRuntimeBytes = [int64]$MaxFfmpegRuntimeMB * 1MB

if ($reviewRuntimeBytes -gt $maxReviewRuntimeBytes) {
    $failures.Add("Review runtime payload is too large for the GPU-ready MSI: $(Format-Size $reviewRuntimeBytes), limit $(Format-Size $maxReviewRuntimeBytes).")
}

if ($asrRuntimeBytes -gt $maxAsrRuntimeBytes) {
    $failures.Add("ASR runtime payload is too large for the GPU-ready MSI: $(Format-Size $asrRuntimeBytes), limit $(Format-Size $maxAsrRuntimeBytes).")
}

if ($bundledPythonBytes -gt $maxBundledPythonBytes) {
    $failures.Add("Bundled Python payload is too large for the GPU-ready MSI: $(Format-Size $bundledPythonBytes), limit $(Format-Size $maxBundledPythonBytes).")
}

if ($ffmpegRuntimeBytes -gt $maxFfmpegRuntimeBytes) {
    $failures.Add("FFmpeg runtime payload is too large for the GPU-ready MSI: $(Format-Size $ffmpegRuntimeBytes), limit $(Format-Size $maxFfmpegRuntimeBytes).")
}

if ($failures.Count -gt 0) {
    $message = "Release payload guard failed:`n- " + ($failures -join "`n- ")
    throw $message
}

[pscustomobject]@{
    PayloadDir = $payloadRoot
    ReviewRuntimeMB = [math]::Round($reviewRuntimeBytes / 1MB, 2)
    MaxReviewRuntimeMB = $MaxReviewRuntimeMB
    AsrRuntimeMB = [math]::Round($asrRuntimeBytes / 1MB, 2)
    MaxAsrRuntimeMB = $MaxAsrRuntimeMB
    BundledPythonMB = [math]::Round($bundledPythonBytes / 1MB, 2)
    MaxBundledPythonMB = $MaxBundledPythonMB
    FfmpegRuntimeMB = [math]::Round($ffmpegRuntimeBytes / 1MB, 2)
    MaxFfmpegRuntimeMB = $MaxFfmpegRuntimeMB
}
