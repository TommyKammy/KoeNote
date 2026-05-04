using System.Diagnostics;
using System.IO;
using KoeNote.App.Models;

namespace KoeNote.App.Services;

public sealed class ToolStatusService(AppPaths paths)
{
    public IReadOnlyList<StatusItem> GetStatusItems()
    {
        return
        [
            CheckCommand("dotnet", "dotnet", "--version", required: false),
            CheckFile("ffmpeg", paths.FfmpegPath, required: true),
            CheckCommand("nvidia-smi", "nvidia-smi", "--query-gpu=name,memory.total --format=csv,noheader,nounits", required: false),
            CheckFile("crispasr", paths.CrispAsrPath, required: true),
            CheckFile("llama-completion", paths.LlamaCompletionPath, required: true),
            CheckFile("ASR model", paths.VibeVoiceAsrModelPath, required: true),
            CheckFile("Review model", paths.ReviewModelPath, required: true),
            new("AppData", paths.Root, Directory.Exists(paths.Root)),
            new("SQLite", paths.DatabasePath, File.Exists(paths.DatabasePath))
        ];
    }

    private static StatusItem CheckFile(string name, string path, bool required)
    {
        var exists = File.Exists(path);
        var value = exists ? "Found" : required ? "Missing" : "Not installed yet";
        var detail = exists ? path : $"Place the file here: {path}";
        return new StatusItem(name, value, exists || !required, detail);
    }

    private static StatusItem CheckCommand(string name, string commandName, string? arguments, bool required)
    {
        var commandPath = ResolveCommand(commandName);
        if (commandPath is null)
        {
            return new StatusItem(name, required ? "Missing" : "Not installed yet", !required, $"Add {commandName} to PATH.");
        }

        var detail = commandPath;
        var versionLine = TryGetFirstLine(commandPath, arguments);
        if (!string.IsNullOrWhiteSpace(versionLine))
        {
            detail = $"{versionLine} ({commandPath})";
        }

        return new StatusItem(name, "Found", true, detail);
    }

    private static string? ResolveCommand(string commandName)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var path in paths)
        {
            var executable = commandName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? commandName : $"{commandName}.exe";
            var candidate = Path.Combine(path, executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? TryGetFirstLine(string fileName, string? arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return null;
            }

            if (!process.WaitForExit(2500))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            if (string.IsNullOrWhiteSpace(output))
            {
                output = process.StandardError.ReadToEnd();
            }

            return output
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
}
