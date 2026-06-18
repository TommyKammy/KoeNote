param(
    [Parameter(Mandatory = $true)][string]$MsiPath,
    [string]$UpgradeFromMsiPath,
    [string]$DisplayName = "KoeNote",
    [switch]$AllowExistingUserData,
    [switch]$TestAllDataCleanup,
    [switch]$SkipLaunchCheck,
    [switch]$SkipInstall
)

$ErrorActionPreference = "Stop"

$msi = Resolve-Path $MsiPath
$upgradeFromMsi = if ($UpgradeFromMsiPath) { Resolve-Path $UpgradeFromMsiPath } else { $null }
$logRoot = Join-Path ([IO.Path]::GetTempPath()) "KoeNote-MsiSmoke"
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

function Test-KoeNoteMsiSmokeLog {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description log was not created: $Path"
    }

    $log = Get-Item -LiteralPath $Path
    if ($log.Length -le 0) {
        throw "$Description log is empty: $Path"
    }
}

function Get-KoeNoteMsiSmokeRegistration {
    param(
        [Parameter(Mandatory = $true)][string]$Name
    )

    $uninstallRoots = @(
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )

    $registryEntries = $uninstallRoots | ForEach-Object {
        Get-ItemProperty -Path $_ -ErrorAction SilentlyContinue
    }

    $apps = $registryEntries | Where-Object {
        $_.DisplayName -eq $Name
    }

    if (-not $apps) {
        throw "$Name was not found in Windows Apps management uninstall registry entries."
    }

    $visibleApps = @($apps | Where-Object {
        $_.SystemComponent -ne 1
    })
    if ($visibleApps.Count -gt 1) {
        $productCodes = $visibleApps | ForEach-Object {
            Split-Path -Path $_.PSPath -Leaf
        }
        throw "$Name has duplicate visible Windows Apps registration entries: $($productCodes -join ', ')"
    }

    $app = $visibleApps | Select-Object -First 1
    if (-not $app) {
        throw "$Name was found only as hidden Windows Installer ARP entries."
    }

    $productCode = Split-Path -Path $app.PSPath -Leaf
    $metadataEntry = $registryEntries | Where-Object {
        (Split-Path -Path $_.PSPath -Leaf) -eq $productCode -and
        ($_.InstallLocation -or $_.DisplayIcon -or $_.QuietUninstallString)
    } | Select-Object -First 1

    if (-not $app.UninstallString -or $app.UninstallString -notmatch "msiexec") {
        throw "$Name uninstall entry is missing a standard msiexec UninstallString."
    }

    if ($app.UninstallString -notmatch "(?i)/I") {
        throw "$Name uninstall entry must open MSI maintenance UI with /I. Actual: $($app.UninstallString)"
    }

    $quietUninstallString = if ($app.QuietUninstallString) { $app.QuietUninstallString } else { $metadataEntry.QuietUninstallString }
    if (-not $quietUninstallString -or $quietUninstallString -notmatch "msiexec" -or $quietUninstallString -notmatch "/q") {
        throw "$Name uninstall entry is missing a standard quiet msiexec QuietUninstallString."
    }

    [pscustomobject]@{
        App = $app
        ProductCode = $productCode
        MetadataEntry = $metadataEntry
        InstallLocation = if ($app.InstallLocation) { $app.InstallLocation } else { $metadataEntry.InstallLocation }
        DisplayIcon = if ($app.DisplayIcon) { $app.DisplayIcon } else { $metadataEntry.DisplayIcon }
        QuietUninstallString = $quietUninstallString
    }
}

