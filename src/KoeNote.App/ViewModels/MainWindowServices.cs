using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Audio;
using KoeNote.App.Services.Diarization;
using KoeNote.App.Services.Export;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Presets;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Setup;
using KoeNote.App.Services.SystemStatus;

namespace KoeNote.App.ViewModels;

public sealed record MainWindowServices(
    JobRepository JobRepository,
    JobLogRepository JobLogRepository,
    AsrSettingsRepository AsrSettingsRepository,
    TranscriptSegmentRepository TranscriptSegmentRepository,
    AsrEngineRegistry AsrEngineRegistry,
    JobRunCoordinator JobRunCoordinator,
    CorrectionDraftRepository CorrectionDraftRepository,
    ReviewOperationService ReviewOperationService,
    TranscriptEditService TranscriptEditService,
    CorrectionMemoryService CorrectionMemoryService,
    TranscriptExportService TranscriptExportService,
    ModelCatalogService ModelCatalogService,
    InstalledModelRepository InstalledModelRepository,
    ModelDownloadJobRepository ModelDownloadJobRepository,
    ModelInstallService ModelInstallService,
    ModelDownloadService ModelDownloadService,
    ModelLicenseViewer ModelLicenseViewer,
    SetupWizardService SetupWizardService,
    DiarizationRuntimeService DiarizationRuntimeService,
    IAudioPlaybackService AudioPlaybackService,
    ToolStatusService ToolStatusService,
    StatusBarInfoService StatusBarInfoService,
    DatabaseMaintenanceService DatabaseMaintenanceService,
    DomainPresetImportService DomainPresetImportService)
{
    public static MainWindowServices Create(AppPaths paths)
    {
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();

        var repositories = MainWindowRepositoryComposition.Create(paths);
        var runtime = MainWindowRuntimeComposition.Create(paths);
        var model = MainWindowModelComposition.Create(paths);
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
            workers.AsrWorker,
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
            asrEngineRegistry,
            jobRunCoordinator,
            repositories.CorrectionDraftRepository,
            review.ReviewOperationService,
            review.TranscriptEditService,
            review.CorrectionMemoryService,
            review.TranscriptExportService,
            model.ModelCatalogService,
            model.InstalledModelRepository,
            model.ModelDownloadJobRepository,
            model.ModelInstallService,
            model.ModelDownloadService,
            model.ModelLicenseViewer,
            setupWizardService,
            runtime.DiarizationRuntimeService,
            runtime.AudioPlaybackService,
            runtime.ToolStatusService,
            runtime.StatusBarInfoService,
            runtime.DatabaseMaintenanceService,
            new DomainPresetImportService(paths, repositories.AsrSettingsRepository));
    }
}
