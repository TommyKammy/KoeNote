param(
    [Parameter(Mandatory = $true)][string]$ReleaseManifestPath,
    [Parameter(Mandatory = $true)][string]$BaseDownloadUrl,
    [string]$ReleaseNotesUrl = "",
    [switch]$Mandatory,
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

$manifestPath = Resolve-Path $ReleaseManifestPath
$releaseManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

if ($releaseManifest.schema_version -ne 1) {
    throw "Unsupported release manifest schema_version: $($releaseManifest.schema_version)"
}

if ([string]::IsNullOrWhiteSpace($releaseManifest.artifacts.msi_path)) {
    throw "Release manifest is missing artifacts.msi_path."
}

if ([string]::IsNullOrWhiteSpace($releaseManifest.sha256)) {
    throw "Release manifest is missing sha256."
}

$baseDownloadUrlValue = if ($BaseDownloadUrl.EndsWith("/", [StringComparison]::Ordinal)) {
    $BaseDownloadUrl
}
else {
    "$BaseDownloadUrl/"
}
$baseUri = [Uri]$baseDownloadUrlValue
if (-not $baseUri.IsAbsoluteUri -or $baseUri.Scheme -ne [Uri]::UriSchemeHttps) {
    throw "BaseDownloadUrl must be an absolute HTTPS URL."
}

if (-not [string]::IsNullOrWhiteSpace($ReleaseNotesUrl)) {
    $releaseNotesUri = [Uri]$ReleaseNotesUrl
    if (-not $releaseNotesUri.IsAbsoluteUri -or $releaseNotesUri.Scheme -ne [Uri]::UriSchemeHttps) {
        throw "ReleaseNotesUrl must be an absolute HTTPS URL when provided."
    }
}

$msiName = [System.IO.Path]::GetFileName($releaseManifest.artifacts.msi_path)
$sha256Name = "$msiName.sha256"
$msiUri = [Uri]::new($baseUri, $msiName)
$sha256Uri = [Uri]::new($baseUri, $sha256Name)

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $manifestDirectory = [System.IO.Path]::GetDirectoryName($manifestPath.Path)
    if ([string]::IsNullOrWhiteSpace($manifestDirectory)) {
        throw "Could not resolve release manifest directory."
    }

    $OutputPath = Join-Path $manifestDirectory "latest.json"
}

$latest = [ordered]@{
    schema_version = 1
    product_name = $releaseManifest.product_name
    version = $releaseManifest.product_version
    runtime_identifier = $releaseManifest.runtime_identifier
    msi_url = $msiUri.AbsoluteUri
    sha256 = $releaseManifest.sha256
    sha256_url = $sha256Uri.AbsoluteUri
    release_notes_url = if ([string]::IsNullOrWhiteSpace($ReleaseNotesUrl)) { $null } else { $ReleaseNotesUrl }
    mandatory = [bool]$Mandatory
    published_at = [DateTimeOffset]::Now.ToString("o")
}

$latest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding UTF8

[pscustomobject]@{
    LatestJsonPath = (Resolve-Path $OutputPath).Path
    Version = $latest.version
    RuntimeIdentifier = $latest.runtime_identifier
    MsiUrl = $latest.msi_url
    Sha256 = $latest.sha256
    Mandatory = $latest.mandatory
}
