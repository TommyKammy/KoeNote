namespace KoeNote.App.Services.Asr;

public static class AsrWorkerFailureClassifier
{
    public static AsrFailureCategory ClassifyProcessFailure(int exitCode, string workerOutput)
    {
        if (IsCudaRuntimeFailure(workerOutput))
        {
            return AsrFailureCategory.CudaRuntimeMissing;
        }

        return IsNativeCrashExitCode(exitCode)
            ? AsrFailureCategory.NativeCrash
            : AsrFailureCategory.ProcessFailed;
    }

    public static string DescribeExitCode(int exitCode)
    {
        return exitCode switch
        {
            0 => "success",
            -1073740791 => "0xC0000409 STATUS_STACK_BUFFER_OVERRUN/native fail-fast",
            -1073741819 => "0xC0000005 access violation/native crash",
            -1073741571 => "0xC00000FD stack overflow/native crash",
            -1073741515 => "0xC0000135 missing native dependency",
            _ when exitCode < 0 => $"0x{unchecked((uint)exitCode):X8} native process failure",
            _ => "process failed"
        };
    }

    private static bool IsCudaRuntimeFailure(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var mentionsCudaRuntimeDll =
            value.Contains("cublas", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("cudnn", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("cudart", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("zlibwapi", StringComparison.OrdinalIgnoreCase);
        if (!mentionsCudaRuntimeDll)
        {
            return false;
        }

        return value.Contains("could not load", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("failed to load", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("cannot open shared object", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("specified module could not be found", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNativeCrashExitCode(int exitCode)
    {
        return exitCode < 0;
    }
}
