using KoeNote.App.Services;
using KoeNote.App.Services.Diagnostics;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Setup;

namespace KoeNote.App.Tests;

public sealed class GpuRuntimeDiagnosticServiceTests
{
    [Fact]
    public void BuildSnapshot_ReportsSelectedTernaryReviewProfileAsCpu()
    {
        var paths = CreatePathsWithCatalog();
        MakeTernaryBonsaiSelectable(paths);
        InstallCudaReviewRuntime(paths);
        new SetupStateService(paths).Save(SetupState.Default(paths.DefaultModelStorageRoot) with
        {
            SelectedReviewModelId = "ternary-bonsai-8b-q2-0"
        });

        var snapshot = new GpuRuntimeDiagnosticService(paths, new FixedSetupHostResourceProbe()).BuildSnapshot();

        Assert.Equal("ternary-bonsai-8b-q2-0", snapshot.Resolver.SelectedReviewModelId);
        Assert.Equal("runtime-llama-cpp-ternary", snapshot.Resolver.ReviewRuntimePackageId);
        Assert.Equal("cpu", snapshot.Resolver.ReviewBackendMode);
        Assert.Equal(paths.TernaryLlamaCompletionPath, snapshot.Resolver.ReviewLlamaCompletionPath);
        Assert.True(snapshot.Resolver.LlamaRuntimeEnvironmentReady);
    }

    private static AppPaths CreatePathsWithCatalog()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var appBase = Path.Combine(root, "app");
        Directory.CreateDirectory(Path.Combine(appBase, "catalog"));
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "catalog", "model-catalog.json"),
            Path.Combine(appBase, "catalog", "model-catalog.json"));
        var paths = new AppPaths(root, root, appBase);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        return paths;
    }

    private static void MakeTernaryBonsaiSelectable(AppPaths paths)
    {
        var catalogService = new ModelCatalogService(paths);
        var catalog = catalogService.LoadBuiltInCatalog();
        var models = catalog.Models
            .Select(static model => model.ModelId.Equals("ternary-bonsai-8b-q2-0", StringComparison.OrdinalIgnoreCase)
                ? model with { Status = "available" }
                : model)
            .ToArray();
        File.WriteAllText(paths.ModelCatalogPath, ModelCatalogService.Serialize(catalog with { Models = models }));
    }

    private static void InstallCudaReviewRuntime(AppPaths paths)
    {
        Directory.CreateDirectory(paths.ReviewRuntimeDirectory);
        File.WriteAllText(paths.LlamaCompletionPath, "llama");
        File.WriteAllText(Path.Combine(paths.ReviewRuntimeDirectory, "ggml-cuda.dll"), "cuda");
        Directory.CreateDirectory(paths.CudaReviewRuntimeDirectory);
        File.WriteAllText(Path.Combine(paths.CudaReviewRuntimeDirectory, "cublas64_12.dll"), "cublas");
        File.WriteAllText(Path.Combine(paths.CudaReviewRuntimeDirectory, "cublasLt64_12.dll"), "cublasLt");
        File.WriteAllText(Path.Combine(paths.CudaReviewRuntimeDirectory, "cudart64_12.dll"), "cudart");
        File.WriteAllText(paths.CudaReviewRuntimeMarkerPath, "test");
    }

    private sealed class FixedSetupHostResourceProbe : ISetupHostResourceProbe
    {
        public SetupHostResources GetResources()
        {
            return new SetupHostResources(null, 8, true, null, "NVIDIA GPU");
        }
    }
}
