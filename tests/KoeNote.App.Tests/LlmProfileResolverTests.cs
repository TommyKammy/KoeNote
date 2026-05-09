using KoeNote.App.Services.Llm;
using KoeNote.App.Services;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

public sealed class LlmProfileResolverTests
{
    [Fact]
    public void Resolve_UsesStandardRuntimeAndCurrentDefaultsForGemma()
    {
        var paths = CreateIsolatedPaths();
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var resolver = new LlmProfileResolver(paths, new InstalledModelRepository(paths));

        var profile = resolver.Resolve(catalog, "gemma-4-e4b-it-q4-k-m");

        Assert.Equal("builtin:gemma-4-e4b-it-q4-k-m:gemma:balanced", profile.ProfileId);
        Assert.Equal("gemma-4-e4b-it-q4-k-m", profile.ModelId);
        Assert.Equal("gemma", profile.ModelFamily);
        Assert.Equal("Gemma 4 E4B it Q4_K_M", profile.DisplayName);
        Assert.Equal("llama-cpp", profile.RuntimeKind);
        Assert.Equal("runtime-llama-cpp", profile.RuntimePackageId);
        Assert.Equal(paths.ReviewModelPath, profile.ModelPath);
        Assert.Equal(paths.LlamaCompletionPath, profile.LlamaCompletionPath);
        Assert.Equal("cpu", profile.AccelerationMode);
        Assert.Equal(8192, profile.ContextSize);
        Assert.Equal(999, profile.GpuLayers);
        Assert.Null(profile.Threads);
        Assert.True(profile.NoConversation);
        Assert.Equal(LlmOutputSanitizerProfiles.MarkdownSectionOnly, profile.OutputSanitizerProfile);
        Assert.Equal(TimeSpan.FromHours(2), profile.Timeout);
    }

    [Fact]
    public void Resolve_UsesTernaryRuntimeAndBoundedCpuDefaultsForTernaryBonsai()
    {
        var paths = CreateIsolatedPaths();
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var resolver = new LlmProfileResolver(paths, new InstalledModelRepository(paths));

        var profile = resolver.Resolve(catalog, "ternary-bonsai-8b-q2-0");

        Assert.Equal("builtin:ternary-bonsai-8b-q2-0:ternary-bonsai:cpu-bounded", profile.ProfileId);
        Assert.Equal("runtime-llama-cpp-ternary", profile.RuntimePackageId);
        Assert.Equal(paths.TernaryLlamaCompletionPath, profile.LlamaCompletionPath);
        Assert.Equal("cpu", profile.AccelerationMode);
        Assert.Equal(1024, profile.ContextSize);
        Assert.Equal(0, profile.GpuLayers);
        Assert.NotNull(profile.Threads);
        Assert.Equal(LlmOutputSanitizerProfiles.Strict, profile.OutputSanitizerProfile);
        Assert.Equal(TimeSpan.FromMinutes(20), profile.Timeout);
    }

    [Fact]
    public void Resolve_UsesCudaAccelerationWhenCudaReviewRuntimeIsInstalled()
    {
        var paths = CreateIsolatedPaths();
        Directory.CreateDirectory(paths.ReviewRuntimeDirectory);
        File.WriteAllText(paths.LlamaCompletionPath, "llama");
        File.WriteAllText(Path.Combine(paths.ReviewRuntimeDirectory, "ggml-cuda.dll"), "cuda");
        File.WriteAllText(paths.CudaReviewRuntimeMarkerPath, "test");
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var resolver = new LlmProfileResolver(paths, new InstalledModelRepository(paths));

        var profile = resolver.Resolve(catalog, "gemma-4-e4b-it-q4-k-m");

        Assert.Equal("cuda", profile.AccelerationMode);
        Assert.Equal(999, profile.GpuLayers);
        Assert.Equal(paths.LlamaCompletionPath, profile.LlamaCompletionPath);
    }

    private static AppPaths CreateIsolatedPaths()
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

    [Fact]
    public void Resolve_UsesInstalledReviewModelPathWhenAvailable()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var modelPath = Path.Combine(paths.Root, "models", "gemma.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        File.WriteAllText(modelPath, "model");
        var repository = new InstalledModelRepository(paths);
        repository.UpsertInstalledModel(new InstalledModel(
            "gemma-4-e4b-it-q4-k-m",
            "review",
            "llama-cpp",
            "Gemma 4",
            "gemma",
            null,
            modelPath,
            null,
            null,
            null,
            true,
            "Apache-2.0",
            "download",
            DateTimeOffset.Now,
            DateTimeOffset.Now,
            "installed"));
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var resolver = new LlmProfileResolver(paths, repository);

        var profile = resolver.Resolve(catalog, "gemma-4-e4b-it-q4-k-m");

        Assert.Equal(modelPath, profile.ModelPath);
    }

    [Fact]
    public void Resolve_FallsBackToInstalledDefaultReviewModelPathWhenSelectedModelIsMissing()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var fallbackPath = Path.Combine(paths.Root, "models", "llm-jp.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(fallbackPath)!);
        File.WriteAllText(fallbackPath, "model");
        var repository = new InstalledModelRepository(paths);
        repository.UpsertInstalledModel(new InstalledModel(
            LlmProfileResolver.FallbackReviewModelId,
            "review",
            "llama-cpp",
            "llm-jp 4",
            "llm-jp",
            null,
            fallbackPath,
            null,
            null,
            null,
            true,
            "Apache-2.0",
            "download",
            DateTimeOffset.Now,
            DateTimeOffset.Now,
            "installed"));
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var resolver = new LlmProfileResolver(paths, repository);

        var profile = resolver.Resolve(catalog, "gemma-4-e4b-it-q4-k-m");

        Assert.Equal(fallbackPath, profile.ModelPath);
    }

    [Fact]
    public void Resolve_UsesExplicitBonsaiRuntimePresetForBonsaiQ1()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var resolver = new LlmProfileResolver(paths, new InstalledModelRepository(paths));

        var profile = resolver.Resolve(catalog, "bonsai-8b-q1-0");

        Assert.Equal("builtin:bonsai-8b-q1-0:bonsai:conservative", profile.ProfileId);
        Assert.Equal("bonsai", profile.ModelFamily);
        Assert.Equal(8192, profile.ContextSize);
        Assert.Equal(999, profile.GpuLayers);
        Assert.Equal(TimeSpan.FromHours(2), profile.Timeout);
    }
}
