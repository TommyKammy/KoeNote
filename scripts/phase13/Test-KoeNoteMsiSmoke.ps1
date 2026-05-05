param(
    [Parameter(Mandatory = $true)][string]$MsiPath,
    [string]$DisplayName = "KoeNote",
    [switch]$SkipInstall
)

$ErrorActionPreference = "Stop"

$msi = Resolve-Path $MsiPath
$logRoot = Join-Path ([IO.Path]::GetTempPath()) "KoeNote-MsiSmoke"
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

if (-not $SkipInstall) {
    $installLog = Join-Path $logRoot "install.log"
    $installArgs = "/i `"$msi`" /qn /norestart /L*v `"$installLog`""
    $install = Start-Process -FilePath "msiexec.exe" -ArgumentList $installArgs -Wait -PassThru
    if ($install.ExitCode -ne 0) {
        throw "MSI install failed with exit code $($install.ExitCode). Log: $installLog"
    }
}

$uninstallRoots = @(
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*",
    "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*",
    "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
)

$registryEntries = $uninstallRoots | ForEach-Object {
    Get-ItemProperty -Path $_ -ErrorAction SilentlyContinue
}

$apps = $registryEntries | Where-Object {
    $_.DisplayName -eq $DisplayName
}

if (-not $apps) {
    throw "$DisplayName was not found in Windows Apps management uninstall registry entries."
}

$app = $apps | Select-Object -First 1
$productCode = Split-Path -Path $app.PSPath -Leaf
$metadataEntry = $registryEntries | Where-Object {
    (Split-Path -Path $_.PSPath -Leaf) -eq $productCode -and
    ($_.InstallLocation -or $_.DisplayIcon -or $_.QuietUninstallString)
} | Select-Object -First 1

if (-not $app.UninstallString -or $app.UninstallString -notmatch "msiexec") {
    throw "$DisplayName uninstall entry is missing a standard msiexec UninstallString."
}

$quietUninstallString = if ($app.QuietUninstallString) { $app.QuietUninstallString } else { $metadataEntry.QuietUninstallString }
if (-not $quietUninstallString -or $quietUninstallString -notmatch "msiexec" -or $quietUninstallString -notmatch "/q") {
    throw "$DisplayName uninstall entry is missing a standard quiet msiexec QuietUninstallString."
}

Write-Host "Found $DisplayName app registration:"
[pscustomobject]@{
    DisplayName = $app.DisplayName
    DisplayVersion = $app.DisplayVersion
    Publisher = $app.Publisher
    InstallLocation = if ($app.InstallLocation) { $app.InstallLocation } else { $metadataEntry.InstallLocation }
    DisplayIcon = if ($app.DisplayIcon) { $app.DisplayIcon } else { $metadataEntry.DisplayIcon }
    UninstallString = $app.UninstallString
    QuietUninstallString = $quietUninstallString
}

if (-not $SkipInstall) {
    $uninstallLog = Join-Path $logRoot "uninstall.log"
    $uninstallArgs = "/x `"$msi`" /qn /norestart /L*v `"$uninstallLog`""
    $uninstall = Start-Process -FilePath "msiexec.exe" -ArgumentList $uninstallArgs -Wait -PassThru
    if ($uninstall.ExitCode -ne 0) {
        throw "MSI uninstall failed with exit code $($uninstall.ExitCode). Log: $uninstallLog"
    }
}
