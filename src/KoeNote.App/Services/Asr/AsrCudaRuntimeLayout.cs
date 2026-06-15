using System.IO;

namespace KoeNote.App.Services.Asr;

public static class AsrCudaRuntimeLayout
{
    public static readonly string[] RequiredBundledFilePatterns =
    [
        "crispasr*.exe",
        "crispasr*.dll",
        "whisper.dll",
        "ggml-cuda.dll"
    ];

    public static readonly string[] RequiredNvidiaFilePatterns =
    [
        "cublas64_*.dll",
        "cublasLt64_*.dll",
        "cudart64_*.dll",
        "cudnn*.dll"
    ];

    public static readonly string[] RequiredFilePatterns =
    [
        ..RequiredBundledFilePatterns,
        ..RequiredNvidiaFilePatterns
    ];

    public static bool HasPackage(AppPaths paths)
    {
        return File.Exists(paths.AsrCudaRuntimeMarkerPath) &&
            HasBundledRuntimeFiles(paths.AsrRuntimeDirectory) &&
            HasNvidiaRuntimeFiles(paths.AsrCTranslate2RuntimeDirectory);
    }

    public static IReadOnlyList<string> GetMissingPackageItems(AppPaths paths)
    {
        List<string> items = [];
        if (!File.Exists(paths.AsrCudaRuntimeMarkerPath))
        {
            items.Add($"marker: {paths.AsrCudaRuntimeMarkerPath}");
        }

        AddMissingFiles(items, "bundled ASR GPU runtime", paths.AsrRuntimeDirectory, RequiredBundledFilePatterns);
        AddMissingFiles(items, "NVIDIA ASR runtime", paths.AsrCTranslate2RuntimeDirectory, RequiredNvidiaFilePatterns);
        return items;
    }

    public static bool HasLegacyNvidiaRuntimeFiles(AppPaths paths)
    {
        return HasNvidiaRuntimeFiles(Path.Combine(paths.RuntimeTools, "asr-ctranslate2-cuda")) ||
            HasNvidiaRuntimeFiles(paths.AsrRuntimeDirectory);
    }

    public static bool HasRequiredFiles(IEnumerable<string> fileNames)
    {
        var names = fileNames.ToArray();
        return RequiredFilePatterns.All(pattern => names.Any(fileName =>
            System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true)));
    }

    public static bool HasBundledRuntimeFiles(string directory)
    {
        return HasFiles(directory, RequiredBundledFilePatterns);
    }

    public static bool HasNvidiaRuntimeFiles(string directory)
    {
        return HasFiles(directory, RequiredNvidiaFilePatterns);
    }

    private static bool HasFiles(string directory, IReadOnlyCollection<string> patterns)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        var names = Directory
            .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(static fileName => !string.IsNullOrWhiteSpace(fileName))
            .Cast<string>()
            .ToArray();
        return patterns.All(pattern => names.Any(fileName =>
            System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true)));
    }

    private static void AddMissingFiles(List<string> items, string label, string directory, IReadOnlyCollection<string> patterns)
    {
        var names = Directory.Exists(directory)
            ? Directory
                .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(static fileName => !string.IsNullOrWhiteSpace(fileName))
                .Cast<string>()
                .ToArray()
            : [];
        foreach (var pattern in patterns.Where(pattern => !names.Any(fileName =>
                     System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true))))
        {
            items.Add($"{label}: {pattern} missing under {directory}");
        }
    }
}
