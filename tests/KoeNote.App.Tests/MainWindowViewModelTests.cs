using System.Collections;
using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Setup;
using KoeNote.App.Services.Review;
using KoeNote.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task SelectedAsrEngine_RestoresAfterRecreatingViewModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        var first = new MainWindowViewModel(paths)
        {
            SelectedAsrEngineId = "faster-whisper-large-v3-turbo"
        };
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        var second = new MainWindowViewModel(paths);

        Assert.Equal(first.SelectedAsrEngineId, second.SelectedAsrEngineId);
        Assert.Contains(second.AvailableAsrEngines, engine => engine.EngineId == "reazonspeech-k2-v3");
    }

    [Fact]
    public void AsrModel_ReflectsSelectedAsrEngine()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedAsrEngineId = "faster-whisper-large-v3-turbo";

        Assert.Contains("faster-whisper", viewModel.AsrModel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AsrModel_DistinguishesFasterWhisperLargeV3FromTurbo()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedAsrEngineId = "faster-whisper-large-v3";

        Assert.Equal("faster-whisper large-v3", viewModel.AsrModel);
    }

    [Fact]
    public void JobSearchText_FiltersJobsByTitleFileNameAndStatus()
    {
        var viewModel = CreateViewModel();
        viewModel.Jobs.Clear();
        viewModel.Jobs.Add(new JobSummary("job-001", "企画会議", "planning.wav", @"C:\audio\planning.wav", "登録済み", 0, 0, DateTimeOffset.Now));
        viewModel.Jobs.Add(new JobSummary("job-002", "レビュー会議", "review.wav", @"C:\audio\review.wav", "キャンセル済み", 10, 0, DateTimeOffset.Now));

        viewModel.JobSearchText = "キャンセル";

        var jobs = ViewItems<JobSummary>(viewModel.FilteredJobs);
        var job = Assert.Single(jobs);
        Assert.Equal("job-002", job.JobId);

        viewModel.JobSearchText = "planning";

        jobs = ViewItems<JobSummary>(viewModel.FilteredJobs);
        job = Assert.Single(jobs);
        Assert.Equal("job-001", job.JobId);
    }

    [Fact]
    public void SegmentSearchAndSpeakerFilter_CombinePredicates()
    {
        var viewModel = CreateViewModel();

        viewModel.SegmentSearchText = "サーバー";
        viewModel.SelectedSpeakerFilter = "Speaker_1";

        var segments = ViewItems<TranscriptSegmentPreview>(viewModel.FilteredSegments);
        var segment = Assert.Single(segments);
        Assert.Equal("Speaker_1", segment.Speaker);
        Assert.Contains("サーバー", segment.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AsrSettings_RestoreAfterRecreatingViewModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        var first = new MainWindowViewModel(paths)
        {
            AsrContextText = "製品開発会議",
            AsrHotwordsText = "KoeNote\r\nRTX 3060",
            EnableReviewStage = false
        };
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        var second = new MainWindowViewModel(paths);

        Assert.Equal(first.AsrContextText, second.AsrContextText);
        Assert.Equal(first.AsrHotwordsText, second.AsrHotwordsText);
        Assert.False(second.EnableReviewStage);
    }

    [Fact]
    public async Task ReviewQueue_LoadsSelectedDraftAndAcceptCommandAdvances()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "ミギワ"),
            new TranscriptSegment("segment-002", job.JobId, 2, 3, "Speaker_1", "公正")
        ]);
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", job.JobId, "segment-001", "意味不明語", "ミギワ", "右側", "候補", 0.8),
            new CorrectionDraft("draft-002", job.JobId, "segment-002", "同音異義語", "公正", "構成", "候補", 0.7)
        ]);

        var viewModel = new MainWindowViewModel(paths);

        Assert.Equal("draft-001", viewModel.SelectedCorrectionDraftId);
        Assert.Equal("1 / 2", viewModel.DraftPositionText);
        Assert.Equal("segment-001", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal(viewModel.SelectedSegment?.Text, viewModel.SelectedSegmentEditText);
        Assert.True(viewModel.AcceptDraftCommand.CanExecute(null));
        var initialFocusRequest = viewModel.ReviewSegmentFocusRequestId;
        viewModel.SegmentSearchText = "hidden-segment";
        Assert.Empty(ViewItems<TranscriptSegmentPreview>(viewModel.FilteredSegments));

        viewModel.FocusSelectedDraftSegmentCommand.Execute(null);

        Assert.Empty(viewModel.SegmentSearchText);
        Assert.Contains(viewModel.SelectedSegment, ViewItems<TranscriptSegmentPreview>(viewModel.FilteredSegments));

        viewModel.AcceptDraftCommand.Execute(null);
        Assert.False(viewModel.SelectNextDraftCommand.CanExecute(null));
        for (var i = 0; i < 20 && viewModel.SelectedCorrectionDraftId == "draft-001"; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.True(
            viewModel.SelectedCorrectionDraftId == "draft-002",
            $"Expected draft-002 but was {viewModel.SelectedCorrectionDraftId}. InProgress: {viewModel.IsReviewOperationInProgress}. LatestLog: {viewModel.LatestLog}");
        Assert.Equal("1 / 1", viewModel.DraftPositionText);
        Assert.Equal("segment-002", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal(viewModel.SelectedSegment?.Text, viewModel.SelectedSegmentEditText);
        Assert.True(viewModel.ReviewSegmentFocusRequestId > initialFocusRequest);
        Assert.True(
            viewModel.SelectedJobUnreviewedDrafts == 1,
            $"Expected one pending draft but saw {viewModel.SelectedJobUnreviewedDrafts}. LatestLog: {viewModel.LatestLog}");
    }

    [Fact]
    public void ReviewPane_StartsEmptyWhenThereAreNoDrafts()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.HasReviewDraft);
        Assert.Equal("候補なし", viewModel.ReviewIssueType);
        Assert.Empty(viewModel.OriginalText);
        Assert.Empty(viewModel.SuggestedText);
        Assert.Equal("推敲候補はありません。", viewModel.ReviewReason);
        Assert.Equal(0, viewModel.Confidence);
        Assert.Equal("0 / 0", viewModel.DraftPositionText);
    }

    [Fact]
    public async Task AcceptDraft_RemembersSuggestedFragmentInsteadOfWholeSegment()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "今日は旧サービス名を確認します")
        ]);
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", job.JobId, "segment-001", "wording", "旧サービス名", "KoeNote", "suggestion", 0.8)
        ]);

        var viewModel = new MainWindowViewModel(paths);
        viewModel.AcceptDraftCommand.Execute(null);
        for (var i = 0; i < 20 && viewModel.SelectedCorrectionDraftId == "draft-001"; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT wrong_text, correct_text
            FROM correction_memory;
            """;

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("旧サービス名", reader.GetString(0));
        Assert.Equal("KoeNote", reader.GetString(1));
    }

    [Fact]
    public void ExportSelectedJobToFolder_RecordsErrorWithoutThrowing()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "raw")
        ]);

        var viewModel = new MainWindowViewModel(paths);

        viewModel.ExportSelectedJobToFolder(string.Empty);

        Assert.StartsWith("Export failed:", viewModel.ExportWarning, StringComparison.Ordinal);
        Assert.StartsWith("Export failed:", viewModel.LatestLog, StringComparison.Ordinal);
        Assert.Contains(viewModel.Logs, entry => entry.Stage == "export" && entry.Level == "error");
    }

    [Fact]
    public void FormatExportCommands_EnableWhenSelectedJobHasSegments()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "raw")
        ]);

        var viewModel = new MainWindowViewModel(paths);

        Assert.True(viewModel.ExportTxtCommand.CanExecute(null));
        Assert.True(viewModel.ExportJsonCommand.CanExecute(null));
        Assert.True(viewModel.ExportSrtCommand.CanExecute(null));
        Assert.True(viewModel.ExportDocxCommand.CanExecute(null));
    }

    [Fact]
    public void IncludeExportTimestamps_CanBeToggled()
    {
        var viewModel = CreateViewModel();

        viewModel.IncludeExportTimestamps = false;

        Assert.False(viewModel.IncludeExportTimestamps);
    }

    [Fact]
    public void FirstRunSummary_ReportsMissingRuntimeAssets()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));

        var viewModel = new MainWindowViewModel(paths);

        Assert.Contains("初回チェック:", viewModel.FirstRunSummary, StringComparison.Ordinal);
        Assert.Contains("ASR model", viewModel.FirstRunDetail, StringComparison.Ordinal);
        Assert.Contains(paths.VibeVoiceAsrModelPath, viewModel.FirstRunDetail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("セットアップを開く / モデル導入へ", viewModel.FirstRunDetail, StringComparison.Ordinal);
        Assert.False(viewModel.RequiredRuntimeAssetsReady);
        Assert.False(viewModel.RunSelectedJobCommand.CanExecute(null));
    }

    [Fact]
    public void OpenSetupCommand_ReportsPlaceholderGuidance()
    {
        var viewModel = CreateViewModel();
        viewModel.CloseSetupWizardModalCommand.Execute(null);

        viewModel.OpenSetupCommand.Execute(null);

        Assert.Contains("Model Catalog", viewModel.LatestLog, StringComparison.Ordinal);
        Assert.Contains("Setup Wizard", viewModel.LatestLog, StringComparison.Ordinal);
        Assert.True(viewModel.IsSetupWizardModalOpen);
        Assert.False(viewModel.IsDetailPanelOpen);
        Assert.Contains("KoeNote", viewModel.SetupWizardModalTitle, StringComparison.Ordinal);
    }

    [Fact]
    public void HeaderNavigationCommands_SelectExpectedLogPanelTabs()
    {
        var viewModel = CreateViewModel();

        viewModel.OpenSettingsCommand.Execute(null);
        Assert.Equal(1, viewModel.SelectedLogPanelTabIndex);
        Assert.Equal(0, viewModel.SelectedDetailPanelTabIndex);
        Assert.True(viewModel.IsDetailPanelOpen);

        viewModel.ShowModelCatalogCommand.Execute(null);
        Assert.Equal(3, viewModel.SelectedLogPanelTabIndex);
        Assert.Equal(2, viewModel.SelectedDetailPanelTabIndex);

        viewModel.OpenSetupCommand.Execute(null);
        Assert.True(viewModel.IsSetupWizardModalOpen);
    }

    [Fact]
    public void SetupWizardModal_OpensOnIncompleteSetupAndCanBeDismissed()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.IsSetupComplete);
        Assert.True(viewModel.IsSetupWizardModalOpen);

        viewModel.CloseSetupWizardModalCommand.Execute(null);

        Assert.False(viewModel.IsSetupWizardModalOpen);
        Assert.False(viewModel.RunSelectedJobCommand.CanExecute(null));
    }

    [Fact]
    public void SetupWizardModal_DoesNotAutoOpenAfterSetupIsComplete()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            CurrentStep = SetupStep.Complete,
            LastSmokeSucceeded = true,
            LicenseAccepted = true
        });

        var viewModel = new MainWindowViewModel(paths);

        Assert.True(viewModel.IsSetupComplete);
        Assert.False(viewModel.IsSetupWizardModalOpen);

        viewModel.OpenSetupCommand.Execute(null);

        Assert.True(viewModel.IsSetupWizardModalOpen);
        Assert.False(viewModel.IsDetailPanelOpen);
    }

    [Fact]
    public void DetailPanelCommands_OpenAndCloseWidePanel()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.OpenSelectedDetailPanelCommand.CanExecute(null));

        viewModel.SelectedLogPanelTabIndex = 3;
        Assert.True(viewModel.OpenSelectedDetailPanelCommand.CanExecute(null));
        viewModel.OpenSelectedDetailPanelCommand.Execute(null);

        Assert.True(viewModel.IsDetailPanelOpen);
        Assert.Equal(2, viewModel.SelectedDetailPanelTabIndex);
        Assert.Equal("Models", viewModel.DetailPanelTitle);

        viewModel.CloseDetailPanelCommand.Execute(null);
        Assert.False(viewModel.IsDetailPanelOpen);
    }

    [Fact]
    public void PlayPauseAudioCommand_IsDisabledWhenNoPlayableAudioIsSelected()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.PlayPauseAudioCommand.CanExecute(null));
        Assert.Empty(viewModel.SelectedJobPlaybackPath);
    }

    [Fact]
    public void PlayPauseAudioCommand_UsesSelectedJobSourceAudioWhenNormalizedAudioIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        var viewModel = new MainWindowViewModel(new AppPaths(root, root, AppContext.BaseDirectory));
        var job = new JobSummary("job-001", "meeting", "meeting.wav", audioPath, "registered", 0, 0, DateTimeOffset.Now);

        viewModel.Jobs.Add(job);
        viewModel.SelectedJob = job;

        Assert.True(viewModel.PlayPauseAudioCommand.CanExecute(null));
        Assert.Equal(audioPath, viewModel.SelectedJobPlaybackPath);
        Assert.Equal("\uE768", viewModel.PlayPauseAudioIcon);
        Assert.Equal("00:00 / 00:00", viewModel.PlaybackTimeDisplay);
    }

    [Fact]
    public void PlaybackRate_UpdatesSelectedSpeed()
    {
        var viewModel = CreateViewModel();

        viewModel.PlaybackRate = 1.5;

        Assert.Equal(1.5, viewModel.PlaybackRate);
        Assert.Contains(2.0, viewModel.PlaybackRates);
    }

    [Fact]
    public void SelectingSegment_SeeksPlaybackPositionToSegmentStart()
    {
        var viewModel = CreateViewModel();
        var segment = new TranscriptSegmentPreview(
            "00:01:12.340",
            "00:01:20.000",
            "Speaker_0",
            "target",
            "",
            "segment-001",
            StartSeconds: 72.34,
            EndSeconds: 80);

        viewModel.SelectedSegment = segment;

        Assert.Equal(72.34, viewModel.PlaybackPositionSeconds, 2);
    }

    [Fact]
    public void RunSelectedJobCommand_IsEnabledWhenJobAndRuntimeAssetsAreReady()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        Touch(paths.FfmpegPath);
        Touch(paths.CrispAsrPath);
        Touch(paths.LlamaCompletionPath);
        Touch(paths.VibeVoiceAsrModelPath);
        Touch(paths.ReviewModelPath);
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            LicenseAccepted = true
        });

        var viewModel = new MainWindowViewModel(paths);
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", Path.Combine(root, "meeting.wav"), "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];

        Assert.True(viewModel.RequiredRuntimeAssetsReady);
        Assert.True(viewModel.RunSelectedJobCommand.CanExecute(null));
    }

    [Fact]
    public void RunSelectedJobCommand_DoesNotRequireReviewModelWhenReviewStageIsDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.CrispAsrPath);
        Touch(paths.VibeVoiceAsrModelPath);
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, VibeVoiceCrispAsrEngine.Id, false));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            LicenseAccepted = true
        });

        var viewModel = new MainWindowViewModel(paths);
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", Path.Combine(root, "meeting.wav"), "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];

        Assert.False(viewModel.EnableReviewStage);
        Assert.True(viewModel.RequiredRuntimeAssetsReady);
        Assert.True(viewModel.RunSelectedJobCommand.CanExecute(null));
    }

    [Fact]
    public void RunSelectedJobCommand_AcceptsInstalledUserReviewModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        Touch(paths.FasterWhisperScriptPath);
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var installService = new ModelInstallService(paths, new InstalledModelRepository(paths), new ModelVerificationService());
        var asrItem = catalog.Models.First(model => model.ModelId == "faster-whisper-large-v3");
        var asrPath = Path.Combine(paths.UserModels, "asr", "faster-whisper-large-v3");
        Directory.CreateDirectory(asrPath);
        File.WriteAllText(Path.Combine(asrPath, "model.bin"), "asr");
        installService.RegisterLocalModel(asrItem, asrPath, "download");
        var reviewItem = catalog.Models.First(model => model.ModelId == "llm-jp-4-8b-thinking-q4-k-m");
        var reviewPath = installService.GetDefaultInstallPath(reviewItem);
        Touch(reviewPath);
        installService.RegisterLocalModel(reviewItem, reviewPath, "download");
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "faster-whisper-large-v3"));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            LicenseAccepted = true,
            SelectedAsrModelId = asrItem.ModelId,
            SelectedReviewModelId = reviewItem.ModelId
        });

        var viewModel = new MainWindowViewModel(paths);
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", Path.Combine(root, "meeting.wav"), "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];

        Assert.True(viewModel.RequiredRuntimeAssetsReady);
        Assert.True(viewModel.RunSelectedJobCommand.CanExecute(null));
    }

    private static MainWindowViewModel CreateViewModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        return new MainWindowViewModel(new AppPaths(root, root, AppContext.BaseDirectory));
    }

    private static List<T> ViewItems<T>(IEnumerable view)
    {
        return view.Cast<T>().ToList();
    }

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
    }
}
