using System.IO;

namespace KoeNote.App.Services;

internal static class RuntimeInstallFileOps
{
    public static bool MatchesPattern(string fileName, string pattern)
    {
        return NvidiaRedistInstaller.MatchesPattern(fileName, pattern);
    }

    public static bool MatchesAnyPattern(string fileName, IReadOnlyCollection<string> patterns)
    {
        return patterns.Any(pattern => MatchesPattern(fileName, pattern));
    }

    public static void CopyMatchingFiles(
        string sourceDirectory,
        string destinationDirectory,
        IReadOnlyCollection<string> patterns,
        bool deleteSourceFiles = false,
        string searchPattern = "*")
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        Directory.CreateDirectory(destinationDirectory);
        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, searchPattern, SearchOption.TopDirectoryOnly)
                     .Where(file => MatchesAnyPattern(Path.GetFileName(file), patterns)))
        {
            File.Copy(sourcePath, Path.Combine(destinationDirectory, Path.GetFileName(sourcePath)), overwrite: true);
            if (deleteSourceFiles)
            {
                File.Delete(sourcePath);
            }
        }
    }

    public static void DeleteMatchingFiles(string sourceDirectory, IReadOnlyCollection<string> patterns)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly)
                     .Where(file => MatchesAnyPattern(Path.GetFileName(file), patterns)))
        {
            File.Delete(sourcePath);
        }
    }

    public static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
