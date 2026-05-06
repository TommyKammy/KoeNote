using System.Diagnostics;
using System.IO;

namespace KoeNote.App.Services.Updates;

public sealed record UpdateInstallerLaunchResult(string InstallerPath, DateTimeOffset StartedAt);

public interface IUpdateInstallerLauncher
{
    UpdateInstallerLaunchResult Launch(string installerPath);
}

public sealed class UpdateInstallerLauncher(Func<ProcessStartInfo, Process?>? startProcess = null) : IUpdateInstallerLauncher
{
    private readonly Func<ProcessStartInfo, Process?> _startProcess = startProcess ?? Process.Start;

    public UpdateInstallerLaunchResult Launch(string installerPath)
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

        var startInfo = new ProcessStartInfo
        {
            FileName = "msiexec.exe",
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("/i");
        startInfo.ArgumentList.Add(fullPath);

        _startProcess(startInfo);
        return new UpdateInstallerLaunchResult(fullPath, DateTimeOffset.Now);
    }
}
