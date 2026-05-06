using System.IO;

namespace KoeNote.Cleanup;

public sealed record CleanupPaths(
    string AppDataRoot,
    string LocalAppDataRoot,
    string ProgramDataRoot)
{
    public string UserRoot => Path.Combine(AppDataRoot, "KoeNote");

    public string Jobs => Path.Combine(UserRoot, "jobs");

    public string DatabasePath => Path.Combine(UserRoot, "jobs.sqlite");

    public string SettingsPath => Path.Combine(UserRoot, "settings.json");

    public string SetupStatePath => Path.Combine(UserRoot, "setup-state.json");

    public string SetupReportPath => Path.Combine(UserRoot, "setup_report.json");

    public string Logs => Path.Combine(LocalAppDataRoot, "KoeNote", "logs");

    public string ModelDownloads => Path.Combine(LocalAppDataRoot, "KoeNote", "model-downloads");

    public string PythonPackages => Path.Combine(LocalAppDataRoot, "KoeNote", "python-packages");

    public string UpdateBackups => Path.Combine(LocalAppDataRoot, "KoeNote", "backups", "updates");

    public string UserModels => Path.Combine(LocalAppDataRoot, "KoeNote", "models");

    public string MachineModels => Path.Combine(ProgramDataRoot, "KoeNote", "models");

    public static CleanupPaths FromEnvironment()
    {
        return new CleanupPaths(
            Environment.GetEnvironmentVariable("KOENOTE_APPDATA_ROOT") ??
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetEnvironmentVariable("KOENOTE_LOCALAPPDATA_ROOT") ??
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetEnvironmentVariable("KOENOTE_PROGRAMDATA_ROOT") ??
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
    }
}
