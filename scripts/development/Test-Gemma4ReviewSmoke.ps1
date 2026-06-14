param(
    [string]$CatalogPath,

    [string]$RuntimePath,

    [string]$ModelPath,

    [string]$OutputPath,

    [switch]$SkipNetwork,

    [switch]$RunLocalRuntimeSmoke,

    [int]$RuntimeTimeoutSeconds = 300
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
if ([string]::IsNullOrWhiteSpace($CatalogPath)) {
    $CatalogPath = Join-Path $repoRoot "src\KoeNote.App\catalog\model-catalog.json"
}

if ([string]::IsNullOrWhiteSpace($RuntimePath)) {
    $RuntimePath = Join-Path $repoRoot "tools\review\llama-completion.exe"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "artifacts\smoke\gemma4-12b-review-smoke.json"
}

$modelId = "gemma-4-12b-it-qat-q4-0"
$modelFile = "gemma-4-12b-it-qat-q4_0.gguf"
$modelUrl = "https://huggingface.co/google/gemma-4-12B-it-qat-q4_0-gguf/resolve/main/$modelFile"
$defaultReviewModelId = "gemma-4-e4b-it-q4-k-m"
$expectedSize = 6975877728L
$expectedXetHash = "0edf41aec84b20e4d1dffc587493dbd68bc1a74ceea3bdf8b6691a6c9d165234"

$checks = New-Object System.Collections.Generic.List[object]

function Add-Check {
    param(
        [string]$Name,
        [string]$Status,
        [string]$Detail,
        [object]$Data = $null
    )

    $checks.Add([pscustomobject]@{
        name = $Name
        status = $Status
        detail = $Detail
        data = $Data
    })
}

function Convert-Headers {
    param([System.Net.WebHeaderCollection]$Headers)

    $result = [ordered]@{}
    foreach ($key in $Headers.AllKeys) {
        $result[$key] = $Headers[$key]
    }

    return $result
}

function Invoke-HeadRequest {
    param(
        [string]$Uri,
        [bool]$AllowRedirect
    )

    $request = [System.Net.HttpWebRequest]::Create($Uri)
    $request.Method = "HEAD"
    $request.AllowAutoRedirect = $AllowRedirect
    $request.UserAgent = "KoeNote-Gemma4ReviewSmoke"

    try {
        $response = $request.GetResponse()
    }
    catch [System.Net.WebException] {
        if ($_.Exception.Response -eq $null) {
            throw
        }

        $response = $_.Exception.Response
    }

    try {
        return [pscustomobject]@{
            status_code = [int]$response.StatusCode
            response_uri = $response.ResponseUri.AbsoluteUri
            headers = Convert-Headers $response.Headers
        }
    }
    finally {
        $response.Dispose()
    }
}

function Invoke-RangeProbe {
    param([string]$Uri)

    $request = [System.Net.HttpWebRequest]::Create($Uri)
    $request.Method = "GET"
    $request.AllowAutoRedirect = $true
    $request.UserAgent = "KoeNote-Gemma4ReviewSmoke"
    $request.AddRange(0, 0)

    $response = $request.GetResponse()
    try {
        $stream = $response.GetResponseStream()
        $buffer = New-Object byte[] 1
        $bytesRead = $stream.Read($buffer, 0, 1)
        return [pscustomobject]@{
            status_code = [int]$response.StatusCode
            response_uri = $response.ResponseUri.AbsoluteUri
            bytes_read = $bytesRead
            headers = Convert-Headers $response.Headers
        }
    }
    finally {
        $response.Dispose()
    }
}

function Invoke-ProcessWithTimeout {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [int]$TimeoutSeconds
    )

    $job = Start-Job -ScriptBlock {
        param(
            [string]$JobFilePath,
            [string[]]$JobArguments,
            [string]$JobWorkingDirectory
        )

        Set-Location -LiteralPath $JobWorkingDirectory
        $output = & $JobFilePath @JobArguments 2>&1
        $exitCode = $LASTEXITCODE
        $output
        "__KOENOTE_EXIT_CODE__=$exitCode"
    } -ArgumentList $FilePath, $Arguments, $WorkingDirectory

    try {
        if ((Wait-Job -Job $job -Timeout $TimeoutSeconds) -eq $null) {
            Stop-Job -Job $job -ErrorAction SilentlyContinue
            throw "Process timed out after $TimeoutSeconds seconds."
        }

        $lines = @(Receive-Job -Job $job)
        $exitLine = @($lines | Where-Object { $_ -is [string] -and $_.StartsWith("__KOENOTE_EXIT_CODE__=") } | Select-Object -Last 1)
        $exitCode = if ($exitLine.Count -gt 0) {
            [int]($exitLine[0] -replace "^__KOENOTE_EXIT_CODE__=", "")
        }
        else {
            1
        }
        $output = @($lines | Where-Object { -not ($_ -is [string] -and $_.StartsWith("__KOENOTE_EXIT_CODE__=")) }) -join "`n"

        return [pscustomobject]@{
            exit_code = $exitCode
            stdout = $output
            stderr = ""
        }
    }
    finally {
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    }
}

