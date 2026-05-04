param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$publishScript = Join-Path $repoRoot "scripts\phase10\Publish-KoeNote.ps1"
$publishDir = Join-Path $repoRoot "artifacts\publish\KoeNote-$RuntimeIdentifier"
$outputDir = Join-Path $repoRoot "artifacts\installers"
$workRoot = Join-Path $repoRoot "artifacts\installer-work"
$payloadFooterMagic = [Text.Encoding]::UTF8.GetBytes("KOENOTE_PAYLOAD_FOOTER_V2")
$embeddedPayloadLimitBytes = 3500000000

& powershell -NoProfile -ExecutionPolicy Bypass -File $publishScript -Configuration $Configuration -RuntimeIdentifier $RuntimeIdentifier
if ($LASTEXITCODE -ne 0) {
    throw "Publish-KoeNote.ps1 failed with exit code $LASTEXITCODE."
}

Remove-Item -LiteralPath $outputDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $outputDir, $workRoot | Out-Null

function Write-TextFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    Set-Content -LiteralPath $Path -Value $Content -Encoding UTF8
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string]$DestinationDir
    )

    robocopy $SourceDir $DestinationDir /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) {
        throw "Failed to copy $SourceDir to $DestinationDir. robocopy exit code: $LASTEXITCODE"
    }
}

function Get-PayloadRelativePath {
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
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString())
}

