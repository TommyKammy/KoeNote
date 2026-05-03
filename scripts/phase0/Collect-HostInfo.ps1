param(
    [string]$OutputRoot = "experiments/phase0/runs"
)

$ErrorActionPreference = "Stop"

function Get-CommandPath {
    param([string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        return $null
    }

    return $command.Source
}

function Invoke-TextCommand {
    param(
        [string]$Command,
        [string[]]$Arguments = @()
    )

    $path = Get-CommandPath $Command
    if ($null -eq $path) {
        return $null
    }

    try {
        return (& $path @Arguments 2>&1) -join [Environment]::NewLine
    }
    catch {
        return "Command failed: $($_.Exception.Message)"
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$machineSlug = ($env:COMPUTERNAME -replace "[^A-Za-z0-9_-]", "-").ToLowerInvariant()
$runDir = Join-Path $repoRoot (Join-Path $OutputRoot "$timestamp-$machineSlug")
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

$os = Get-CimInstance Win32_OperatingSystem
$computer = Get-CimInstance Win32_ComputerSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1

$nvidiaSmi = Invoke-TextCommand "nvidia-smi"
$cudaVersion = $null
if ($nvidiaSmi -match "CUDA Version:\s+([0-9.]+)") {
    $cudaVersion = $Matches[1]
}

$gpuCsv = Invoke-TextCommand "nvidia-smi" @(
    "--query-gpu=name,driver_version,memory.total",
    "--format=csv,noheader,nounits"
)

$gpus = @()
if (-not [string]::IsNullOrWhiteSpace($gpuCsv) -and -not $gpuCsv.StartsWith("Command failed")) {
    foreach ($line in ($gpuCsv -split "`r?`n")) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $parts = $line.Split(",") | ForEach-Object { $_.Trim() }
        $gpus += [ordered]@{
            name = $parts[0]
            driver_version = if ($parts.Count -gt 1) { $parts[1] } else { $null }
            memory_total_mib = if ($parts.Count -gt 2) { [int]$parts[2] } else { $null }
            cuda_version = $cudaVersion
        }
    }
}

$ffmpegVersion = Invoke-TextCommand "ffmpeg" @("-version")
$dotnetVersion = Invoke-TextCommand "dotnet" @("--version")
$dotnetInfo = Invoke-TextCommand "dotnet" @("--info")

$hostInfo = [ordered]@{
    captured_at = (Get-Date).ToString("o")
    machine_name = $env:COMPUTERNAME
    repo_root = $repoRoot.Path
    os = [ordered]@{
        caption = $os.Caption
        version = $os.Version
        build_number = $os.BuildNumber
        architecture = $os.OSArchitecture
    }
    cpu = [ordered]@{
        name = $cpu.Name
        logical_processors = $cpu.NumberOfLogicalProcessors
        cores = $cpu.NumberOfCores
    }
    memory = [ordered]@{
        total_physical_gib = [math]::Round($computer.TotalPhysicalMemory / 1GB, 2)
    }
    gpu = $gpus
    tools = [ordered]@{
        dotnet = [ordered]@{
            path = Get-CommandPath "dotnet"
            version = if ($dotnetVersion) { $dotnetVersion.Trim() } else { $null }
        }
        ffmpeg = [ordered]@{
            path = Get-CommandPath "ffmpeg"
            version_line = if ($ffmpegVersion) { ($ffmpegVersion -split "`r?`n" | Select-Object -First 1) } else { $null }
        }
        nvidia_smi = [ordered]@{
            path = Get-CommandPath "nvidia-smi"
        }
    }
}

$hostInfoPath = Join-Path $runDir "host-info.json"
$notesPath = Join-Path $runDir "notes.md"
$dotnetInfoPath = Join-Path $runDir "dotnet-info.txt"
$nvidiaSmiPath = Join-Path $runDir "nvidia-smi.txt"

$hostInfo | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $hostInfoPath -Encoding UTF8
if ($dotnetInfo) {
    Set-Content -LiteralPath $dotnetInfoPath -Value $dotnetInfo -Encoding UTF8
}
if ($nvidiaSmi) {
    Set-Content -LiteralPath $nvidiaSmiPath -Value $nvidiaSmi -Encoding UTF8
}

$gpuSummary = if ($gpus.Count -gt 0) {
    ($gpus | ForEach-Object { "- $($_.name), VRAM $($_.memory_total_mib) MiB, driver $($_.driver_version), CUDA $($_.cuda_version)" }) -join [Environment]::NewLine
}
else {
    "- nvidia-smi GPU query unavailable"
}

$dotnetPath = $hostInfo.tools.dotnet.path
$ffmpegPath = $hostInfo.tools.ffmpeg.path
$nvidiaSmiPath = $hostInfo.tools.nvidia_smi.path
$bt = [char]96

$notes = @"
# Phase 0 Host Capture

Captured at: $($hostInfo.captured_at)

## Machine

- Name: $($hostInfo.machine_name)
- OS: $($hostInfo.os.caption) $($hostInfo.os.version) build $($hostInfo.os.build_number)
- CPU: $($hostInfo.cpu.name)
- Memory: $($hostInfo.memory.total_physical_gib) GiB

## GPU

$gpuSummary

## Tools

- dotnet: $($hostInfo.tools.dotnet.version) at $bt$dotnetPath$bt
- ffmpeg: $($hostInfo.tools.ffmpeg.version_line) at $bt$ffmpegPath$bt
- nvidia-smi: $bt$nvidiaSmiPath$bt

## Notes

- This is a development-host capture. RTX 3060 12GB target validation remains separate.
- Do not add private audio, model files, or transcript dumps to Git.
"@

Set-Content -LiteralPath $notesPath -Value $notes -Encoding UTF8

Write-Host "Wrote Phase 0 host capture to:"
Write-Host $runDir
