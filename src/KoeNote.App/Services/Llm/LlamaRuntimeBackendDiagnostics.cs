namespace KoeNote.App.Services.Llm;

public sealed record LlamaRuntimeBackendDiagnostic(
    int RequestedGpuLayers,
    bool CudaRuntimeEnvironmentPresent,
    string? CudaRuntimeDirectory,
    bool CudaBackendLoaded,
    bool CudaBackendMissing,
    string Summary);

public static class LlamaRuntimeBackendDiagnostics
{
    private static readonly string[] CudaLoadedSignals =
    [
        "load_backend: loaded cuda",
        "loaded cuda backend",
        "found cuda",
        "cuda backend initialized",
        "cuda backend loaded"
    ];

    private static readonly string[] CudaMissingSignals =
    [
        "failed to load cuda",
        "cuda backend not found",
        "no cuda backend",
        "no cuda devices",
        "could not load cuda",
        "ggml_cuda_init: failed",
        "cublas",
        "cudart",
        "cublaslt"
    ];

    private static readonly string[] MissingQualifiers =
    [
        "failed",
        "missing",
        "not found",
        "could not",
        "cannot",
        "unable",
        "no "
    ];

    public static LlamaRuntimeBackendDiagnostic Analyze(
        int requestedGpuLayers,
        IReadOnlyDictionary<string, string>? environment,
        string? standardError)
    {
        var cudaRuntimeDirectory = TryGetCudaRuntimeDirectory(environment);
        var cudaRuntimeEnvironmentPresent = !string.IsNullOrWhiteSpace(cudaRuntimeDirectory);
        var stderr = standardError ?? string.Empty;
        var cudaBackendMissing = requestedGpuLayers > 0 &&
            cudaRuntimeEnvironmentPresent &&
            ContainsCudaMissingSignal(stderr);
        var cudaBackendLoaded = !cudaBackendMissing && ContainsAny(stderr, CudaLoadedSignals);

        return new LlamaRuntimeBackendDiagnostic(
            requestedGpuLayers,
            cudaRuntimeEnvironmentPresent,
            cudaRuntimeDirectory,
            cudaBackendLoaded,
            cudaBackendMissing,
            BuildSummary(requestedGpuLayers, cudaRuntimeEnvironmentPresent, cudaRuntimeDirectory, cudaBackendLoaded, cudaBackendMissing));
    }

    private static string? TryGetCudaRuntimeDirectory(IReadOnlyDictionary<string, string>? environment)
    {
        if (environment is null)
        {
            return null;
        }

        return environment.TryGetValue(LlamaRuntimeEnvironment.CudaReviewRuntimeDirectoryVariable, out var value)
            ? value
            : null;
    }

    private static bool ContainsCudaMissingSignal(string text)
    {
        var normalized = text.ToLowerInvariant();
        if (!ContainsAny(normalized, CudaMissingSignals))
        {
            return false;
        }

        return MissingQualifiers.Any(normalized.Contains);
    }

    private static bool ContainsAny(string text, IReadOnlyCollection<string> signals)
    {
        return signals.Any(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildSummary(
        int requestedGpuLayers,
        bool cudaRuntimeEnvironmentPresent,
        string? cudaRuntimeDirectory,
        bool cudaBackendLoaded,
        bool cudaBackendMissing)
    {
        var status = cudaBackendLoaded
            ? "cuda-backend-loaded"
            : cudaBackendMissing
                ? "cuda-backend-missing"
                : "cuda-backend-not-confirmed";
        var directory = string.IsNullOrWhiteSpace(cudaRuntimeDirectory)
            ? "(none)"
            : cudaRuntimeDirectory;
        return $"requested_gpu_layers={requestedGpuLayers}; cuda_runtime_env={cudaRuntimeEnvironmentPresent}; cuda_runtime_dir={directory}; status={status}";
    }
}
