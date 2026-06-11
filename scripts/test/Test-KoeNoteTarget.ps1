param(
    [string[]]$Target = @("quick"),

    [string]$Configuration = "Debug",

    [switch]$NoRestore,

    [string[]]$DotnetArgs = @()
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$unitProject = Join-Path $repoRoot "tests\KoeNote.App.Tests\KoeNote.App.Tests.csproj"
$uiProject = Join-Path $repoRoot "tests\KoeNote.App.UiIntegrationTests\KoeNote.App.UiIntegrationTests.csproj"

function Join-TestFilter {
    param([string[]]$Pattern)

    return ($Pattern | ForEach-Object { "FullyQualifiedName~$_" }) -join "|"
}

function New-TestRun {
    param(
        [string]$Name,
        [string]$Project,
        [string[]]$Pattern
    )

    [pscustomobject]@{
        Name = $Name
        Project = $Project
        Filter = if ($Pattern.Count -eq 0) { $null } else { Join-TestFilter $Pattern }
    }
}

function Split-Target {
    param([string[]]$Value)

    return @($Value |
        ForEach-Object { $_ -split "," } |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

$targets = [ordered]@{
    quick = @(
        New-TestRun "quick-unit" $unitProject @(
            "AppPathsTests",
            "VersioningTests",
            "TextDiffServiceTests",
            "TranscriptSummaryValidatorTests",
            "LlmPresetCatalogTests",
            "ToolStatusServiceTests"
        )
    )
    asr = @(
        New-TestRun "asr" $unitProject @(
            "Asr",
            "ScriptedJsonAsrEngineTests",
            "FasterWhisperRuntime",
            "PythonRuntimeResolverTests",
            "AudioWaveformReaderTests"
        )
    )
    review = @(
        New-TestRun "review" $unitProject @(
            "Review",
            "Correction",
            "TextDiffServiceTests",
            "TernaryReviewRuntimeServiceTests",
            "CudaReviewRuntimeServiceTests"
        )
    )
    transcript = @(
        New-TestRun "transcript" $unitProject @(
            "Transcript",
            "ReadablePolishing",
            "LlmOutputSanitizerTests"
        )
    )
    setup = @(
        New-TestRun "setup" $unitProject @(
            "Setup",
            "ModelCatalogCompatibilityTests",
            "ModelVerificationServiceTests"
        )
    )
    viewmodel = @(
        New-TestRun "viewmodel" $uiProject @("MainWindowViewModelTests")
    )
    jobs = @(
        New-TestRun "jobs" $unitProject @(
            "Job",
            "StageProgress",
            "ExternalProcessRunnerTests"
        )
    )
    models = @(
        New-TestRun "models" $unitProject @(
            "Model",
            "InstalledModel"
        )
    )
    updates = @(
        New-TestRun "updates" $unitProject @(
            "Update"
        )
    )
    presets = @(
        New-TestRun "presets" $unitProject @(
            "DomainPreset",
            "DomainPrompt",
            "ReviewGuideline"
        )
    )
    export = @(
        New-TestRun "export" $unitProject @(
            "Export"
        )
    )
    llm = @(
        New-TestRun "llm" $unitProject @(
            "Llm",
            "Llama",
            "TranscriptSummary",
            "TranscriptPolishing",
            "ReadablePolishing"
        )
    )
    diarization = @(
        New-TestRun "diarization" $unitProject @(
            "Diarization"
        )
    )
    database = @(
        New-TestRun "database" $unitProject @(
            "Database",
            "Repository",
            "Sqlite",
            "StageProgressRepositoryTests",
            "InstalledModelRepositoryTests",
            "ReadablePolishingPromptSettingsRepositoryTests",
            "LlmSettingsRepositoryTests"
        )
    )
    runtime = @(
        New-TestRun "runtime" $unitProject @(
            "Runtime",
            "Cuda",
            "PythonRuntime",
            "ToolStatus",
            "LlamaRuntimePathBridgeTests",
            "ExternalProcessRunnerTests"
        )
    )
    cleanup = @(
        New-TestRun "cleanup" $unitProject @(
            "Cleanup"
        )
    )
    eval = @(
        New-TestRun "eval" $unitProject @(
            "EvaluationBench"
        )
    )
    ui = @(
        New-TestRun "ui" $uiProject @()
    )
}

$selectedTargets = Split-Target $Target
if ($selectedTargets.Count -eq 0) {
    throw "No test target was specified."
}

$validTargets = @("list", "all") + @($targets.Keys)
foreach ($targetName in $selectedTargets) {
    if ($validTargets -notcontains $targetName) {
        throw "Unknown target: $targetName. Valid targets: $($validTargets -join ', ')"
    }
}

if ($selectedTargets -contains "list") {
    Write-Host "Available targets:"
    foreach ($name in $targets.Keys) {
        $runs = $targets[$name] | ForEach-Object { $_.Name }
        Write-Host ("  {0,-12} {1}" -f $name, ($runs -join ", "))
    }
    exit 0
}

$requestedTargets = if ($selectedTargets -contains "all") {
    $targets.Keys
}
else {
    $selectedTargets
}

$runsToExecute = foreach ($targetName in $requestedTargets) {
    $targets[$targetName]
}

foreach ($run in $runsToExecute) {
    $arguments = @(
        "test",
        $run.Project,
        "--configuration",
        $Configuration,
        "--logger",
        "console;verbosity=minimal"
    )

    if ($NoRestore) {
        $arguments += "--no-restore"
    }

    if (-not [string]::IsNullOrWhiteSpace($run.Filter)) {
        $arguments += "--filter"
        $arguments += $run.Filter
    }

    $arguments += $DotnetArgs

    Write-Host ""
    Write-Host "==> $($run.Name)"
    Write-Host ("dotnet " + ($arguments -join " "))
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Target '$($run.Name)' failed with exit code $LASTEXITCODE."
    }
}
