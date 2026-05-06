param(
    [Parameter(Mandatory = $true)][string]$MsiPath,
    [string]$UpgradeFromMsiPath,
    [string]$DisplayName = "KoeNote",
    [switch]$AllowExistingUserData,
    [switch]$SkipInstall
)

$ErrorActionPreference = "Stop"

$msi = Resolve-Path $MsiPath
$upgradeFromMsi = if ($UpgradeFromMsiPath) { Resolve-Path $UpgradeFromMsiPath } else { $null }
$logRoot = Join-Path ([IO.Path]::GetTempPath()) "KoeNote-MsiSmoke"
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

function Test-KoeNoteSmokeDataRootAvailable {
    $existingRoots = @(
        (Join-Path $env:APPDATA "KoeNote"),
        (Join-Path $env:LOCALAPPDATA "KoeNote")
    ) | Where-Object {
        Test-Path -LiteralPath $_
    }

    if ($existingRoots -and -not $AllowExistingUserData) {
        throw "Refusing to run upgrade smoke because KoeNote user data already exists. Run this on a clean VM or pass -AllowExistingUserData after backing up: $($existingRoots -join ', ')"
    }
}

function Set-NewKoeNoteSmokeFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Value
    )

    if ((Test-Path -LiteralPath $Path) -and -not $AllowExistingUserData) {
        throw "Refusing to overwrite existing KoeNote smoke target: $Path"
    }

    Set-Content -LiteralPath $Path -Encoding UTF8 -Value $Value
}

function New-KoeNoteUpgradeSmokeData {
    $appDataRoot = Join-Path $env:APPDATA "KoeNote"
    $localDataRoot = Join-Path $env:LOCALAPPDATA "KoeNote"
    $jobsRoot = Join-Path $appDataRoot "jobs"
    $jobRoot = Join-Path $jobsRoot "upgrade-smoke-job"
    $modelsRoot = Join-Path $localDataRoot "models"

    New-Item -ItemType Directory -Force -Path $jobRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $modelsRoot | Out-Null

    $settingsPath = Join-Path $appDataRoot "settings.json"
    $setupStatePath = Join-Path $appDataRoot "setup-state.json"
    $jobMarkerPath = Join-Path $jobRoot "marker.txt"
    $modelMarkerPath = Join-Path $modelsRoot "upgrade-smoke-model.marker"

    Set-NewKoeNoteSmokeFile -Path $settingsPath -Value '{ "asrEngine": "upgrade-smoke", "reviewEngine": "upgrade-smoke", "networkAccess": false }'
    Set-NewKoeNoteSmokeFile -Path $setupStatePath -Value '{ "completed": true, "source": "upgrade-smoke" }'
    Set-NewKoeNoteSmokeFile -Path $jobMarkerPath -Value "upgrade smoke job data"
    Set-NewKoeNoteSmokeFile -Path $modelMarkerPath -Value "upgrade smoke model data"

    [pscustomobject]@{
        SettingsPath = $settingsPath
        SetupStatePath = $setupStatePath
        JobMarkerPath = $jobMarkerPath
        ModelMarkerPath = $modelMarkerPath
    }
}

function Test-KoeNoteUpgradeSmokeData {
    param(
        [Parameter(Mandatory = $true)]$Seed
    )

    foreach ($path in @($Seed.SettingsPath, $Seed.SetupStatePath, $Seed.JobMarkerPath, $Seed.ModelMarkerPath)) {
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Upgrade did not preserve expected KoeNote data: $path"
        }
    }
}

if (-not $SkipInstall) {
    if ($upgradeFromMsi) {
        Test-KoeNoteSmokeDataRootAvailable

        $previousInstallLog = Join-Path $logRoot "install-previous.log"
        $previousInstallArgs = "/i `"$upgradeFromMsi`" /qn /norestart /L*v `"$previousInstallLog`""
        $previousInstall = Start-Process -FilePath "msiexec.exe" -ArgumentList $previousInstallArgs -Wait -PassThru
        if ($previousInstall.ExitCode -ne 0) {
            throw "Previous MSI install failed with exit code $($previousInstall.ExitCode). Log: $previousInstallLog"
        }

        $seed = New-KoeNoteUpgradeSmokeData
    }

    $installLog = Join-Path $logRoot "install.log"
    $installArgs = "/i `"$msi`" /qn /norestart /L*v `"$installLog`""
    $install = Start-Process -FilePath "msiexec.exe" -ArgumentList $installArgs -Wait -PassThru
    if ($install.ExitCode -ne 0) {
        throw "MSI install failed with exit code $($install.ExitCode). Log: $installLog"
    }

    if ($upgradeFromMsi) {
        Test-KoeNoteUpgradeSmokeData -Seed $seed
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
