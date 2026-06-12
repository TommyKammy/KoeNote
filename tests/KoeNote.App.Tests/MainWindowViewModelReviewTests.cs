using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Input;
using System.Xml.Linq;
using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Diarization;
using KoeNote.App.Services.Dialogs;
using KoeNote.App.Services.Export;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Setup;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.SystemStatus;
using KoeNote.App.Services.Transcript;
using KoeNote.App.Services.Updates;
using KoeNote.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Tests;

[Trait("Category", "Slow")]
[Trait("Category", "UiIntegration")]
public sealed class MainWindowViewModelReviewTests : MainWindowViewModelTestBase
{
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

        viewModel.AcceptDraftCommand.Execute(null);
        for (var i = 0; i < 20 && viewModel.SelectedCorrectionDraftId == "draft-002"; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.Empty(viewModel.SelectedCorrectionDraftId);
        Assert.Equal(0, viewModel.SelectedJobUnreviewedDrafts);
        Assert.Equal("完成文書作成待ち", viewModel.SelectedJob?.Status);
        Assert.Equal(JobRunProgressPlan.ReviewSucceeded, viewModel.SelectedJob?.ProgressPercent);
        Assert.Equal(3, viewModel.StageStatuses.Count);
        Assert.True(viewModel.StageStatuses.Last().IsToggleable);
    }

