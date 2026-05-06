using System.IO;

namespace KoeNote.App.Services;

public enum InstallScope
{
    CurrentUser,
    AllUsers
}

public sealed record AppPathOptions(
    string? AppDataRoot = null,
    string? LocalAppDataRoot = null,
    string? ProgramDataRoot = null,
    string? AppBaseDirectory = null,
    InstallScope InstallScope = InstallScope.CurrentUser);

public sealed class AppPaths
{
    public AppPaths(string? appDataRoot = null, string? localAppDataRoot = null, string? appBaseDirectory = null)
        : this(new AppPathOptions(
            AppDataRoot: appDataRoot,
            LocalAppDataRoot: localAppDataRoot,
            AppBaseDirectory: appBaseDirectory))
    {
    }

    public AppPaths(AppPathOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var appData = options.AppDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = options.LocalAppDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programData = options.ProgramDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var baseDirectory = options.AppBaseDirectory ?? AppContext.BaseDirectory;

        InstallScope = options.InstallScope;
        Root = Path.Combine(appData, "KoeNote");
        Jobs = Path.Combine(Root, "jobs");
        DatabasePath = Path.Combine(Root, "jobs.sqlite");
        SettingsPath = Path.Combine(Root, "settings.json");
        SetupStatePath = Path.Combine(Root, "setup-state.json");
        SetupReportPath = Path.Combine(Root, "setup_report.json");
        Logs = Path.Combine(localAppData, "KoeNote", "logs");
        UserModels = Path.Combine(localAppData, "KoeNote", "models");
        MachineModels = Path.Combine(programData, "KoeNote", "models");
        DefaultModelStorageRoot = InstallScope == InstallScope.AllUsers ? MachineModels : UserModels;
        ModelDownloads = Path.Combine(localAppData, "KoeNote", "model-downloads");
        UpdateDownloads = Path.Combine(localAppData, "KoeNote", "updates");
        UpdateHistoryPath = Path.Combine(UpdateDownloads, "history.jsonl");
        PythonPackages = Path.Combine(localAppData, "KoeNote", "python-packages");
        UpdateBackups = Path.Combine(localAppData, "KoeNote", "backups", "updates");
        RuntimeTools = Path.Combine(baseDirectory, "tools");
        Models = Path.Combine(baseDirectory, "models");
        ModelCatalogPath = Path.Combine(baseDirectory, "catalog", "model-catalog.json");
        FfmpegPath = Path.Combine(RuntimeTools, "ffmpeg.exe");
        CrispAsrPath = Path.Combine(RuntimeTools, "asr", "crispasr.exe");
        FasterWhisperScriptPath = Path.Combine(baseDirectory, "scripts", "asr", "faster_whisper_transcribe.py");
        ReazonSpeechK2ScriptPath = Path.Combine(baseDirectory, "scripts", "asr", "reazonspeech_k2_transcribe.py");
        DiarizeWorkerScriptPath = Path.Combine(baseDirectory, "scripts", "diarization", "diarize_worker.py");
        LlamaCompletionPath = Path.Combine(RuntimeTools, "review", "llama-completion.exe");
        VibeVoiceAsrModelPath = Path.Combine(Models, "asr", "vibevoice-asr-q4_k.gguf");
        KotobaWhisperFasterModelPath = Path.Combine(Models, "asr", "kotoba-whisper-v2.2-faster");
        FasterWhisperModelPath = Path.Combine(Models, "asr", "faster-whisper-large-v3-turbo");
        FasterWhisperLargeV3ModelPath = Path.Combine(Models, "asr", "faster-whisper-large-v3");
        ReazonSpeechK2ModelPath = Path.Combine(Models, "asr", "reazonspeech-k2-v3");
        ReviewModelPath = Path.Combine(Models, "review", "llm-jp-4-8B-thinking-Q4_K_M.gguf");
    }

    public InstallScope InstallScope { get; }

    public string Root { get; }

    public string Jobs { get; }

    public string DatabasePath { get; }

    public string SettingsPath { get; }

    public string SetupStatePath { get; }

    public string SetupReportPath { get; }

    public string Logs { get; }

    public string UserModels { get; }

    public string MachineModels { get; }

    public string DefaultModelStorageRoot { get; }

    public string ModelDownloads { get; }

    public string UpdateDownloads { get; }

    public string UpdateHistoryPath { get; }

    public string PythonPackages { get; }

    public string UpdateBackups { get; }

    public string RuntimeTools { get; }

    public string Models { get; }

    public string ModelCatalogPath { get; }

    public string FfmpegPath { get; }

    public string CrispAsrPath { get; }

    public string FasterWhisperScriptPath { get; }

    public string ReazonSpeechK2ScriptPath { get; }

    public string DiarizeWorkerScriptPath { get; }

    public string LlamaCompletionPath { get; }

    public string VibeVoiceAsrModelPath { get; }

    public string KotobaWhisperFasterModelPath { get; }

    public string FasterWhisperModelPath { get; }

    public string FasterWhisperLargeV3ModelPath { get; }

    public string ReazonSpeechK2ModelPath { get; }

    public string ReviewModelPath { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Jobs);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(UserModels);
        Directory.CreateDirectory(ModelDownloads);
        Directory.CreateDirectory(UpdateDownloads);
        Directory.CreateDirectory(PythonPackages);
        Directory.CreateDirectory(UpdateBackups);

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
