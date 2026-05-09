using System.IO;
using KoeNote.App.Services.Models;

namespace KoeNote.App.Services.Setup;

internal sealed class SetupModelInstallService(
    SetupStateService stateService,
    SetupModelSelectionService selectionService,
    InstalledModelRepository installedModelRepository,
    ModelInstallService modelInstallService,
    ModelPackImportService modelPackImportService,
    ModelDownloadService modelDownloadService)
{
    public async Task<SetupInstallResult> DownloadSelectedModelAsync(string role, CancellationToken cancellationToken = default)
    {
        return await DownloadSelectedModelAsync(role, progress: null, cancellationToken);
    }

    public async Task<SetupInstallResult> DownloadSelectedModelAsync(
        string role,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var licenseCheck = EnsureLicenseAccepted();
            if (licenseCheck is not null)
            {
                return licenseCheck;
            }

            var installPlanCheck = EnsureInstallPlanReached();
            if (installPlanCheck is not null)
            {
                return installPlanCheck;
            }

            var catalogItem = selectionService.GetSelectedCatalogItem(role);
            if (catalogItem is null)
            {
                return new SetupInstallResult(false, $"No selected {role} model.", []);
            }

            var existing = installedModelRepository.FindInstalledModel(catalogItem.ModelId);
            if (existing is not null &&
                existing.Verified &&
                (File.Exists(existing.FilePath) || Directory.Exists(existing.FilePath)))
            {
                MarkInstallStep();
                return new SetupInstallResult(true, $"Already installed: {existing.DisplayName}", [existing]);
            }

            var storageRoot = stateService.Load().StorageRoot;
            var targetPath = string.IsNullOrWhiteSpace(storageRoot)
                ? modelInstallService.GetDefaultInstallPath(catalogItem)
                : modelInstallService.GetDefaultInstallPath(catalogItem, storageRoot);
            var installed = await modelDownloadService.DownloadAndInstallAsync(catalogItem, targetPath, progress, cancellationToken);
            MarkInstallStep();
            return new SetupInstallResult(true, $"Downloaded and installed: {installed.DisplayName}", [installed]);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            MarkInstallStep();
            return new SetupInstallResult(false, $"Online download failed: {exception.Message}", []);
        }
    }

    public async Task<SetupInstallResult> DownloadSelectedPresetModelsAsync(
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var licenseCheck = EnsureLicenseAccepted();
        if (licenseCheck is not null)
        {
            return licenseCheck;
        }

        var installPlanCheck = EnsureInstallPlanReached();
        if (installPlanCheck is not null)
        {
            return installPlanCheck;
        }

        var results = new List<SetupInstallResult>();
        foreach (var role in new[] { "asr", "review" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await DownloadSelectedModelAsync(role, progress, cancellationToken));
        }

        var installedModels = results
            .SelectMany(static result => result.InstalledModels)
            .ToArray();
        var failedResults = results
            .Where(static result => !result.IsSucceeded)
            .ToArray();
        var message = failedResults.Length == 0
            ? $"Preset models are ready: {installedModels.Length} model(s)."
            : string.Join(" / ", failedResults.Select(static result => result.Message));
        MarkInstallStep();
        return new SetupInstallResult(failedResults.Length == 0, message, installedModels);
    }

    public SetupInstallResult RegisterSelectedLocalModel(string role, string modelPath)
    {
        try
        {
            var licenseCheck = EnsureLicenseAccepted();
            if (licenseCheck is not null)
            {
                return licenseCheck;
            }

            var installPlanCheck = EnsureInstallPlanReached();
            if (installPlanCheck is not null)
            {
                return installPlanCheck;
            }

            if (!File.Exists(modelPath) && !Directory.Exists(modelPath))
            {
                return new SetupInstallResult(false, $"Local model path not found: {modelPath}", []);
            }

            var catalogItem = selectionService.GetSelectedCatalogItem(role);
            if (catalogItem is null)
            {
                return new SetupInstallResult(false, $"No selected {role} model.", []);
            }

            var installed = modelInstallService.RegisterLocalModel(catalogItem, modelPath, "local_file");
            MarkInstallStep();
            return new SetupInstallResult(installed.Verified, $"Local model registered: {installed.DisplayName} ({installed.Status})", [installed]);
        }
        catch (Exception exception)
        {
            MarkInstallStep();
            return new SetupInstallResult(false, $"Local model registration failed: {exception.Message}", []);
        }
    }

    public SetupInstallResult ImportOfflineModelPack(string modelPackPath)
    {
        try
        {
            var licenseCheck = EnsureLicenseAccepted();
            if (licenseCheck is not null)
            {
                return licenseCheck;
            }

            var installPlanCheck = EnsureInstallPlanReached();
            if (installPlanCheck is not null)
            {
                return installPlanCheck;
            }

            var installed = modelPackImportService.ImportModelPack(modelPackPath);
            MarkInstallStep();
            return new SetupInstallResult(true, $"Offline model pack imported: {installed.Count} model(s).", installed);
        }
        catch (Exception exception)
        {
            MarkInstallStep();
            return new SetupInstallResult(false, $"Offline model pack import failed: {exception.Message}", []);
        }
    }

    private void MarkInstallStep()
    {
        stateService.Save(stateService.Load() with { CurrentStep = SetupStep.Install });
    }

    private SetupInstallResult? EnsureLicenseAccepted()
    {
        return stateService.Load().LicenseAccepted
            ? null
            : new SetupInstallResult(false, "Model licenses must be accepted before installation.", []);
    }

    private SetupInstallResult? EnsureInstallPlanReached()
    {
        return SetupStepFlow.HasReached(stateService.Load().CurrentStep, SetupStep.InstallPlan)
            ? null
            : new SetupInstallResult(false, "Review the installation plan before installation.", []);
    }
}
