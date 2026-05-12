using System.IO;

namespace KoeNote.Cleanup;

public sealed record CleanupPaths(
    string AppDataRoot,
    string LocalAppDataRoot,
    string ProgramDataRoot)
{
    public string UserRoot => Path.Combine(AppDataRoot, "KoeNote");

    public string LocalRoot => Path.Combine(LocalAppDataRoot, "KoeNote");

    public string MachineRoot => Path.Combine(ProgramDataRoot, "KoeNote");

    public string Jobs => Path.Combine(UserRoot, "jobs");

    public string DatabasePath => Path.Combine(UserRoot, "jobs.sqlite");

    public string SettingsPath => Path.Combine(UserRoot, "settings.json");

    public string SetupStatePath => Path.Combine(UserRoot, "setup-state.json");

    public string SetupReportPath => Path.Combine(UserRoot, "setup_report.json");

    public string Logs => Path.Combine(LocalRoot, "logs");

    public string ModelDownloads => Path.Combine(LocalRoot, "model-downloads");

    public string PythonPackages => Path.Combine(LocalRoot, "python-packages");

    public string PythonEnvironments => Path.Combine(LocalRoot, "python-envs");

    public string UpdateDownloads => Path.Combine(LocalRoot, "updates");

    public string UpdateBackups => Path.Combine(LocalRoot, "backups", "updates");

    public string UserModels => Path.Combine(LocalRoot, "models");

    public string MachineModels => Path.Combine(MachineRoot, "models");

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
