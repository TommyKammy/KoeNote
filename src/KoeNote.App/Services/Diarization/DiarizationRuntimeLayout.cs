using System.IO;

namespace KoeNote.App.Services.Diarization;

public static class DiarizationRuntimeLayout
{
    public static bool HasPackage(AppPaths paths)
    {
        return HasManagedPackage(paths) || HasLegacyPackage(paths);
    }

    public static bool HasManagedPackage(AppPaths paths)
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

    public static bool HasLegacyPackage(AppPaths paths)
    {
        return Directory.Exists(Path.Combine(paths.PythonPackages, "diarize")) ||
            File.Exists(Path.Combine(paths.PythonPackages, "diarize.py"));
    }
}
