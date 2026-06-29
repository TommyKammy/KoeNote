using System.IO;
using System.Text.Json;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Diarization;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Services.Setup;

internal sealed class SetupReadinessService(
    AppPaths paths,
    SetupStateService stateService,
    ToolStatusService toolStatusService,
    ModelCatalogService modelCatalogService,
    InstalledModelRepository installedModelRepository,
    ISetupHostResourceProbe hostResourceProbe,
    ISetupRuntimeSmokeService? runtimeSmokeService = null)
{
    private readonly SetupReadinessAuditBuilder _auditBuilder = new(paths, modelCatalogService, installedModelRepository);

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
        var resources = hostResourceProbe.GetResources();
        var gpuRuntimeReady = _auditBuilder.IsSelectedGpuRequirementSatisfied(state, resources) &&
            (!resources.NvidiaGpuDetected ||
             (AsrCudaRuntimeLayout.HasPackage(paths) && CudaReviewRuntimeLayout.HasPackage(paths)));
        return new SetupStepReadiness(
            EnvironmentReady: GetEnvironmentChecks().All(static check => check.IsOk),
            AsrModelReady: _auditBuilder.IsSelectedModelReady(state.SelectedAsrModelId, "asr"),
            ReviewModelReady: _auditBuilder.IsSelectedModelReady(state.SelectedReviewModelId, "review") &&
                _auditBuilder.IsSelectedDirectLlmFallbackReady(state.SelectedReviewModelId) &&
                _auditBuilder.IsSelectedGemma12BMtpDraftReady(state.SelectedReviewModelId, state.StorageRoot),
            ReviewRuntimeReady: _auditBuilder.IsSelectedReviewRuntimeReady(state.SelectedReviewModelId),
            GpuRuntimeReady: gpuRuntimeReady,
            StorageReady: Directory.Exists(state.StorageRoot ?? paths.DefaultModelStorageRoot));
    }

    public IReadOnlyList<SetupModelAudit> GetSelectedModelAudit()
    {
        var state = stateService.Load();
        return _auditBuilder.GetSelectedModelAudit(state);
    }

    public IReadOnlyList<SetupExistingDataItem> GetExistingDataSummary()
    {
        return _auditBuilder.GetExistingDataSummary();
    }

    public async Task<SetupSmokeResult> RunSmokeCheckAsync(CancellationToken cancellationToken = default)
    {
        var state = stateService.Load();
        var checks = await BuildSmokeChecksAsync(state, runRuntimeActions: true, cancellationToken);
        var succeeded = checks.All(static check => check.IsOk);
        var updated = stateService.Save(state with
        {
            CurrentStep = state.IsCompleted && succeeded ? SetupStep.Complete : SetupStep.SmokeTest,
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
            LastSmokeSucceeded = state.LastSmokeSucceeded && smokeSucceeded,
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
        return _auditBuilder.IsSelectedTernaryReviewRuntimeMissing(state);
    }

    public bool IsRequiredGpuRuntimeMissing()
    {
        var resources = hostResourceProbe.GetResources();
        return resources.NvidiaGpuDetected &&
            (!AsrCudaRuntimeLayout.HasPackage(paths) || !CudaReviewRuntimeLayout.HasPackage(paths));
    }

    public bool IsSelectedGpuRequirementMissing(SetupState state)
    {
        return !_auditBuilder.IsSelectedGpuRequirementSatisfied(state, hostResourceProbe.GetResources());
    }

    public bool IsSelectedDirectLlmFallbackMissing(SetupState state)
    {
        return !_auditBuilder.IsSelectedDirectLlmFallbackReady(state.SelectedReviewModelId);
    }

    public bool IsSelectedGemma12BMtpRequirementMissing(SetupState state)
    {
        return _auditBuilder.IsSelectedGemma12BMtpRequirementMissing(state);
    }

    private bool IsCompleteStateReady(
        SetupState state,
        out IReadOnlyList<SetupSmokeCheck> checks,
        out bool smokeSucceeded)
    {
        checks = BuildSmokeChecks(state);
        smokeSucceeded = checks.All(static check => check.IsOk);
        var selectedModels = _auditBuilder.GetSelectedInstalledModels(state)
            .Where(static model => model.Verified && (File.Exists(model.FilePath) || Directory.Exists(model.FilePath)))
            .ToArray();
        return state.LicenseAccepted &&
            state.LastSmokeSucceeded &&
            smokeSucceeded &&
            selectedModels.Any(model => model.Role.Equals("asr", StringComparison.OrdinalIgnoreCase)) &&
            selectedModels.Any(model => model.Role.Equals("review", StringComparison.OrdinalIgnoreCase));
    }

    private SetupSmokeCheck CheckFile(string name, string path)
    {
        var exists = File.Exists(path);
        return new SetupSmokeCheck(name, exists, exists ? path : $"Missing: {path}");
    }

    private async Task<IReadOnlyList<SetupSmokeCheck>> BuildSmokeChecksAsync(
        SetupState state,
        bool runRuntimeActions,
        CancellationToken cancellationToken)
    {
        var resources = hostResourceProbe.GetResources();
        List<SetupSmokeCheck> checks =
        [
            CheckFile("ffmpeg", paths.FfmpegPath),
            new("faster-whisper runtime", FasterWhisperRuntimeLayout.HasPackage(paths), FasterWhisperRuntimeLayout.HasPackage(paths) ? paths.AsrPythonEnvironment : $"Not installed: {paths.AsrPythonEnvironment}"),
            _auditBuilder.CheckSelectedModel("ASR model", state.SelectedAsrModelId),
            _auditBuilder.CheckSelectedModel("Review LLM model", state.SelectedReviewModelId),
            _auditBuilder.CheckSelectedReviewRuntime(state.SelectedReviewModelId),
            CheckDiarizationRuntime(),
            new("license accepted", state.LicenseAccepted, state.LicenseAccepted ? "accepted" : "Open License step and accept model licenses."),
            new("storage root", Directory.Exists(state.StorageRoot ?? string.Empty), state.StorageRoot ?? paths.DefaultModelStorageRoot)
        ];
        checks.AddRange(_auditBuilder.CheckDirectLlmFallbackRequirements(state.SelectedReviewModelId));
        checks.AddRange(_auditBuilder.CheckGemma12BMtpRequirements(state.SelectedReviewModelId, state.StorageRoot));
        checks.AddRange(_auditBuilder.CheckSelectedGpuRequirements(state, resources));

        if (resources.NvidiaGpuDetected)
        {
            checks.Add(CheckAsrCudaRuntime());
            checks.Add(CheckCudaReviewRuntime());
        }

        var runtimeChecks = await (runtimeSmokeService ?? new SetupRuntimeSmokeService(paths, installedModelRepository))
            .RunAsync(state, runRuntimeActions, resources.NvidiaGpuDetected, cancellationToken);
        checks.AddRange(runtimeChecks);

        return checks;
    }

    private IReadOnlyList<SetupSmokeCheck> BuildSmokeChecks(SetupState state)
    {
        var resources = hostResourceProbe.GetResources();
        List<SetupSmokeCheck> checks =
        [
            CheckFile("ffmpeg", paths.FfmpegPath),
            new("faster-whisper runtime", FasterWhisperRuntimeLayout.HasPackage(paths), FasterWhisperRuntimeLayout.HasPackage(paths) ? paths.AsrPythonEnvironment : $"Not installed: {paths.AsrPythonEnvironment}"),
            _auditBuilder.CheckSelectedModel("ASR model", state.SelectedAsrModelId),
            _auditBuilder.CheckSelectedModel("Review LLM model", state.SelectedReviewModelId),
            _auditBuilder.CheckSelectedReviewRuntime(state.SelectedReviewModelId),
            CheckDiarizationRuntime(),
            new("license accepted", state.LicenseAccepted, state.LicenseAccepted ? "accepted" : "Open License step and accept model licenses."),
            new("storage root", Directory.Exists(state.StorageRoot ?? string.Empty), state.StorageRoot ?? paths.DefaultModelStorageRoot)
        ];
        checks.AddRange(_auditBuilder.CheckDirectLlmFallbackRequirements(state.SelectedReviewModelId));
        checks.AddRange(_auditBuilder.CheckGemma12BMtpRequirements(state.SelectedReviewModelId, state.StorageRoot));
        checks.AddRange(_auditBuilder.CheckSelectedGpuRequirements(state, resources));

        if (resources.NvidiaGpuDetected)
        {
            checks.Add(CheckAsrCudaRuntime());
            checks.Add(CheckCudaReviewRuntime());
        }

        checks.AddRange((runtimeSmokeService ?? new SetupRuntimeSmokeService(paths, installedModelRepository))
            .RunReadiness(state, resources.NvidiaGpuDetected));

        return checks;
    }

    private SetupSmokeCheck CheckDiarizationRuntime()
    {
        var exists = DiarizationRuntimeLayout.HasPackage(paths);
        return new SetupSmokeCheck(
            "speaker diarization runtime",
            exists,
            exists ? paths.DiarizationPythonEnvironment : DiarizationRuntimeLayout.DescribeMissingRuntimeData(paths));
    }

    private SetupSmokeCheck CheckCudaReviewRuntime()
    {
        var exists = CudaReviewRuntimeLayout.HasPackage(paths);
        return new SetupSmokeCheck(
            "Review CUDA runtime",
            exists,
            exists ? paths.CudaReviewRuntimeDirectory : DescribeCudaReviewRuntimeMissing());
    }

    private SetupSmokeCheck CheckAsrCudaRuntime()
    {
        var exists = AsrCudaRuntimeLayout.HasPackage(paths);
        return new SetupSmokeCheck(
            "ASR CUDA runtime",
            exists,
            exists ? paths.AsrCTranslate2RuntimeDirectory : DescribeAsrCudaRuntimeMissing());
    }

    private string DescribeAsrCudaRuntimeMissing()
    {
        var prefix = AsrCudaRuntimeLayout.HasLegacyNvidiaRuntimeFiles(paths)
            ? "アップデート後のASR GPU runtime移行が必要です。Setup Wizardで導入を実行すると旧保存先から永続保存先へ移行します。"
            : "アップデート後にASR GPU runtimeの再導入が必要です。";
        var missing = AsrCudaRuntimeLayout.GetMissingPackageItems(paths);
        return missing.Count == 0
            ? $"{prefix} Target: {paths.AsrCTranslate2RuntimeDirectory}"
            : $"{prefix} Missing: {string.Join("; ", missing)}";
    }

    private string DescribeCudaReviewRuntimeMissing()
    {
        var prefix = CudaReviewRuntimeLayout.HasLegacyNvidiaRuntimeFiles(paths)
            ? "アップデート後のReview GPU runtime移行が必要です。Setup Wizardで導入を実行すると旧保存先から永続保存先へ移行します。"
            : "アップデート後にReview GPU runtimeの再導入が必要です。";
        var missing = CudaReviewRuntimeLayout.GetMissingPackageItems(paths);
        return missing.Count == 0
            ? $"{prefix} Target: {paths.CudaReviewRuntimeDirectory}"
            : $"{prefix} Missing: {string.Join("; ", missing)}";
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
            _auditBuilder.GetSelectedInstalledModels(state),
            checks,
            state.IsCompleted || checks.All(static check => check.IsOk),
            messages);
        File.WriteAllText(paths.SetupReportPath, JsonSerializer.Serialize(report, JsonOptions));
    }

}
