param(
    [string]$RuntimePath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "tools\review\llama-completion.exe"),
    [string]$ModelPath,
    [string]$E4BModelPath,
    [string]$PromptPath,
    [string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "artifacts\smoke\gemma12b-polishing-validation.json"),
    [int]$RuntimeTimeoutSeconds = 600
)

$ErrorActionPreference = "Stop"

function New-ValidationPrompt {
    @"
You are KoeNote's transcript polishing model.
Return only polished transcript blocks.
Do not output reasoning, thoughts, analysis, code fences, JSON, or channel tokens.
Preserve each BEGIN_BLOCK / END_BLOCK pair exactly.

BEGIN_BLOCK block-001
[00:00 - 00:02] Speaker_0: today we test the readable polishing output
END_BLOCK block-001

BEGIN_BLOCK block-002
[00:02 - 00:05] Speaker_1: please keep the timestamp and speaker format stable
END_BLOCK block-002

Output:
"@
}

function Invoke-ProcessWithTimeout {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [int]$TimeoutSeconds
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $startedAt = [DateTimeOffset]::UtcNow
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try {
            $process.Kill($true)
        }
        catch {
        }
        throw "Process timed out after $TimeoutSeconds seconds."
    }

    $finishedAt = [DateTimeOffset]::UtcNow
    return @{
        exit_code = $process.ExitCode
        elapsed_seconds = [Math]::Round(($finishedAt - $startedAt).TotalSeconds, 3)
        stdout = $stdoutTask.GetAwaiter().GetResult()
        stderr = $stderrTask.GetAwaiter().GetResult()
    }
}

function Test-Anomalies {
    param([string]$Text)

    $reasons = [System.Collections.Generic.List[string]]::new()
    if ($Text -match "0{24,}") {
        [void]$reasons.Add("repeated_zero_token_run")
    }
    if ($Text -match "<\|channel\|?>|<channel\|>|reasoning_content") {
        [void]$reasons.Add("visible_reasoning_channel_token")
    }
    if ($Text -match "<think|</think>") {
        [void]$reasons.Add("visible_thinking_block")
    }

    $beginCount = ([regex]::Matches($Text, "(?im)^\s*BEGIN_BLOCK\b")).Count
    $endCount = ([regex]::Matches($Text, "(?im)^\s*END_BLOCK\b")).Count
    if (($beginCount -gt 0 -or $endCount -gt 0) -and $beginCount -ne $endCount) {
        [void]$reasons.Add("unbalanced_block_markers_begin_$beginCount" + "_end_$endCount")
    }

    return $reasons.ToArray()
}

function Invoke-PolishingProbe {
    param(
        [string]$Label,
        [string]$Model
    )

    if ([string]::IsNullOrWhiteSpace($Model) -or -not (Test-Path -LiteralPath $Model -PathType Leaf)) {
        return @{
            label = $Label
            status = "skipped"
            reason = "model_path_missing"
            model_path = $Model
        }
    }

    $arguments = @(
        "--model", $Model,
        "--file", $script:PromptFile,
        "--ctx-size", "8192",
        "--n-gpu-layers", "999",
        "--n-predict", "768",
        "--temp", "0",
        "--repeat-penalty", "1.15",
        "--no-conversation",
        "--no-display-prompt",
        "--reasoning", "off"
    )

    try {
        $result = Invoke-ProcessWithTimeout $RuntimePath $arguments $RuntimeTimeoutSeconds
        $combined = "$($result.stdout)`n$($result.stderr)"
        $anomalies = @(Test-Anomalies $combined)
        $hasExpectedBlocks = $result.stdout -match "\[00:00 - 00:02\]\s+Speaker_0:" -and
            $result.stdout -match "\[00:02 - 00:05\]\s+Speaker_1:"
        return @{
            label = $Label
            status = if ($result.exit_code -eq 0 -and $anomalies.Count -eq 0 -and $hasExpectedBlocks) { "pass" } else { "fail" }
            model_path = $Model
            elapsed_seconds = $result.elapsed_seconds
            exit_code = $result.exit_code
            stdout_length = $result.stdout.Length
            stderr_tail = (($result.stderr -split "`r?`n") | Select-Object -Last 30) -join "`n"
            anomalies = $anomalies
            has_expected_blocks = $hasExpectedBlocks
            stdout = $result.stdout
        }
    }
    catch {
        return @{
            label = $Label
            status = "fail"
            model_path = $Model
            error = $_.Exception.Message
        }
    }
}

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

if ([string]::IsNullOrWhiteSpace($PromptPath)) {
    $PromptPath = Join-Path $outputDirectory "gemma12b-polishing-validation.prompt.txt"
    Set-Content -LiteralPath $PromptPath -Encoding UTF8 -Value (New-ValidationPrompt)
}

$script:PromptFile = $PromptPath

$checks = @()
if (-not (Test-Path -LiteralPath $RuntimePath -PathType Leaf)) {
    $checks += @{
        label = "runtime"
        status = "fail"
        reason = "runtime_missing"
        runtime_path = $RuntimePath
    }
}
else {
    $checks += @{
        label = "runtime"
        status = "pass"
        runtime_path = $RuntimePath
    }
    $checks += Invoke-PolishingProbe "gemma12b" $ModelPath
    if (-not [string]::IsNullOrWhiteSpace($E4BModelPath)) {
        $checks += Invoke-PolishingProbe "gemma-e4b-baseline" $E4BModelPath
    }
}

$report = @{
    created_at = [DateTimeOffset]::Now.ToString("o")
    purpose = "Gemma 4 12B QAT local polishing validation"
    prompt_path = $PromptPath
    output_path = $OutputPath
    env_flag = "KOENOTE_ENABLE_GEMMA12B_LOCAL_VALIDATION=1"
    acceptance_gate = @{
        gemma12b_anomalies = "0"
        gemma12b_has_expected_blocks = $true
        gemma12b_elapsed_seconds_per_probe = "<= 2x E4B baseline, or <= 20 seconds for this tiny smoke"
    }
    checks = $checks
}

Set-Content -LiteralPath $OutputPath -Encoding UTF8 -Value ($report | ConvertTo-Json -Depth 8)
Write-Host "Gemma 4 12B polishing validation report: $OutputPath"
