param(
    [Parameter(Mandatory = $true)][string]$PayloadDir,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$ComponentGuidSalt = "KoeNote",
    [string]$ProductRegistryKey = "Software\KoeNote\KoeNote"
)

$ErrorActionPreference = "Stop"

$payloadRoot = [IO.Path]::GetFullPath($PayloadDir)
if (-not (Test-Path -LiteralPath $payloadRoot -PathType Container)) {
    throw "PayloadDir does not exist: $payloadRoot"
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $rootFull = [IO.Path]::GetFullPath($Root)
    if (-not $rootFull.EndsWith([IO.Path]::DirectorySeparatorChar)) {
        $rootFull += [IO.Path]::DirectorySeparatorChar
    }

    $rootUri = [Uri]$rootFull
    $pathUri = [Uri]([IO.Path]::GetFullPath($Path))
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace('/', '\')
}

function New-WixId {
    param(
        [Parameter(Mandatory = $true)][string]$Prefix,
        [Parameter(Mandatory = $true)][string]$Value
    )

    $normalized = [Regex]::Replace($Value, '[^A-Za-z0-9_]', '_')
    if ($normalized.Length -gt 60) {
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            $hashBytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value))
        }
        finally {
            $sha.Dispose()
        }
        $hash = [BitConverter]::ToString($hashBytes, 0, 6).Replace('-', '')
        $normalized = $normalized.Substring(0, 47) + "_" + $hash
    }

    return "$Prefix$normalized"
}

function New-WixGuid {
    param([Parameter(Mandatory = $true)][string]$Value)

    $md5 = [Security.Cryptography.MD5]::Create()
    try {
        $bytes = $md5.ComputeHash([Text.Encoding]::UTF8.GetBytes("$ComponentGuidSalt MSI component: $Value"))
    }
    finally {
        $md5.Dispose()
    }

    return ([Guid]::new($bytes)).ToString("B").ToUpperInvariant()
}

function Escape-Xml {
    param([Parameter(Mandatory = $true)][string]$Value)

    return [Security.SecurityElement]::Escape($Value)
}

$directories = [ordered]@{ "" = "INSTALLFOLDER" }
$files = Get-ChildItem -LiteralPath $payloadRoot -Recurse -File |
    Where-Object {
        $relative = Get-RelativePath -Root $payloadRoot -Path $_.FullName
        -not ($relative -match '(^|\\)models\\.+\.(gguf|bin|safetensors|pt|onnx)$')
    } |
    Sort-Object FullName

foreach ($file in $files) {
    $relative = Get-RelativePath -Root $payloadRoot -Path $file.FullName
    $parts = $relative.Split('\')
    $current = ""

    for ($i = 0; $i -lt $parts.Length - 1; $i++) {
        $current = if ($current.Length -eq 0) { $parts[$i] } else { "$current\$($parts[$i])" }
        if (-not $directories.Contains($current)) {
            $directories[$current] = New-WixId -Prefix "dir_" -Value $current
        }
    }
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('<?xml version="1.0" encoding="utf-8"?>')
$lines.Add('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
$lines.Add('  <Fragment>')

foreach ($entry in $directories.GetEnumerator()) {
    if ($entry.Key.Length -eq 0) {
        continue
    }

    $parentPath = Split-Path -Parent $entry.Key
    if ($null -eq $parentPath) {
        $parentPath = ""
    }

    $parentId = $directories[$parentPath]
    $name = Split-Path -Leaf $entry.Key
    $lines.Add("    <DirectoryRef Id=""$parentId"">")
    $lines.Add("      <Directory Id=""$($entry.Value)"" Name=""$(Escape-Xml $name)"" />")
    $lines.Add('    </DirectoryRef>')
}

$lines.Add('  </Fragment>')
$lines.Add('  <Fragment>')
$lines.Add('    <ComponentGroup Id="PayloadComponents">')

foreach ($entry in $directories.GetEnumerator()) {
    if ($entry.Key.Length -eq 0) {
        continue
    }

    $cleanupComponentId = New-WixId -Prefix "cmp_remove_dir_" -Value $entry.Key
    $lines.Add("      <ComponentRef Id=""$cleanupComponentId"" />")
}

foreach ($file in $files) {
    $relative = Get-RelativePath -Root $payloadRoot -Path $file.FullName
    $componentId = New-WixId -Prefix "cmp_" -Value $relative
    $lines.Add("      <ComponentRef Id=""$componentId"" />")
}

$lines.Add('    </ComponentGroup>')
$lines.Add('  </Fragment>')
$lines.Add('  <Fragment>')

foreach ($entry in $directories.GetEnumerator()) {
    if ($entry.Key.Length -eq 0) {
        continue
    }

    $directoryId = $entry.Value
    $cleanupComponentId = New-WixId -Prefix "cmp_remove_dir_" -Value $entry.Key
    $cleanupComponentGuid = New-WixGuid -Value "directory:$($entry.Key)"
    $lines.Add("    <DirectoryRef Id=""$directoryId"">")
    $lines.Add("      <Component Id=""$cleanupComponentId"" Guid=""$cleanupComponentGuid"">")
    $lines.Add("        <RemoveFolder Id=""rm_$cleanupComponentId"" Directory=""$directoryId"" On=""uninstall"" />")
    $lines.Add("        <RegistryValue Root=""HKCU"" Key=""$ProductRegistryKey\Directories"" Name=""$cleanupComponentId"" Type=""integer"" Value=""1"" KeyPath=""yes"" />")
    $lines.Add('      </Component>')
    $lines.Add('    </DirectoryRef>')
}

foreach ($file in $files) {
    $relative = Get-RelativePath -Root $payloadRoot -Path $file.FullName
    $directoryPath = Split-Path -Parent $relative
    if ($null -eq $directoryPath) {
        $directoryPath = ""
    }

    $directoryId = $directories[$directoryPath]
    $componentId = New-WixId -Prefix "cmp_" -Value $relative
    $componentGuid = New-WixGuid -Value $relative
    $fileId = switch -Regex ($relative) {
        '^KoeNote\.App\.exe$' { 'KoeNoteAppExe'; break }
        '^KoeNoteCleanup\.exe$' { 'KoeNoteCleanupExe'; break }
        default { New-WixId -Prefix "fil_" -Value $relative }
    }
    $source = '$(var.PayloadDir)\' + $relative
    $lines.Add("    <DirectoryRef Id=""$directoryId"">")
    $lines.Add("      <Component Id=""$componentId"" Guid=""$componentGuid"">")
    $lines.Add("        <File Id=""$fileId"" Source=""$(Escape-Xml $source)"" />")
    $lines.Add("        <RemoveFolder Id=""rm_$componentId"" Directory=""$directoryId"" On=""uninstall"" />")
    $lines.Add("        <RegistryValue Root=""HKCU"" Key=""$ProductRegistryKey\Components"" Name=""$componentId"" Type=""integer"" Value=""1"" KeyPath=""yes"" />")
    $lines.Add('      </Component>')
    $lines.Add('    </DirectoryRef>')
}

$lines.Add('  </Fragment>')
$lines.Add('</Wix>')

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
Set-Content -LiteralPath $OutputPath -Value $lines -Encoding UTF8
Write-Host "Generated WiX payload: $OutputPath"
