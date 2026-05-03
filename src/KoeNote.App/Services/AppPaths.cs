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
        RuntimeTools = Path.Combine(AppContext.BaseDirectory, "tools");
        Models = Path.Combine(AppContext.BaseDirectory, "models");
        CrispAsrPath = Path.Combine(RuntimeTools, "crispasr.exe");
        LlamaCompletionPath = Path.Combine(RuntimeTools, "llama-completion.exe");
        VibeVoiceAsrModelPath = Path.Combine(Models, "asr", "vibevoice-asr-q4_k.gguf");
        ReviewModelPath = Path.Combine(Models, "review", "llm-jp-4-8B-thinking-Q4_K_M.gguf");
    }

    public string Root { get; }

    public string Jobs { get; }

    public string DatabasePath { get; }

    public string SettingsPath { get; }

    public string Logs { get; }

    public string RuntimeTools { get; }

    public string Models { get; }

    public string CrispAsrPath { get; }

    public string LlamaCompletionPath { get; }

    public string VibeVoiceAsrModelPath { get; }

    public string ReviewModelPath { get; }

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
