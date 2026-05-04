using System.IO;
using System.Text.Json;
using KoeNote.App.Services.Models;

namespace KoeNote.App.Services.Setup;

public sealed class SetupWizardService(
    AppPaths paths,
    SetupStateService stateService,
    ToolStatusService toolStatusService,
    ModelCatalogService modelCatalogService,
    InstalledModelRepository installedModelRepository,
    ModelInstallService modelInstallService,
    ModelPackImportService modelPackImportService,
    ModelDownloadService modelDownloadService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public SetupState LoadState()
    {
        return stateService.Load();
    }

    public IReadOnlyList<SetupEnvironmentCheck> GetEnvironmentChecks()
    {
        return toolStatusService.GetStatusItems()
            .Select(static item => new SetupEnvironmentCheck(item.Name, item.IsOk, item.Detail))
            .ToArray();
    }

    public IReadOnlyList<ModelCatalogEntry> GetSelectableModels(string role)
    {
        return modelCatalogService.ListEntries()
            .Where(entry => entry.Role.Equals(role, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public SetupState UseRecommendedSelections()
    {
        var asrModel = PickRecommended("asr");
        var reviewModel = PickRecommended("review");
        var state = stateService.Load() with
        {
            CurrentStep = SetupStep.AsrModel,
            SelectedAsrModelId = asrModel?.ModelId,
            SelectedReviewModelId = reviewModel?.ModelId,
            StorageRoot = paths.UserModels
        };
        return stateService.Save(state);
    }

    public SetupState SelectSetupMode(string setupMode)
    {
        var normalized = string.IsNullOrWhiteSpace(setupMode) ? "guided" : setupMode.Trim();
        return stateService.Save(stateService.Load() with
        {
            CurrentStep = SetupStep.SetupMode,
            SetupMode = normalized
        });
    }

    public SetupState SelectModel(string role, string modelId)
    {
        var catalogItem = modelCatalogService.LoadBuiltInCatalog().Models
            .FirstOrDefault(model => model.Role.Equals(role, StringComparison.OrdinalIgnoreCase) &&
                model.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase));
        if (catalogItem is null)
        {
            throw new InvalidOperationException($"Model is not in the catalog: {modelId}");
        }

        var state = stateService.Load();
        state = role.Equals("asr", StringComparison.OrdinalIgnoreCase)
            ? state with { CurrentStep = SetupStep.AsrModel, SelectedAsrModelId = catalogItem.ModelId }
            : state with { CurrentStep = SetupStep.ReviewModel, SelectedReviewModelId = catalogItem.ModelId };
        return stateService.Save(state);
    }

    public SetupState SetStorageRoot(string storageRoot)
    {
        var root = string.IsNullOrWhiteSpace(storageRoot) ? paths.UserModels : storageRoot.Trim();
        Directory.CreateDirectory(root);
        return stateService.Save(stateService.Load() with
        {
            CurrentStep = SetupStep.Storage,
            StorageRoot = root
        });
    }

    public SetupState AcceptLicenses()
    {
        return stateService.Save(stateService.Load() with
        {
            CurrentStep = SetupStep.License,
            LicenseAccepted = true
        });
    }

    public async Task<SetupInstallResult> DownloadSelectedModelAsync(string role, CancellationToken cancellationToken = default)
    {
        try
        {
            var catalogItem = GetSelectedCatalogItem(role);
            if (catalogItem is null)
            {
                return new SetupInstallResult(false, $"No selected {role} model.", []);
            }

            var targetPath = modelInstallService.GetDefaultInstallPath(catalogItem);
            var installed = await modelDownloadService.DownloadAndInstallAsync(catalogItem, targetPath, cancellationToken: cancellationToken);
            stateService.Save(stateService.Load() with { CurrentStep = SetupStep.Install });
            return new SetupInstallResult(true, $"Downloaded and installed: {installed.DisplayName}", [installed]);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stateService.Save(stateService.Load() with { CurrentStep = SetupStep.Install });
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

            var catalogItem = GetSelectedCatalogItem(role);
            if (catalogItem is null)
            {
                return new SetupInstallResult(false, $"No selected {role} model.", []);
            }

            var installed = modelInstallService.RegisterLocalModel(catalogItem, modelPath, "local_file");
            stateService.Save(stateService.Load() with { CurrentStep = SetupStep.Install });
            return new SetupInstallResult(installed.Verified, $"Local model registered: {installed.DisplayName} ({installed.Status})", [installed]);
        }
        catch (Exception exception)
        {
            stateService.Save(stateService.Load() with { CurrentStep = SetupStep.Install });
            return new SetupInstallResult(false, $"Local model registration failed: {exception.Message}", []);
        }
    }

    public SetupInstallResult ImportOfflineModelPack(string modelPackPath)
    {
        try
        {
            var installed = modelPackImportService.ImportModelPack(modelPackPath);
            stateService.Save(stateService.Load() with { CurrentStep = SetupStep.Install });
            return new SetupInstallResult(true, $"Offline model pack imported: {installed.Count} model(s).", installed);
        }
        catch (Exception exception)
        {
            stateService.Save(stateService.Load() with { CurrentStep = SetupStep.Install });
            return new SetupInstallResult(false, $"Offline model pack import failed: {exception.Message}", []);
        }
    }

    public IReadOnlyList<SetupModelAudit> GetSelectedModelAudit()
    {
        var state = stateService.Load();
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

    public SetupState MoveNext()
    {
        var state = stateService.Load();
        var next = state.CurrentStep >= SetupStep.Complete ? SetupStep.Complete : state.CurrentStep + 1;
        return stateService.Save(state with { CurrentStep = next });
    }

    public SetupState MoveBack()
    {
        var state = stateService.Load();
        var previous = state.CurrentStep <= SetupStep.Welcome ? SetupStep.Welcome : state.CurrentStep - 1;
        return stateService.Save(state with { CurrentStep = previous });
    }

    public SetupSmokeResult RunSmokeCheck()
    {
        var state = stateService.Load();
        var checks = BuildSmokeChecks(state);
        var succeeded = checks.All(static check => check.IsOk);
        var updated = stateService.Save(state with
        {
            CurrentStep = SetupStep.SmokeTest,
            LastSmokeSucceeded = succeeded,
            IsCompleted = state.IsCompleted && succeeded
        });
        WriteReport(updated, checks);
        return new SetupSmokeResult(succeeded, checks, paths.SetupReportPath);
    }

    public SetupState CompleteIfReady()
    {
        var state = stateService.Load();
        var checks = BuildSmokeChecks(state);
        var smokeSucceeded = checks.All(static check => check.IsOk);
        var selectedModels = GetSelectedInstalledModels(state)
            .Where(static model => model.Verified && (File.Exists(model.FilePath) || Directory.Exists(model.FilePath)))
            .ToArray();
        var ready = state.LicenseAccepted &&
            smokeSucceeded &&
            selectedModels.Any(model => model.Role.Equals("asr", StringComparison.OrdinalIgnoreCase)) &&
            selectedModels.Any(model => model.Role.Equals("review", StringComparison.OrdinalIgnoreCase));

        var updated = stateService.Save(state with
        {
            CurrentStep = ready ? SetupStep.Complete : SetupStep.SmokeTest,
            LastSmokeSucceeded = smokeSucceeded,
            IsCompleted = ready
        });
        WriteReport(updated, checks);
        return updated;
    }

    public IReadOnlyList<SetupStepItem> BuildStepItems(SetupState state)
    {
        return Enum.GetValues<SetupStep>()
            .Select(step => new SetupStepItem(step, GetStepTitle(step), GetStepStatus(step, state)))
            .ToArray();
    }

    private ModelCatalogEntry? PickRecommended(string role)
    {
        return GetSelectableModels(role)
            .FirstOrDefault(entry => entry.CatalogItem.RecommendedFor.Contains("v0.1", StringComparer.OrdinalIgnoreCase)) ??
            GetSelectableModels(role).FirstOrDefault();
    }

    private ModelCatalogItem? GetSelectedCatalogItem(string role)
    {
        var state = stateService.Load();
        var modelId = role.Equals("asr", StringComparison.OrdinalIgnoreCase)
            ? state.SelectedAsrModelId
            : state.SelectedReviewModelId;
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        return modelCatalogService.LoadBuiltInCatalog().Models
            .FirstOrDefault(model => model.Role.Equals(role, StringComparison.OrdinalIgnoreCase) &&
                model.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase));
    }

    private SetupSmokeCheck CheckFile(string name, string path)
    {
        var exists = File.Exists(path);
        return new SetupSmokeCheck(name, exists, exists ? path : $"Missing: {path}");
    }

    private IReadOnlyList<SetupSmokeCheck> BuildSmokeChecks(SetupState state)
    {
        return
        [
            CheckFile("ffmpeg", paths.FfmpegPath),
            CheckSelectedModel("ASR model", state.SelectedAsrModelId),
            CheckSelectedModel("Review LLM model", state.SelectedReviewModelId),
            new("license accepted", state.LicenseAccepted, state.LicenseAccepted ? "accepted" : "Open License step and accept model licenses."),
            new("storage root", Directory.Exists(state.StorageRoot ?? string.Empty), state.StorageRoot ?? paths.UserModels)
        ];
    }

    private SetupSmokeCheck CheckSelectedModel(string name, string? modelId)
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

    private void WriteReport(SetupState state, IReadOnlyList<SetupSmokeCheck> checks)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.SetupReportPath)!);
        var messages = checks
            .Where(static check => !check.IsOk)
            .Select(static check => $"{check.Name}: {check.Detail}")
            .DefaultIfEmpty("Setup smoke check passed.")
            .ToArray();
        var report = new SetupReport(
            DateTimeOffset.Now,
            state,
            toolStatusService.GetStatusItems(),
            GetSelectedInstalledModels(state),
            checks,
            state.IsCompleted || checks.All(static check => check.IsOk),
            messages);
        File.WriteAllText(paths.SetupReportPath, JsonSerializer.Serialize(report, JsonOptions));
    }

    private IReadOnlyList<InstalledModel> GetSelectedInstalledModels(SetupState state)
    {
        return new[] { state.SelectedAsrModelId, state.SelectedReviewModelId }
            .Where(static modelId => !string.IsNullOrWhiteSpace(modelId))
            .Select(modelId => installedModelRepository.FindInstalledModel(modelId!))
            .OfType<InstalledModel>()
            .ToArray();
    }

    private static string GetStepTitle(SetupStep step)
    {
        return step switch
        {
            SetupStep.Welcome => "Welcome",
            SetupStep.EnvironmentCheck => "Environment check",
            SetupStep.SetupMode => "Setup mode",
            SetupStep.AsrModel => "ASR model",
            SetupStep.ReviewModel => "Review LLM",
            SetupStep.Storage => "Storage",
            SetupStep.License => "License",
            SetupStep.Install => "Install/import",
            SetupStep.SmokeTest => "Smoke test",
            SetupStep.Complete => "Complete",
            _ => step.ToString()
        };
    }

    private static string GetStepStatus(SetupStep step, SetupState state)
    {
        if (state.IsCompleted)
        {
            return "done";
        }

        if (step == state.CurrentStep)
        {
            return "current";
        }

        return step < state.CurrentStep ? "done" : "pending";
    }
}
