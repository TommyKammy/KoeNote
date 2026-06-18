using System.Diagnostics;
using System.ComponentModel;
using System.Security.Cryptography;
using KoeNote.App.Services.Updates;

namespace KoeNote.App.Tests;

public sealed class UpdateInstallerLauncherTests
{
    [Fact]
    public void Launch_StartsUpdaterHelperFromTemporaryCopyWithVerifiedInstallerMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var installerPath = Path.Combine(root, "KoeNote-v0.14.0-win-x64.msi");
        var helperPath = Path.Combine(root, "app", "KoeNote.Updater.exe");
        var targetExePath = Path.Combine(root, "app", "KoeNote.App.exe");
        var helperWorkingRoot = Path.Combine(root, "helper-work");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.GetDirectoryName(helperPath)!);
        var payload = "msi";
        File.WriteAllText(installerPath, payload);
        File.WriteAllText(helperPath, "helper");
        File.WriteAllText(Path.ChangeExtension(helperPath, ".dll"), "helper dll");
        ProcessStartInfo? captured = null;
        var launcher = new UpdateInstallerLauncher(
            startInfo =>
            {
                captured = startInfo;
                return new Process();
            },
            options: new UpdateInstallerLaunchOptions(
                RequireAuthenticodeSignature: false,
                HelperPath: helperPath,
                TargetExePath: targetExePath,
                HelperWorkingRoot: helperWorkingRoot,
                ParentProcessId: 1234,
                ParentExitTimeoutSeconds: 15));

        var result = launcher.Launch(installerPath, ComputeSha256(payload), "0.14.0");

        Assert.Equal(Path.GetFullPath(installerPath), result.InstallerPath);
        Assert.Equal("SHA256 verified download", result.TrustDescription);
        Assert.False(result.SignatureVerified);
        Assert.NotNull(captured);
        Assert.NotEqual(Path.GetFullPath(helperPath), captured.FileName);
        Assert.StartsWith(Path.GetFullPath(helperWorkingRoot), captured.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("KoeNote.Updater.exe", Path.GetFileName(captured.FileName));
        Assert.Equal(Path.GetDirectoryName(captured.FileName), captured.WorkingDirectory);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(captured.FileName)!, "KoeNote.Updater.dll")));
        var arguments = captured.ArgumentList.ToArray();
        Assert.Contains("--msi", arguments);
        Assert.Contains(Path.GetFullPath(installerPath), arguments);
        Assert.Contains("--sha256", arguments);
        Assert.Contains(ComputeSha256(payload), arguments);
        Assert.Contains("--target-exe", arguments);
        Assert.Contains(Path.GetFullPath(targetExePath), arguments);
        Assert.Contains("--install-folder", arguments);
        Assert.Contains(Path.GetFullPath(Path.GetDirectoryName(targetExePath)!), arguments);
        Assert.Contains("--parent-pid", arguments);
        Assert.Contains("1234", arguments);
        Assert.Contains("--parent-timeout-seconds", arguments);
        Assert.Contains("15", arguments);
        Assert.Contains("--log", arguments);
        Assert.Contains("--result", arguments);
        Assert.Contains("--version", arguments);
        Assert.Contains("0.14.0", arguments);
        Assert.False(captured.UseShellExecute);
        Assert.True(captured.CreateNoWindow);
    }

    [Fact]
    public void Launch_RemovesOldTemporaryHelperCopies()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var installerPath = Path.Combine(root, "KoeNote-v0.14.0-win-x64.msi");
        var helperPath = Path.Combine(root, "app", "KoeNote.Updater.exe");
        var targetExePath = Path.Combine(root, "app", "KoeNote.App.exe");
        var helperWorkingRoot = Path.Combine(root, "helper-work");
        Directory.CreateDirectory(Path.GetDirectoryName(helperPath)!);
        Directory.CreateDirectory(helperWorkingRoot);
        var oldHelperDirectory = Path.Combine(helperWorkingRoot, "old-helper");
        var recentHelperDirectory = Path.Combine(helperWorkingRoot, "recent-helper");
        Directory.CreateDirectory(oldHelperDirectory);
        Directory.CreateDirectory(recentHelperDirectory);
        File.WriteAllText(Path.Combine(oldHelperDirectory, "KoeNote.Updater.exe"), "old");
        File.WriteAllText(installerPath, "msi");
        File.WriteAllText(helperPath, "helper");
        File.WriteAllText(targetExePath, "app");
        Directory.SetLastWriteTimeUtc(oldHelperDirectory, DateTime.UtcNow.AddDays(-2));
        Directory.SetLastWriteTimeUtc(recentHelperDirectory, DateTime.UtcNow);
        var launcher = new UpdateInstallerLauncher(
            _ => new Process(),
            options: new UpdateInstallerLaunchOptions(
                RequireAuthenticodeSignature: false,
                HelperPath: helperPath,
                TargetExePath: targetExePath,
                HelperWorkingRoot: helperWorkingRoot));

        launcher.Launch(installerPath, ComputeSha256("msi"), "0.14.0");

        Assert.False(Directory.Exists(oldHelperDirectory));
        Assert.True(Directory.Exists(recentHelperDirectory));
        Assert.Contains(
            Directory.EnumerateDirectories(helperWorkingRoot),
            directory => string.Equals(Path.GetFileName(directory), "recent-helper", StringComparison.OrdinalIgnoreCase) is false);
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
                return new Process();
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
        var helperPath = CreateHelper(root);
        Directory.CreateDirectory(root);
        File.WriteAllText(installerPath, "msi");
        var verifier = new RecordingSignatureVerifier();
        var launcher = new UpdateInstallerLauncher(
            _ => new Process(),
            verifier,
            new UpdateInstallerLaunchOptions(RequireAuthenticodeSignature: true, HelperPath: helperPath));

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

    private static string CreateHelper(string root)
    {
        var helperPath = Path.Combine(root, "app", "KoeNote.Updater.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(helperPath)!);
        File.WriteAllText(helperPath, "helper");
        return helperPath;
    }
}
