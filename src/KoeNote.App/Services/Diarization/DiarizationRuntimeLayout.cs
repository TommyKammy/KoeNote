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
        return Directory.Exists(Path.Combine(paths.DiarizationPythonEnvironment, "Lib", "site-packages", "diarize")) ||
            File.Exists(Path.Combine(paths.DiarizationPythonEnvironment, "Lib", "site-packages", "diarize.py")) ||
            Directory.Exists(Path.Combine(paths.DiarizationPythonEnvironment, "Lib", "site-packages", "diarize-0.1.1.dist-info"));
    }

    public static bool HasLegacyPackage(AppPaths paths)
    {
        return Directory.Exists(Path.Combine(paths.PythonPackages, "diarize")) ||
            File.Exists(Path.Combine(paths.PythonPackages, "diarize.py"));
    }
}
