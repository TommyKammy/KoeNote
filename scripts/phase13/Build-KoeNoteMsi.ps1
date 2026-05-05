param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$ProductVersion = "0.13.0",
    [string]$Publisher = "KoeNote Project",
    [string]$ProductName = "KoeNote",
    [string]$OutputName = "KoeNote",
    [string]$UpgradeCode = "{C73B4C12-0F3B-4B7A-8F4C-43D1C8C5EB1D}",
    [string]$InstallFolderName = "KoeNote",
    [string]$StartMenuFolderName = "KoeNote",
    [string]$ShortcutComponentGuid = "{4CC8E09B-7104-4D95-A5D3-3682E68BD080}",
    [string]$ArpMetadataComponentGuid = "{39818D14-4A4F-4B12-A29D-67E13FC97FD3}",
    [string]$ProductRegistryKey = "Software\KoeNote\KoeNote",
    [string]$ComponentGuidSalt = "KoeNote",
    [string]$CleanupUiCommand = "",
    [string]$CleanupQuietCommand = "--quiet"
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

& powershell -NoProfile -ExecutionPolicy Bypass -File $publishScript -Configuration $Configuration -RuntimeIdentifier $RuntimeIdentifier
if ($LASTEXITCODE -ne 0) {
    throw "Publish-KoeNote.ps1 failed with exit code $LASTEXITCODE."
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

& powershell -NoProfile -ExecutionPolicy Bypass -File $payloadScript -PayloadDir $publishDir -OutputPath $generatedWxs -ComponentGuidSalt $ComponentGuidSalt -ProductRegistryKey $ProductRegistryKey
if ($LASTEXITCODE -ne 0) {
    throw "New-WixPayload.ps1 failed with exit code $LASTEXITCODE."
}

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

Get-ChildItem -LiteralPath $installerOut -Filter "*.msi" -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1 FullName, Length, LastWriteTime
