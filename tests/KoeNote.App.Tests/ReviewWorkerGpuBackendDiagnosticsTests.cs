using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

public sealed class ReviewWorkerGpuBackendDiagnosticsTests
{
    [Fact]
    public async Task RunAsync_FailsWhenCudaBackendIsExplicitlyMissing()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var runtimePath = PrepareRuntime(paths);
        var modelPath = PrepareModel(paths);
        var worker = CreateWorker(paths, new CudaBackendMissingRunner());

        var exception = await Assert.ThrowsAsync<ReviewWorkerException>(() => worker.RunAsync(new ReviewRunOptions(
            "job-001",
            runtimePath,
            modelPath,
            Path.Combine(paths.Jobs, "job-001", "review"),
            CreateSegments(),
            RuntimeEnvironment: CreateCudaEnvironment(paths),
            GpuLayers: 999)));

        Assert.Equal(ReviewFailureCategory.MissingRuntime, exception.Category);
        Assert.Contains("Review CUDA backend was not loaded", exception.Message);
    }

    [Fact]
    public async Task RunAsync_ClassifiesNonZeroCudaBackendFailureAsMissingRuntime()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var runtimePath = PrepareRuntime(paths);
        var modelPath = PrepareModel(paths);
        var worker = CreateWorker(paths, new CudaBackendMissingRunner(exitCode: 1));

        var exception = await Assert.ThrowsAsync<ReviewWorkerException>(() => worker.RunAsync(new ReviewRunOptions(
            "job-001",
            runtimePath,
            modelPath,
            Path.Combine(paths.Jobs, "job-001", "review"),
            CreateSegments(),
            RuntimeEnvironment: CreateCudaEnvironment(paths),
            GpuLayers: 999)));

        Assert.Equal(ReviewFailureCategory.MissingRuntime, exception.Category);
    }

    [Fact]
    public async Task RunAsync_ReturnsRuntimeDiagnosticSummaryForSuccessfulRun()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var runtimePath = PrepareRuntime(paths);
        var modelPath = PrepareModel(paths);
        var worker = CreateWorker(paths, new CudaBackendLoadedRunner());

        var result = await worker.RunAsync(new ReviewRunOptions(
            "job-001",
            runtimePath,
            modelPath,
            Path.Combine(paths.Jobs, "job-001", "review"),
            CreateSegments(),
            RuntimeEnvironment: CreateCudaEnvironment(paths),
            GpuLayers: 999));

        Assert.NotNull(result.RuntimeDiagnostics);
        var diagnostic = Assert.Single(result.RuntimeDiagnostics);
        Assert.Contains("cuda-backend-loaded", diagnostic);
    }

    private static ReviewWorker CreateWorker(AppPaths paths, ExternalProcessRunner runner)
    {
        return new ReviewWorker(
            runner,
            new ReviewCommandBuilder(),
            new ReviewPromptBuilder(),
            new ReviewJsonNormalizer(),
            new ReviewResultStore(),
            new CorrectionDraftRepository(paths));
    }

    private static string PrepareRuntime(AppPaths paths)
    {
        var runtimePath = Path.Combine(paths.Root, "runtime", "llama-completion.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(runtimePath)!);
        File.WriteAllText(runtimePath, "runtime");
        return runtimePath;
    }

    private static string PrepareModel(AppPaths paths)
    {
        var modelPath = Path.Combine(paths.UserModels, "review", "model.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        File.WriteAllText(modelPath, "model");
        return modelPath;
    }

    private static IReadOnlyList<TranscriptSegment> CreateSegments()
    {
        return
        [
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "hello", "hello")
        ];
    }

    private static IReadOnlyDictionary<string, string> CreateCudaEnvironment(AppPaths paths)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [LlamaRuntimeEnvironment.CudaReviewRuntimeDirectoryVariable] = paths.CudaReviewRuntimeDirectory
        };
    }

    private sealed class CudaBackendMissingRunner(int exitCode = 0) : ExternalProcessRunner
    {
        public override Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            IReadOnlyDictionary<string, string>? environment = null)
        {
            return Task.FromResult(new ProcessRunResult(
                exitCode,
                TimeSpan.FromMilliseconds(10),
                "[]",
                "CUDA backend not found; failed to load ggml-cuda.dll"));
        }
    }

    private sealed class CudaBackendLoadedRunner : ExternalProcessRunner
    {
        public override Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            IReadOnlyDictionary<string, string>? environment = null)
        {
            return Task.FromResult(new ProcessRunResult(
                0,
                TimeSpan.FromMilliseconds(10),
                "[]",
                "load_backend: loaded CUDA backend from ggml-cuda.dll"));
        }
    }
}
