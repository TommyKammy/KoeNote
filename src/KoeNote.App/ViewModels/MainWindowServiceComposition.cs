using System.Net.Http;
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
using KoeNote.App.Services.Transcript;
using KoeNote.App.Services.Updates;

namespace KoeNote.App.ViewModels;

internal sealed record MainWindowRepositoryServices(
    JobRepository JobRepository,
    StageProgressRepository StageProgressRepository,
    JobLogRepository JobLogRepository,
    AsrSettingsRepository AsrSettingsRepository,
    CorrectionDraftRepository CorrectionDraftRepository,
    TranscriptSegmentRepository TranscriptSegmentRepository,
    TranscriptDerivativeRepository TranscriptDerivativeRepository,
    TranscriptReadRepository TranscriptReadRepository);

internal static class MainWindowRepositoryComposition
{
    public static MainWindowRepositoryServices Create(AppPaths paths)
    {
        return new MainWindowRepositoryServices(
            new JobRepository(paths),
            new StageProgressRepository(paths),
            new JobLogRepository(paths),
            new AsrSettingsRepository(paths),
            new CorrectionDraftRepository(paths),
            new TranscriptSegmentRepository(paths),
            new TranscriptDerivativeRepository(paths),
            new TranscriptReadRepository(paths));
    }
}

internal sealed record MainWindowRuntimeServices(
    ExternalProcessRunner ProcessRunner,
    ToolStatusService ToolStatusService,
    FasterWhisperRuntimeService FasterWhisperRuntimeService,
    DiarizationRuntimeService DiarizationRuntimeService,
    IAudioPlaybackService AudioPlaybackService,
    StatusBarInfoService StatusBarInfoService,
    DatabaseMaintenanceService DatabaseMaintenanceService,
    IUpdateCheckService UpdateCheckService,
    IUpdateDownloadService UpdateDownloadService,
    IUpdateInstallerLauncher UpdateInstallerLauncher,
    IUpdateHistoryService UpdateHistoryService);

internal static class MainWindowRuntimeComposition
{
    public static MainWindowRuntimeServices Create(AppPaths paths)
    {
        var processRunner = new ExternalProcessRunner();
        var updateHttpClient = new HttpClient();
        return new MainWindowRuntimeServices(
            processRunner,
            new ToolStatusService(paths),
            new FasterWhisperRuntimeService(paths, processRunner),
            new DiarizationRuntimeService(paths, processRunner),
            new AudioPlaybackService(),
            new StatusBarInfoService(paths),
            new DatabaseMaintenanceService(paths),
            new UpdateCheckService(updateHttpClient, UpdateCheckOptions.FromEnvironment()),
            new UpdateDownloadService(updateHttpClient, paths),
            new UpdateInstallerLauncher(),
            new UpdateHistoryService(paths));
    }
}

internal sealed record MainWindowModelServices(
    ModelCatalogService ModelCatalogService,
    InstalledModelRepository InstalledModelRepository,
    ModelDownloadJobRepository ModelDownloadJobRepository,
    ModelInstallService ModelInstallService,
    ModelDownloadService ModelDownloadService,
    ModelLicenseViewer ModelLicenseViewer,
    ModelPackImportService ModelPackImportService);

internal static class MainWindowModelComposition
{
    public static MainWindowModelServices Create(AppPaths paths)
    {
        var modelCatalogService = new ModelCatalogService(paths);
        var installedModelRepository = new InstalledModelRepository(paths);
        var modelDownloadJobRepository = new ModelDownloadJobRepository(paths);
        var modelVerificationService = new ModelVerificationService();
        var modelInstallService = new ModelInstallService(paths, installedModelRepository, modelVerificationService);
        var modelDownloadService = new ModelDownloadService(
            new HttpClient(),
            modelDownloadJobRepository,
            modelVerificationService,
            modelInstallService);

        return new MainWindowModelServices(
            modelCatalogService,
            installedModelRepository,
            modelDownloadJobRepository,
            modelInstallService,
            modelDownloadService,
            new ModelLicenseViewer(modelCatalogService),
            new ModelPackImportService(paths, modelCatalogService, modelInstallService));
    }
}

internal sealed record MainWindowReviewServices(
    ReviewOperationService ReviewOperationService,
    TranscriptEditService TranscriptEditService,
    CorrectionMemoryService CorrectionMemoryService,
    TranscriptExportService TranscriptExportService,
    TranscriptSummaryService TranscriptSummaryService);

internal static class MainWindowReviewComposition
{
    public static MainWindowReviewServices Create(AppPaths paths)
    {
        var derivativeRepository = new TranscriptDerivativeRepository(paths);
        return new MainWindowReviewServices(
            new ReviewOperationService(paths),
            new TranscriptEditService(paths),
            new CorrectionMemoryService(paths),
            new TranscriptExportService(paths),
            new TranscriptSummaryService(
                new TranscriptReadRepository(paths),
                derivativeRepository,
                new LlamaTranscriptSummaryRuntime(new ExternalProcessRunner(), new TranscriptSummaryPromptBuilder())));
    }
}

internal static class MainWindowSetupComposition
{
    public static SetupWizardService Create(
        AppPaths paths,
        MainWindowRuntimeServices runtime,
        MainWindowModelServices model)
    {
        return new SetupWizardService(
            paths,
            new SetupStateService(paths),
            runtime.ToolStatusService,
            model.ModelCatalogService,
            model.InstalledModelRepository,
            model.ModelInstallService,
            model.ModelPackImportService,
            model.ModelDownloadService,
            runtime.FasterWhisperRuntimeService,
            runtime.DiarizationRuntimeService);
    }
}

