using KoeNote.App.Services.Llm;

namespace KoeNote.App.Tests;

public sealed class LlamaRuntimeBackendDiagnosticsTests
{
    [Fact]
    public void Analyze_DoesNotFailWhenCudaOutputIsAbsent()
    {
        var diagnostic = LlamaRuntimeBackendDiagnostics.Analyze(
            999,
            CreateCudaEnvironment(),
            string.Empty);

        Assert.False(diagnostic.CudaBackendMissing);
        Assert.False(diagnostic.CudaBackendLoaded);
        Assert.Contains("cuda-backend-not-confirmed", diagnostic.Summary);
    }

    [Fact]
    public void Analyze_DetectsLoadedCudaBackend()
    {
        var diagnostic = LlamaRuntimeBackendDiagnostics.Analyze(
            999,
            CreateCudaEnvironment(),
            "load_backend: loaded CUDA backend from ggml-cuda.dll");

        Assert.True(diagnostic.CudaBackendLoaded);
        Assert.False(diagnostic.CudaBackendMissing);
        Assert.Contains("cuda-backend-loaded", diagnostic.Summary);
    }

    [Fact]
    public void Analyze_DetectsMissingCudaBackendWhenGpuLayersWereRequested()
    {
        var diagnostic = LlamaRuntimeBackendDiagnostics.Analyze(
            999,
            CreateCudaEnvironment(),
            "CUDA backend not found; failed to load ggml-cuda.dll");

        Assert.False(diagnostic.CudaBackendLoaded);
        Assert.True(diagnostic.CudaBackendMissing);
        Assert.Contains("cuda-backend-missing", diagnostic.Summary);
    }

    [Fact]
    public void Analyze_DoesNotTreatFailedCudaInitAsLoaded()
    {
        var diagnostic = LlamaRuntimeBackendDiagnostics.Analyze(
            999,
            CreateCudaEnvironment(),
            "ggml_cuda_init: failed: no CUDA devices found");

        Assert.False(diagnostic.CudaBackendLoaded);
        Assert.True(diagnostic.CudaBackendMissing);
        Assert.Contains("cuda-backend-missing", diagnostic.Summary);
    }

    [Fact]
    public void Analyze_DoesNotTreatNormalCublasSettingAsMissing()
    {
        var diagnostic = LlamaRuntimeBackendDiagnostics.Analyze(
            999,
            CreateCudaEnvironment(),
            """
            ggml_cuda_init: GGML_CUDA_FORCE_CUBLAS: no
            ggml_cuda_init: found 1 CUDA devices:
            CUDA0: NVIDIA GeForce RTX 3060 Ti
            """);

        Assert.True(diagnostic.CudaBackendLoaded);
        Assert.False(diagnostic.CudaBackendMissing);
        Assert.Contains("cuda-backend-loaded", diagnostic.Summary);
    }

    [Fact]
    public void Analyze_RecognizesCudaDeviceDiscoveryAsLoaded()
    {
        var diagnostic = LlamaRuntimeBackendDiagnostics.Analyze(
            999,
            CreateCudaEnvironment(),
            """
            ggml_cuda_init: found 1 CUDA devices:
            CUDA0: NVIDIA GeForce RTX 3060 Ti
            """);

        Assert.True(diagnostic.CudaBackendLoaded);
        Assert.False(diagnostic.CudaBackendMissing);
    }

    [Fact]
    public void Analyze_IgnoresCudaBackendFailureWhenGpuLayersAreDisabled()
    {
        var diagnostic = LlamaRuntimeBackendDiagnostics.Analyze(
            0,
            CreateCudaEnvironment(),
            "CUDA backend not found; failed to load ggml-cuda.dll");

        Assert.False(diagnostic.CudaBackendMissing);
    }

    private static IReadOnlyDictionary<string, string> CreateCudaEnvironment()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [LlamaRuntimeEnvironment.CudaReviewRuntimeDirectoryVariable] = @"C:\KoeNote\runtime\review-cuda"
        };
    }
}
