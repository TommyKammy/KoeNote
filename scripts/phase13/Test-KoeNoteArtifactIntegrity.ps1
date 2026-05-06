param(
    [Parameter(Mandatory = $true)][string]$MsiPath
)

$ErrorActionPreference = "Stop"

$msi = Resolve-Path $MsiPath
$hashPath = "$msi.sha256"
if (-not (Test-Path -LiteralPath $hashPath)) {
    throw "SHA256 sidecar file is missing: $hashPath"
}

$expectedLine = (Get-Content -LiteralPath $hashPath -Raw).Trim()
if ($expectedLine -notmatch '^(?<hash>[0-9a-fA-F]{64})\s+') {
    throw "SHA256 sidecar has an invalid format: $hashPath"
}

$expectedHash = $Matches["hash"].ToLowerInvariant()
$actualHash = (Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $expectedHash) {
    throw "SHA256 mismatch for $msi. Expected $expectedHash but got $actualHash."
}

[pscustomobject]@{
    MsiPath = $msi.Path
    Sha256Path = $hashPath
    Sha256 = $actualHash
}
