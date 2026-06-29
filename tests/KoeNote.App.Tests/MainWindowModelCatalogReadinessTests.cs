using KoeNote.App.Services;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Models;
using KoeNote.App.ViewModels;

namespace KoeNote.App.Tests;

[Collection(Gemma12BEnvironmentCollection.Name)]
public sealed class MainWindowModelCatalogReadinessTests
{
    [Fact]
    public void IsReviewRuntimeReady_RequiresGemma12BMtpServerWhenEnabled()
    {
        var previousMtp = Environment.GetEnvironmentVariable(Gemma12BLocalValidation.EnableMtpServerEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.EnableMtpServerEnvironmentVariable, null);
            var root = CreateRoot();
            var llamaCompletionPath = Path.Combine(root, "runtime", "llama-completion.exe");
            Touch(llamaCompletionPath);

            Assert.False(MainWindowModelCatalogReadiness.IsReviewRuntimeReady(
                Gemma12BLocalValidation.ModelId,
                llamaCompletionPath));

            Touch(Gemma12BLocalValidation.ResolveLlamaServerPath(llamaCompletionPath));
            Assert.True(MainWindowModelCatalogReadiness.IsReviewRuntimeReady(
                Gemma12BLocalValidation.ModelId,
                llamaCompletionPath));

            File.Delete(Gemma12BLocalValidation.ResolveLlamaServerPath(llamaCompletionPath));
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.EnableMtpServerEnvironmentVariable, "0");
            Assert.True(MainWindowModelCatalogReadiness.IsReviewRuntimeReady(
                Gemma12BLocalValidation.ModelId,
                llamaCompletionPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.EnableMtpServerEnvironmentVariable, previousMtp);
        }
    }

    [Fact]
    public void IsReviewModelReady_RequiresGemma12BMtpDraftAndDirectStageFallbackWhenEnabled()
    {
        var previousMtp = Environment.GetEnvironmentVariable(Gemma12BLocalValidation.EnableMtpServerEnvironmentVariable);
        var previousDraft = Environment.GetEnvironmentVariable(Gemma12BLocalValidation.DraftModelPathEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.EnableMtpServerEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.DraftModelPathEnvironmentVariable, null);
            var root = CreateRoot();
            var paths = new AppPaths(root, root, AppContext.BaseDirectory);
            paths.EnsureCreated();
            var modelPath = Path.Combine(root, "models", "gemma-12b.gguf");
            Touch(modelPath);
            var installedModels = new Dictionary<string, InstalledModel>(StringComparer.OrdinalIgnoreCase)
            {
                [Gemma12BLocalValidation.ModelId] = CreateInstalledModel(
                    Gemma12BLocalValidation.ModelId,
                    "review",
                    modelPath)
            };

            Assert.False(MainWindowModelCatalogReadiness.IsReviewModelReady(
                Gemma12BLocalValidation.ModelId,
                paths,
                modelId => installedModels.GetValueOrDefault(modelId),
                paths.UserModels));

            var draftOverridePath = Path.Combine(root, "external", Gemma12BLocalValidation.MtpDraftFileName);
            Touch(draftOverridePath);
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.DraftModelPathEnvironmentVariable, draftOverridePath);
            Assert.False(MainWindowModelCatalogReadiness.IsReviewModelReady(
                Gemma12BLocalValidation.ModelId,
                paths,
                modelId => installedModels.GetValueOrDefault(modelId),
                paths.UserModels));

            var fallbackPath = Path.Combine(root, "models", "gemma-e4b.gguf");
            Touch(fallbackPath);
            installedModels[ReviewModelSelectionResolver.DefaultReviewModelId] = CreateInstalledModel(
                ReviewModelSelectionResolver.DefaultReviewModelId,
                "review",
                fallbackPath);
            Assert.True(MainWindowModelCatalogReadiness.IsReviewModelReady(
                Gemma12BLocalValidation.ModelId,
                paths,
                modelId => installedModels.GetValueOrDefault(modelId),
                paths.UserModels));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.EnableMtpServerEnvironmentVariable, previousMtp);
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.DraftModelPathEnvironmentVariable, previousDraft);
        }
    }

    private static InstalledModel CreateInstalledModel(string modelId, string role, string filePath)
    {
        return new InstalledModel(
            modelId,
            role,
            "llama-cpp",
            modelId,
            null,
            null,
            filePath,
            null,
            null,
            null,
            true,
            null,
            "test",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "verified");
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "test");
    }
}