function New-InstallerStubSource {
    param(
        [Parameter(Mandatory = $true)][string]$Mode,
        [Parameter(Mandatory = $true)][string]$Title
    )

@"
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

internal static class Installer
{
    private const string Mode = "$Mode";
    private const string Title = "$Title";
    private static readonly byte[] FooterMagic = Encoding.UTF8.GetBytes("KOENOTE_PAYLOAD_FOOTER_V2");

    [STAThread]
    private static int Main()
    {
        try
        {
            string targetOverride = Environment.GetEnvironmentVariable("KOENOTE_INSTALL_TARGET");
            bool isSmokeTest = !string.IsNullOrWhiteSpace(targetOverride);
            string target = isSmokeTest
                ? targetOverride
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "KoeNote");

            if (Mode == "core" || Mode == "full")
            {
                if (Directory.Exists(target))
                {
                    Directory.Delete(target, true);
                }
            }

            Directory.CreateDirectory(target);
            ExtractPayloadToTarget(target);

            if (Mode == "core" || Mode == "full")
            {
                string exe = Path.Combine(target, "KoeNote.App.exe");
                if (!File.Exists(exe))
                {
                    throw new FileNotFoundException("KoeNote.App.exe was not installed.", exe);
                }

                if (!isSmokeTest)
                {
                    CreateShortcut(exe, target);
                }
            }

            Console.WriteLine(Title + " installed to " + target);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(Title + " failed: " + ex.Message);
            return 1;
        }
    }

    private static void ExtractPayloadToTarget(string target)
    {
        string self = System.Reflection.Assembly.GetExecutingAssembly().Location;
        string payloadSource = self;
        PayloadBounds bounds;

        if (!TryReadPayloadBounds(payloadSource, out bounds))
        {
            payloadSource = Path.ChangeExtension(self, ".payload");
            if (!File.Exists(payloadSource) || !TryReadPayloadBounds(payloadSource, out bounds))
            {
                throw new InvalidOperationException("Installer payload was not found. Keep the .payload file next to this .exe.");
            }
        }

        using (FileStream input = File.OpenRead(payloadSource))
        {
            long payloadEnd = bounds.Offset + bounds.Length;
            input.Seek(bounds.Offset, SeekOrigin.Begin);
            while (input.Position < payloadEnd)
            {
                byte[] pathLengthBytes = new byte[sizeof(int)];
                ReadExact(input, pathLengthBytes, 0, pathLengthBytes.Length);
                int pathLength = BitConverter.ToInt32(pathLengthBytes, 0);
                if (pathLength <= 0 || pathLength > 32767)
                {
                    throw new InvalidOperationException("Installer payload entry path length is invalid.");
                }

                byte[] pathBytes = new byte[pathLength];
                ReadExact(input, pathBytes, 0, pathBytes.Length);
                string relativePath = Encoding.UTF8.GetString(pathBytes).Replace('/', Path.DirectorySeparatorChar);

                byte[] fileLengthBytes = new byte[sizeof(long)];
                ReadExact(input, fileLengthBytes, 0, fileLengthBytes.Length);
                long fileLength = BitConverter.ToInt64(fileLengthBytes, 0);
                if (fileLength < 0 || input.Position + fileLength > payloadEnd)
                {
                    throw new InvalidOperationException("Installer payload entry length is invalid: " + relativePath);
                }

                string destination = GetSafeDestination(target, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                using (FileStream output = File.Create(destination))
                {
                    CopyExact(input, output, fileLength);
                }
            }

            if (input.Position != payloadEnd)
            {
                throw new InvalidOperationException("Installer payload ended at an unexpected position.");
            }
        }
    }

    private static bool TryReadPayloadBounds(string path, out PayloadBounds bounds)
    {
        bounds = new PayloadBounds();
        long footerLength = FooterMagic.Length + (sizeof(long) * 2);
        FileInfo file = new FileInfo(path);
        if (!file.Exists || file.Length <= footerLength)
        {
            return false;
        }

        using (FileStream input = File.OpenRead(path))
        {
            input.Seek(-footerLength, SeekOrigin.End);
            byte[] footer = new byte[footerLength];
            ReadExact(input, footer, 0, footer.Length);

            for (int i = 0; i < FooterMagic.Length; i++)
            {
                if (footer[i] != FooterMagic[i])
                {
                    return false;
                }
            }

            long payloadOffset = BitConverter.ToInt64(footer, FooterMagic.Length);
            long payloadLength = BitConverter.ToInt64(footer, FooterMagic.Length + sizeof(long));
            long payloadEnd = payloadOffset + payloadLength;
            if (payloadLength <= 0 || payloadOffset < 0 || payloadEnd != input.Length - footerLength)
            {
                return false;
            }

            bounds = new PayloadBounds(payloadOffset, payloadLength);
            return true;
        }
    }

    private static void CopyExact(Stream input, Stream output, long bytesToCopy)
    {
        byte[] buffer = new byte[1024 * 1024];
        while (bytesToCopy > 0)
        {
            int readSize = bytesToCopy > buffer.Length ? buffer.Length : (int)bytesToCopy;
            int read = input.Read(buffer, 0, readSize);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            output.Write(buffer, 0, read);
            bytesToCopy -= read;
        }
    }

    private static void ReadExact(Stream input, byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            int read = input.Read(buffer, offset, count);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
            count -= read;
        }
    }

    private static string GetSafeDestination(string target, string relativePath)
    {
        string destination = Path.GetFullPath(Path.Combine(target, relativePath));
        string normalizedTarget = Path.GetFullPath(target);
        if (!normalizedTarget.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            normalizedTarget += Path.DirectorySeparatorChar;
        }

        if (!destination.StartsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Payload entry escapes install target: " + relativePath);
        }

        return destination;
    }

    private struct PayloadBounds
    {
        public readonly long Offset;
        public readonly long Length;

        public PayloadBounds(long offset, long length)
        {
            Offset = offset;
            Length = length;
        }
    }

    private static void CreateShortcut(string exe, string workingDirectory)
    {
        string shortcutDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs");
        Directory.CreateDirectory(shortcutDir);
        string shortcutPath = Path.Combine(shortcutDir, "KoeNote.lnk");
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
        {
            return;
        }

        dynamic shell = Activator.CreateInstance(shellType);
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = exe;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.Description = "KoeNote";
        shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }
}
"@
}

