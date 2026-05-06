using System.Diagnostics;
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
        var launcher = new UpdateInstallerLauncher(startInfo =>
        {
            captured = startInfo;
            return null;
        });

        var result = launcher.Launch(installerPath);

        Assert.Equal(Path.GetFullPath(installerPath), result.InstallerPath);
        Assert.NotNull(captured);
        Assert.Equal("msiexec.exe", captured.FileName);
        Assert.Equal(["/i", Path.GetFullPath(installerPath)], captured.ArgumentList.ToArray());
        Assert.False(captured.UseShellExecute);
    }

    [Fact]
    public void Launch_RejectsMissingInstaller()
    {
        var launcher = new UpdateInstallerLauncher(_ => null);

        Assert.Throws<FileNotFoundException>(() => launcher.Launch(Path.Combine(Path.GetTempPath(), "missing.msi")));
    }

    [Fact]
    public void Launch_RejectsNonMsiFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var installerPath = Path.Combine(root, "KoeNote-v0.14.0-win-x64.txt");
        Directory.CreateDirectory(root);
        File.WriteAllText(installerPath, "not-msi");
        var launcher = new UpdateInstallerLauncher(_ => null);

        Assert.Throws<InvalidOperationException>(() => launcher.Launch(installerPath));
    }
}
