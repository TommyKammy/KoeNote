using System.IO;

namespace KoeNote.App.Services;

public sealed class AppPaths
{
    public AppPaths(string? appDataRoot = null, string? localAppDataRoot = null)
    {
        var appData = appDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = localAppDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Root = Path.Combine(appData, "KoeNote");
        Jobs = Path.Combine(Root, "jobs");
        DatabasePath = Path.Combine(Root, "jobs.sqlite");
        SettingsPath = Path.Combine(Root, "settings.json");
        Logs = Path.Combine(localAppData, "KoeNote", "logs");
    }

    public string Root { get; }

    public string Jobs { get; }

    public string DatabasePath { get; }

    public string SettingsPath { get; }

    public string Logs { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Jobs);
        Directory.CreateDirectory(Logs);

        if (!File.Exists(SettingsPath))
        {
            File.WriteAllText(SettingsPath, """
                {
                  "asrEngine": "vibevoice-asr-gguf",
                  "reviewEngine": "llm-jp-gguf",
                  "networkAccess": false
                }
                """);
        }
    }
}
