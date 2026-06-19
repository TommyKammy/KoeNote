using System.IO;

namespace KoeNote.App.Services.Asr;

public static class AsrWorkerProcessEnvironmentBuilder
{
    public static AsrProcessEnvironment Build(AsrEngineConfig config, AppPaths paths)
    {
        var ctranslate2PathEntries = new List<string>();
        var asrToolEntries = new List<string>();
        var appAsrTools = paths.AsrRuntimeDirectory;
        if (AsrCudaRuntimeLayout.HasNvidiaRuntimeFiles(paths.AsrCTranslate2RuntimeDirectory))
        {
            ctranslate2PathEntries.Add(paths.AsrCTranslate2RuntimeDirectory);
        }

        if (Directory.Exists(appAsrTools))
        {
            asrToolEntries.Add(appAsrTools);
        }

        var workerDirectory = Path.GetDirectoryName(config.WorkerPath);
        if (!string.IsNullOrWhiteSpace(workerDirectory))
        {
            var siblingCTranslate2Cuda = Path.GetFullPath(Path.Combine(workerDirectory, "..", "..", "tools", "asr-ctranslate2-cuda"));
            if (AsrCudaRuntimeLayout.HasNvidiaRuntimeFiles(siblingCTranslate2Cuda))
            {
                ctranslate2PathEntries.Add(siblingCTranslate2Cuda);
            }

            var siblingAsrTools = Path.GetFullPath(Path.Combine(workerDirectory, "..", "..", "tools", "asr"));
            if (Directory.Exists(siblingAsrTools))
            {
                asrToolEntries.Add(siblingAsrTools);
            }
        }

        var ctranslate2Entries = ctranslate2PathEntries.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var pathEntries = new List<string>(ctranslate2Entries);
        if (!ctranslate2Entries.Any(AsrCudaRuntimeLayout.HasNvidiaRuntimeFiles))
        {
            pathEntries.AddRange(asrToolEntries.Where(AsrCudaRuntimeLayout.HasNvidiaRuntimeFiles));
        }

        var addedPathEntries = pathEntries.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var asrToolsEntry = asrToolEntries.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (addedPathEntries.Length == 0 && string.IsNullOrWhiteSpace(asrToolsEntry))
        {
            return new AsrProcessEnvironment(new Dictionary<string, string>(), []);
        }

        var environment = new Dictionary<string, string>();
        if (addedPathEntries.Length > 0)
        {
            var existingPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            pathEntries.Add(existingPath);
            environment["PATH"] = string.Join(Path.PathSeparator, pathEntries.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        if (ctranslate2Entries.Length > 0)
        {
            environment["KOENOTE_CTRANSLATE2_CUDA_DIR"] = ctranslate2Entries[0];
        }

        if (!string.IsNullOrWhiteSpace(asrToolsEntry))
        {
            environment["KOENOTE_ASR_TOOLS_DIR"] = asrToolsEntry;
        }

        return new AsrProcessEnvironment(environment, addedPathEntries);
    }
}

public sealed record AsrProcessEnvironment(
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<string> AddedPathEntries);
