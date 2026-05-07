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
    UpdateInstallerLaunchResult Launch(string installerPath, string? expectedSha256 = null);
}

public sealed record UpdateInstallerLaunchOptions(bool RequireAuthenticodeSignature)
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

    public UpdateInstallerLaunchResult Launch(string installerPath, string? expectedSha256 = null)
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

        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            var actualSha256 = ComputeSha256(fullPath);
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Verified update installer SHA256 no longer matches the release metadata.");
            }
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

        var startInfo = new ProcessStartInfo
        {
            FileName = "msiexec.exe",
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("/i");
        startInfo.ArgumentList.Add(fullPath);

        _startProcess(startInfo);
        return new UpdateInstallerLaunchResult(fullPath, DateTimeOffset.Now, trustDescription, signatureVerified);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
