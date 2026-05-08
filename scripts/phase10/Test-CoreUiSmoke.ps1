param(
    [string]$InstallDir = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")) "artifacts\installer-smoke-core")
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$installer = Join-Path $repoRoot "artifacts\installers\KoeNote-Core-Setup.exe"
$installDirFull = if ([IO.Path]::IsPathRooted($InstallDir)) {
    [IO.Path]::GetFullPath($InstallDir)
}
else {
    [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $InstallDir))
}

if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw "Missing Core installer: $installer. Run scripts\phase10\New-InstallerPackages.ps1 first."
}

if (Test-Path -LiteralPath $installDirFull) {
    Remove-Item -LiteralPath $installDirFull -Recurse -Force
}

$env:KOENOTE_INSTALL_TARGET = $installDirFull
& $installer
if ($LASTEXITCODE -ne 0) {
    throw "Core installer smoke install failed with exit code $LASTEXITCODE."
}

$requiredFiles = @(
    "KoeNote.App.exe",
    "README.distribution.md",
    "licenses\license-manifest.json",
    "tools\ffmpeg.exe",
    "samples\koenote-smoke-1s.wav"
)

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $installDirFull $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing installed Core file: $path"
    }
}

$forbiddenModelPatterns = @("*.gguf", "*.safetensors", "*.onnx", "*.pt", "*.pth", "*.bin")
foreach ($pattern in $forbiddenModelPatterns) {
    $modelFiles = Get-ChildItem -LiteralPath (Join-Path $installDirFull "models") -Recurse -File -Filter $pattern -ErrorAction SilentlyContinue
    if ($modelFiles.Count -gt 0) {
        throw "Core install must not include model binaries: $($modelFiles[0].FullName)"
    }
}

Write-Host "Core UI smoke install check passed: $installDirFull"
Write-Host "Manual UI checklist: docs\development\core-ui-smoke.md"
