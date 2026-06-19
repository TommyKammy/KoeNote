using System.IO;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Services.Setup;

internal sealed class SetupReadinessAuditBuilder(
    AppPaths paths,
    ModelCatalogService modelCatalogService,
    InstalledModelRepository installedModelRepository)
{
    public IReadOnlyList<SetupModelAudit> GetSelectedModelAudit(SetupState state)
    {
        return GetSelectedInstalledModels(state)
            .Select(model => new SetupModelAudit(
                model.ModelId,
                !string.IsNullOrWhiteSpace(model.Sha256),
                string.IsNullOrWhiteSpace(model.Sha256) ? "checksum not recorded" : model.Sha256,
                !string.IsNullOrWhiteSpace(model.ManifestPath) && File.Exists(model.ManifestPath),
                string.IsNullOrWhiteSpace(model.ManifestPath) ? "manifest not recorded" : model.ManifestPath,
                !string.IsNullOrWhiteSpace(model.LicenseName),
                string.IsNullOrWhiteSpace(model.LicenseName) ? "license not recorded" : model.LicenseName))
            .ToArray();
    }

    public IReadOnlyList<SetupExistingDataItem> GetExistingDataSummary()
    {
        var installedModels = File.Exists(paths.DatabasePath)
            ? installedModelRepository.ListInstalledModels()
                .Where(static model => File.Exists(model.FilePath) || Directory.Exists(model.FilePath))
                .ToArray()
            : [];
        var jobCount = Directory.Exists(paths.Jobs)
            ? Directory.EnumerateDirectories(paths.Jobs).Count()
            : 0;

        return
        [
            new("setup state", File.Exists(paths.SetupStatePath), paths.SetupStatePath),
            new("jobs database", File.Exists(paths.DatabasePath), paths.DatabasePath),
            new("job folders", jobCount > 0, $"{jobCount} folder(s) under {paths.Jobs}"),
            new("registered models", installedModels.Length > 0, $"{installedModels.Length} model(s) recorded in installed_models"),
            new("user model storage", Directory.Exists(paths.UserModels), paths.UserModels),
            new("machine model storage", Directory.Exists(paths.MachineModels), paths.MachineModels)
        ];
    }

    public bool IsSelectedModelReady(string? modelId, string role)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return false;
        }

        var installed = installedModelRepository.FindInstalledModel(modelId);
        return installed is not null &&
            installed.Role.Equals(role, StringComparison.OrdinalIgnoreCase) &&
            installed.Verified &&
            (File.Exists(installed.FilePath) || Directory.Exists(installed.FilePath));
    }

    public bool IsSelectedReviewRuntimeReady(string? modelId)
    {
        return File.Exists(GetSelectedReviewRuntimePath(modelId));
    }

    public bool IsSelectedTernaryReviewRuntimeMissing(SetupState state)
    {
        if (string.IsNullOrWhiteSpace(state.SelectedReviewModelId))
        {
            return false;
        }

        var runtimePath = GetSelectedReviewRuntimePath(state.SelectedReviewModelId);
        return string.Equals(runtimePath, paths.TernaryLlamaCompletionPath, StringComparison.OrdinalIgnoreCase) &&
            !File.Exists(runtimePath);
    }

    public SetupSmokeCheck CheckSelectedModel(string name, string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return new SetupSmokeCheck(name, false, "Select a model in Setup.");
        }

        var installed = installedModelRepository.FindInstalledModel(modelId);
        if (installed is null)
        {
            return new SetupSmokeCheck(name, false, $"Not installed: {modelId}");
        }

        var exists = File.Exists(installed.FilePath) || Directory.Exists(installed.FilePath);
        var verified = installed.Verified && exists;
        return new SetupSmokeCheck(name, verified, verified ? installed.FilePath : $"Verification failed or missing: {installed.FilePath}");
    }

    public SetupSmokeCheck CheckSelectedReviewRuntime(string? modelId)
    {
        var runtimePath = GetSelectedReviewRuntimePath(modelId);
        var exists = File.Exists(runtimePath);
        return new SetupSmokeCheck("Review LLM runtime", exists, exists ? runtimePath : $"Missing: {runtimePath}");
    }

    public IReadOnlyList<InstalledModel> GetSelectedInstalledModels(SetupState state)
    {
        return new[] { state.SelectedAsrModelId, state.SelectedReviewModelId }
            .Where(static modelId => !string.IsNullOrWhiteSpace(modelId))
            .Select(modelId => installedModelRepository.FindInstalledModel(modelId!))
            .OfType<InstalledModel>()
            .ToArray();
    }

    private string GetSelectedReviewRuntimePath(string? modelId)
    {
        var catalog = modelCatalogService.LoadBuiltInCatalog();
        var effectiveModelId = !string.IsNullOrWhiteSpace(modelId)
            ? modelId
            : catalog.Presets?.FirstOrDefault(preset =>
                string.Equals(preset.PresetId, "recommended", StringComparison.OrdinalIgnoreCase))?.ReviewModelId;
        if (string.IsNullOrWhiteSpace(effectiveModelId))
        {
            return paths.LlamaCompletionPath;
        }

        return ReviewRuntimeResolver.ResolveLlamaCompletionPath(paths, catalog, effectiveModelId);
    }
}
