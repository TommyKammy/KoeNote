using System.IO;
using System.Text.Json;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Services.Setup;

internal sealed class SetupReadinessService(
    AppPaths paths,
    SetupStateService stateService,
    ToolStatusService toolStatusService,
    ModelCatalogService modelCatalogService,
    InstalledModelRepository installedModelRepository,
    ISetupHostResourceProbe hostResourceProbe)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public IReadOnlyList<SetupEnvironmentCheck> GetEnvironmentChecks()
    {
        return toolStatusService.GetStatusItems()
            .Select(static item => new SetupEnvironmentCheck(item.Name, item.IsOk, item.Detail))
            .ToArray();
    }

    public SetupStepReadiness GetStepReadiness(SetupState state)
    {
        return new SetupStepReadiness(
            EnvironmentReady: GetEnvironmentChecks().All(static check => check.IsOk),
            AsrModelReady: IsSelectedModelReady(state.SelectedAsrModelId, "asr"),
            ReviewModelReady: IsSelectedModelReady(state.SelectedReviewModelId, "review"),
            ReviewRuntimeReady: IsSelectedReviewRuntimeReady(state.SelectedReviewModelId),
            StorageReady: Directory.Exists(state.StorageRoot ?? paths.DefaultModelStorageRoot));
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
        var ready = IsCompleteStateReady(state, out var checks, out var smokeSucceeded);
        var updated = stateService.Save(state with
        {
            CurrentStep = ready ? SetupStep.Complete : SetupStep.SmokeTest,
            LastSmokeSucceeded = smokeSucceeded,
            IsCompleted = ready
        });
        WriteReport(updated, checks);
        return updated;
    }

    public bool IsCompleteStateReady(SetupState state)
    {
        return IsCompleteStateReady(state, out _, out _);
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

    private bool IsCompleteStateReady(
        SetupState state,
        out IReadOnlyList<SetupSmokeCheck> checks,
        out bool smokeSucceeded)
    {
        checks = BuildSmokeChecks(state);
        smokeSucceeded = checks.All(static check => check.IsOk);
        var selectedModels = GetSelectedInstalledModels(state)
            .Where(static model => model.Verified && (File.Exists(model.FilePath) || Directory.Exists(model.FilePath)))
            .ToArray();
        return state.LicenseAccepted &&
            smokeSucceeded &&
            selectedModels.Any(model => model.Role.Equals("asr", StringComparison.OrdinalIgnoreCase)) &&
            selectedModels.Any(model => model.Role.Equals("review", StringComparison.OrdinalIgnoreCase));
    }

    private SetupSmokeCheck CheckFile(string name, string path)
    {
        var exists = File.Exists(path);
        return new SetupSmokeCheck(name, exists, exists ? path : $"Missing: {path}");
    }

    private IReadOnlyList<SetupSmokeCheck> BuildSmokeChecks(SetupState state)
    {
        List<SetupSmokeCheck> checks =
        [
            CheckFile("ffmpeg", paths.FfmpegPath),
            new("faster-whisper runtime", FasterWhisperRuntimeLayout.HasPackage(paths), FasterWhisperRuntimeLayout.HasPackage(paths) ? paths.AsrPythonEnvironment : $"Not installed: {paths.AsrPythonEnvironment}"),
            CheckSelectedModel("ASR model", state.SelectedAsrModelId),
            CheckSelectedModel("Review LLM model", state.SelectedReviewModelId),
            CheckSelectedReviewRuntime(state.SelectedReviewModelId),
            new("license accepted", state.LicenseAccepted, state.LicenseAccepted ? "accepted" : "Open License step and accept model licenses."),
            new("storage root", Directory.Exists(state.StorageRoot ?? string.Empty), state.StorageRoot ?? paths.DefaultModelStorageRoot)
        ];

        if (hostResourceProbe.GetResources().NvidiaGpuDetected)
        {
            checks.Add(new SetupSmokeCheck(
                "ASR CUDA runtime",
                AsrCudaRuntimeLayout.HasPackage(paths),
                AsrCudaRuntimeLayout.HasPackage(paths) ? paths.AsrRuntimeDirectory : $"Not installed: {paths.AsrRuntimeDirectory}"));
            checks.Add(new SetupSmokeCheck(
                "Review CUDA runtime",
                CudaReviewRuntimeLayout.HasPackage(paths),
                CudaReviewRuntimeLayout.HasPackage(paths) ? paths.ReviewRuntimeDirectory : $"Not installed: {paths.ReviewRuntimeDirectory}"));
        }

        return checks;
    }

    private SetupSmokeCheck CheckSelectedReviewRuntime(string? modelId)
    {
        var runtimePath = GetSelectedReviewRuntimePath(modelId);
        var exists = File.Exists(runtimePath);
        return new SetupSmokeCheck("Review LLM runtime", exists, exists ? runtimePath : $"Missing: {runtimePath}");
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

    private bool IsSelectedModelReady(string? modelId, string role)
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

    private bool IsSelectedReviewRuntimeReady(string? modelId)
    {
        return File.Exists(GetSelectedReviewRuntimePath(modelId));
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
            GetExistingDataSummary(),
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
}
