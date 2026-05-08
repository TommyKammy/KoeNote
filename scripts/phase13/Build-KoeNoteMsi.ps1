param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$ProductVersion = "",
    [string]$Publisher = "KoeNote Project",
    [string]$ProductName = "KoeNote",
    [string]$OutputName = "",
    [string]$UpgradeCode = "{C73B4C12-0F3B-4B7A-8F4C-43D1C8C5EB1D}",
    [string]$InstallFolderName = "KoeNote",
    [string]$StartMenuFolderName = "KoeNote",
    [string]$ShortcutComponentGuid = "{4CC8E09B-7104-4D95-A5D3-3682E68BD080}",
    [string]$ArpMetadataComponentGuid = "{39818D14-4A4F-4B12-A29D-67E13FC97FD3}",
    [string]$ProductRegistryKey = "Software\KoeNote\KoeNote",
    [string]$ComponentGuidSalt = "KoeNote",
    [string]$CleanupUiCommand = "",
    [string]$CleanupQuietCommand = "--quiet",
    [switch]$RequireCodeSigning
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$publishScript = Join-Path $repoRoot "scripts\phase10\Publish-KoeNote.ps1"
$payloadScript = Join-Path $repoRoot "scripts\phase13\New-WixPayload.ps1"
$cleanupProject = Join-Path $repoRoot "src\KoeNote.Cleanup\KoeNote.Cleanup.csproj"
$installerProject = Join-Path $repoRoot "src\KoeNote.Installer\KoeNote.Installer.wixproj"
$publishDir = Join-Path $repoRoot "artifacts\publish\KoeNote-$RuntimeIdentifier"
$generatedWxs = Join-Path $repoRoot "src\KoeNote.Installer\PayloadFiles.wxs"
$installerOut = Join-Path $repoRoot "artifacts\msi"
$ternaryReviewRuntimeTag = "prism-b8846-d104cf1"
$ternaryReviewRuntimeSourceUrl = "https://github.com/PrismML-Eng/llama.cpp/releases/download/$ternaryReviewRuntimeTag/llama-bin-win-cpu-x64.zip"
$updateLogDir = Join-Path $repoRoot "artifacts\logs\updates"
New-Item -ItemType Directory -Force -Path $updateLogDir | Out-Null
$updateLogPath = Join-Path $updateLogDir "release-build-$([DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss')).jsonl"
$signedArtifactPaths = @()
$codeSigningSkippedReasons = @()

function Write-UpdateLog {
    param(
        [Parameter(Mandatory = $true)][string]$Event,
        [hashtable]$Data = @{}
    )

    $entry = [ordered]@{
        timestamp = [DateTimeOffset]::Now.ToString("o")
        event = $Event
    }

    foreach ($key in $Data.Keys) {
        $entry[$key] = $Data[$key]
    }

    $entry | ConvertTo-Json -Compress | Add-Content -LiteralPath $updateLogPath -Encoding UTF8
}

function Invoke-CodeSigningIfConfigured {
    param(
        [Parameter(Mandatory = $true)][string[]]$Paths
    )

    $signToolPath = $env:KOENOTE_SIGNTOOL_PATH
    if ([string]::IsNullOrWhiteSpace($signToolPath)) {
        $reason = "KOENOTE_SIGNTOOL_PATH is not set"
        Write-UpdateLog "code_signing_skipped" @{ reason = $reason; required = [bool]$RequireCodeSigning }
        $script:codeSigningSkippedReasons += $reason
        if ($RequireCodeSigning) {
            Write-UpdateLog "code_signing_failed" @{ reason = $reason }
            throw "Code signing is required, but $reason."
        }
        return
    }

    if (-not (Test-Path -LiteralPath $signToolPath)) {
        Write-UpdateLog "code_signing_failed" @{ reason = "KOENOTE_SIGNTOOL_PATH points to a missing file"; path = $signToolPath }
        throw "KOENOTE_SIGNTOOL_PATH points to a missing file: $signToolPath"
    }

    $signArgs = @("sign", "/fd", "SHA256")
    if (-not [string]::IsNullOrWhiteSpace($env:KOENOTE_SIGN_CERT_SHA1)) {
        $signArgs += @("/sha1", $env:KOENOTE_SIGN_CERT_SHA1)
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:KOENOTE_SIGN_CERT_PATH)) {
        $signArgs += @("/f", $env:KOENOTE_SIGN_CERT_PATH)
        if (-not [string]::IsNullOrWhiteSpace($env:KOENOTE_SIGN_CERT_PASSWORD)) {
            $signArgs += @("/p", $env:KOENOTE_SIGN_CERT_PASSWORD)
        }
    }
    else {
        $reason = "No signing certificate was configured"
        Write-UpdateLog "code_signing_skipped" @{ reason = $reason; required = [bool]$RequireCodeSigning }
        $script:codeSigningSkippedReasons += $reason
        if ($RequireCodeSigning) {
            Write-UpdateLog "code_signing_failed" @{ reason = $reason }
            throw "Code signing is required, but $reason."
        }
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($env:KOENOTE_SIGN_TIMESTAMP_URL)) {
        $signArgs += @("/tr", $env:KOENOTE_SIGN_TIMESTAMP_URL, "/td", "SHA256")
    }

    foreach ($path in $Paths) {
        if (-not (Test-Path -LiteralPath $path)) {
            Write-UpdateLog "code_signing_failed" @{ reason = "Target file is missing"; path = $path }
            throw "Cannot sign missing file: $path"
        }

        Write-UpdateLog "code_signing_started" @{ path = $path }
        & $signToolPath @signArgs $path
        if ($LASTEXITCODE -ne 0) {
            Write-UpdateLog "code_signing_failed" @{ path = $path; exit_code = $LASTEXITCODE }
            throw "Code signing failed for $path with exit code $LASTEXITCODE."
        }
        Write-UpdateLog "code_signing_completed" @{ path = $path }
        $script:signedArtifactPaths += $path
    }
}