function Test-KoeNoteInstallPayload {
    param(
        [Parameter(Mandatory = $true)]$Registration,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ([string]::IsNullOrWhiteSpace($Registration.InstallLocation)) {
        throw "$Name app registration is missing InstallLocation."
    }

    $installLocation = [IO.Path]::GetFullPath($Registration.InstallLocation).TrimEnd('\', '/')
    $expectedInstallLocation = [IO.Path]::GetFullPath((Join-Path (Join-Path $env:LOCALAPPDATA "Programs") $Name)).TrimEnd('\', '/')
    if (-not $installLocation.Equals($expectedInstallLocation, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Name installed to an unexpected location. Expected $expectedInstallLocation but got $installLocation."
    }

    $appExe = Join-Path $installLocation "KoeNote.App.exe"
    $cleanupExe = Join-Path $installLocation "KoeNoteCleanup.exe"
    foreach ($path in @($appExe, $cleanupExe)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "$Name install payload is missing: $path"
        }
    }

    if ([string]::IsNullOrWhiteSpace($Registration.DisplayIcon)) {
        throw "$Name app registration is missing DisplayIcon."
    }

    $displayIcon = $Registration.DisplayIcon.Trim('"')
    if (-not (Test-Path -LiteralPath $displayIcon -PathType Leaf)) {
        throw "$Name DisplayIcon target does not exist: $displayIcon"
    }
}

function Test-KoeNoteStartMenuShortcuts {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$InstallLocation
    )

    $programsFolder = [Environment]::GetFolderPath("Programs")
    $shortcutFolder = Join-Path $programsFolder $Name
    $shortcuts = @(
        [pscustomobject]@{
            Path = Join-Path $shortcutFolder "$Name.lnk"
            Target = Join-Path $InstallLocation "KoeNote.App.exe"
        },
        [pscustomobject]@{
            Path = Join-Path $shortcutFolder "$Name Cleanup.lnk"
            Target = Join-Path $InstallLocation "KoeNoteCleanup.exe"
        }
    )

    $shell = New-Object -ComObject WScript.Shell
    try {
        foreach ($shortcutInfo in $shortcuts) {
            if (-not (Test-Path -LiteralPath $shortcutInfo.Path -PathType Leaf)) {
                throw "$Name Start Menu shortcut is missing: $($shortcutInfo.Path)"
            }

            $shortcut = $shell.CreateShortcut($shortcutInfo.Path)
            $actualTarget = [IO.Path]::GetFullPath($shortcut.TargetPath)
            $expectedTarget = [IO.Path]::GetFullPath($shortcutInfo.Target)
            if (-not $actualTarget.Equals($expectedTarget, [StringComparison]::OrdinalIgnoreCase)) {
                throw "$Name Start Menu shortcut points to $actualTarget instead of $expectedTarget."
            }
        }
    }
    finally {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    }
}

function Test-KoeNoteInstalledAppLaunch {
    param(
        [Parameter(Mandatory = $true)]$Registration,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($SkipLaunchCheck) {
        Write-Host "Skipping installed app launch check because -SkipLaunchCheck was passed."
        return
    }

    if (-not [Environment]::UserInteractive) {
        throw "$Name launch smoke requires an interactive user session. Re-run on a clean VM session or pass -SkipLaunchCheck only for non-interactive packaging validation."
    }

    $installLocation = [IO.Path]::GetFullPath($Registration.InstallLocation).TrimEnd('\', '/')
    $appExe = Join-Path $installLocation "KoeNote.App.exe"
    $process = Start-Process -FilePath $appExe -WorkingDirectory $installLocation -PassThru
    try {
        Start-Sleep -Seconds 5
        if ($process.HasExited) {
            throw "$Name exited during launch smoke with exit code $($process.ExitCode)."
        }

        Write-Host "$Name launched successfully from $appExe."
    }
    finally {
        if (-not $process.HasExited) {
            [void]$process.CloseMainWindow()
            if (-not $process.WaitForExit(5000)) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

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
    $runtimesRoot = Join-Path (Join-Path $localDataRoot "runtimes") "gpu"

    New-Item -ItemType Directory -Force -Path $jobRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $modelsRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $runtimesRoot | Out-Null

    $settingsPath = Join-Path $appDataRoot "settings.json"
    $setupStatePath = Join-Path $appDataRoot "setup-state.json"
    $jobMarkerPath = Join-Path $jobRoot "marker.txt"
    $modelMarkerPath = Join-Path $modelsRoot "upgrade-smoke-model.marker"
    $runtimeMarkerPath = Join-Path $runtimesRoot "upgrade-smoke-runtime.marker"

    Set-NewKoeNoteSmokeFile -Path $settingsPath -Value '{ "asrEngine": "upgrade-smoke", "reviewEngine": "upgrade-smoke", "networkAccess": false }'
    Set-NewKoeNoteSmokeFile -Path $setupStatePath -Value '{ "completed": true, "source": "upgrade-smoke" }'
    Set-NewKoeNoteSmokeFile -Path $jobMarkerPath -Value "upgrade smoke job data"
    Set-NewKoeNoteSmokeFile -Path $modelMarkerPath -Value "upgrade smoke model data"
    Set-NewKoeNoteSmokeFile -Path $runtimeMarkerPath -Value "upgrade smoke runtime data"

    [pscustomobject]@{
        SettingsPath = $settingsPath
        SetupStatePath = $setupStatePath
        JobMarkerPath = $jobMarkerPath
        ModelMarkerPath = $modelMarkerPath
        RuntimeMarkerPath = $runtimeMarkerPath
    }
}

function Test-KoeNoteUpgradeSmokeData {
    param(
        [Parameter(Mandatory = $true)]$Seed
    )

    foreach ($path in @($Seed.SettingsPath, $Seed.SetupStatePath, $Seed.JobMarkerPath, $Seed.ModelMarkerPath, $Seed.RuntimeMarkerPath)) {
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Upgrade did not preserve expected KoeNote data: $path"
        }
    }
}

function New-KoeNoteIsolatedCleanupSmokeData {
    $root = Join-Path ([IO.Path]::GetTempPath()) "KoeNote-MsiAllDataSmoke-$([Guid]::NewGuid().ToString('N'))"
    $appDataRoot = Join-Path $root "roaming"
    $localAppDataRoot = Join-Path $root "local"
    $programDataRoot = Join-Path $root "programdata"

    foreach ($dataRoot in @($appDataRoot, $localAppDataRoot, $programDataRoot)) {
        $koeNoteRoot = Join-Path $dataRoot "KoeNote"
        New-Item -ItemType Directory -Force -Path $koeNoteRoot | Out-Null
        Set-Content -LiteralPath (Join-Path $koeNoteRoot "marker.txt") -Encoding UTF8 -Value "delete me"
    }

    [pscustomobject]@{
        Root = $root
        AppDataRoot = $appDataRoot
        LocalAppDataRoot = $localAppDataRoot
        ProgramDataRoot = $programDataRoot
    }
}

function Test-KoeNoteIsolatedCleanupSmokeDataRemoved {
    param(
        [Parameter(Mandatory = $true)]$Seed
    )

    $remaining = @(
        (Join-Path $Seed.AppDataRoot "KoeNote"),
        (Join-Path $Seed.LocalAppDataRoot "KoeNote"),
        (Join-Path $Seed.ProgramDataRoot "KoeNote")
    ) | Where-Object {
        Test-Path -LiteralPath $_
    }

    if ($remaining) {
        throw "All-data cleanup did not remove expected isolated KoeNote roots: $($remaining -join ', ')"
    }
}

if (-not $SkipInstall) {
    $previousRegistration = $null
    if ($upgradeFromMsi) {
        Test-KoeNoteSmokeDataRootAvailable

        $previousInstallLog = Join-Path $logRoot "install-previous.log"
        $previousInstallArgs = "/i `"$upgradeFromMsi`" /qn /norestart /L*v `"$previousInstallLog`""
        $previousInstall = Start-Process -FilePath "msiexec.exe" -ArgumentList $previousInstallArgs -Wait -PassThru
        if ($previousInstall.ExitCode -ne 0) {
            throw "Previous MSI install failed with exit code $($previousInstall.ExitCode). Log: $previousInstallLog"
        }
        Test-KoeNoteMsiSmokeLog -Path $previousInstallLog -Description "Previous MSI install"

        $previousRegistration = Get-KoeNoteMsiSmokeRegistration -Name $DisplayName
        Test-KoeNoteInstallPayload -Registration $previousRegistration -Name $DisplayName
        Test-KoeNoteStartMenuShortcuts -Name $DisplayName -InstallLocation $previousRegistration.InstallLocation

        $seed = New-KoeNoteUpgradeSmokeData
    }

    $installLog = Join-Path $logRoot "install.log"
    $installArgs = "/i `"$msi`" /qn /norestart /L*v `"$installLog`""
    $install = Start-Process -FilePath "msiexec.exe" -ArgumentList $installArgs -Wait -PassThru
    if ($install.ExitCode -ne 0) {
        throw "MSI install failed with exit code $($install.ExitCode). Log: $installLog"
    }
    Test-KoeNoteMsiSmokeLog -Path $installLog -Description "MSI install"

    if ($upgradeFromMsi) {
        Test-KoeNoteUpgradeSmokeData -Seed $seed
    }
}

$registration = Get-KoeNoteMsiSmokeRegistration -Name $DisplayName
$app = $registration.App
$productCode = $registration.ProductCode
$metadataEntry = $registration.MetadataEntry
$quietUninstallString = $registration.QuietUninstallString

if ($previousRegistration) {
    $sameProductCode = $registration.ProductCode.Equals($previousRegistration.ProductCode, [StringComparison]::OrdinalIgnoreCase)
    $sameDisplayVersion = [string]::Equals($registration.App.DisplayVersion, $previousRegistration.App.DisplayVersion, [StringComparison]::OrdinalIgnoreCase)
    if ($sameProductCode -and $sameDisplayVersion) {
        throw "Silent upgrade left $DisplayName registered as the previous MSI version/product. Version: $($registration.App.DisplayVersion), ProductCode: $($registration.ProductCode)"
    }
}

Test-KoeNoteInstallPayload -Registration $registration -Name $DisplayName
Test-KoeNoteStartMenuShortcuts -Name $DisplayName -InstallLocation $registration.InstallLocation
if ($TestAllDataCleanup) {
    Write-Host "Skipping installed app launch check during isolated all-data cleanup smoke to avoid creating real KoeNote user data."
}
else {
    Test-KoeNoteInstalledAppLaunch -Registration $registration -Name $DisplayName
}

Write-Host "Found $DisplayName app registration:"
[pscustomobject]@{
    DisplayName = $app.DisplayName
    DisplayVersion = $app.DisplayVersion
    Publisher = $app.Publisher
    InstallLocation = $registration.InstallLocation
    DisplayIcon = $registration.DisplayIcon
    UninstallString = $app.UninstallString
    QuietUninstallString = $quietUninstallString
    SmokeLogDirectory = $logRoot
}

if (-not $SkipInstall) {
    $uninstallLog = Join-Path $logRoot "uninstall.log"
    $cleanupSeed = $null
    if ($TestAllDataCleanup) {
        $cleanupSeed = New-KoeNoteIsolatedCleanupSmokeData
        $uninstallArgs = "/x $productCode /qn /norestart KOENOTE_CLEANUP_ALL_DATA=1 KOENOTE_CLEANUP_APPDATA_ROOT=`"$($cleanupSeed.AppDataRoot)`" KOENOTE_CLEANUP_LOCALAPPDATA_ROOT=`"$($cleanupSeed.LocalAppDataRoot)`" KOENOTE_CLEANUP_PROGRAMDATA_ROOT=`"$($cleanupSeed.ProgramDataRoot)`" /L*v `"$uninstallLog`""
    }
    else {
        $uninstallArgs = "/x `"$msi`" /qn /norestart /L*v `"$uninstallLog`""
    }

    $uninstall = Start-Process -FilePath "msiexec.exe" -ArgumentList $uninstallArgs -Wait -PassThru
    if ($uninstall.ExitCode -ne 0) {
        throw "MSI uninstall failed with exit code $($uninstall.ExitCode). Log: $uninstallLog"
    }
    Test-KoeNoteMsiSmokeLog -Path $uninstallLog -Description "MSI uninstall"

    $installLocation = $registration.InstallLocation
    if ($installLocation -and (Test-Path -LiteralPath $installLocation)) {
        throw "$DisplayName uninstall left the application install directory behind: $installLocation"
    }

    if ($cleanupSeed) {
        Test-KoeNoteIsolatedCleanupSmokeDataRemoved -Seed $cleanupSeed
        Remove-Item -LiteralPath $cleanupSeed.Root -Recurse -Force -ErrorAction SilentlyContinue
    }
}
