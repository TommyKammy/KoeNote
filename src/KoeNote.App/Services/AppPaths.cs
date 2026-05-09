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
        RuntimeTools = Path.Combine(baseDirectory, "tools");
        AsrRuntimeDirectory = Path.Combine(RuntimeTools, "asr");
        PythonPackages = Path.Combine(localAppData, "KoeNote", "python-packages");
        PythonEnvironments = Path.Combine(localAppData, "KoeNote", "python-envs");
        AsrPythonEnvironment = Path.Combine(PythonEnvironments, "asr");
        AsrPythonPath = Path.Combine(AsrPythonEnvironment, "Scripts", "python.exe");
        DiarizationPythonEnvironment = Path.Combine(PythonEnvironments, "diarization");
        DiarizationPythonPath = Path.Combine(DiarizationPythonEnvironment, "Scripts", "python.exe");
        BundledPythonPath = Path.Combine(RuntimeTools, "python", "python.exe");
        UpdateBackups = Path.Combine(localAppData, "KoeNote", "backups", "updates");
        Models = Path.Combine(baseDirectory, "models");
        ModelCatalogPath = Path.Combine(baseDirectory, "catalog", "model-catalog.json");
        FfmpegPath = Path.Combine(RuntimeTools, "ffmpeg.exe");
        FasterWhisperScriptPath = Path.Combine(baseDirectory, "scripts", "asr", "faster_whisper_transcribe.py");
        ReazonSpeechK2ScriptPath = Path.Combine(baseDirectory, "scripts", "asr", "reazonspeech_k2_transcribe.py");
        DiarizeWorkerScriptPath = Path.Combine(baseDirectory, "scripts", "diarization", "diarize_worker.py");
        ReviewRuntimeDirectory = Path.Combine(RuntimeTools, "review");
        LlamaCompletionPath = Path.Combine(RuntimeTools, "review", "llama-completion.exe");
        CudaReviewRuntimeMarkerPath = Path.Combine(ReviewRuntimeDirectory, ".koenote-cuda-review-runtime");
        AsrCudaRuntimeMarkerPath = Path.Combine(AsrRuntimeDirectory, ".koenote-cuda-asr-runtime");
        TernaryLlamaCompletionPath = Path.Combine(RuntimeTools, "review-ternary", "llama-completion.exe");
        KotobaWhisperFasterModelPath = Path.Combine(Models, "asr", "kotoba-whisper-v2.2-faster");
        WhisperBaseModelPath = Path.Combine(Models, "asr", "whisper-base");
        WhisperSmallModelPath = Path.Combine(Models, "asr", "whisper-small");
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

    public string PythonEnvironments { get; }

    public string AsrPythonEnvironment { get; }

    public string AsrPythonPath { get; }

    public string DiarizationPythonEnvironment { get; }

    public string DiarizationPythonPath { get; }

    public string BundledPythonPath { get; }

    public string UpdateBackups { get; }

    public string RuntimeTools { get; }

    public string AsrRuntimeDirectory { get; }

    public string Models { get; }

    public string ModelCatalogPath { get; }

    public string FfmpegPath { get; }

    public string FasterWhisperScriptPath { get; }

    public string ReazonSpeechK2ScriptPath { get; }

    public string DiarizeWorkerScriptPath { get; }

    public string ReviewRuntimeDirectory { get; }

    public string LlamaCompletionPath { get; }

    public string CudaReviewRuntimeMarkerPath { get; }

    public string AsrCudaRuntimeMarkerPath { get; }

    public string TernaryLlamaCompletionPath { get; }

    public string KotobaWhisperFasterModelPath { get; }

    public string WhisperBaseModelPath { get; }

    public string WhisperSmallModelPath { get; }

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
        Directory.CreateDirectory(PythonEnvironments);
        Directory.CreateDirectory(UpdateBackups);

        if (!File.Exists(SettingsPath))
        {
            File.WriteAllText(SettingsPath, """
                {
                  "asrEngine": "faster-whisper",
                  "reviewEngine": "llm-jp-gguf",
                  "networkAccess": false
                }
                """);
        }
    }
}