if ([string]::IsNullOrWhiteSpace($ProductVersion)) {
    $versionPropsPath = Join-Path $repoRoot "Directory.Build.props"
    if (-not (Test-Path -LiteralPath $versionPropsPath)) {
        throw "ProductVersion was not provided and Directory.Build.props was not found."
    }

    [xml]$versionProps = Get-Content -LiteralPath $versionPropsPath
    $ProductVersion = $versionProps.Project.PropertyGroup.VersionPrefix | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($ProductVersion)) {
        throw "ProductVersion was not provided and VersionPrefix was not found in Directory.Build.props."
    }
}

if ($ProductVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "ProductVersion '$ProductVersion' must use MSI-compatible numeric x.y.z format."
}

if ([string]::IsNullOrWhiteSpace($OutputName)) {
    $OutputName = "$ProductName-v$ProductVersion-$RuntimeIdentifier"
}

Write-UpdateLog "release_build_started" @{
    configuration = $Configuration
    runtime_identifier = $RuntimeIdentifier
    product_version = $ProductVersion
    output_name = $OutputName
    require_code_signing = [bool]$RequireCodeSigning
    require_bundled_python_runtime = $true
    require_review_runtime = $true
    log_path = $updateLogPath
}

& powershell -NoProfile -ExecutionPolicy Bypass -File $publishScript -Configuration $Configuration -RuntimeIdentifier $RuntimeIdentifier -RequireBundledPythonRuntime -RequireReviewRuntime
if ($LASTEXITCODE -ne 0) {
    throw "Publish-KoeNote.ps1 failed with exit code $LASTEXITCODE."
}
Write-UpdateLog "app_published" @{ publish_dir = $publishDir }

$bundledPythonPath = Join-Path $publishDir "tools\python\python.exe"
if (-not (Test-Path -LiteralPath $bundledPythonPath -PathType Leaf)) {
    throw "Bundled Python runtime is required for release MSI builds but was not published: $bundledPythonPath"
}
Write-UpdateLog "bundled_python_runtime_verified" @{ path = $bundledPythonPath }

$reviewRuntimePath = Join-Path $publishDir "tools\review\llama-completion.exe"
if (-not (Test-Path -LiteralPath $reviewRuntimePath -PathType Leaf)) {
    throw "Review runtime is required for release MSI builds but was not published: $reviewRuntimePath"
}
Write-UpdateLog "review_runtime_verified" @{ path = $reviewRuntimePath }

$ternaryReviewRuntimePath = Join-Path $publishDir "tools\review-ternary\llama-completion.exe"
$ternaryReviewRuntimePresent = Test-Path -LiteralPath $ternaryReviewRuntimePath -PathType Leaf
if ($ternaryReviewRuntimePresent) {
    Write-UpdateLog "ternary_review_runtime_verified" @{ path = $ternaryReviewRuntimePath; required = $false }
}
else {
    Write-UpdateLog "ternary_review_runtime_skipped" @{ path = $ternaryReviewRuntimePath; required = $false; reason = "Ternary Bonsai is hidden and not shipped by default." }
}

dotnet publish $cleanupProject `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishDir="$publishDir\" `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:PublishTrimmed=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish for KoeNote.Cleanup failed with exit code $LASTEXITCODE."
}
Write-UpdateLog "cleanup_published" @{ publish_dir = $publishDir }

