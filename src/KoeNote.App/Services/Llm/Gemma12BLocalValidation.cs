using System.Diagnostics;
using System.IO;
using System.Text;

namespace KoeNote.App.Services.Llm;

public static class Gemma12BLocalValidation
{
    private static readonly TimeSpan LlamaServerHelpTimeout = TimeSpan.FromSeconds(5);

    public const string ModelId = "gemma-4-12b-it-qat-q4-0";
    public const string EnableEnvironmentVariable = "KOENOTE_ENABLE_GEMMA12B_LOCAL_VALIDATION";
    public const string EnableMtpServerEnvironmentVariable = "KOENOTE_ENABLE_GEMMA12B_MTP_SERVER";
    public const string LlamaServerPathEnvironmentVariable = "KOENOTE_GEMMA12B_MTP_LLAMA_SERVER_PATH";
    public const string DraftModelPathEnvironmentVariable = "KOENOTE_GEMMA12B_MTP_DRAFT_MODEL_PATH";
    public const string MtpDraftModelId = "gemma-4-12b-it-qat-assistant-MTP-Q8_0-GGUF";
    public const string MtpDraftFileName = "gemma-4-12B-it-qat-assistant-MTP-Q8_0.gguf";

    public static bool IsTargetModel(string? modelId)
    {
        return !string.IsNullOrWhiteSpace(modelId) &&
            modelId.Equals(ModelId, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsEnabled()
    {
        var value = Environment.GetEnvironmentVariable(EnableEnvironmentVariable);
        return IsTruthy(value);
    }

    public static bool IsMtpServerEnabled()
    {
        var value = Environment.GetEnvironmentVariable(EnableMtpServerEnvironmentVariable);
        return value is null || IsTruthy(value);
    }

    public static string ResolveLlamaServerPath(string llamaCompletionPath)
    {
        var configured = Environment.GetEnvironmentVariable(LlamaServerPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var directory = Path.GetDirectoryName(llamaCompletionPath);
        return string.IsNullOrWhiteSpace(directory)
            ? "llama-server.exe"
            : Path.Combine(directory, "llama-server.exe");
    }

    public static string ResolveMtpDraftModelPath()
    {
        var configured = GetConfiguredMtpDraftModelPath();
        if (configured is not null)
        {
            return configured;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            localAppData,
            "KoeNote",
            "models",
            "review_aux",
            MtpDraftModelId,
            MtpDraftFileName);
    }

    public static string ResolveMtpDraftModelPath(string storageRoot)
    {
        var configured = GetConfiguredMtpDraftModelPath();
        if (configured is not null)
        {
            return configured;
        }

        return Path.Combine(
            storageRoot,
            "review_aux",
            MtpDraftModelId,
            MtpDraftFileName);
    }

    public static string? GetConfiguredMtpDraftModelPath()
    {
        var configured = Environment.GetEnvironmentVariable(DraftModelPathEnvironmentVariable);
        return string.IsNullOrWhiteSpace(configured) ? null : configured;
    }

    public static bool IsLlamaServerMtpCapable(string llamaServerPath)
    {
        if (!File.Exists(llamaServerPath))
        {
            return false;
        }

        try
        {
            return LlamaServerHelpSupportsMtp(ReadLlamaServerHelp(llamaServerPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static bool LlamaServerHelpSupportsMtp(string helpText)
    {
        return helpText.Contains("--spec-type", StringComparison.OrdinalIgnoreCase) &&
            helpText.Contains("draft-mtp", StringComparison.OrdinalIgnoreCase) &&
            helpText.Contains("--model-draft", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTruthy(string? value)
    {
        return value is not null &&
            (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadLlamaServerHelp(string llamaServerPath)
    {
        var output = new StringBuilder();
        var outputLock = new object();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = llamaServerPath,
                ArgumentList = { "--help" },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, args) => AppendLine(output, outputLock, args.Data);
        process.ErrorDataReceived += (_, args) => AppendLine(output, outputLock, args.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(LlamaServerHelpTimeout))
        {
            process.Kill(entireProcessTree: true);
            return GetOutput(output, outputLock);
        }

        process.WaitForExit();
        return GetOutput(output, outputLock);
    }

    private static void AppendLine(StringBuilder output, object outputLock, string? line)
    {
        if (line is not null)
        {
            lock (outputLock)
            {
                output.AppendLine(line);
            }
        }
    }

    private static string GetOutput(StringBuilder output, object outputLock)
    {
        lock (outputLock)
        {
            return output.ToString();
        }
    }
}
