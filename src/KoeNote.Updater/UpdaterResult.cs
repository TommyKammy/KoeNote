using System.Text.Json;

namespace KoeNote.Updater;

public sealed record UpdaterResult(
    string Status,
    int ExitCode,
    string Version,
    string InstallerPath,
    string TargetExePath,
    string LogPath,
    DateTimeOffset CompletedAt,
    string Message)
{
    public static UpdaterResult From(UpdaterExitCode exitCode, UpdaterOptions options, string message)
    {
        return new UpdaterResult(
            exitCode.ToString(),
            (int)exitCode,
            options.Version,
            options.MsiPath,
            options.TargetExePath,
            options.LogPath,
            DateTimeOffset.UtcNow,
            message);
    }

    public static void Write(string path, UpdaterResult result)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
