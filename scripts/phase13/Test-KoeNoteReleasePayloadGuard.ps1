param(
    [Parameter(Mandatory = $true)][string]$PayloadDir,
    [int]$MaxReviewRuntimeMB = 120,
    [int]$MaxBundledPythonMB = 120
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

$failures = New-Object System.Collections.Generic.List[string]
$reviewRuntimeDir = Join-Path $payloadRoot "tools\review"
$bundledPythonDir = Join-Path $payloadRoot "tools\python"

$cudaRuntimePatterns = @(
    "ggml-cuda*.dll",
    "cublas*.dll",
    "cudart*.dll",
    "cufft*.dll",
    "curand*.dll",
    "cusparse*.dll"
)

if (Test-Path -LiteralPath $reviewRuntimeDir -PathType Container) {
    foreach ($pattern in $cudaRuntimePatterns) {
        $matches = Get-ChildItem -LiteralPath $reviewRuntimeDir -Recurse -File -Filter $pattern
        foreach ($match in $matches) {
            $relative = Get-RelativePath -Root $payloadRoot -Path $match.FullName
            $failures.Add("CUDA review runtime file is not allowed in the normal MSI payload: $relative ($(Format-Size $match.Length))")
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
            $failures.Add("Forbidden Python package is not allowed in the normal MSI payload: $relative")
        }
    }
}

$reviewRuntimeBytes = Get-DirectorySizeBytes -Path $reviewRuntimeDir
$bundledPythonBytes = Get-DirectorySizeBytes -Path $bundledPythonDir
$maxReviewRuntimeBytes = [int64]$MaxReviewRuntimeMB * 1MB
$maxBundledPythonBytes = [int64]$MaxBundledPythonMB * 1MB

if ($reviewRuntimeBytes -gt $maxReviewRuntimeBytes) {
    $failures.Add("Review runtime payload is too large for the normal MSI: $(Format-Size $reviewRuntimeBytes), limit $(Format-Size $maxReviewRuntimeBytes).")
}

if ($bundledPythonBytes -gt $maxBundledPythonBytes) {
    $failures.Add("Bundled Python payload is too large for the normal MSI: $(Format-Size $bundledPythonBytes), limit $(Format-Size $maxBundledPythonBytes).")
}

if ($failures.Count -gt 0) {
    $message = "Release payload guard failed:`n- " + ($failures -join "`n- ")
    throw $message
}

[pscustomobject]@{
    PayloadDir = $payloadRoot
    ReviewRuntimeMB = [math]::Round($reviewRuntimeBytes / 1MB, 2)
    MaxReviewRuntimeMB = $MaxReviewRuntimeMB
    BundledPythonMB = [math]::Round($bundledPythonBytes / 1MB, 2)
    MaxBundledPythonMB = $MaxBundledPythonMB
}
