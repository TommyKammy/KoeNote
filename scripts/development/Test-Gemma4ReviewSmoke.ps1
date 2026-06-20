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
            return [pscustomobject]@{
                status_code = 0
                response_uri = $Uri
                headers = [ordered]@{}
                error = $_.Exception.Message
            }
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

    try {
        $response = $request.GetResponse()
    }
    catch [System.Net.WebException] {
        if ($_.Exception.Response -eq $null) {
            return [pscustomobject]@{
                status_code = 0
                response_uri = $Uri
                bytes_read = 0
                headers = [ordered]@{}
                error = $_.Exception.Message
            }
        }

        $response = $_.Exception.Response
    }

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

function Get-OutputDirectory {
    param([string]$Path)

    $parent = Split-Path -Parent $Path
    if ([string]::IsNullOrWhiteSpace($parent)) {
        return "."
    }

    return $parent
}

function ConvertTo-CommandLineArgument {
    param([string]$Argument)

    if ($null -eq $Argument) {
        return '""'
    }

    if ($Argument -notmatch '[\s"]') {
        return $Argument
    }

    return '"' + ($Argument -replace '"', '\"') + '"'
}

function Stop-ProcessTree {
    param([int]$ProcessId)

    & taskkill.exe /PID $ProcessId /T /F | Out-Null
}

function New-ReviewSmokePrompt {
    return @"
You are reviewing Japanese ASR transcript segments for KoeNote.
Return only a JSON array. Do not include explanations, markdown, or code fences.
Each item must use these keys: segment_id, issue_type, original_text, suggested_text, reason, confidence.
If there is no likely correction, return [].

Transcript segments:
- segment_id: 000001
  speaker: Speaker_0
  text: KoeNote smoke test says migiwa should be corrected to migigawa in this transcript.

For this smoke test, return one correction draft for segment_id 000001.
"@
}

function New-ReviewSmokeSchema {
    return @'
{
  "type": "array",
  "minItems": 1,
  "items": {
    "type": "object",
    "properties": {
      "segment_id": { "type": "string" },
      "issue_type": { "type": "string" },
      "original_text": { "type": "string" },
      "suggested_text": { "type": "string" },
      "reason": { "type": "string" },
      "confidence": { "type": "number" }
    },
    "required": ["segment_id", "issue_type", "original_text", "suggested_text", "reason", "confidence"],
    "additionalProperties": false
  }
}
'@
}

function Test-ReviewSmokeJson {
    param([string]$Output)

    $trimmedOutput = if ($null -eq $Output) { "" } else { $Output.Trim() }
    if (-not ($trimmedOutput.StartsWith("[") -and $trimmedOutput.EndsWith("]"))) {
        return [pscustomobject]@{
            is_valid = $false
            reason = "stdout JSON root is not an array."
        }
    }

    try {
        $parsed = $trimmedOutput | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        return [pscustomobject]@{
            is_valid = $false
            reason = "stdout is not valid JSON: $($_.Exception.Message)"
        }
    }

    $items = @($parsed)
    if ($items.Count -eq 0) {
        return [pscustomobject]@{
            is_valid = $false
            reason = "stdout JSON array did not contain any review draft items."
        }
    }

    $requiredProperties = @("segment_id", "issue_type", "original_text", "suggested_text", "reason", "confidence")
    foreach ($item in $items) {
        foreach ($propertyName in $requiredProperties) {
            if ($null -eq $item.PSObject.Properties[$propertyName] -or
                [string]::IsNullOrWhiteSpace([string]$item.$propertyName)) {
                return [pscustomobject]@{
                    is_valid = $false
                    reason = "stdout JSON item is missing required review property: $propertyName"
                }
            }
        }

        if ($item.segment_id -ne "000001") {
            return [pscustomobject]@{
                is_valid = $false
                reason = "stdout JSON item references an unexpected segment_id: $($item.segment_id)"
            }
        }
    }

    return [pscustomobject]@{
        is_valid = $true
        reason = "stdout contains Review-shaped JSON draft items."
        item_count = $items.Count
    }
}

