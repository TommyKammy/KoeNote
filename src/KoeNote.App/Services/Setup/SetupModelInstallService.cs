using System.IO;
using KoeNote.App.Services.Models;

namespace KoeNote.App.Services.Setup;

internal sealed class SetupModelInstallService(
    SetupStateService stateService,
    SetupModelSelectionService selectionService,
    ModelInstallService modelInstallService,
    ModelPackImportService modelPackImportService,
    ModelDownloadService modelDownloadService)
{
    public async Task<SetupInstallResult> DownloadSelectedModelAsync(string role, CancellationToken cancellationToken = default)
    {
        try
        {
            var catalogItem = selectionService.GetSelectedCatalogItem(role);
            if (catalogItem is null)
            {
                return new SetupInstallResult(false, $"No selected {role} model.", []);
            }

            var targetPath = modelInstallService.GetDefaultInstallPath(catalogItem);
            var installed = await modelDownloadService.DownloadAndInstallAsync(catalogItem, targetPath, cancellationToken: cancellationToken);
            MarkInstallStep();
            return new SetupInstallResult(true, $"Downloaded and installed: {installed.DisplayName}", [installed]);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            MarkInstallStep();
            return new SetupInstallResult(false, $"Online download failed: {exception.Message}", []);
        }
    }

    public SetupInstallResult RegisterSelectedLocalModel(string role, string modelPath)
    {
        try
        {
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
}
