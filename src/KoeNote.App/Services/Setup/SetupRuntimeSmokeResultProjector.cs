using KoeNote.App.Services.Asr;

namespace KoeNote.App.Services.Setup;

internal static class SetupRuntimeSmokeResultProjector
{
    private const string AsrGpuProfileSmokeName = "ASR GPU profile smoke";

    public static SetupSmokeCheck CreateSkippedAsrGpuSmokeCheck(SetupSmokeCheck asrRuntimeSmoke)
    {
        return new SetupSmokeCheck(
            AsrGpuProfileSmokeName,
            false,
            $"Skipped because ASR runtime smoke failed: {asrRuntimeSmoke.Detail}");
    }

    public static SetupSmokeCheck CreatePassedAsrGpuSmokeCheck(AsrExecutionProfile profile)
    {
        return new SetupSmokeCheck(
            AsrGpuProfileSmokeName,
            true,
            $"GPU ASR smoke passed with profile {profile.ProfileId}.");
    }

    public static SetupSmokeCheck CreateFailedAsrGpuSmokeCheck(IEnumerable<string> failures)
    {
        return new SetupSmokeCheck(
            AsrGpuProfileSmokeName,
            false,
            $"GPU ASR smoke failed for all CUDA profiles. {string.Join(" | ", failures)}");
    }

    public static string SummarizeProfileAttempt(AsrExecutionProfile profile, ProcessRunResult result, bool outputJsonCreated)
    {
        if (result.ExitCode == 0)
        {
            return outputJsonCreated
                ? string.Empty
                : $"{profile.ProfileId}: process succeeded but output JSON was not created";
        }

        return SummarizeProfileFailure(profile, result.ExitCode, result.StandardOutput, result.StandardError);
    }

    public static string SummarizeProfileException(AsrExecutionProfile profile, Exception exception)
    {
        return $"{profile.ProfileId}: {exception.GetType().Name} - {exception.Message}";
    }

    private static string SummarizeProfileFailure(
        AsrExecutionProfile profile,
        int exitCode,
        string standardOutput,
        string standardError)
    {
        var output = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
        var category = ClassifySmokeFailure(exitCode, output);
        return $"{profile.ProfileId}: {category} (exit {exitCode}) {Trim(output, 500)}";
    }

    private static string ClassifySmokeFailure(int exitCode, string output)
    {
        if (exitCode < 0)
        {
            return "native crash";
        }

        if (MentionsCudaRuntimeDll(output) &&
            (output.Contains("could not load", StringComparison.OrdinalIgnoreCase) ||
             output.Contains("failed to load", StringComparison.OrdinalIgnoreCase) ||
             output.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
             output.Contains("specified module could not be found", StringComparison.OrdinalIgnoreCase)))
        {
            return "missing CUDA runtime";
        }

        if (output.Contains("out of memory", StringComparison.OrdinalIgnoreCase))
        {
            return "VRAM or compute-type failure";
        }

        if (output.Contains("compute capability", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("no kernel image", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("unsupported", StringComparison.OrdinalIgnoreCase))
        {
            return "unsupported CUDA or compute type";
        }

        return "process failed";
    }

    private static bool MentionsCudaRuntimeDll(string output)
    {
        return output.Contains("cublas", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("cudnn", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("cudart", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("zlibwapi", StringComparison.OrdinalIgnoreCase);
    }

    private static string Trim(string value, int maxLength)
    {
        var normalized = value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