    [Fact]
    public async Task RejectDraft_AfterReadablePolishingKeepsSelectedJobCompleted()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var jobRepository = new JobRepository(paths);
        var job = jobRepository.CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "original")
        ]);
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", job.JobId, "segment-001", "wording", "original", "suggested", "suggestion", 0.8)
        ]);
        jobRepository.MarkReadablePolishingSucceeded(job);
        var viewModel = new MainWindowViewModel(paths);

        Assert.Equal("draft-001", viewModel.SelectedCorrectionDraftId);

        viewModel.RejectDraftCommand.Execute(null);
        for (var i = 0; i < 20 && viewModel.SelectedCorrectionDraftId == "draft-001"; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.Empty(viewModel.SelectedCorrectionDraftId);
        Assert.Equal(0, viewModel.SelectedJobUnreviewedDrafts);
        Assert.Equal("整文完了", viewModel.SelectedJob?.Status);
        Assert.Equal(JobRunProgressPlan.Completed, viewModel.SelectedJob?.ProgressPercent);
    }

    [Fact]
    public void ReviewPane_StartsEmptyWhenThereAreNoDrafts()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.HasReviewDraft);
        Assert.Equal("候補なし", viewModel.ReviewIssueType);
        Assert.Empty(viewModel.OriginalText);
        Assert.Empty(viewModel.SuggestedText);
        Assert.Equal("レビュー候補はありません。", viewModel.ReviewReason);
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
    public void PostProcessMenuItems_DisableWhenSelectedJobHasNoTranscriptSegments()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));

        var viewModel = new MainWindowViewModel(paths);

        Assert.False(viewModel.CanRunPostReview);
        Assert.False(viewModel.CanRunPostSummary);
        Assert.False(viewModel.CanRunReadablePolishing);
        Assert.False(viewModel.CanRunPostReviewAndSummary);
        Assert.False(viewModel.RunReadablePolishingCommand.CanExecute(null));
    }

    [Fact]
    public void PostProcessMenuItems_EnableWhenSelectedJobHasTranscriptSegments()
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

        Assert.True(viewModel.CanRunPostReview);
        Assert.True(viewModel.CanRunPostSummary);
        Assert.True(viewModel.CanRunReadablePolishing);
        Assert.True(viewModel.CanRunPostReviewAndSummary);
        Assert.True(viewModel.RunReadablePolishingCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(nameof(MainWindowViewModel.RunPostReviewCommand), PostProcessMode.ReviewOnly)]
    [InlineData(nameof(MainWindowViewModel.RunPostSummaryCommand), PostProcessMode.SummaryOnly)]
    [InlineData(nameof(MainWindowViewModel.RunPostReviewAndSummaryCommand), PostProcessMode.ReviewAndSummary)]
    public void PostProcessCommands_RecordRequestedMode(string commandName, PostProcessMode expectedMode)
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
        var command = (ICommand)typeof(MainWindowViewModel).GetProperty(commandName)!.GetValue(viewModel)!;

        Assert.True(command.CanExecute(null));

        command.Execute(null);

        Assert.Equal(expectedMode, viewModel.LastRequestedPostProcessMode);
    }

    [Fact]
    public void ReadablePolishingState_ShowsRunningActionAndDisablesReadableExports()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "raw")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(paths);
        derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
            job.JobId,
            TranscriptDerivativeKinds.Polished,
            TranscriptDerivativeFormats.PlainText,
            "[00:00 - 00:01] Speaker_0: readable",
            TranscriptDerivativeSourceKinds.Raw,
            derivativeRepository.ComputeCurrentRawTranscriptHash(job.JobId),
            "segment-001..segment-001",
            null,
            "model",
            "prompt",
            "profile"));
        var viewModel = new MainWindowViewModel(paths);

        Assert.True(viewModel.ExportReadablePolishedTxtCommand.CanExecute(null));

        SetPrivateProperty(viewModel, nameof(MainWindowViewModel.IsReadablePolishingInProgress), true);
        SetPrivateProperty(viewModel, nameof(MainWindowViewModel.IsRunInProgress), true);

        Assert.True(viewModel.IsReadablePolishingInProgress);
        Assert.Equal("生成中", viewModel.ReadablePolishedActionText);
        Assert.Contains("生成しています", viewModel.ReadablePolishedActionToolTip, StringComparison.Ordinal);
        Assert.False(viewModel.ExportReadablePolishedTxtCommand.CanExecute(null));
        Assert.False(viewModel.ExportReadablePolishedMarkdownCommand.CanExecute(null));
        Assert.False(viewModel.ExportReadablePolishedDocxCommand.CanExecute(null));
    }

    [Fact]
    public void EnableSummaryStage_RemainsDisabledForManualSummaryFlow()
    {
        var viewModel = CreateViewModel();

        viewModel.EnableSummaryStage = true;

        Assert.False(viewModel.EnableSummaryStage);
    }

    [Fact]
    public void SummaryActionText_ReflectsWhetherSummaryExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "raw")
        ]);

        var viewModelWithoutSummary = new MainWindowViewModel(paths);

        Assert.False(viewModelWithoutSummary.HasSummaryContent);
        Assert.Equal("生成", viewModelWithoutSummary.SummaryActionText);

        new TranscriptDerivativeRepository(paths).Save(new TranscriptDerivativeSaveRequest(
            job.JobId,
            TranscriptDerivativeKinds.Summary,
            TranscriptDerivativeFormats.Markdown,
            "# summary",
            TranscriptDerivativeSourceKinds.Raw,
            "hash",
            "segment-001..segment-001",
            null,
            "model",
            "prompt",
            "profile"));
        var viewModelWithSummary = new MainWindowViewModel(paths);

        Assert.True(viewModelWithSummary.HasSummaryContent);
        Assert.Equal("再生成", viewModelWithSummary.SummaryActionText);
    }

    [Fact]
    public void ReadablePolishedTab_LoadsLatestPolishedDerivativeForSelectedJob()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "raw")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(paths);
        derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
            job.JobId,
            TranscriptDerivativeKinds.Polished,
            TranscriptDerivativeFormats.PlainText,
            """
            # meeting

            。

            Output:
            [00:00 - 00:01] Speaker_0: 読みやすい本文です。
            """,
            TranscriptDerivativeSourceKinds.Raw,
            derivativeRepository.ComputeCurrentRawTranscriptHash(job.JobId),
            "segment-001..segment-001",
            null,
            "model",
            "prompt",
            "profile"));

        var viewModel = new MainWindowViewModel(paths);

        Assert.True(viewModel.HasReadablePolishedContent);
        Assert.Equal("[00:00 - 00:01] Speaker_0: 読みやすい本文です。", viewModel.ReadablePolishedContent);
        Assert.Contains("整文済み", viewModel.ReadablePolishedStatus, StringComparison.Ordinal);
        Assert.Equal("再生成", viewModel.ReadablePolishedActionText);
        Assert.Contains("再生成", viewModel.ReadablePolishedActionToolTip, StringComparison.Ordinal);
        Assert.True(viewModel.CopyReadablePolishedContentCommand.CanExecute(null));
    }

    [Fact]
    public void ReadablePolishedTab_RejectsBrokenSuccessfulDerivative()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "raw")
        ]);
        new TranscriptDerivativeRepository(paths).Save(new TranscriptDerivativeSaveRequest(
            job.JobId,
            TranscriptDerivativeKinds.Polished,
            TranscriptDerivativeFormats.PlainText,
            """
            壊れた出力です。
            �
            同じ行です。
            同じ行です。
            同じ行です。
            同じ行です。
            """,
            TranscriptDerivativeSourceKinds.Raw,
            "hash",
            "segment-001..segment-001",
            null,
            "model",
            "prompt",
            "profile"));

        var viewModel = new MainWindowViewModel(paths);

        Assert.False(viewModel.HasReadablePolishedContent);
        Assert.Contains("破損", viewModel.ReadablePolishedStatus, StringComparison.Ordinal);
        Assert.False(viewModel.CopyReadablePolishedContentCommand.CanExecute(null));
        Assert.False(viewModel.ExportReadablePolishedTxtCommand.CanExecute(null));
    }

    [Fact]
    public void SummaryStatus_BecomesStaleAfterInlineSegmentEdit()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "raw")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(paths);
        derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
            job.JobId,
            TranscriptDerivativeKinds.Summary,
            TranscriptDerivativeFormats.Markdown,
            "# summary",
            TranscriptDerivativeSourceKinds.Raw,
            derivativeRepository.ComputeCurrentRawTranscriptHash(job.JobId),
            "segment-001..segment-001",
            null,
            "model",
            "prompt",
            "profile"));
        derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
            job.JobId,
            TranscriptDerivativeKinds.Polished,
            TranscriptDerivativeFormats.PlainText,
            "[00:00 - 00:01] Speaker_0: readable",
            TranscriptDerivativeSourceKinds.Raw,
            derivativeRepository.ComputeCurrentRawTranscriptHash(job.JobId),
            "segment-001..segment-001",
            null,
            "model",
            "prompt",
            "profile"));
        var viewModel = new MainWindowViewModel(paths);
        var segment = Assert.Single(viewModel.Segments);

        Assert.False(viewModel.IsSummaryStale);
        Assert.Contains("要約済み", viewModel.SummaryStatus, StringComparison.Ordinal);
        Assert.Contains("整文済み", viewModel.ReadablePolishedStatus, StringComparison.Ordinal);

        viewModel.BeginSegmentInlineEditCommand.Execute(segment);
        viewModel.SelectedSegmentEditText = "edited raw";
        viewModel.SaveSegmentInlineEditCommand.Execute(null);

        Assert.True(viewModel.IsSummaryStale);
        Assert.Contains("古い要約", viewModel.SummaryStatus, StringComparison.Ordinal);
        Assert.Contains("本文が更新", viewModel.SummaryActionToolTip, StringComparison.Ordinal);
        Assert.Contains("古い整文", viewModel.ReadablePolishedStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void PostProcessOverwriteConfirmation_CancelsWhenReviewOutputExistsAndUserDeclines()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "raw")
        ]);
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", job.JobId, "segment-001", "wording", "raw", "fixed", "reason", 0.8)
        ]);
        var viewModel = new MainWindowViewModel(paths);
        var promptSeen = false;
        viewModel.ConfirmAction = (_, message) =>
        {
            promptSeen = message.Contains("レビュー候補", StringComparison.Ordinal);
            return false;
        };

        var result = InvokePrivate<bool>(
            viewModel,
            "ConfirmOverwriteExistingPostProcessOutputs",
            PostProcessMode.ReviewOnly,
            job.JobId);

        Assert.False(result);
        Assert.True(promptSeen);
    }

    [Fact]
    public void PostProcessOverwriteConfirmation_CoversSummaryOutputForLegacyCombinedRun()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "raw")
        ]);
        new TranscriptDerivativeRepository(paths).Save(new TranscriptDerivativeSaveRequest(
            job.JobId,
            TranscriptDerivativeKinds.Summary,
            TranscriptDerivativeFormats.Markdown,
            "# summary",
            TranscriptDerivativeSourceKinds.Raw,
            "hash",
            "segment-001..segment-001",
            null,
            "model",
            "prompt",
            "profile"));
        var viewModel = new MainWindowViewModel(paths);
        var promptSeen = false;
        viewModel.ConfirmAction = (_, message) =>
        {
            promptSeen = message.Contains("要約", StringComparison.Ordinal);
            return true;
        };

        var result = InvokePrivate<bool>(
            viewModel,
            "ConfirmOverwriteExistingPostProcessOutputs",
            PostProcessMode.ReviewAndSummary,
            job.JobId);

        Assert.True(result);
        Assert.True(promptSeen);
    }

    [Fact]
    public void StageStatuses_EndWithReviewStage()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(3, viewModel.StageStatuses.Count);
        Assert.True(viewModel.StageStatuses.Last().IsToggleable);
        Assert.False(viewModel.StageStatuses.Last().ShowConnectorAfter);
        Assert.DoesNotContain(viewModel.StageStatuses, stage => stage.Name == "レビュー");
    }

    [Fact]
    public void Constructor_ShowsReviewStageSkippedWhenSavedSettingIsOff()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        new AsrSettingsRepository(paths).Save(new AsrSettings(
            "context",
            "term",
            "kotoba-whisper-v2.2-faster",
            false));

        var viewModel = new MainWindowViewModel(paths);
        var reviewStage = viewModel.StageStatuses.Single(stage => stage.IsToggleable);

        Assert.False(viewModel.EnableReviewStage);
        Assert.Equal("スキップ", reviewStage.Status);
        Assert.Contains("スキップ", reviewStage.ToggleToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackAutoScroll_DoesNotChangeSelectedReviewDraft()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 5, "Speaker_0", "first"),
            new TranscriptSegment("segment-002", job.JobId, 5, 10, "Speaker_1", "second")
        ]);
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", job.JobId, "segment-001", "wording", "first", "1st", "suggestion", 0.8),
            new CorrectionDraft("draft-002", job.JobId, "segment-002", "wording", "second", "2nd", "suggestion", 0.8)
        ]);
        var viewModel = new MainWindowViewModel(paths);

        Assert.Equal("draft-001", viewModel.SelectedCorrectionDraftId);

        viewModel.IsTranscriptAutoScrollEnabled = true;
        viewModel.PlaybackPositionSeconds = 6;

        Assert.Equal("segment-002", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal("draft-001", viewModel.SelectedCorrectionDraftId);
    }

    [Fact]
    public async Task RunSelectedJobAsync_SkipsReadableDocumentWhenReviewStageIsDisabled()
    {
        var fixture = CreateRunReadyViewModel(
            enableReviewStage: false,
            new FakeTranscriptPolishingRuntime("[00:00 - 00:01] Speaker_0: 読める本文です。"));

        await fixture.ViewModel.RunSelectedJobAsync();

        Assert.False(fixture.ReviewStageRunner.RunWasCalled);
        Assert.True(fixture.ReviewStageRunner.SkipWasCalled);
        Assert.False(fixture.PolishingRuntime.WasCalled);
        Assert.False(fixture.ViewModel.HasReadablePolishedContent);
    }

    [Fact]
    public async Task RunReadablePolishingAsync_UsesEditedRawTranscriptText()
    {
        var fixture = CreateRunReadyViewModel(
            enableReviewStage: true,
            new FakeTranscriptPolishingRuntime("[00:00 - 00:01] Speaker_0: 読める本文です。"));
        Assert.NotNull(fixture.ViewModel.SelectedJob);
        var jobId = fixture.ViewModel.SelectedJob.JobId;
        new TranscriptSegmentRepository(fixture.ViewModel.Paths).SaveSegments([
            new TranscriptSegment("segment-001", jobId, 0, 1, "Speaker_0", "はい"),
            new TranscriptSegment("segment-002", jobId, 12, 16.5, "Speaker_0", "meeting agenda and project context"),
            new TranscriptSegment("segment-003", jobId, 24, 27, "Speaker_0", "raw text"),
            new TranscriptSegment("segment-004", jobId, 30, 31, "Speaker_0", "raw text")
        ]);
        InvokePrivate(fixture.ViewModel, "ReloadSegmentsForSelectedJob", "segment-001");
        var segment = Assert.Single(fixture.ViewModel.Segments);

        fixture.ViewModel.BeginSegmentInlineEditCommand.Execute(segment);
        fixture.ViewModel.SelectedSegmentEditText = "edited source text";
        fixture.ViewModel.SaveSegmentInlineEditCommand.Execute(null);

        await InvokePrivate<Task>(fixture.ViewModel, "RunReadablePolishingAsync");

        Assert.Contains("edited source text", fixture.PolishingRuntime.SeenSegmentTexts, StringComparer.Ordinal);
    }

    [Fact]
    public async Task RunReadablePolishingAsync_ConfirmsSpeakerNamesBeforeStarting()
    {
        var fixture = CreateRunReadyViewModel(
            enableReviewStage: true,
            new FakeTranscriptPolishingRuntime("[00:00 - 00:01] Speaker_0: readable text"));
        Assert.NotNull(fixture.ViewModel.SelectedJob);
        var jobId = fixture.ViewModel.SelectedJob.JobId;
        new TranscriptSegmentRepository(fixture.ViewModel.Paths).SaveSegments([
            new TranscriptSegment("segment-001", jobId, 0, 1, "Speaker_0", "raw text")
        ]);
        SpeakerNameConfirmationRequest? request = null;
        fixture.ViewModel.ConfirmSpeakerNamesDialog = dialogRequest =>
        {
            request = dialogRequest;
            return null;
        };

        await InvokePrivate<Task>(fixture.ViewModel, "RunReadablePolishingAsync");

        Assert.False(fixture.PolishingRuntime.WasCalled);
        Assert.NotNull(request);
        var speaker = Assert.Single(request.Speakers);
        Assert.Equal("Speaker_0", speaker.SpeakerId);
        Assert.Equal("Speaker_0", speaker.DisplayName);
        Assert.Equal(4, speaker.SegmentCount);
        Assert.Contains("raw text", speaker.PreviewTexts, StringComparer.Ordinal);
        Assert.Equal(3, speaker.PreviewSamples.Count);
        Assert.Contains(speaker.PreviewSamples, sample =>
            sample.StartSeconds == 12 &&
            sample.EndSeconds == 16.5 &&
            sample.Text == "meeting agenda and project context");
    }

    [Fact]
    public async Task RunReadablePolishingAsync_AppliesConfirmedSpeakerNamesBeforeStarting()
    {
        var fixture = CreateRunReadyViewModel(
            enableReviewStage: true,
            new FakeTranscriptPolishingRuntime("[00:00 - 00:01] 田中: readable text"));
        Assert.NotNull(fixture.ViewModel.SelectedJob);
        var jobId = fixture.ViewModel.SelectedJob.JobId;
        new TranscriptSegmentRepository(fixture.ViewModel.Paths).SaveSegments([
            new TranscriptSegment("segment-001", jobId, 0, 1, "Speaker_0", "raw text")
        ]);
        InvokePrivate(fixture.ViewModel, "ReloadSegmentsForSelectedJob", "segment-001");
        fixture.ViewModel.ConfirmSpeakerNamesDialog = request => new SpeakerNameConfirmationResult(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [request.Speakers.Single().SpeakerId] = "田中"
            });

        await InvokePrivate<Task>(fixture.ViewModel, "RunReadablePolishingAsync");

        Assert.True(fixture.PolishingRuntime.WasCalled);
        Assert.All(fixture.ViewModel.Segments, segment => Assert.Equal(fixture.PolishingRuntime.SeenSpeakerNames.Single(), segment.Speaker));
        Assert.Contains(fixture.PolishingRuntime.SeenSpeakerNames.Single(), fixture.ViewModel.SpeakerFilters, StringComparer.Ordinal);
        Assert.DoesNotContain("Speaker_0", fixture.ViewModel.SpeakerFilters, StringComparer.Ordinal);
        Assert.Contains("田中", fixture.PolishingRuntime.SeenSpeakerNames, StringComparer.Ordinal);
    }

    [Fact]
    public void RunSelectedJobCommand_RequiresReviewAssetsWhenReviewStageIsEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.FasterWhisperScriptPath);
        CreateFasterWhisperRuntime(paths);
        Directory.CreateDirectory(paths.KotobaWhisperFasterModelPath);
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "kotoba-whisper-v2.2-faster", true));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            LicenseAccepted = true
        });

        var viewModel = new MainWindowViewModel(paths);
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", audioPath, "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];

        Assert.True(viewModel.EnableReviewStage);
        Assert.True(viewModel.RequiredRuntimeAssetsReady);
        Assert.False(viewModel.ReviewStageAssetsReady);
        Assert.False(viewModel.RunSelectedJobCommand.CanExecute(null));
        Assert.Contains("整文ランタイムまたは整文モデルが不足しています", viewModel.RunPreflightDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void RunSelectedJobCommand_DoesNotRequireReviewModelWhenReviewStageIsDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.FasterWhisperScriptPath);
        CreateFasterWhisperRuntime(paths);
        Directory.CreateDirectory(paths.KotobaWhisperFasterModelPath);
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "kotoba-whisper-v2.2-faster", false));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            LicenseAccepted = true
        });

        var viewModel = new MainWindowViewModel(paths);
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", audioPath, "registered", 0, 0, DateTimeOffset.Now));
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
        CreateFasterWhisperRuntime(paths);
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
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
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", audioPath, "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];

        Assert.True(viewModel.RequiredRuntimeAssetsReady);
        Assert.True(viewModel.RunSelectedJobCommand.CanExecute(null));
    }

    [Fact]
    public void RunSelectedJobCommand_RepairsHiddenTernaryReviewModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = CreatePathsWithoutTernaryRuntime(root);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        Touch(paths.FasterWhisperScriptPath);
        CreateFasterWhisperRuntime(paths);
        Directory.CreateDirectory(paths.KotobaWhisperFasterModelPath);
        Touch(Path.Combine(paths.KotobaWhisperFasterModelPath, "model.bin"));
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var installService = new ModelInstallService(paths, new InstalledModelRepository(paths), new ModelVerificationService());
        var asrItem = catalog.Models.First(model => model.ModelId == "kotoba-whisper-v2.2-faster");
        installService.RegisterLocalModel(asrItem, paths.KotobaWhisperFasterModelPath, "download");
        var reviewItem = catalog.Models.First(model => model.ModelId == "ternary-bonsai-8b-q2-0");
        var reviewPath = installService.GetDefaultInstallPath(reviewItem);
        Touch(reviewPath);
        installService.RegisterLocalModel(reviewItem, reviewPath, "download");
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "kotoba-whisper-v2.2-faster", false, true));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            LicenseAccepted = true,
            SelectedAsrModelId = "kotoba-whisper-v2.2-faster",
            SelectedReviewModelId = reviewItem.ModelId
        });

        var viewModel = new MainWindowViewModel(paths);
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", audioPath, "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];

        Assert.False(viewModel.SelectedSetupModelsReady);
        Assert.Equal("gemma-4-e4b-it-q4-k-m", viewModel.SelectedSetupReviewModel?.ModelId);
        Assert.True(viewModel.SetupTernaryReviewRuntimeReady);
        Assert.False(viewModel.SelectedSetupConfigurationReady);
        Assert.False(viewModel.ReviewStageAssetsReady);
        Assert.False(viewModel.RunSelectedJobCommand.CanExecute(null));
        Assert.DoesNotContain("Ternary review runtime", viewModel.SetupPrimaryInstallSummary, StringComparison.Ordinal);
        Assert.DoesNotContain(paths.TernaryLlamaCompletionPath, viewModel.RunPreflightDetail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunSelectedJobCommand_UsesPresetReviewModelWhenSelectedReviewModelIsEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        Touch(paths.FasterWhisperScriptPath);
        CreateFasterWhisperRuntime(paths);
        Directory.CreateDirectory(paths.WhisperSmallModelPath);
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var installService = new ModelInstallService(paths, new InstalledModelRepository(paths), new ModelVerificationService());
        var reviewItem = catalog.Models.First(model => model.ModelId == "bonsai-8b-q1-0");
        var reviewPath = installService.GetDefaultInstallPath(reviewItem);
        Touch(reviewPath);
        installService.RegisterLocalModel(reviewItem, reviewPath, "download");
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "whisper-small", true));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            LicenseAccepted = true,
            SelectedModelPresetId = "lightweight",
            SelectedAsrModelId = "whisper-small",
            SelectedReviewModelId = string.Empty
        });

        var viewModel = new MainWindowViewModel(paths);
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", audioPath, "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];

        Assert.True(viewModel.ReviewStageAssetsReady);
        Assert.True(viewModel.RunSelectedJobCommand.CanExecute(null));
    }

    [Fact]
    public void ReadablePolishingPromptSettings_LoadDefaultSettings()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(ReadablePolishingPromptModelFamilies.Gemma, viewModel.SelectedReadablePolishingPromptModelFamily?.ModelFamily);
        Assert.Contains("Gemma 4", viewModel.ReadablePolishingPromptActiveModelFamilySummary, StringComparison.Ordinal);
        Assert.Equal(ReadablePolishingPromptPresets.StrongPunctuation, viewModel.SelectedReadablePolishingPromptPreset?.PresetId);
        Assert.False(viewModel.ReadablePolishingPromptUseCustomPrompt);
        Assert.True(viewModel.IsReadablePolishingPromptPresetEnabled);
        Assert.Empty(viewModel.ReadablePolishingPromptAdditionalInstruction);
        Assert.Empty(viewModel.ReadablePolishingPromptCustomPrompt);
    }

    [Fact]
    public void ReadablePolishingPromptSettings_InitialSelectionUsesCurrentReviewModelFamily()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            SelectedReviewModelId = "bonsai-8b-q1-0"
        });
        new ReadablePolishingPromptSettingsRepository(paths).Save(
            ReadablePolishingPromptSettings.CreateDefault(ReadablePolishingPromptModelFamilies.Bonsai) with
            {
                PresetId = ReadablePolishingPromptPresets.Faithful,
                AdditionalInstruction = "Keep product names unchanged."
            });

        var viewModel = new MainWindowViewModel(paths);

        Assert.Equal(ReadablePolishingPromptModelFamilies.Bonsai, viewModel.SelectedReadablePolishingPromptModelFamily?.ModelFamily);
        Assert.Contains("Bonsai 8B", viewModel.ReadablePolishingPromptActiveModelFamilySummary, StringComparison.Ordinal);
        Assert.Equal(ReadablePolishingPromptPresets.Faithful, viewModel.SelectedReadablePolishingPromptPreset?.PresetId);
        Assert.Equal("Keep product names unchanged.", viewModel.ReadablePolishingPromptAdditionalInstruction);
        Assert.Equal(TranscriptPolishingPromptBuilder.BonsaiCompactPromptTemplateId, ReadablePolishingPromptSettings.ResolveDefaultPromptTemplateId(viewModel.SelectedReadablePolishingPromptModelFamily!.ModelFamily));
    }

    [Fact]
    public void ReadablePolishingPromptSettings_SaveAndReloadPerModelFamily()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        var first = new MainWindowViewModel(paths);

        first.SelectedReadablePolishingPromptModelFamily = first.ReadablePolishingPromptModelFamilyOptions
            .Single(option => option.ModelFamily == ReadablePolishingPromptModelFamilies.Bonsai);
        first.SelectedReadablePolishingPromptPreset = first.ReadablePolishingPromptPresetOptions
            .Single(option => option.PresetId == ReadablePolishingPromptPresets.Faithful);
        first.ReadablePolishingPromptAdditionalInstruction = "専門用語を残してください。";
        first.ReadablePolishingPromptUseCustomPrompt = true;
        first.ReadablePolishingPromptCustomPrompt = "読みやすい講演録にしてください。";
        first.SaveReadablePolishingPromptSettingsCommand.Execute(null);

        var second = new MainWindowViewModel(paths);
        second.SelectedReadablePolishingPromptModelFamily = second.ReadablePolishingPromptModelFamilyOptions
            .Single(option => option.ModelFamily == ReadablePolishingPromptModelFamilies.Bonsai);

        Assert.Equal(ReadablePolishingPromptPresets.Standard, second.SelectedReadablePolishingPromptPreset?.PresetId);
        Assert.Equal("専門用語を残してください。", second.ReadablePolishingPromptAdditionalInstruction);
        Assert.True(second.ReadablePolishingPromptUseCustomPrompt);
        Assert.False(second.IsReadablePolishingPromptPresetEnabled);
        Assert.Equal("読みやすい講演録にしてください。", second.ReadablePolishingPromptCustomPrompt);
    }

    [Fact]
    public void ReadablePolishingPromptSettings_SelectActiveModelFamilyCommandSelectsCurrentReviewModelFamily()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            SelectedReviewModelId = "bonsai-8b-q1-0"
        });
        var viewModel = new MainWindowViewModel(paths);

        viewModel.SelectActiveReadablePolishingPromptModelFamilyCommand.Execute(null);

        Assert.Equal(ReadablePolishingPromptModelFamilies.Bonsai, viewModel.SelectedReadablePolishingPromptModelFamily?.ModelFamily);
        Assert.Contains("Bonsai 8B", viewModel.ReadablePolishingPromptActiveModelFamilySummary, StringComparison.Ordinal);
        Assert.Contains("Bonsai 8B", viewModel.ReadablePolishingPromptSettingsStatus, StringComparison.Ordinal);
        Assert.Contains("対象モデル: Bonsai 8B", viewModel.ReadablePolishingPromptPreviewText, StringComparison.Ordinal);
        Assert.Contains("テンプレート: bonsai-polishing-compact", viewModel.ReadablePolishingPromptPreviewText, StringComparison.Ordinal);
    }
}