internal sealed record MainWindowWorkerServices(
    AudioPreprocessWorker AudioPreprocessWorker,
    AsrResultStore AsrResultStore,
    ReviewWorker ReviewWorker);

internal static class MainWindowWorkerComposition
{
    public static MainWindowWorkerServices Create(
        AppPaths paths,
        ExternalProcessRunner processRunner,
        MainWindowRepositoryServices repositories,
        CorrectionMemoryService correctionMemoryService)
    {
        var asrResultStore = new AsrResultStore();
        return new MainWindowWorkerServices(
            new AudioPreprocessWorker(processRunner, repositories.StageProgressRepository, repositories.JobLogRepository),
            asrResultStore,
            new ReviewWorker(
                processRunner,
                new ReviewCommandBuilder(),
                new ReviewPromptBuilder(new ReviewGuidelineRepository(paths)),
                new ReviewJsonNormalizer(),
                new ReviewResultStore(),
                repositories.CorrectionDraftRepository,
                correctionMemoryService));
    }
}

internal static class MainWindowAsrEngineComposition
{
    public static AsrEngineRegistry Create(
        AppPaths paths,
        ExternalProcessRunner processRunner,
        AsrResultStore asrResultStore,
        TranscriptSegmentRepository transcriptSegmentRepository)
    {
        var asrJsonNormalizer = new AsrJsonNormalizer();
        return new AsrEngineRegistry([
            CreateScriptedEngine(
                paths,
                processRunner,
                asrJsonNormalizer,
                asrResultStore,
                transcriptSegmentRepository,
                "kotoba-whisper-v2.2-faster",
                "kotoba-whisper v2.2 faster",
                "faster-whisper"),
            CreateScriptedEngine(
                paths,
                processRunner,
                asrJsonNormalizer,
                asrResultStore,
                transcriptSegmentRepository,
                "whisper-base",
                "Whisper base",
                "faster-whisper"),
            CreateScriptedEngine(
                paths,
                processRunner,
                asrJsonNormalizer,
                asrResultStore,
                transcriptSegmentRepository,
                "whisper-small",
                "Whisper small",
                "faster-whisper"),
            CreateScriptedEngine(
                paths,
                processRunner,
                asrJsonNormalizer,
                asrResultStore,
                transcriptSegmentRepository,
                "faster-whisper-large-v3-turbo",
                "faster-whisper large-v3-turbo",
                "faster-whisper"),
            CreateScriptedEngine(
                paths,
                processRunner,
                asrJsonNormalizer,
                asrResultStore,
                transcriptSegmentRepository,
                "faster-whisper-large-v3",
                "faster-whisper large-v3",
                "faster-whisper"),
            CreateScriptedEngine(
                paths,
                processRunner,
                asrJsonNormalizer,
                asrResultStore,
                transcriptSegmentRepository,
                "reazonspeech-k2-v3",
                "ReazonSpeech v3 k2",
                "reazonspeech-k2")
        ]);
    }

    private static ScriptedJsonAsrEngine CreateScriptedEngine(
        AppPaths paths,
        ExternalProcessRunner processRunner,
        AsrJsonNormalizer asrJsonNormalizer,
        AsrResultStore asrResultStore,
        TranscriptSegmentRepository transcriptSegmentRepository,
        string engineId,
        string displayName,
        string runtimeId)
    {
        return new ScriptedJsonAsrEngine(
            engineId,
            displayName,
            runtimeId,
            processRunner,
            asrJsonNormalizer,
            asrResultStore,
            transcriptSegmentRepository,
            new AsrRunRepository(paths));
    }
}

internal static class MainWindowJobRunComposition
{
    public static JobRunCoordinator Create(
        AppPaths paths,
        MainWindowRepositoryServices repositories,
        MainWindowModelServices model,
        MainWindowReviewServices review,
        MainWindowWorkerServices workers,
        AsrEngineRegistry asrEngineRegistry,
        ExternalProcessRunner processRunner)
    {
        var diarizationService = new ScriptedDiarizationService(
            paths,
            processRunner,
            new DiarizationJsonNormalizer(),
            new DiarizationSegmentAssigner(),
            repositories.TranscriptSegmentRepository,
            workers.AsrResultStore);

        return new JobRunCoordinator(
            new PreprocessStageRunner(
                paths,
                repositories.JobRepository,
                repositories.JobLogRepository,
                workers.AudioPreprocessWorker),
            new AsrStageRunner(
                paths,
                repositories.JobRepository,
                repositories.StageProgressRepository,
                repositories.JobLogRepository,
                asrEngineRegistry,
                model.InstalledModelRepository,
                diarizationService,
                review.CorrectionMemoryService),
            new ReviewStageRunner(
                paths,
                repositories.JobRepository,
                repositories.StageProgressRepository,
                repositories.JobLogRepository,
                model.InstalledModelRepository,
                new SetupStateService(paths),
                workers.ReviewWorker),
            new SummaryStageRunner(
                paths,
                repositories.JobRepository,
                repositories.StageProgressRepository,
                repositories.JobLogRepository,
                model.InstalledModelRepository,
                new SetupStateService(paths),
                review.TranscriptSummaryService));
    }
}
