using KoeNote.App.Services;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Tests;

public sealed class ReviewRuntimeResolverTests
{
    [Fact]
    public void ResolveLlamaCompletionPath_UsesTernaryRuntimeForTernaryPackage()
    {
        var paths = new AppPaths(CreateRoot(), CreateRoot(), AppContext.BaseDirectory);
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();

        var path = ReviewRuntimeResolver.ResolveLlamaCompletionPath(paths, catalog, "ternary-bonsai-8b-q2-0");

        Assert.Equal(paths.TernaryLlamaCompletionPath, path);
    }

    [Fact]
    public void ResolveLlamaCompletionPath_UsesStandardRuntimeForNormalLlamaCppModels()
    {
        var paths = new AppPaths(CreateRoot(), CreateRoot(), AppContext.BaseDirectory);
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();

        var path = ReviewRuntimeResolver.ResolveLlamaCompletionPath(paths, catalog, "gemma-4-e4b-it-q4-k-m");

        Assert.Equal(paths.LlamaCompletionPath, path);
    }

    private static string CreateRoot()
    {
        return Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
    }
}