function Invoke-ProcessWithTimeout {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [int]$TimeoutSeconds
    )

    $stdoutPath = [System.IO.Path]::GetTempFileName()
    $stderrPath = [System.IO.Path]::GetTempFileName()
    $argumentLine = ($Arguments | ForEach-Object { ConvertTo-CommandLineArgument $_ }) -join " "
    $process = $null
    try {
        $process = Start-Process `
            -FilePath $FilePath `
            -ArgumentList $argumentLine `
            -WorkingDirectory $WorkingDirectory `
            -RedirectStandardOutput $stdoutPath `
            -RedirectStandardError $stderrPath `
            -WindowStyle Hidden `
            -PassThru

        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            Stop-ProcessTree $process.Id
            $process.WaitForExit()
            throw "Process timed out after $TimeoutSeconds seconds."
        }

        return [pscustomobject]@{
            exit_code = $process.ExitCode
            stdout = Get-Content -LiteralPath $stdoutPath -Raw -ErrorAction SilentlyContinue
            stderr = Get-Content -LiteralPath $stderrPath -Raw -ErrorAction SilentlyContinue
        }
    }
    finally {
        if ($process -ne $null) {
            $process.Dispose()
        }

        Remove-Item -LiteralPath $stdoutPath, $stderrPath -ErrorAction SilentlyContinue
    }
}

$catalog = Get-Content -LiteralPath $CatalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
$catalogModel = @($catalog.models | Where-Object { $_.model_id -eq $modelId }) | Select-Object -First 1
$experimentalPreset = @($catalog.presets | Where-Object { $_.preset_id -eq "experimental" }) | Select-Object -First 1
$recommendedPreset = @($catalog.presets | Where-Object { $_.preset_id -eq "recommended" }) | Select-Object -First 1
$highAccuracyPreset = @($catalog.presets | Where-Object { $_.preset_id -eq "high_accuracy" }) | Select-Object -First 1
$unexpected12bPresets = @($catalog.presets | Where-Object { $_.review_model_id -eq $modelId })

if ($catalogModel -eq $null) {
    Add-Check "catalog model" "fail" "The Gemma 4 12B model is missing from the catalog."
}
elseif ($catalogModel.download.url -ne $modelUrl -or $catalogModel.status -ne "hidden") {
    Add-Check "catalog model" "fail" "The Gemma 4 12B catalog entry must remain hidden with the expected GGUF URL." @{
        url = $catalogModel.download.url
        status = $catalogModel.status
    }
}
else {
    Add-Check "catalog model" "pass" "The Gemma 4 12B GGUF model is cataloged but hidden from user selection." @{
        model_id = $catalogModel.model_id
        url = $catalogModel.download.url
        size_bytes = $catalogModel.size_bytes
        status = $catalogModel.status
    }
}

if ($experimentalPreset -ne $null -and $experimentalPreset.review_model_id -eq $defaultReviewModelId) {
    Add-Check "experimental preset" "pass" "Experimental preset stays on Gemma 4 E4B while 12B is hidden."
}
else {
    Add-Check "experimental preset" "fail" "Experimental preset must not select Gemma 4 12B while the runtime path is unstable." @{
        expected_review_model_id = $defaultReviewModelId
        actual_review_model_id = if ($experimentalPreset -eq $null) { $null } else { $experimentalPreset.review_model_id }
    }
}

if ($recommendedPreset -ne $null -and
    $highAccuracyPreset -ne $null -and
    $unexpected12bPresets.Count -eq 0 -and
    $recommendedPreset.review_model_id -eq $defaultReviewModelId -and
    $highAccuracyPreset.review_model_id -eq $defaultReviewModelId) {
    Add-Check "12B selection guard" "pass" "Recommended and high accuracy presets remain on Gemma 4 E4B; 12B is not user-selectable." @{
        recommended_review_model_id = $recommendedPreset.review_model_id
        high_accuracy_review_model_id = $highAccuracyPreset.review_model_id
        unexpected_12b_preset_ids = @()
    }
}
else {
    Add-Check "12B selection guard" "fail" "No preset should select Gemma 4 12B until the llama.cpp CUDA/reasoning path is stable." @{
        expected_recommended_review_model_id = $defaultReviewModelId
        expected_high_accuracy_review_model_id = $defaultReviewModelId
        recommended_review_model_id = if ($recommendedPreset -eq $null) { $null } else { $recommendedPreset.review_model_id }
        high_accuracy_review_model_id = if ($highAccuracyPreset -eq $null) { $null } else { $highAccuracyPreset.review_model_id }
        unexpected_12b_preset_ids = @($unexpected12bPresets | ForEach-Object { $_.preset_id })
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
        $smokeRoot = Get-OutputDirectory $OutputPath
        New-Item -ItemType Directory -Force -Path $smokeRoot | Out-Null
        $promptPath = Join-Path $smokeRoot "gemma4-12b-review-smoke.prompt.txt"
        $schemaPath = Join-Path $smokeRoot "gemma4-12b-review-smoke.schema.json"
        Set-Content -LiteralPath $promptPath -Encoding UTF8 -Value (New-ReviewSmokePrompt)
        Set-Content -LiteralPath $schemaPath -Encoding UTF8 -Value (New-ReviewSmokeSchema)

        $arguments = @(
            "--model", $ModelPath,
            "--file", $promptPath,
            "--ctx-size", "8192",
            "--n-gpu-layers", "999",
            "--n-predict", "256",
            "--temp", "0.1",
            "--no-conversation",
            "--no-display-prompt",
            "--reasoning", "off",
            "--json-schema-file", $schemaPath
        )

        try {
            $result = Invoke-ProcessWithTimeout $RuntimePath $arguments $repoRoot $RuntimeTimeoutSeconds
            $combined = "$($result.stdout)`n$($result.stderr)"
            $hasThinking = $combined -match "<think|</think>|reasoning_content|<\|channel\>thought|<channel\|>"
            $jsonCheck = Test-ReviewSmokeJson $result.stdout
            if ($result.exit_code -eq 0 -and -not $hasThinking -and $jsonCheck.is_valid) {
                Add-Check "local runtime smoke" "pass" "Local Gemma 4 12B runtime smoke completed with Review-shaped JSON and without visible thinking output." @{
                    exit_code = $result.exit_code
                    stdout = $result.stdout.Trim()
                    stderr_tail = ($result.stderr -split "`r?`n" | Select-Object -Last 20) -join "`n"
                    json_item_count = $jsonCheck.item_count
                }
            }
            else {
                Add-Check "local runtime smoke" "fail" "Local Gemma 4 12B runtime smoke failed, emitted thinking output, or did not produce Review-shaped JSON." @{
                    exit_code = $result.exit_code
                    stdout = $result.stdout
                    stderr = $result.stderr
                    has_thinking = $hasThinking
                    json_check = $jsonCheck
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
    release_decision = "hidden_until_llama_cpp_gemma4_12b_stable"
    release_decision_reason = "Keep Gemma 4 12B hidden from user selection because current llama.cpp CUDA/reasoning behavior can emit channel tokens or invalid output; keep presets on Gemma 4 E4B."
    checks = $checks
}

New-Item -ItemType Directory -Force -Path (Get-OutputDirectory $OutputPath) | Out-Null
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8

$failed = @($checks | Where-Object { $_.status -eq "fail" })
Write-Host "Gemma 4 12B review smoke report: $OutputPath"
foreach ($check in $checks) {
    Write-Host "[$($check.status)] $($check.name): $($check.detail)"
}

if ($failed.Count -gt 0) {
    exit 1
}
