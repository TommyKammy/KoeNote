using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;

namespace KoeNote.App.Services.Updates;

public sealed record UpdateInstallerLaunchResult(
    string InstallerPath,
    DateTimeOffset StartedAt,
    string TrustDescription,
    bool SignatureVerified);

public interface IUpdateInstallerLauncher
{
    UpdateInstallerLaunchResult Launch(string installerPath, string? expectedSha256 = null, string? version = null);
}

public sealed record UpdateInstallerLaunchOptions(
    bool RequireAuthenticodeSignature,
    string? HelperPath = null,
    string? TargetExePath = null,
    string? HelperWorkingRoot = null,
    int? ParentProcessId = null,
    int ParentExitTimeoutSeconds = 120)
{
    public static UpdateInstallerLaunchOptions FromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable("KOENOTE_UPDATE_REQUIRE_AUTHENTICODE_SIGNATURE");
        var requireSignature = string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

        return new UpdateInstallerLaunchOptions(requireSignature);
    }
}

public sealed class UpdateInstallerLauncher(
    Func<ProcessStartInfo, Process?>? startProcess = null,
    IUpdateInstallerSignatureVerifier? signatureVerifier = null,
    UpdateInstallerLaunchOptions? options = null) : IUpdateInstallerLauncher
{
    private readonly Func<ProcessStartInfo, Process?> _startProcess = startProcess ?? Process.Start;
    private readonly IUpdateInstallerSignatureVerifier _signatureVerifier =
        signatureVerifier ?? new AuthenticodeUpdateInstallerSignatureVerifier();
    private readonly UpdateInstallerLaunchOptions _options = options ?? UpdateInstallerLaunchOptions.FromEnvironment();

    public UpdateInstallerLaunchResult Launch(string installerPath, string? expectedSha256 = null, string? version = null)
    {
        if (string.IsNullOrWhiteSpace(installerPath))
        {
            throw new ArgumentException("Installer path is required.", nameof(installerPath));
        }

        var fullPath = Path.GetFullPath(installerPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Verified update installer was not found.", fullPath);
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".msi", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Verified update installer must be an MSI file.");
        }

        var verifiedSha256 = expectedSha256;
        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            var actualSha256 = ComputeSha256(fullPath);
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Verified update installer SHA256 no longer matches the release metadata.");
            }
        }
        else
        {
            verifiedSha256 = ComputeSha256(fullPath);
        }

        var trustDescription = "SHA256 verified download";
        var signatureVerified = false;
        if (_options.RequireAuthenticodeSignature)
        {
            UpdateInstallerSignatureVerificationResult signature;
            try
            {
                signature = _signatureVerifier.Verify(fullPath);
            }
            catch (Win32Exception exception)
            {
                throw new InvalidOperationException($"Update installer signature verification failed: {exception.Message}", exception);
            }

            trustDescription = $"Authenticode signature: {signature.SignerSubject}";
            signatureVerified = true;
        }

        var helperPath = CopyHelperToTemp();
        var targetExePath = ResolveTargetExePath();
        var installFolderPath = Path.GetDirectoryName(targetExePath)
            ?? throw new InvalidOperationException("KoeNote target executable path has no directory.");
        var parentProcessId = _options.ParentProcessId ?? Environment.ProcessId;
        var logPath = CreateUpdateLogPath(fullPath, version, ".log");
        var resultPath = CreateUpdateLogPath(fullPath, version, ".result.json");

        var startInfo = new ProcessStartInfo
        {
            FileName = helperPath,
            WorkingDirectory = Path.GetDirectoryName(helperPath) ?? Path.GetTempPath(),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--msi");
        startInfo.ArgumentList.Add(fullPath);
        startInfo.ArgumentList.Add("--sha256");
        startInfo.ArgumentList.Add(verifiedSha256!);
        startInfo.ArgumentList.Add("--target-exe");
        startInfo.ArgumentList.Add(targetExePath);
        startInfo.ArgumentList.Add("--install-folder");
        startInfo.ArgumentList.Add(installFolderPath);
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(parentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--parent-timeout-seconds");
        startInfo.ArgumentList.Add(_options.ParentExitTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--log");
        startInfo.ArgumentList.Add(logPath);
        startInfo.ArgumentList.Add("--result");
        startInfo.ArgumentList.Add(resultPath);
        startInfo.ArgumentList.Add("--version");
        startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(version) ? "unknown" : version);

        Process? process;
        try
        {
            process = _startProcess(startInfo);
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException($"KoeNote updater helper could not be started: {exception.Message}", exception);
        }

        if (process is null)
        {
            throw new InvalidOperationException("KoeNote updater helper could not be started.");
        }

        return new UpdateInstallerLaunchResult(fullPath, DateTimeOffset.Now, trustDescription, signatureVerified);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string CopyHelperToTemp()
    {
        var sourcePath = Path.GetFullPath(_options.HelperPath ?? Path.Combine(AppContext.BaseDirectory, "KoeNote.Updater.exe"));
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("KoeNote updater helper was not found.", sourcePath);
        }

        var workingRoot = Path.GetFullPath(_options.HelperWorkingRoot ?? Path.Combine(Path.GetTempPath(), "KoeNote", "updater-helper"));
        var helperDirectory = Path.Combine(workingRoot, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(helperDirectory);

        var sourceDirectory = Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException("KoeNote updater helper path has no directory.");
        var helperFilePrefix = Path.GetFileNameWithoutExtension(sourcePath);
        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory, $"{helperFilePrefix}.*"))
        {
            File.Copy(filePath, Path.Combine(helperDirectory, Path.GetFileName(filePath)), overwrite: true);
        }

        var copiedHelperPath = Path.Combine(helperDirectory, Path.GetFileName(sourcePath));
        if (!File.Exists(copiedHelperPath))
        {
            throw new FileNotFoundException("KoeNote updater helper could not be copied to a temporary location.", copiedHelperPath);
        }

        return copiedHelperPath;
    }

    private string ResolveTargetExePath()
    {
        if (!string.IsNullOrWhiteSpace(_options.TargetExePath))
        {
            return Path.GetFullPath(_options.TargetExePath);
        }

        return Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppContext.BaseDirectory, "KoeNote.App.exe");
    }

    private static string CreateUpdateLogPath(string installerPath, string? version, string extension)
    {
        var directory = Path.GetDirectoryName(installerPath) ?? Path.GetTempPath();
        var safeVersion = string.Concat((string.IsNullOrWhiteSpace(version) ? "unknown" : version)
            .Select(static character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        return Path.Combine(directory, $"KoeNote-update-{safeVersion}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}{extension}");
    }
}
