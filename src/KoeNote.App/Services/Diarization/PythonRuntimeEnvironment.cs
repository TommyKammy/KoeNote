using System.IO;

namespace KoeNote.App.Services.Diarization;

internal static class PythonRuntimeEnvironment
{
    public static IReadOnlyDictionary<string, string> Build(AppPaths paths)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var existingPythonPath = Environment.GetEnvironmentVariable("PYTHONPATH");
        values["PYTHONPATH"] = string.IsNullOrWhiteSpace(existingPythonPath)
            ? paths.PythonPackages
            : paths.PythonPackages + Path.PathSeparator + existingPythonPath;
        return values;
    }
}
