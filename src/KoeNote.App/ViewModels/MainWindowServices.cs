using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Audio;
using KoeNote.App.Services.Diarization;
using KoeNote.App.Services.Export;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Presets;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Setup;
using KoeNote.App.Services.SystemStatus;
using KoeNote.App.Services.Transcript;
using KoeNote.App.Services.Updates;

namespace KoeNote.App.ViewModels;

public sealed record MainWindowServices(
    JobRepository JobRepository,
    JobLogRepository JobLogRepository,
    AsrSettingsRepository AsrSettingsRepository,
    TranscriptSegmentRepository TranscriptSegmentRepository,
    TranscriptDerivativeRepository TranscriptDerivativeRepository,
    AsrEngineRegistry AsrEngineRegistry,
    JobRunCoordinator JobRunCoordinator,
    CorrectionDraftRepository CorrectionDraftRepository,
    ReviewOperationService ReviewOperationService,
    TranscriptEditService TranscriptEditService,
    CorrectionMemoryService CorrectionMemoryService,
    TranscriptExportService TranscriptExportService,
    JobLogExportService JobLogExportService,
    TranscriptSummaryService TranscriptSummaryService,
    ModelCatalogService ModelCatalogService,
    InstalledModelRepository InstalledModelRepository,
    ModelDownloadJobRepository ModelDownloadJobRepository,
    ModelInstallService ModelInstallService,
    ModelDownloadService ModelDownloadService,
    ModelLicenseViewer ModelLicenseViewer,
    SetupWizardService SetupWizardService,
    FasterWhisperRuntimeService FasterWhisperRuntimeService,
    DiarizationRuntimeService DiarizationRuntimeService,
    IAudioPlaybackService AudioPlaybackService,
    ToolStatusService ToolStatusService,
    StatusBarInfoService StatusBarInfoService,
    DatabaseMaintenanceService DatabaseMaintenanceService,
    IUpdateCheckService UpdateCheckService,
    IUpdateDownloadService UpdateDownloadService,
    IUpdateInstallerLauncher UpdateInstallerLauncher,
    IUpdateHistoryService UpdateHistoryService,
    DomainPresetImportService DomainPresetImportService,
    LlmSettingsSeedService LlmSettingsSeedService,
    LlmSettingsDisplayService LlmSettingsDisplayService)
{
    public static MainWindowServices Create(AppPaths paths)
    {
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();

        var repositories = MainWindowRepositoryComposition.Create(paths);
        var runtime = MainWindowRuntimeComposition.Create(paths);
        var model = MainWindowModelComposition.Create(paths);
        var llmSettingsRepository = new LlmSettingsRepository(paths);
        var llmSettingsSeedService = new LlmSettingsSeedService(
            paths,
            model.ModelCatalogService,
            model.InstalledModelRepository,
            new SetupStateService(paths),
            llmSettingsRepository);
        llmSettingsSeedService.EnsureActiveProfileFromSetup();
        var review = MainWindowReviewComposition.Create(paths);
        var setupWizardService = MainWindowSetupComposition.Create(paths, runtime, model);
        var workers = MainWindowWorkerComposition.Create(
            paths,
            runtime.ProcessRunner,
            repositories,
            review.CorrectionMemoryService);
        var asrEngineRegistry = MainWindowAsrEngineComposition.Create(
            paths,
            runtime.ProcessRunner,
            workers.AsrResultStore,
            repositories.TranscriptSegmentRepository);
        var jobRunCoordinator = MainWindowJobRunComposition.Create(
            paths,
            repositories,
            model,
            review,
            workers,
            asrEngineRegistry,
            runtime.ProcessRunner);

        return new MainWindowServices(
            repositories.JobRepository,
            repositories.JobLogRepository,
            repositories.AsrSettingsRepository,
            repositories.TranscriptSegmentRepository,
            repositories.TranscriptDerivativeRepository,
            asrEngineRegistry,
            jobRunCoordinator,
            repositories.CorrectionDraftRepository,
            review.ReviewOperationService,
            review.TranscriptEditService,
            review.CorrectionMemoryService,
            review.TranscriptExportService,
            new JobLogExportService(paths, repositories.JobLogRepository),
            review.TranscriptSummaryService,
            model.ModelCatalogService,
            model.InstalledModelRepository,
            model.ModelDownloadJobRepository,
            model.ModelInstallService,
            model.ModelDownloadService,
            model.ModelLicenseViewer,
            setupWizardService,
            runtime.FasterWhisperRuntimeService,
            runtime.DiarizationRuntimeService,
            runtime.AudioPlaybackService,
            runtime.ToolStatusService,
            runtime.StatusBarInfoService,
            runtime.DatabaseMaintenanceService,
            runtime.UpdateCheckService,
            runtime.UpdateDownloadService,
            runtime.UpdateInstallerLauncher,
            runtime.UpdateHistoryService,
            new DomainPresetImportService(paths, repositories.AsrSettingsRepository),
            llmSettingsSeedService,
            new LlmSettingsDisplayService(llmSettingsRepository));
    }
}
