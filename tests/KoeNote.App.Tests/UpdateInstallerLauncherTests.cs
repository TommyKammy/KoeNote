using System.Diagnostics;
using System.ComponentModel;
using System.Security.Cryptography;
using KoeNote.App.Services.Updates;

namespace KoeNote.App.Tests;

public sealed class UpdateInstallerLauncherTests
{
    [Fact]
    public void Launch_StartsMsiexecWithSha256VerifiedInstallerPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var installerPath = Path.Combine(root, "KoeNote-v0.14.0-win-x64.msi");
        Directory.CreateDirectory(root);
        var payload = "msi";
        File.WriteAllText(installerPath, payload);
        ProcessStartInfo? captured = null;
        var launcher = new UpdateInstallerLauncher(
            startInfo =>
            {
                captured = startInfo;
                return null;
            });

        var result = launcher.Launch(installerPath, ComputeSha256(payload));

        Assert.Equal(Path.GetFullPath(installerPath), result.InstallerPath);
        Assert.Equal("SHA256 verified download", result.TrustDescription);
        Assert.False(result.SignatureVerified);
        Assert.NotNull(captured);
        Assert.Equal("msiexec.exe", captured.FileName);
        Assert.Equal(["/i", Path.GetFullPath(installerPath)], captured.ArgumentList.ToArray());
        Assert.False(captured.UseShellExecute);
    }

    [Fact]
    public void Launch_RejectsMissingInstaller()
    {
        var launcher = new UpdateInstallerLauncher(_ => null, new RecordingSignatureVerifier());

        Assert.Throws<FileNotFoundException>(() => launcher.Launch(Path.Combine(Path.GetTempPath(), "missing.msi")));
    }

    [Fact]
    public void Launch_RejectsNonMsiFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var installerPath = Path.Combine(root, "KoeNote-v0.14.0-win-x64.txt");
        Directory.CreateDirectory(root);
        File.WriteAllText(installerPath, "not-msi");
        var launcher = new UpdateInstallerLauncher(_ => null, new RecordingSignatureVerifier());

        Assert.Throws<InvalidOperationException>(() => launcher.Launch(installerPath));
    }

    [Fact]
    public void Launch_RejectsInstallerWhenSha256ChangedBeforeStartingMsiexec()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var installerPath = Path.Combine(root, "KoeNote-v0.14.0-win-x64.msi");
        Directory.CreateDirectory(root);
        File.WriteAllText(installerPath, "tampered-msi");
        var started = false;
        var launcher = new UpdateInstallerLauncher(
            _ =>
            {
                started = true;
                return null;
            },
            options: new UpdateInstallerLaunchOptions(RequireAuthenticodeSignature: false));

        var exception = Assert.Throws<InvalidOperationException>(() => launcher.Launch(installerPath, ComputeSha256("original-msi")));
        Assert.Contains("SHA256", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(started);
    }

    [Fact]
    public void Launch_RequiresSignatureWhenConfiguredBeforeStartingMsiexec()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var installerPath = Path.Combine(root, "KoeNote-v0.14.0-win-x64.msi");
        Directory.CreateDirectory(root);
        File.WriteAllText(installerPath, "msi");
        var started = false;
        var launcher = new UpdateInstallerLauncher(
            _ =>
            {
                started = true;
                return null;
            },
            new FailingSignatureVerifier(),
            new UpdateInstallerLaunchOptions(RequireAuthenticodeSignature: true));

        Assert.Throws<InvalidOperationException>(() => launcher.Launch(installerPath));
        Assert.False(started);
    }

    [Fact]
    public void Launch_NormalizesVerifierWin32FailureWhenSignatureIsRequiredBeforeStartingMsiexec()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var installerPath = Path.Combine(root, "KoeNote-v0.14.0-win-x64.msi");
        Directory.CreateDirectory(root);
        File.WriteAllText(installerPath, "msi");
        var started = false;
        var launcher = new UpdateInstallerLauncher(
            _ =>
            {
                started = true;
                return null;
            },
            new Win32FailingSignatureVerifier(),
            new UpdateInstallerLaunchOptions(RequireAuthenticodeSignature: true));

        Assert.Throws<InvalidOperationException>(() => launcher.Launch(installerPath));
        Assert.False(started);
    }

    [Fact]
    public void Launch_UsesSignatureVerifierWhenSignatureIsRequired()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var installerPath = Path.Combine(root, "KoeNote-v0.14.0-win-x64.msi");
        Directory.CreateDirectory(root);
        File.WriteAllText(installerPath, "msi");
        var verifier = new RecordingSignatureVerifier();
        var launcher = new UpdateInstallerLauncher(
            _ => null,
            verifier,
            new UpdateInstallerLaunchOptions(RequireAuthenticodeSignature: true));

        var result = launcher.Launch(installerPath);

        Assert.Equal(Path.GetFullPath(installerPath), verifier.VerifiedPath);
        Assert.Equal("Authenticode signature: CN=KoeNote Test", result.TrustDescription);
        Assert.True(result.SignatureVerified);
    }

    private sealed class RecordingSignatureVerifier : IUpdateInstallerSignatureVerifier
    {
        public string? VerifiedPath { get; private set; }

        public UpdateInstallerSignatureVerificationResult Verify(string installerPath)
        {
            VerifiedPath = installerPath;
            return new UpdateInstallerSignatureVerificationResult(installerPath, "CN=KoeNote Test", DateTimeOffset.Now);
        }
    }

    private sealed class FailingSignatureVerifier : IUpdateInstallerSignatureVerifier
    {
        public UpdateInstallerSignatureVerificationResult Verify(string installerPath)
        {
            throw new InvalidOperationException("Unsigned update installer.");
        }
    }

    private sealed class Win32FailingSignatureVerifier : IUpdateInstallerSignatureVerifier
    {
        public UpdateInstallerSignatureVerificationResult Verify(string installerPath)
        {
            throw new Win32Exception(unchecked((int)0x800B0100));
        }
    }

    private static string ComputeSha256(string payload)
    {
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