$catalog = Get-Content -LiteralPath $CatalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
$catalogModel = @($catalog.models | Where-Object { $_.model_id -eq $modelId }) | Select-Object -First 1
$experimentalPreset = @($catalog.presets | Where-Object { $_.preset_id -eq "experimental" }) | Select-Object -First 1
$recommendedPreset = @($catalog.presets | Where-Object { $_.preset_id -eq "recommended" }) | Select-Object -First 1
$highAccuracyPreset = @($catalog.presets | Where-Object { $_.preset_id -eq "high_accuracy" }) | Select-Object -First 1

if ($catalogModel -eq $null) {
    Add-Check "catalog model" "fail" "The Gemma 4 12B model is missing from the catalog."
}
elseif ($catalogModel.download.url -ne $modelUrl) {
    Add-Check "catalog model" "fail" "The Gemma 4 12B catalog URL does not match the expected GGUF URL." $catalogModel.download.url
}
else {
    Add-Check "catalog model" "pass" "The Gemma 4 12B GGUF model is cataloged." @{
        model_id = $catalogModel.model_id
        url = $catalogModel.download.url
        size_bytes = $catalogModel.size_bytes
    }
}

if ($experimentalPreset -ne $null -and $experimentalPreset.review_model_id -eq $modelId) {
    Add-Check "experimental preset" "pass" "Experimental preset selects Gemma 4 12B."
}
else {
    Add-Check "experimental preset" "fail" "Experimental preset does not select Gemma 4 12B."
}

if ($recommendedPreset -ne $null -and
    $highAccuracyPreset -ne $null -and
    $recommendedPreset.review_model_id -eq $defaultReviewModelId -and
    $highAccuracyPreset.review_model_id -eq $defaultReviewModelId) {
    Add-Check "default promotion guard" "pass" "Recommended and high_accuracy presets remain on Gemma 4 E4B." @{
        recommended_review_model_id = $recommendedPreset.review_model_id
        high_accuracy_review_model_id = $highAccuracyPreset.review_model_id
    }
}
else {
    Add-Check "default promotion guard" "fail" "Recommended and high_accuracy presets must remain on Gemma 4 E4B." @{
        expected_review_model_id = $defaultReviewModelId
        recommended_review_model_id = if ($recommendedPreset -eq $null) { $null } else { $recommendedPreset.review_model_id }
        high_accuracy_review_model_id = if ($highAccuracyPreset -eq $null) { $null } else { $highAccuracyPreset.review_model_id }
    }
}

if ($SkipNetwork) {
    Add-Check "download metadata" "skipped" "Network checks were skipped."
}
else {
    $redirectHead = Invoke-HeadRequest $modelUrl $false
    $finalHead = Invoke-HeadRequest $modelUrl $true
    $rangeProbe = Invoke-RangeProbe $modelUrl
    $linkedSize = [long]$redirectHead.headers["X-Linked-Size"]
    $xetHash = $redirectHead.headers["X-Xet-Hash"]
    $contentLength = [long]$finalHead.headers["Content-Length"]
    $etagHeader = $finalHead.headers["ETag"]
    $etag = if ($etagHeader -eq $null) { "" } else { $etagHeader.Trim('"') }

    if ($linkedSize -eq $expectedSize -and
        $contentLength -eq $expectedSize -and
        $xetHash -eq $expectedXetHash -and
        $etag -eq $expectedXetHash -and
        $rangeProbe.status_code -eq 206 -and
        $rangeProbe.bytes_read -eq 1) {
        Add-Check "download metadata" "pass" "HEAD and one-byte range probe matched the expected Gemma 4 12B artifact." @{
            repo_commit = $redirectHead.headers["X-Repo-Commit"]
            linked_size = $linkedSize
            linked_etag = $redirectHead.headers["X-Linked-ETag"]
            xet_hash = $xetHash
            content_length = $contentLength
            etag = $etag
            range_status = $rangeProbe.status_code
        }
    }
    else {
        Add-Check "download metadata" "fail" "Gemma 4 12B download metadata did not match the expected artifact." @{
            redirect_head = $redirectHead
            final_head = $finalHead
            range_probe = $rangeProbe
        }
    }
}

