using System.IO;
using KoeNote.App.Services.Llm;
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

            var installItems = ResolveRequiredInstallItems(catalogItem);
            return await DownloadCatalogItemsAsync(installItems, progress, cancellationToken);
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

        IReadOnlyList<ModelCatalogItem> installItems;
        try
        {
            installItems = ResolveSelectedPresetInstallItems();
        }
        catch (Exception exception)
        {
            MarkInstallStep();
            return new SetupInstallResult(false, exception.Message, []);
        }

        return await DownloadCatalogItemsAsync(installItems, progress, cancellationToken);
    }

    private IReadOnlyList<ModelCatalogItem> ResolveSelectedPresetInstallItems()
    {
        var items = new List<ModelCatalogItem>();
        foreach (var role in new[] { "asr", "review" })
        {
            var selected = selectionService.GetSelectedCatalogItem(role);
            if (selected is not null)
            {
                items.AddRange(ResolveRequiredInstallItems(selected));
            }
        }

        return items
            .DistinctBy(static item => item.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<ModelCatalogItem> ResolveRequiredInstallItems(ModelCatalogItem catalogItem)
    {
        var items = new List<ModelCatalogItem> { catalogItem };
        if (RequiresDirectLlmStageFallback(catalogItem) &&
            !IsInstalledModelReady(ReviewModelSelectionResolver.DefaultReviewModelId, "review"))
        {
            var fallbackModel = selectionService.GetCatalogItemById(ReviewModelSelectionResolver.DefaultReviewModelId)
                ?? throw new InvalidOperationException($"Direct LLM fallback model is not in the catalog: {ReviewModelSelectionResolver.DefaultReviewModelId}");
            items.Add(fallbackModel);
        }

        if (RequiresGemma12BMtpDraft(catalogItem) && !IsGemma12BMtpDraftAlreadyReady())
        {
            var mtpDraft = selectionService.GetCatalogItemById(Gemma12BLocalValidation.MtpDraftModelId)
                ?? throw new InvalidOperationException($"Gemma 4 12B MTP draft model is not in the catalog: {Gemma12BLocalValidation.MtpDraftModelId}");
            items.Add(mtpDraft);
        }

        return items;
    }

    private static bool RequiresDirectLlmStageFallback(ModelCatalogItem catalogItem)
    {
        return catalogItem.Role.Equals("review", StringComparison.OrdinalIgnoreCase) &&
            Gemma12BLocalValidation.IsTargetModel(catalogItem.ModelId);
    }

    private bool IsInstalledModelReady(string modelId, string role)
    {
        var installed = installedModelRepository.FindInstalledModel(modelId);
        return installed is not null &&
            installed.Role.Equals(role, StringComparison.OrdinalIgnoreCase) &&
            installed.Verified &&
            (File.Exists(installed.FilePath) || Directory.Exists(installed.FilePath));
    }

    private static bool RequiresGemma12BMtpDraft(ModelCatalogItem catalogItem)
    {
        return catalogItem.Role.Equals("review", StringComparison.OrdinalIgnoreCase) &&
            Gemma12BLocalValidation.IsTargetModel(catalogItem.ModelId) &&
            Gemma12BLocalValidation.IsMtpServerEnabled();
    }

    private bool IsGemma12BMtpDraftAlreadyReady()
    {
        var configured = Gemma12BLocalValidation.GetConfiguredMtpDraftModelPath();
        if (configured is not null)
        {
            return LlamaRuntimePathBridge.CanPrepareModelPath(configured);
        }

        var installed = installedModelRepository.FindInstalledModel(Gemma12BLocalValidation.MtpDraftModelId);
        if (installed is not null &&
            installed.Role.Equals("review_aux", StringComparison.OrdinalIgnoreCase) &&
            installed.Verified &&
            File.Exists(installed.FilePath) &&
            LlamaRuntimePathBridge.CanPrepareModelPath(installed.FilePath))
        {
            return true;
        }

        var storageRoot = stateService.Load().StorageRoot;
        var fallbackPath = string.IsNullOrWhiteSpace(storageRoot)
            ? Gemma12BLocalValidation.ResolveMtpDraftModelPath()
            : Gemma12BLocalValidation.ResolveMtpDraftModelPath(storageRoot);
        return LlamaRuntimePathBridge.CanPrepareModelPath(fallbackPath);
    }

    private async Task<SetupInstallResult> DownloadCatalogItemsAsync(
        IReadOnlyList<ModelCatalogItem> catalogItems,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (catalogItems.Count == 1)
        {
            return await DownloadCatalogItemAsync(catalogItems[0], progress, cancellationToken);
        }

        var results = new List<SetupInstallResult>();
        foreach (var catalogItem in catalogItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                results.Add(await DownloadCatalogItemAsync(catalogItem, progress, cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(new SetupInstallResult(false, $"Online download failed: {exception.Message}", []));
            }
        }

        var installedModels = results
            .SelectMany(static result => result.InstalledModels)
            .ToArray();
        var failedResults = results
            .Where(static result => !result.IsSucceeded)
            .ToArray();
        var message = failedResults.Length == 0
            ? $"Model assets are ready: {installedModels.Length} item(s)."
            : string.Join(" / ", failedResults.Select(static result => result.Message));
        MarkInstallStep();
        return new SetupInstallResult(failedResults.Length == 0, message, installedModels);
    }

    private async Task<SetupInstallResult> DownloadCatalogItemAsync(
        ModelCatalogItem catalogItem,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
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
