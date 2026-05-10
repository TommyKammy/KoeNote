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
        if (!Directory.Exists(paths.AsrRuntimeDirectory))
        {
            return false;
        }

        var installedFileNames = Directory
            .EnumerateFiles(paths.AsrRuntimeDirectory, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(static fileName => !string.IsNullOrWhiteSpace(fileName))
            .Cast<string>();
        return File.Exists(paths.AsrCudaRuntimeMarkerPath) && HasRequiredFiles(installedFileNames);
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
}
