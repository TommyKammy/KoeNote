param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$project = Join-Path $repoRoot "src\KoeNote.App\KoeNote.App.csproj"
$publishDir = Join-Path $repoRoot "artifacts\publish\KoeNote-$RuntimeIdentifier"

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

dotnet publish $project `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishDir="$publishDir\" `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:PublishTrimmed=false

New-Item -ItemType Directory -Force -Path (Join-Path $publishDir "tools") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $publishDir "models\asr") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $publishDir "models\review") | Out-Null

Write-Host "Published KoeNote to $publishDir"