Invoke-CodeSigningIfConfigured -Paths @(
    (Join-Path $publishDir "KoeNote.App.exe"),
    (Join-Path $publishDir "KoeNoteCleanup.exe")
)

& powershell -NoProfile -ExecutionPolicy Bypass -File $payloadScript -PayloadDir $publishDir -OutputPath $generatedWxs -ComponentGuidSalt $ComponentGuidSalt -ProductRegistryKey $ProductRegistryKey
if ($LASTEXITCODE -ne 0) {
    throw "New-WixPayload.ps1 failed with exit code $LASTEXITCODE."
}
Write-UpdateLog "wix_payload_generated" @{ wxs_path = $generatedWxs }

New-Item -ItemType Directory -Force -Path $installerOut | Out-Null

dotnet build $installerProject `
    -c $Configuration `
    -p:OutputName="$OutputName" `
    -p:ProductName="$ProductName" `
    -p:ProductVersion=$ProductVersion `
    -p:Publisher="$Publisher" `
    -p:UpgradeCode="$UpgradeCode" `
    -p:InstallFolderName="$InstallFolderName" `
    -p:StartMenuFolderName="$StartMenuFolderName" `
    -p:ShortcutComponentGuid="$ShortcutComponentGuid" `
    -p:ArpMetadataComponentGuid="$ArpMetadataComponentGuid" `
    -p:ProductRegistryKey="$ProductRegistryKey" `
    -p:CleanupUiCommand="$CleanupUiCommand" `
    -p:CleanupQuietCommand="$CleanupQuietCommand" `
    -p:PayloadDir="$publishDir\" `
    -p:OutputPath="$installerOut\"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build for KoeNote.Installer failed with exit code $LASTEXITCODE."
}

$msiArtifactPath = Join-Path $installerOut "$OutputName.msi"
if (-not (Test-Path -LiteralPath $msiArtifactPath)) {
    throw "Expected MSI artifact was not produced: $msiArtifactPath"
}

$msiArtifact = Get-Item -LiteralPath $msiArtifactPath

Invoke-CodeSigningIfConfigured -Paths @($msiArtifact.FullName)

$hash = Get-FileHash -LiteralPath $msiArtifact.FullName -Algorithm SHA256
$hashPath = "$($msiArtifact.FullName).sha256"
"$($hash.Hash.ToLowerInvariant())  $($msiArtifact.Name)" | Set-Content -LiteralPath $hashPath -Encoding ASCII
$signingStatus = if ($signedArtifactPaths.Count -gt 0 -and $codeSigningSkippedReasons.Count -eq 0) {
    "signed"
}
elseif ($codeSigningSkippedReasons.Count -gt 0) {
    "skipped"
}
else {
    "not_configured"
}
$manifestPath = Join-Path $installerOut "$OutputName.release-manifest.json"
$manifest = [ordered]@{
    schema_version = 1
    generated_at = [DateTimeOffset]::Now.ToString("o")
    product_name = $ProductName
    product_version = $ProductVersion
    runtime_identifier = $RuntimeIdentifier
    configuration = $Configuration
    output_name = $OutputName
    artifacts = [ordered]@{
        msi_path = $msiArtifact.FullName
        sha256_path = $hashPath
        update_log_path = $updateLogPath
    }
    bundled_python_runtime = [ordered]@{
        required = $true
        path = $bundledPythonPath
    }
    review_runtime = [ordered]@{
        required = $true
        path = $reviewRuntimePath
    }
    ternary_review_runtime = [ordered]@{
        required = $false
        present = [bool]$ternaryReviewRuntimePresent
        tag = $ternaryReviewRuntimeTag
        source_url = $ternaryReviewRuntimeSourceUrl
        path = $ternaryReviewRuntimePath
    }
    sha256 = $hash.Hash.ToLowerInvariant()
    signing = [ordered]@{
        required = [bool]$RequireCodeSigning
        status = $signingStatus
        signed_files = @($signedArtifactPaths)
        skipped_reasons = @($codeSigningSkippedReasons | Select-Object -Unique)
    }
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-UpdateLog "release_manifest_written" @{ manifest_path = $manifestPath; signing_status = $signingStatus }
Write-UpdateLog "release_artifact_completed" @{
    msi_path = $msiArtifact.FullName
    sha256_path = $hashPath
    manifest_path = $manifestPath
    sha256 = $hash.Hash.ToLowerInvariant()
}

[pscustomobject]@{
    FullName = $msiArtifact.FullName
    Length = $msiArtifact.Length
    LastWriteTime = $msiArtifact.LastWriteTime
    Sha256 = $hash.Hash.ToLowerInvariant()
    Sha256Path = $hashPath
    ManifestPath = $manifestPath
    UpdateLogPath = $updateLogPath
}
