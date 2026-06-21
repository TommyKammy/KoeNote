namespace KoeNote.App.Services.Asr;

internal static class AsrCudaRuntimeInstallProjection
{
    public static AsrCudaRuntimeOptions ResolveOptionsFromEnvironment()
    {
        return new AsrCudaRuntimeOptions(
            Environment.GetEnvironmentVariable(AsrCudaRuntimeService.CudaRedistManifestUrlEnvironmentVariable) ??
                AsrCudaRuntimeService.DefaultCudaRedistManifestUrl,
            Environment.GetEnvironmentVariable(AsrCudaRuntimeService.CudaRedistBaseUrlEnvironmentVariable) ??
                AsrCudaRuntimeService.DefaultCudaRedistBaseUrl,
            Environment.GetEnvironmentVariable(AsrCudaRuntimeService.CudnnRedistManifestUrlEnvironmentVariable) ??
                AsrCudaRuntimeService.DefaultCudnnRedistManifestUrl,
            Environment.GetEnvironmentVariable(AsrCudaRuntimeService.CudnnRedistBaseUrlEnvironmentVariable) ??
                AsrCudaRuntimeService.DefaultCudnnRedistBaseUrl,
            Environment.GetEnvironmentVariable(AsrCudaRuntimeService.RuntimeUrlEnvironmentVariable),
            Environment.GetEnvironmentVariable(AsrCudaRuntimeService.RuntimeSha256EnvironmentVariable));
    }

    public static bool HasNvidiaRedistSources(AsrCudaRuntimeOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.CudaRedistManifestUrl) &&
            !string.IsNullOrWhiteSpace(options.CudaRedistBaseUrl) &&
            !string.IsNullOrWhiteSpace(options.CudnnRedistManifestUrl) &&
            !string.IsNullOrWhiteSpace(options.CudnnRedistBaseUrl);
    }

    public static AsrCudaRuntimeInstallResult MissingNvidiaRedistSources(AppPaths paths)
    {
        return AsrCudaRuntimeInstallResult.Failed(
            $"NVIDIA redistributable source is not configured. Set {AsrCudaRuntimeService.CudaRedistManifestUrlEnvironmentVariable}, {AsrCudaRuntimeService.CudaRedistBaseUrlEnvironmentVariable}, {AsrCudaRuntimeService.CudnnRedistManifestUrlEnvironmentVariable}, and {AsrCudaRuntimeService.CudnnRedistBaseUrlEnvironmentVariable} before installing.",
            paths.AsrRuntimeDirectory,
            AsrCudaRuntimeService.FailureCategoryConfigurationMissing);
    }

    public static AsrCudaRuntimeInstallResult VerifyInstallResult(
        bool isInstalled,
        string successMessage,
        string failureMessage,
        string successInstallPath,
        string failureInstallPath,
        string failureCategory,
        string? sha256 = null)
    {
        return isInstalled
            ? AsrCudaRuntimeInstallResult.Succeeded(successMessage, successInstallPath, sha256)
            : AsrCudaRuntimeInstallResult.Failed(failureMessage, failureInstallPath, failureCategory, sha256);
    }

    public static AsrCudaRuntimeInstallResult MigrationFailed(Exception exception, string installPath)
    {
        return AsrCudaRuntimeInstallResult.Failed(
            $"CUDA ASR runtime migration failed: {exception.Message}",
            installPath,
            AsrCudaRuntimeService.FailureCategoryInstallFailed);
    }
}
