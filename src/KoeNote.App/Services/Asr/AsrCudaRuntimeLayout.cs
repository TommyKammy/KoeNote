using System.IO;

namespace KoeNote.App.Services.Asr;

public static class AsrCudaRuntimeLayout
{
    public static readonly string[] RequiredFilePatterns =
    [
        "cublas64_*.dll",
        "cublasLt64_*.dll",
        "cudart64_*.dll",
        "cudnn*.dll",
        "zlibwapi.dll"
    ];

    public static bool HasPackage(AppPaths paths)
    {
        if (!Directory.Exists(paths.AsrRuntimeDirectory))
        {
            return false;
        }

        var installedFileNames = Directory
            .EnumerateFiles(paths.AsrRuntimeDirectory, "*.dll")
            .Select(Path.GetFileName)
            .Where(static fileName => !string.IsNullOrWhiteSpace(fileName))
            .Cast<string>();
        return HasRequiredFiles(installedFileNames);
    }

    public static bool HasRequiredFiles(IEnumerable<string> fileNames)
    {
        var names = fileNames.ToArray();
        return RequiredFilePatterns.All(pattern => names.Any(fileName =>
            System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true)));
    }
}