if (-not (Test-Path -LiteralPath $RuntimePath -PathType Leaf)) {
    Add-Check "review runtime options" "fail" "Review runtime is missing: $RuntimePath"
}
else {
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $help = (& $RuntimePath --help) 2>&1 | Out-String
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    $requiredOptions = @("--hf-repo", "--hf-file", "--no-conversation", "--json-schema-file", "--reasoning", "--chat-template")
    $missingOptions = @($requiredOptions | Where-Object { $help -notmatch [regex]::Escape($_) })
    if ($missingOptions.Count -eq 0) {
        Add-Check "review runtime options" "pass" "Review runtime exposes the options needed for Gemma 4 12B smoke checks." @{
            runtime_path = $RuntimePath
            required_options = $requiredOptions
        }
    }
    else {
        Add-Check "review runtime options" "fail" "Review runtime is missing required options: $($missingOptions -join ', ')"
    }
}

if ($RunLocalRuntimeSmoke) {
    if ([string]::IsNullOrWhiteSpace($ModelPath) -or -not (Test-Path -LiteralPath $ModelPath -PathType Leaf)) {
        Add-Check "local runtime smoke" "fail" "RunLocalRuntimeSmoke was requested, but ModelPath does not point to a local GGUF file."
    }
    else {
        $smokeRoot = Split-Path -Parent $OutputPath
        New-Item -ItemType Directory -Force -Path $smokeRoot | Out-Null
        $promptPath = Join-Path $smokeRoot "gemma4-12b-review-smoke.prompt.txt"
        $schemaPath = Join-Path $smokeRoot "gemma4-12b-review-smoke.schema.json"
        Set-Content -LiteralPath $promptPath -Encoding UTF8 -Value "Return [] only for this short transcript fragment: [00:00] Speaker 1: This is a KoeNote smoke test."
        Set-Content -LiteralPath $schemaPath -Encoding UTF8 -Value '{"type":"array","items":{"type":"object"}}'

        $arguments = @(
            "--model", $ModelPath,
            "--file", $promptPath,
            "--ctx-size", "8192",
            "--n-gpu-layers", "999",
            "--n-predict", "256",
            "--temp", "0.1",
            "--no-conversation",
            "--no-display-prompt",
            "--json-schema-file", $schemaPath
        )

        try {
            $result = Invoke-ProcessWithTimeout $RuntimePath $arguments $repoRoot $RuntimeTimeoutSeconds
            $combined = "$($result.stdout)`n$($result.stderr)"
            $hasThinking = $combined -match "<think|</think>|reasoning_content"
            if ($result.exit_code -eq 0 -and -not $hasThinking) {
                Add-Check "local runtime smoke" "pass" "Local Gemma 4 12B runtime smoke completed without visible thinking output." @{
                    exit_code = $result.exit_code
                    stdout = $result.stdout.Trim()
                    stderr_tail = ($result.stderr -split "`r?`n" | Select-Object -Last 20) -join "`n"
                }
            }
            else {
                Add-Check "local runtime smoke" "fail" "Local Gemma 4 12B runtime smoke failed or emitted thinking output." @{
                    exit_code = $result.exit_code
                    stdout = $result.stdout
                    stderr = $result.stderr
                    has_thinking = $hasThinking
                }
            }
        }
        catch {
            Add-Check "local runtime smoke" "fail" "Local Gemma 4 12B runtime smoke could not complete: $($_.Exception.Message)" @{
                exception_type = $_.Exception.GetType().FullName
                timeout_seconds = $RuntimeTimeoutSeconds
                model_path = $ModelPath
            }
        }
    }
}
else {
    Add-Check "local runtime smoke" "skipped" "Local GGUF load/generation smoke was not requested. Pass -RunLocalRuntimeSmoke -ModelPath MODEL_PATH after downloading the 12B GGUF."
}

$report = [pscustomobject]@{
    generated_at_utc = [DateTime]::UtcNow.ToString("o")
    issue = 64
    model_id = $modelId
    model_url = $modelUrl
    release_decision = "experimental_only"
    release_decision_reason = "Keep Gemma 4 12B behind the experimental preset until full local runtime smoke and field memory/load-time data justify promotion."
    checks = $checks
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8

$failed = @($checks | Where-Object { $_.status -eq "fail" })
Write-Host "Gemma 4 12B review smoke report: $OutputPath"
foreach ($check in $checks) {
    Write-Host "[$($check.status)] $($check.name): $($check.detail)"
}

if ($failed.Count -gt 0) {
    exit 1
}
