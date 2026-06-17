using System.IO;

namespace KoeNote.App.Services.Diarization;

public static class DiarizationRuntimeLayout
{
    private static readonly string[] RequiredManagedDataRelativePaths =
    [
        Path.Combine("silero_vad", "data", "silero_vad.jit")
    ];

    public static bool HasPackage(AppPaths paths)
    {
        return HasManagedPackage(paths) || HasLegacyPackage(paths);
    }

    public static bool HasManagedPackage(AppPaths paths)
    {
        return HasManagedPackageMetadata(paths) && HasRequiredManagedRuntimeData(paths);
    }

    public static bool HasManagedPackageMetadata(AppPaths paths)
    {
        var sitePackages = Path.Combine(paths.DiarizationPythonEnvironment, "Lib", "site-packages");
        if (!Directory.Exists(sitePackages))
        {
            return false;
        }

        if (Directory.Exists(Path.Combine(sitePackages, $"{DiarizationRuntimeService.PackageName}-{DiarizationRuntimeService.RequiredPackageVersion}.dist-info")))
        {
            return true;
        }

        if (Directory.EnumerateDirectories(sitePackages, $"{DiarizationRuntimeService.PackageName}-*.dist-info").Any())
        {
            return false;
        }

        return Directory.Exists(Path.Combine(sitePackages, DiarizationRuntimeService.PackageName)) ||
            File.Exists(Path.Combine(sitePackages, $"{DiarizationRuntimeService.PackageName}.py"));
    }

    public static bool HasRequiredManagedRuntimeData(AppPaths paths)
    {
        var sitePackages = Path.Combine(paths.DiarizationPythonEnvironment, "Lib", "site-packages");
        return RequiredManagedDataRelativePaths.All(relativePath => File.Exists(Path.Combine(sitePackages, relativePath)));
    }

    public static IReadOnlyList<string> GetMissingManagedRuntimeData(AppPaths paths)
    {
        var sitePackages = Path.Combine(paths.DiarizationPythonEnvironment, "Lib", "site-packages");
        return RequiredManagedDataRelativePaths
            .Select(relativePath => Path.Combine(sitePackages, relativePath))
            .Where(path => !File.Exists(path))
            .ToArray();
    }

    public static bool HasLegacyPackage(AppPaths paths)
    {
        var hasPackage = Directory.Exists(Path.Combine(paths.PythonPackages, "diarize")) ||
            File.Exists(Path.Combine(paths.PythonPackages, "diarize.py"));
        return hasPackage && HasRequiredLegacyRuntimeData(paths);
    }

    public static bool HasLegacyPackageMetadata(AppPaths paths)
    {
        return Directory.Exists(Path.Combine(paths.PythonPackages, "diarize")) ||
            File.Exists(Path.Combine(paths.PythonPackages, "diarize.py"));
    }

    public static bool HasRequiredLegacyRuntimeData(AppPaths paths)
    {
        return RequiredManagedDataRelativePaths.All(relativePath => File.Exists(Path.Combine(paths.PythonPackages, relativePath)));
    }

    public static IReadOnlyList<string> GetMissingLegacyRuntimeData(AppPaths paths)
    {
        return RequiredManagedDataRelativePaths
            .Select(relativePath => Path.Combine(paths.PythonPackages, relativePath))
            .Where(path => !File.Exists(path))
            .ToArray();
    }

    public static bool HasPackage(AppPaths paths, string installPath)
    {
        return IsLegacyInstallPath(paths, installPath)
            ? HasLegacyPackage(paths)
            : HasManagedPackage(paths);
    }

    public static string DescribeMissingRuntimeData(AppPaths paths, string installPath)
    {
        if (IsLegacyInstallPath(paths, installPath))
        {
            return DescribeMissingRuntimeData(
                paths.PythonPackages,
                HasLegacyPackageMetadata(paths),
                GetMissingLegacyRuntimeData(paths));
        }

        return DescribeMissingRuntimeData(
            paths.DiarizationPythonEnvironment,
            HasManagedPackageMetadata(paths),
            GetMissingManagedRuntimeData(paths));
    }

    public static string DescribeMissingRuntimeData(AppPaths paths)
    {
        var managedMissing = HasManagedPackageMetadata(paths)
            ? GetMissingManagedRuntimeData(paths)
            : [];
        if (managedMissing.Count > 0)
        {
            return $"Runtime package data is missing. Reinstall speaker diarization runtime. Missing: {string.Join("; ", managedMissing)}";
        }

        var legacyMissing = HasLegacyPackageMetadata(paths)
            ? GetMissingLegacyRuntimeData(paths)
            : [];
        if (legacyMissing.Count > 0)
        {
            return $"Runtime package data is missing. Reinstall speaker diarization runtime. Missing: {string.Join("; ", legacyMissing)}";
        }

        return $"Not installed: {paths.DiarizationPythonEnvironment}";
    }

    private static string DescribeMissingRuntimeData(
        string installPath,
        bool hasPackageMetadata,
        IReadOnlyList<string> missing)
    {
        if (hasPackageMetadata && missing.Count > 0)
        {
            return $"Runtime package data is missing. Reinstall speaker diarization runtime. Missing: {string.Join("; ", missing)}";
        }

        return $"Not installed: {installPath}";
    }

    private static bool IsLegacyInstallPath(AppPaths paths, string installPath)
    {
        return string.Equals(installPath, paths.PythonPackages, StringComparison.OrdinalIgnoreCase);
    }
}
