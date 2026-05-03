param(
    [string]$FfmpegPath = "ffmpeg",
    [string]$CrispAsrPath = "tools/crispasr.exe",
    [string]$LlamaCompletionPath = "tools/llama-completion.exe",
    [string]$AsrModelPath = "models/asr/vibevoice-asr-q4_k.gguf",
    [string]$ReviewModelPath = "models/review/llm-jp-4-8B-thinking-Q4_K_M.gguf",
    [switch]$Strict
)

$ErrorActionPreference = "Stop"

function Resolve-Tool {
    param([string]$PathOrCommand)

    if (Test-Path -LiteralPath $PathOrCommand) {
        return (Resolve-Path -LiteralPath $PathOrCommand).Path
    }

    $command = Get-Command $PathOrCommand -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    return $null
}

function Test-Item {
    param(
        [string]$Name,
        [string]$PathOrCommand,
        [bool]$Required
    )

    $resolved = Resolve-Tool $PathOrCommand
    $ok = $null -ne $resolved

    return [ordered]@{
        name = $Name
        required = $Required
        requested = $PathOrCommand
        resolved = $resolved
        ok = $ok
    }
}

$checks = @(
    (Test-Item "dotnet" "dotnet" $true),
    (Test-Item "nvidia-smi" "nvidia-smi" $true),
    (Test-Item "ffmpeg" $FfmpegPath $true),
    (Test-Item "crispasr" $CrispAsrPath $false),
    (Test-Item "llama-completion" $LlamaCompletionPath $false),
    (Test-Item "VibeVoice ASR model" $AsrModelPath $false),
    (Test-Item "llm-jp review model" $ReviewModelPath $false)
)

$result = [ordered]@{
    checked_at = (Get-Date).ToString("o")
    strict = [bool]$Strict
    checks = $checks
}

$result | ConvertTo-Json -Depth 6

$failedRequired = @($checks | Where-Object { $_.required -and -not $_.ok })
$failedOptional = @($checks | Where-Object { -not $_.required -and -not $_.ok })

if ($failedRequired.Count -gt 0) {
    Write-Error "Missing required Phase 0 prerequisite(s): $($failedRequired.name -join ', ')"
}

if ($Strict -and $failedOptional.Count -gt 0) {
    Write-Error "Missing optional Phase 0 runtime/model item(s) in strict mode: $($failedOptional.name -join ', ')"
}

if ($failedOptional.Count -gt 0) {
    Write-Warning "Optional runtime/model item(s) not found yet: $($failedOptional.name -join ', ')"
}
