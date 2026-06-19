using System.IO;
using System.IO.Compression;

namespace KoeNote.App.Services.Review;

internal static class CudaReviewRuntimeLayoutProbe
{
    public static IEnumerable<ZipArchiveEntry> GetLegacyCudaEntries(ZipArchive archive)
    {
        var patterns = CudaReviewRuntimeLayout.RequiredFilePatterns
            .Concat(CudaReviewRuntimeLayout.OptionalNvidiaFilePatterns)
            .ToArray();
        return archive.Entries.Where(entry =>
            !string.IsNullOrWhiteSpace(entry.Name) &&
            patterns.Any(pattern => RuntimeInstallFileOps.MatchesPattern(entry.Name, pattern)));
    }

    public static bool HasCudaBridge(string directory)
    {
        return Directory.Exists(directory) &&
            Directory.EnumerateFiles(directory, "ggml-cuda*.dll", SearchOption.TopDirectoryOnly).Any();
    }

    public static bool HasRequiredNvidiaDependencies(string directory)
    {
        return Directory.Exists(directory) &&
            CudaReviewRuntimeLayout.RequiredNvidiaFilePatterns.All(pattern =>
                Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).Any());
    }

    public static bool HasNvidiaDependencies(string directory)
    {
        return Directory.Exists(directory) &&
            CudaReviewRuntimeLayout.NvidiaFilePatterns.Any(pattern =>
                Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).Any());
    }
}
