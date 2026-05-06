using System.Diagnostics;
using System.ComponentModel;
using KoeNote.App.Services.Updates;

namespace KoeNote.App.Tests;

public sealed class UpdateInstallerLauncherTests
{
    [Fact]
    public void Launch_StartsMsiexecWithVerifiedInstallerPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var installerPath = Path.Combine(root, "KoeNote-v0.14.0-win-x64.msi");
        Directory.CreateDirectory(root);
        File.WriteAllText(installerPath, "msi");
        ProcessStartInfo? captured = null;
        var verifier = new RecordingSignatureVerifier();
        var launcher = new UpdateInstallerLauncher(
            startInfo =>
            {
                captured = startInfo;
                return null;
            },
            verifier);

        var result = launcher.Launch(installerPath);

        Assert.Equal(Path.GetFullPath(installerPath), result.InstallerPath);
        Assert.Equal("CN=KoeNote Test", result.SignerSubject);
        Assert.Equal(Path.GetFullPath(installerPath), verifier.VerifiedPath);
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
    public void Launch_RejectsUnsignedInstallerBeforeStartingMsiexec()
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
            new FailingSignatureVerifier());

        Assert.Throws<InvalidOperationException>(() => launcher.Launch(installerPath));
        Assert.False(started);
    }

    [Fact]
    public void Launch_NormalizesVerifierWin32FailureBeforeStartingMsiexec()
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
            new Win32FailingSignatureVerifier());

        Assert.Throws<InvalidOperationException>(() => launcher.Launch(installerPath));
        Assert.False(started);
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
}
