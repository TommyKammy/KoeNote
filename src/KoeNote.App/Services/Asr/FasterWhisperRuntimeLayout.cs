using System.IO;

namespace KoeNote.App.Services.Asr;

public static class FasterWhisperRuntimeLayout
{
    public static bool HasPackage(AppPaths paths)
    {
        var sitePackages = Path.Combine(paths.AsrPythonEnvironment, "Lib", "site-packages");
        if (!Directory.Exists(sitePackages))
        {
            return false;
        }

        if (Directory.Exists(Path.Combine(sitePackages, $"faster_whisper-{FasterWhisperRuntimeService.RequiredPackageVersion}.dist-info")))
        {
            return true;
        }

        if (Directory.EnumerateDirectories(sitePackages, "faster_whisper-*.dist-info").Any())
        {
            return false;
        }

        return Directory.Exists(Path.Combine(sitePackages, "faster_whisper")) ||
            File.Exists(Path.Combine(sitePackages, "faster_whisper.py"));
    }
}
