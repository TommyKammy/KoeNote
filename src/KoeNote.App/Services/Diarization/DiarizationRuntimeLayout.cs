using System.IO;

namespace KoeNote.App.Services.Diarization;

public static class DiarizationRuntimeLayout
{
    public static bool HasPackage(AppPaths paths)
    {
        return Directory.Exists(Path.Combine(paths.PythonPackages, "diarize")) ||
            File.Exists(Path.Combine(paths.PythonPackages, "diarize.py"));
    }
}