function Resolve-CSharpCompiler {
    $candidates = @(
        (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
        (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw "C# compiler was not found in the Windows .NET Framework directories."
}

function New-InstallerExe {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][string]$Mode,
        [Parameter(Mandatory = $true)][string]$PayloadDir
    )

    $work = Join-Path $workRoot $Name
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $source = Join-Path $work "Installer.cs"
    $stub = Join-Path $work "stub.exe"
    $exe = Join-Path $outputDir "$Name.exe"
    $sidecarPayload = Join-Path $outputDir "$Name.payload"
    Set-Content -LiteralPath $source -Value (New-InstallerStubSource -Mode $Mode -Title $Title) -Encoding UTF8

    $csc = Resolve-CSharpCompiler
    $compileOutput = & $csc /nologo /target:exe /platform:anycpu /out:$stub /reference:Microsoft.CSharp.dll $source 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to compile installer stub for $Name`n$($compileOutput -join [Environment]::NewLine)"
    }

    $payloadContentBytes = (Get-ChildItem -LiteralPath $PayloadDir -Recurse -File | Measure-Object Length -Sum).Sum
    $embedPayload = $payloadContentBytes -lt $embeddedPayloadLimitBytes
    if (Test-Path -LiteralPath $sidecarPayload) {
        Remove-Item -LiteralPath $sidecarPayload -Force
    }

    $output = [IO.File]::Create($exe)
    try {
        $stubInput = [IO.File]::OpenRead($stub)
        try {
            $stubInput.CopyTo($output)
        }
        finally {
            $stubInput.Dispose()
        }

        if (-not $embedPayload) {
            $output.Dispose()
            Copy-InstallerPayload -PayloadDir $PayloadDir -OutputPath $sidecarPayload -PayloadOffset 0
            return $exe
        }

        $payloadOffset = $output.Position
        Write-PayloadEntries -PayloadDir $PayloadDir -Output $output -PayloadOffset $payloadOffset
    }
    finally {
        $output.Dispose()
    }

    return $exe
}

function Write-PayloadEntries {
    param(
        [Parameter(Mandatory = $true)][string]$PayloadDir,
        [Parameter(Mandatory = $true)][IO.Stream]$Output,
        [Parameter(Mandatory = $true)][Int64]$PayloadOffset
    )

    $files = Get-ChildItem -LiteralPath $PayloadDir -Recurse -File | Sort-Object FullName
    foreach ($file in $files) {
        $relativePath = Get-PayloadRelativePath -Root $PayloadDir -Path $file.FullName
        $pathBytes = [Text.Encoding]::UTF8.GetBytes($relativePath)
        $pathLengthBytes = [BitConverter]::GetBytes([Int32]$pathBytes.Length)
        $fileLengthBytes = [BitConverter]::GetBytes([Int64]$file.Length)
        $Output.Write($pathLengthBytes, 0, $pathLengthBytes.Length)
        $Output.Write($pathBytes, 0, $pathBytes.Length)
        $Output.Write($fileLengthBytes, 0, $fileLengthBytes.Length)

        $payloadInput = [IO.File]::OpenRead($file.FullName)
        try {
            $payloadInput.CopyTo($Output)
        }
        finally {
            $payloadInput.Dispose()
        }
    }

    $payloadLength = $Output.Position - $PayloadOffset
    $footerOffsetBytes = [BitConverter]::GetBytes([Int64]$PayloadOffset)
    $footerLengthBytes = [BitConverter]::GetBytes([Int64]$payloadLength)
    $Output.Write($payloadFooterMagic, 0, $payloadFooterMagic.Length)
    $Output.Write($footerOffsetBytes, 0, $footerOffsetBytes.Length)
    $Output.Write($footerLengthBytes, 0, $footerLengthBytes.Length)
}

function Copy-InstallerPayload {
    param(
        [Parameter(Mandatory = $true)][string]$PayloadDir,
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [Parameter(Mandatory = $true)][Int64]$PayloadOffset
    )

    $payloadOutput = [IO.File]::Create($OutputPath)
    try {
        Write-PayloadEntries -PayloadDir $PayloadDir -Output $payloadOutput -PayloadOffset $PayloadOffset
    }
    finally {
        $payloadOutput.Dispose()
    }
}

$corePayload = Join-Path $workRoot "core-payload"
Copy-DirectoryContents -SourceDir $publishDir -DestinationDir $corePayload

$created = @(
    (New-InstallerExe -Name "KoeNote-Core-Setup" -Title "KoeNote Core" -Mode "core" -PayloadDir $corePayload)
)

$manifest = [ordered]@{
    generatedAt = (Get-Date).ToString("o")
    outputDirectory = $outputDir
    installTarget = "%LOCALAPPDATA%\Programs\KoeNote"
    smokeTestOverride = "Set KOENOTE_INSTALL_TARGET to install into a temporary directory without creating a Start Menu shortcut."
    packageRole = "lightweight-core"
    modelPayloads = [ordered]@{
        included = $false
        note = "Phase 10 core setup intentionally excludes ASR and review model binaries. Model installation moves to Phase 11 Model Catalog / Download Manager and Phase 12 Setup Wizard."
    }
    installers = $created | ForEach-Object {
        $sidecar = [IO.Path]::ChangeExtension($_, ".payload")
        [ordered]@{
            path = $_
            bytes = (Get-Item -LiteralPath $_).Length
            sidecarPayloadPath = if (Test-Path -LiteralPath $sidecar) { $sidecar } else { $null }
            sidecarPayloadBytes = if (Test-Path -LiteralPath $sidecar) { (Get-Item -LiteralPath $sidecar).Length } else { $null }
        }
    }
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputDir "installer-manifest.json") -Encoding UTF8

$created | ForEach-Object { Get-Item -LiteralPath $_ | Select-Object FullName, Length }
