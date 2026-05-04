using System.Net.Http;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Audio;
using KoeNote.App.Services.Export;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Models;
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
    IAudioPlaybackService AudioPlaybackService,
    ToolStatusService ToolStatusService,
    StatusBarInfoService StatusBarInfoService,
    DatabaseMaintenanceService DatabaseMaintenanceService)
{
    public static MainWindowServices Create(AppPaths paths)
    {
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();

        var jobRepository = new JobRepository(paths);
        var stageProgressRepository = new StageProgressRepository(paths);
        var jobLogRepository = new JobLogRepository(paths);
        var asrSettingsRepository = new AsrSettingsRepository(paths);
        var correctionDraftRepository = new CorrectionDraftRepository(paths);
        var reviewOperationService = new ReviewOperationService(paths);
        var transcriptEditService = new TranscriptEditService(paths);
        var correctionMemoryService = new CorrectionMemoryService(paths);
        var transcriptExportService = new TranscriptExportService(paths);
        var modelCatalogService = new ModelCatalogService(paths);
        var installedModelRepository = new InstalledModelRepository(paths);
        var modelDownloadJobRepository = new ModelDownloadJobRepository(paths);
        var modelVerificationService = new ModelVerificationService();
        var modelInstallService = new ModelInstallService(paths, installedModelRepository, modelVerificationService);
        var modelLicenseViewer = new ModelLicenseViewer(modelCatalogService);
        var modelPackImportService = new ModelPackImportService(paths, modelCatalogService, modelInstallService);
        var modelDownloadService = new ModelDownloadService(
            new HttpClient(),
            modelDownloadJobRepository,
            modelVerificationService,
            modelInstallService);
        var setupStateService = new SetupStateService(paths);
        var toolStatusService = new ToolStatusService(paths);
        var audioPlaybackService = new AudioPlaybackService();
        var setupWizardService = new SetupWizardService(
            paths,
            setupStateService,
            toolStatusService,
            modelCatalogService,
            installedModelRepository,
            modelInstallService,
            modelPackImportService,
            modelDownloadService);
        var transcriptSegmentRepository = new TranscriptSegmentRepository(paths);
        var processRunner = new ExternalProcessRunner();
        var audioPreprocessWorker = new AudioPreprocessWorker(processRunner, stageProgressRepository, jobLogRepository);
        var asrWorker = new AsrWorker(
            processRunner,
            new AsrCommandBuilder(),
            new AsrJsonNormalizer(),
            new AsrResultStore(),
            transcriptSegmentRepository);
        var reviewWorker = new ReviewWorker(
            processRunner,
            new ReviewCommandBuilder(),
            new ReviewPromptBuilder(),
            new ReviewJsonNormalizer(),
            new ReviewResultStore(),
            correctionDraftRepository,
            correctionMemoryService);
        var asrEngineRegistry = new AsrEngineRegistry([
            new VibeVoiceCrispAsrEngine(asrWorker, new AsrRunRepository(paths)),
            new ScriptedJsonAsrEngine(
                "faster-whisper-large-v3-turbo",
                "faster-whisper large-v3-turbo",
                "faster-whisper",
                processRunner,
                new AsrJsonNormalizer(),
                new AsrResultStore(),
                transcriptSegmentRepository,
                new AsrRunRepository(paths)),
            new ScriptedJsonAsrEngine(
                "faster-whisper-large-v3",
                "faster-whisper large-v3",
                "faster-whisper",
                processRunner,
                new AsrJsonNormalizer(),
                new AsrResultStore(),
                transcriptSegmentRepository,
                new AsrRunRepository(paths)),
            new ScriptedJsonAsrEngine(
                "reazonspeech-k2-v3",
                "ReazonSpeech v3 k2",
                "reazonspeech-k2",
                processRunner,
                new AsrJsonNormalizer(),
                new AsrResultStore(),
                transcriptSegmentRepository,
                new AsrRunRepository(paths))
        ]);
        var jobRunCoordinator = new JobRunCoordinator(
            paths,
            jobRepository,
            stageProgressRepository,
            jobLogRepository,
            audioPreprocessWorker,
            asrEngineRegistry,
            installedModelRepository,
            reviewWorker,
            correctionMemoryService);
        var statusBarInfoService = new StatusBarInfoService(paths);
        var databaseMaintenanceService = new DatabaseMaintenanceService(paths);

        return new MainWindowServices(
            jobRepository,
            jobLogRepository,
            asrSettingsRepository,
            transcriptSegmentRepository,
            asrEngineRegistry,
            jobRunCoordinator,
            correctionDraftRepository,
            reviewOperationService,
            transcriptEditService,
            correctionMemoryService,
            transcriptExportService,
            modelCatalogService,
            installedModelRepository,
            modelDownloadJobRepository,
            modelInstallService,
            modelDownloadService,
            modelLicenseViewer,
            setupWizardService,
            audioPlaybackService,
            toolStatusService,
            statusBarInfoService,
            databaseMaintenanceService);
    }
}
