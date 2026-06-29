param(
    [string]$RuntimePath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "tools\review\llama-server.exe"),
    [string]$CompletionRuntimePath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "tools\review\llama-completion.exe"),
    [string]$ModelPath,
    [string]$MtpDraftModelPath,
    [string]$E4BModelPath,
    [string]$PromptPath,
    [string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "artifacts\smoke\gemma12b-polishing-validation.json"),
    [int]$RuntimeTimeoutSeconds = 600,
    [string]$CudaReviewRuntimeDirectory
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

function ConvertTo-ProcessArgument {
    param([string]$Argument)

    if ([string]::IsNullOrEmpty($Argument)) {
        return '""'
    }

    if ($Argument -notmatch '[\s"]') {
        return $Argument
    }

    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.Append('"')
    $backslashes = 0
    foreach ($character in $Argument.ToCharArray()) {
        if ($character -eq '\') {
            $backslashes++
            continue
        }

        if ($character -eq '"') {
            if ($backslashes -gt 0) {
                [void]$builder.Append('\' * ($backslashes * 2))
                $backslashes = 0
            }

            [void]$builder.Append('\"')
            continue
        }

        if ($backslashes -gt 0) {
            [void]$builder.Append('\' * $backslashes)
            $backslashes = 0
        }

        [void]$builder.Append($character)
    }

    if ($backslashes -gt 0) {
        [void]$builder.Append('\' * ($backslashes * 2))
    }

    [void]$builder.Append('"')
    return $builder.ToString()
}

function Join-ProcessArguments {
    param([string[]]$Arguments)

    return (($Arguments | ForEach-Object { ConvertTo-ProcessArgument $_ }) -join " ")
}

function Invoke-ProcessWithTimeout {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [int]$TimeoutSeconds,
        [hashtable]$Environment = @{}
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = Join-ProcessArguments $Arguments
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($key in $Environment.Keys) {
        $startInfo.Environment[$key] = [string]$Environment[$key]
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $startedAt = [DateTimeOffset]::UtcNow
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try {
            & taskkill.exe /PID $process.Id /T /F | Out-Null
            [void]$process.WaitForExit(5000)
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

function Get-FreeLoopbackPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Parse("127.0.0.1"), 0)
    try {
        $listener.Start()
        return $listener.LocalEndpoint.Port
    }
    finally {
        $listener.Stop()
    }
}

function Stop-ProcessTree {
    param([System.Diagnostics.Process]$Process)

    if ($null -eq $Process -or $Process.HasExited) {
        return
    }

    try {
        & taskkill.exe /PID $Process.Id /T /F | Out-Null
        [void]$Process.WaitForExit(5000)
    }
    catch {
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

function Invoke-CompletionPolishingProbe {
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

    if (-not (Test-Path -LiteralPath $CompletionRuntimePath -PathType Leaf)) {
        return @{
            label = $Label
            status = "skipped"
            reason = "completion_runtime_missing"
            runtime_path = $CompletionRuntimePath
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

    $environment = @{}
    if (-not [string]::IsNullOrWhiteSpace($CudaReviewRuntimeDirectory)) {
        $environment["PATH"] = "$CudaReviewRuntimeDirectory$([IO.Path]::PathSeparator)$env:PATH"
        $environment["KOENOTE_CUDA_REVIEW_RUNTIME_DIR"] = $CudaReviewRuntimeDirectory
    }

    try {
        $result = Invoke-ProcessWithTimeout $CompletionRuntimePath $arguments $RuntimeTimeoutSeconds $environment
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

function Invoke-MtpServerPolishingProbe {
    param(
        [string]$Label,
        [string]$Model,
        [string]$DraftModel
    )

    if ([string]::IsNullOrWhiteSpace($Model) -or -not (Test-Path -LiteralPath $Model -PathType Leaf)) {
        return @{
            label = $Label
            status = "skipped"
            reason = "model_path_missing"
            model_path = $Model
        }
    }

    if ([string]::IsNullOrWhiteSpace($DraftModel) -or -not (Test-Path -LiteralPath $DraftModel -PathType Leaf)) {
        return @{
            label = $Label
            status = "fail"
            reason = "mtp_draft_model_missing"
            model_path = $Model
            draft_model_path = $DraftModel
        }
    }

    $port = Get-FreeLoopbackPort
    $serverArguments = @(
        "--model", $Model,
        "--ctx-size", "8192",
        "--n-gpu-layers", "999",
        "--host", "127.0.0.1",
        "--port", "$port",
        "--spec-type", "draft-mtp",
        "--model-draft", $DraftModel,
        "--n-gpu-layers-draft", "999",
        "--spec-draft-n-max", "4",
        "--reasoning", "off"
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $RuntimePath
    $startInfo.Arguments = Join-ProcessArguments $serverArguments
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WorkingDirectory = Split-Path -Parent $RuntimePath
    if (-not [string]::IsNullOrWhiteSpace($CudaReviewRuntimeDirectory)) {
        $startInfo.Environment["PATH"] = "$CudaReviewRuntimeDirectory$([IO.Path]::PathSeparator)$($startInfo.Environment["PATH"])"
        $startInfo.Environment["KOENOTE_CUDA_REVIEW_RUNTIME_DIR"] = $CudaReviewRuntimeDirectory
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $startedAt = [DateTimeOffset]::UtcNow
    try {
        [void]$process.Start()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $baseUri = "http://127.0.0.1:$port"
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds([Math]::Min($RuntimeTimeoutSeconds, 60))
        $healthy = $false
        while ([DateTimeOffset]::UtcNow -lt $deadline) {
            if ($process.HasExited) {
                break
            }

            try {
                $health = Invoke-WebRequest -Uri "$baseUri/health" -UseBasicParsing -TimeoutSec 2
                if ($health.StatusCode -eq 200) {
                    $healthy = $true
                    break
                }
            }
            catch {
                Start-Sleep -Milliseconds 500
            }
        }

        if (-not $healthy) {
            Stop-ProcessTree $process
            return @{
                label = $Label
                status = "fail"
                reason = "mtp_server_health_failed"
                runtime_path = $RuntimePath
                model_path = $Model
                draft_model_path = $DraftModel
                stderr_tail = (($stderrTask.GetAwaiter().GetResult() -split "`r?`n") | Select-Object -Last 30) -join "`n"
            }
        }

        $payload = @{
            messages = @(@{ role = "user"; content = (Get-Content -LiteralPath $script:PromptFile -Raw) })
            max_tokens = 768
            temperature = 0
            repeat_penalty = 1.15
            stream = $false
        } | ConvertTo-Json -Depth 8
        $response = Invoke-RestMethod -Uri "$baseUri/v1/chat/completions" -Method Post -ContentType "application/json" -Body $payload -TimeoutSec $RuntimeTimeoutSeconds
        $content = [string]$response.choices[0].message.content
        $anomalies = @(Test-Anomalies $content)
        $hasExpectedBlocks = $content -match "\[00:00 - 00:02\]\s+Speaker_0:" -and
            $content -match "\[00:02 - 00:05\]\s+Speaker_1:"
        $finishedAt = [DateTimeOffset]::UtcNow
        return @{
            label = $Label
            status = if ($anomalies.Count -eq 0 -and $hasExpectedBlocks) { "pass" } else { "fail" }
            runtime_path = $RuntimePath
            model_path = $Model
            draft_model_path = $DraftModel
            elapsed_seconds = [Math]::Round(($finishedAt - $startedAt).TotalSeconds, 3)
            stdout_length = $content.Length
            anomalies = $anomalies
            has_expected_blocks = $hasExpectedBlocks
            stdout = $content
        }
    }
    catch {
        return @{
            label = $Label
            status = "fail"
            runtime_path = $RuntimePath
            model_path = $Model
            draft_model_path = $DraftModel
            error = $_.Exception.Message
        }
    }
    finally {
        Stop-ProcessTree $process
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
        label = "mtp_server_runtime"
        status = "pass"
        runtime_path = $RuntimePath
    }
    $checks += Invoke-MtpServerPolishingProbe "gemma12b-mtp" $ModelPath $MtpDraftModelPath
    if (-not [string]::IsNullOrWhiteSpace($E4BModelPath)) {
        $checks += Invoke-CompletionPolishingProbe "gemma-e4b-baseline" $E4BModelPath
    }
}

$report = @{
    created_at = [DateTimeOffset]::Now.ToString("o")
    purpose = "Gemma 4 12B QAT local polishing validation"
    prompt_path = $PromptPath
    output_path = $OutputPath
    env_flag = "KOENOTE_ENABLE_GEMMA12B_LOCAL_VALIDATION=1"
    mtp_draft_model_path = $MtpDraftModelPath
    acceptance_gate = @{
        gemma12b_anomalies = "0"
        gemma12b_has_expected_blocks = $true
        gemma12b_elapsed_seconds_per_probe = "<= 2x E4B baseline, or <= 20 seconds for this tiny smoke"
    }
    checks = $checks
}

Set-Content -LiteralPath $OutputPath -Encoding UTF8 -Value ($report | ConvertTo-Json -Depth 8)
Write-Host "Gemma 4 12B polishing validation report: $OutputPath"

$failedChecks = @($checks | Where-Object {
    $_.status -eq "fail" -or
        ($_.label -eq "gemma12b-mtp" -and $_.status -eq "skipped")
})
if ($failedChecks.Count -gt 0) {
    [Console]::Error.WriteLine("Gemma 4 12B polishing validation failed: $($failedChecks.Count) required check(s) failed or skipped.")
    exit 1
}
