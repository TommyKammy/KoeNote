using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Input;
using System.Xml.Linq;
using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Diarization;
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
public sealed class MainWindowViewModelJobTests : MainWindowViewModelTestBase
{
    [Fact]
    public void AppVersionDisplay_UsesAssemblyVersionForHeader()
    {
        var viewModel = CreateViewModel();

        Assert.Matches(@"^v\d+\.\d+\.\d+", viewModel.AppVersionDisplay);
        Assert.StartsWith("KoeNote ", viewModel.AppVersionToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void ZoomCommands_AdjustAndPersistTranscriptContentFontScale()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        var first = new MainWindowViewModel(paths);

        Assert.Equal(1.0, first.MainContentZoomScale);
        Assert.Equal("100%", first.MainContentZoomPercentText);
        Assert.Equal(14.0, first.TranscriptBodyFontSize);
        Assert.Equal(22.0, first.TranscriptBodyLineHeight);
        Assert.Equal(14.0, first.ReadableDocumentFontSize);
        Assert.Equal(23.0, first.ReadableDocumentLineHeight);
        Assert.Contains("本文", first.MainContentZoomToolTip, StringComparison.Ordinal);

        first.ZoomInCommand.Execute(null);
        Assert.Equal(1.1, first.MainContentZoomScale, 3);
        Assert.Equal("110%", first.MainContentZoomPercentText);
        Assert.Equal(15.4, first.TranscriptBodyFontSize, 3);
        Assert.Equal(24.2, first.TranscriptBodyLineHeight, 3);
        Assert.Equal(15.4, first.ReadableDocumentFontSize, 3);
        Assert.Equal(25.3, first.ReadableDocumentLineHeight, 3);
        Assert.True(first.ZoomOutCommand.CanExecute(null));

        var second = new MainWindowViewModel(paths);
        Assert.Equal(1.1, second.MainContentZoomScale, 3);
        Assert.Equal(15.4, second.TranscriptBodyFontSize, 3);

        second.ResetZoomCommand.Execute(null);
        Assert.Equal(1.0, second.MainContentZoomScale);
        Assert.Equal(14.0, second.TranscriptBodyFontSize);
        Assert.False(second.ResetZoomCommand.CanExecute(null));
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
    public void Constructor_DoesNotInjectSampleSegments()
    {
        var viewModel = CreateViewModel();

        Assert.Empty(viewModel.Segments);
        Assert.Empty(ViewItems<TranscriptSegmentPreview>(viewModel.FilteredSegments));
    }

    [Fact]
    public async Task DeleteJobCommand_CancelledConfirmationDoesNotMoveJob()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        var viewModel = new MainWindowViewModel(new AppPaths(root, root, AppContext.BaseDirectory));
        var job = viewModel.RegisterAudioFile(audioPath);
        viewModel.ConfirmAction = (_, _) => false;

        viewModel.DeleteJobCommand.Execute(job);
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        Assert.Single(viewModel.Jobs);
        Assert.Empty(viewModel.DeletedJobs);
    }

    [Fact]
    public async Task DeleteJobCommand_UnstartedRegisteredJobDoesNotMoveToHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        var viewModel = new MainWindowViewModel(new AppPaths(root, root, AppContext.BaseDirectory));
        var job = viewModel.RegisterAudioFile(audioPath);
        viewModel.ConfirmAction = (_, message) =>
        {
            Assert.Contains("履歴に残さず", message, StringComparison.Ordinal);
            return true;
        };

        viewModel.DeleteJobCommand.Execute(job);
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        Assert.Empty(viewModel.Jobs);
        Assert.Empty(viewModel.DeletedJobs);
        Assert.Empty(new JobRepository(viewModel.Paths).LoadRecent());
        Assert.Empty(new JobRepository(viewModel.Paths).LoadDeleted());
    }

    [Fact]
    public void SegmentSearchAndSpeakerFilter_CombinePredicates()
    {
        var viewModel = CreateViewModel();
        viewModel.Segments.Add(new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.810",
            "Speaker_0",
            "今日は会議の議事録を作成するために音声認識をテストしています。",
            "候補なし"));
        viewModel.Segments.Add(new TranscriptSegmentPreview(
            "00:03:21.400",
            "00:03:27.800",
            "Speaker_1",
            "この仕様はサーバーの右側で処理します。",
            "候補なし"));

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
    public void ImportDomainPresetFromFile_AppliesSpeakerAliasesToSelectedJob()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "こんにちは")
        ]);
        var presetPath = Path.Combine(root, "preset.json");
        File.WriteAllText(presetPath, """
            {
              "schema_version": 1,
              "speaker_aliases": [
                {
                  "speaker_id": "Speaker_0",
                  "display_name": "聞き手"
                }
              ]
            }
            """);
        var viewModel = new MainWindowViewModel(paths);

        viewModel.ImportDomainPresetFromFile(presetPath);

        Assert.True(viewModel.HasLoadedDomainPreset);
        Assert.Equal("Speaker_0", Assert.Single(viewModel.Segments).Speaker);

        viewModel.ApplyLoadedDomainPresetCommand.Execute(null);

        Assert.Equal("聞き手", Assert.Single(viewModel.Segments).Speaker);
        Assert.Contains("話者別名: 1件追加", viewModel.LatestLog, StringComparison.Ordinal);
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

        Assert.StartsWith("整文の出力に失敗しました:", viewModel.ExportWarning, StringComparison.Ordinal);
        Assert.StartsWith("整文の出力に失敗しました:", viewModel.LatestLog, StringComparison.Ordinal);
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

        Assert.False(viewModel.ExportSelectedJobCommand.CanExecute(null));
        Assert.False(viewModel.ExportTxtCommand.CanExecute(null));
        Assert.True(viewModel.ExportJsonCommand.CanExecute(null));
        Assert.True(viewModel.ExportSrtCommand.CanExecute(null));
        Assert.False(viewModel.ExportDocxCommand.CanExecute(null));
        Assert.True(viewModel.ExportRawXlsxCommand.CanExecute(null));
        Assert.True(viewModel.ExportPolishedXlsxCommand.CanExecute(null));
        Assert.False(viewModel.ExportReadablePolishedTxtCommand.CanExecute(null));

        var derivativeRepository = new TranscriptDerivativeRepository(paths);
        derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
            job.JobId,
            TranscriptDerivativeKinds.Polished,
            TranscriptDerivativeFormats.PlainText,
            "[00:00 - 00:01] Speaker_0: readable polished",
            TranscriptDerivativeSourceKinds.Raw,
            "hash",
            "segment-001..segment-001",
            null,
            "model",
            "prompt",
            "profile"));
        viewModel.SelectedJob = job;

        Assert.True(viewModel.ExportSelectedJobCommand.CanExecute(null));
        Assert.True(viewModel.ExportTxtCommand.CanExecute(null));
        Assert.True(viewModel.ExportDocxCommand.CanExecute(null));
        Assert.True(viewModel.ExportReadablePolishedTxtCommand.CanExecute(null));
        Assert.True(viewModel.ExportReadablePolishedMarkdownCommand.CanExecute(null));
        Assert.True(viewModel.ExportReadablePolishedXlsxCommand.CanExecute(null));
        Assert.True(viewModel.ExportReadablePolishedDocxCommand.CanExecute(null));
    }

    [Fact]
    public void IncludeExportTimestamps_CanBeToggled()
    {
        var viewModel = CreateViewModel();

        viewModel.IncludeExportTimestamps = false;

        Assert.False(viewModel.IncludeExportTimestamps);
    }

    [Fact]
    public void MergeConsecutiveSpeakersOnExport_CanBeToggled()
    {
        var viewModel = CreateViewModel();

        viewModel.MergeConsecutiveSpeakersOnExport = true;

        Assert.True(viewModel.MergeConsecutiveSpeakersOnExport);
    }

    [Fact]
    public void ExportSelectedJobToFolder_UsesMergeConsecutiveSpeakersOption()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "raw one"),
            new TranscriptSegment("segment-002", job.JobId, 1.5, 2.5, "Speaker_0", "raw two")
        ]);
        var output = Path.Combine(root, "exports");
        var viewModel = new MainWindowViewModel(paths)
        {
            MergeConsecutiveSpeakersOnExport = true
        };

        viewModel.ExportSelectedJobToFolder(output, TranscriptExportFormat.Xlsx, TranscriptExportSource.Raw);

        using var archive = ZipFile.OpenRead(Path.Combine(output, "meeting.xlsx"));
        var cells = ReadInlineStringCells(ReadZipXml(archive, "xl/worksheets/sheet1.xml"));
        Assert.Equal("00:00:00.000", cells["A2"]);
        Assert.Equal("00:00:02.500", cells["B2"]);
        Assert.Equal("Speaker_0", cells["C2"]);
        Assert.Equal("raw one\nraw two", cells["D2"]);
        Assert.False(cells.ContainsKey("A3"));
    }

    [Fact]
    public void HeaderNavigationCommands_SelectExpectedLogPanelTabs()
    {
        var viewModel = CreateViewModel();

        viewModel.OpenSettingsCommand.Execute(null);
        Assert.Equal(0, viewModel.SelectedLogPanelTabIndex);
        Assert.Equal(0, viewModel.SelectedDetailPanelTabIndex);
        Assert.Equal("設定", viewModel.DetailPanelTitle);
        Assert.True(viewModel.IsDetailPanelOpen);

        viewModel.ShowModelCatalogCommand.Execute(null);
        Assert.Equal(2, viewModel.SelectedLogPanelTabIndex);
        Assert.Equal(2, viewModel.SelectedDetailPanelTabIndex);
        Assert.Equal("モデル", viewModel.DetailPanelTitle);

        viewModel.OpenLogsCommand.Execute(null);
        Assert.Equal(4, viewModel.SelectedLogPanelTabIndex);
        Assert.Equal(4, viewModel.SelectedDetailPanelTabIndex);
        Assert.Equal("ログ", viewModel.DetailPanelTitle);

        viewModel.OpenSetupCommand.Execute(null);
        Assert.True(viewModel.IsSetupWizardModalOpen);
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
    public void SkipToNextSegmentCommand_SelectsNextSegmentStart()
    {
        var viewModel = CreateViewModel();
        var first = new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.000",
            "Speaker_0",
            "first",
            "",
            "segment-001",
            StartSeconds: 0,
            EndSeconds: 5);
        var second = new TranscriptSegmentPreview(
            "00:00:05.000",
            "00:00:10.000",
            "Speaker_1",
            "second",
            "",
            "segment-002",
            StartSeconds: 5,
            EndSeconds: 10);
        viewModel.Segments.Add(first);
        viewModel.Segments.Add(second);
        viewModel.PlaybackPositionSeconds = 1;

        Assert.True(viewModel.SkipToNextSegmentCommand.CanExecute(null));

        viewModel.SkipToNextSegmentCommand.Execute(null);

        Assert.Equal("segment-002", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal(5, viewModel.PlaybackPositionSeconds, 2);
        Assert.Equal(1, viewModel.TranscriptAutoScrollRequestId);
    }

    [Fact]
    public void SkipToPreviousSegmentCommand_RestartsCurrentSegmentBeforeMovingPrevious()
    {
        var viewModel = CreateViewModel();
        var first = new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.000",
            "Speaker_0",
            "first",
            "",
            "segment-001",
            StartSeconds: 0,
            EndSeconds: 5);
        var second = new TranscriptSegmentPreview(
            "00:00:05.000",
            "00:00:10.000",
            "Speaker_1",
            "second",
            "",
            "segment-002",
            StartSeconds: 5,
            EndSeconds: 10);
        viewModel.Segments.Add(first);
        viewModel.Segments.Add(second);
        viewModel.PlaybackPositionSeconds = 7;

        viewModel.SkipToPreviousSegmentCommand.Execute(null);

        Assert.Equal("segment-002", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal(5, viewModel.PlaybackPositionSeconds, 2);

        viewModel.SkipToPreviousSegmentCommand.Execute(null);

        Assert.Equal("segment-001", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal(0, viewModel.PlaybackPositionSeconds, 2);
    }

    [Fact]
    public void ReadableTranscriptTabNavigationCommands_SelectSupplementalTabs()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedTranscriptTabIndex = 3;
        viewModel.ShowReadableTranscriptTabCommand.Execute(null);
        Assert.Equal(0, viewModel.SelectedTranscriptTabIndex);

        viewModel.ShowRawTranscriptTabCommand.Execute(null);
        Assert.Equal(1, viewModel.SelectedTranscriptTabIndex);

        viewModel.ShowDiffTranscriptTabCommand.Execute(null);
        Assert.Equal(2, viewModel.SelectedTranscriptTabIndex);

        viewModel.ShowReviewCandidateTranscriptTabCommand.Execute(null);
        Assert.Equal(3, viewModel.SelectedTranscriptTabIndex);
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
    public void PlaybackPosition_SelectsVisibleSegmentWhenAutoScrollIsEnabled()
    {
        var viewModel = CreateViewModel();
        viewModel.Segments.Add(new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.000",
            "Speaker_0",
            "first",
            "",
            "segment-001",
            StartSeconds: 0,
            EndSeconds: 5));
        viewModel.Segments.Add(new TranscriptSegmentPreview(
            "00:00:05.000",
            "00:00:10.000",
            "Speaker_1",
            "second",
            "",
            "segment-002",
            StartSeconds: 5,
            EndSeconds: 10));

        viewModel.PlaybackPositionSeconds = 6;
        Assert.Null(viewModel.SelectedSegment);

        viewModel.IsTranscriptAutoScrollEnabled = true;

        Assert.Equal("segment-002", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal(1, viewModel.TranscriptAutoScrollRequestId);
        Assert.Equal(6, viewModel.PlaybackPositionSeconds, 2);
    }

    [Fact]
    public void PlaybackPosition_DoesNotAutoSelectFilteredOutSegment()
    {
        var viewModel = CreateViewModel();
        viewModel.Segments.Add(new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.000",
            "Speaker_0",
            "visible first",
            "",
            "segment-001",
            StartSeconds: 0,
            EndSeconds: 5));
        viewModel.Segments.Add(new TranscriptSegmentPreview(
            "00:00:05.000",
            "00:00:10.000",
            "Speaker_1",
            "hidden second",
            "",
            "segment-002",
            StartSeconds: 5,
            EndSeconds: 10));

        viewModel.SegmentSearchText = "visible";
        viewModel.IsTranscriptAutoScrollEnabled = true;
        viewModel.PlaybackPositionSeconds = 6;

        Assert.Equal("segment-001", viewModel.SelectedSegment?.SegmentId);
    }

    [Fact]
    public void PlaybackAutoScroll_DoesNotReplacePendingSegmentEdit()
    {
        var viewModel = CreateViewModel();
        var first = new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.000",
            "Speaker_0",
            "first",
            "",
            "segment-001",
            StartSeconds: 0,
            EndSeconds: 5);
        var second = new TranscriptSegmentPreview(
            "00:00:05.000",
            "00:00:10.000",
            "Speaker_1",
            "second",
            "",
            "segment-002",
            StartSeconds: 5,
            EndSeconds: 10);
        viewModel.Segments.Add(first);
        viewModel.Segments.Add(second);
        viewModel.SelectedSegment = first;
        viewModel.SelectedSegmentEditText = "unsaved edit";

        viewModel.IsTranscriptAutoScrollEnabled = true;
        viewModel.PlaybackPositionSeconds = 6;

        Assert.Equal("segment-001", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal("unsaved edit", viewModel.SelectedSegmentEditText);
    }

    [Fact]
    public void InlineSegmentEditCommand_ActivatesAndCancelRestoresSegmentText()
    {
        var viewModel = CreateViewModel();
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", @"C:\audio\meeting.wav", "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];
        var segment = new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.000",
            "Speaker_0",
            "first",
            "",
            "segment-001",
            StartSeconds: 0,
            EndSeconds: 5);
        viewModel.Segments.Add(segment);

        Assert.True(viewModel.BeginSegmentInlineEditCommand.CanExecute(segment));
        viewModel.BeginSegmentInlineEditCommand.Execute(segment);

        Assert.True(viewModel.IsSegmentInlineEditActive);
        Assert.Equal(segment, viewModel.SelectedSegment);
        Assert.Equal("first", viewModel.SelectedSegmentEditText);

        viewModel.SelectedSegmentEditText = "edited";
        viewModel.CancelSegmentInlineEditCommand.Execute(null);

        Assert.False(viewModel.IsSegmentInlineEditActive);
        Assert.Equal("first", viewModel.SelectedSegmentEditText);
    }

    [Fact]
    public void PlaybackAutoScroll_DoesNotReplaceActiveInlineSegmentEdit()
    {
        var viewModel = CreateViewModel();
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", @"C:\audio\meeting.wav", "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];
        var first = new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.000",
            "Speaker_0",
            "first",
            "",
            "segment-001",
            StartSeconds: 0,
            EndSeconds: 5);
        var second = new TranscriptSegmentPreview(
            "00:00:05.000",
            "00:00:10.000",
            "Speaker_1",
            "second",
            "",
            "segment-002",
            StartSeconds: 5,
            EndSeconds: 10);
        viewModel.Segments.Add(first);
        viewModel.Segments.Add(second);
        viewModel.BeginSegmentInlineEditCommand.Execute(first);

        viewModel.IsTranscriptAutoScrollEnabled = true;
        viewModel.PlaybackPositionSeconds = 6;

        Assert.True(viewModel.IsSegmentInlineEditActive);
        Assert.Equal("segment-001", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal("first", viewModel.SelectedSegmentEditText);
    }

    [Fact]
    public void InlineSegmentEdit_AutoSavesWhenSelectingAnotherSegment()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        var repository = new TranscriptSegmentRepository(paths);
        repository.SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 5, "Speaker_0", "first"),
            new TranscriptSegment("segment-002", job.JobId, 5, 10, "Speaker_1", "second")
        ]);
        var viewModel = new MainWindowViewModel(paths);
        var first = viewModel.Segments.Single(segment => segment.SegmentId == "segment-001");
        var second = viewModel.Segments.Single(segment => segment.SegmentId == "segment-002");

        viewModel.BeginSegmentInlineEditCommand.Execute(first);
        viewModel.SelectedSegmentEditText = "edited first";
        viewModel.SelectedSegment = second;

        Assert.False(viewModel.IsSegmentInlineEditActive);
        Assert.Equal("segment-002", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal("edited first", repository.ReadPreviews(job.JobId).Single(segment => segment.SegmentId == "segment-001").Text);
    }

    [Fact]
    public void InlineSegmentEdit_DoesNotChangeSelectionWhenAutoSaveCannotCommit()
    {
        var viewModel = CreateViewModel();
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", @"C:\audio\meeting.wav", "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];
        var first = new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.000",
            "Speaker_0",
            "first",
            "",
            "segment-001",
            StartSeconds: 0,
            EndSeconds: 5);
        var second = new TranscriptSegmentPreview(
            "00:00:05.000",
            "00:00:10.000",
            "Speaker_1",
            "second",
            "",
            "segment-002",
            StartSeconds: 5,
            EndSeconds: 10);
        viewModel.Segments.Add(first);
        viewModel.Segments.Add(second);

        viewModel.BeginSegmentInlineEditCommand.Execute(first);
        viewModel.SelectedSegmentEditText = "   ";
        viewModel.SelectedSegment = second;

        Assert.True(viewModel.IsSegmentInlineEditActive);
        Assert.Equal("segment-001", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal("   ", viewModel.SelectedSegmentEditText);
    }

    [Fact]
    public void InlineSegmentEdit_DoesNotOverwriteTextWhenBeginEditCannotChangeSelection()
    {
        var viewModel = CreateViewModel();
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", @"C:\audio\meeting.wav", "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];
        var first = new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.000",
            "Speaker_0",
            "first",
            "",
            "segment-001",
            StartSeconds: 0,
            EndSeconds: 5);
        var second = new TranscriptSegmentPreview(
            "00:00:05.000",
            "00:00:10.000",
            "Speaker_1",
            "second",
            "",
            "segment-002",
            StartSeconds: 5,
            EndSeconds: 10);
        viewModel.Segments.Add(first);
        viewModel.Segments.Add(second);

        viewModel.BeginSegmentInlineEditCommand.Execute(first);
        viewModel.SelectedSegmentEditText = "   ";
        viewModel.BeginSegmentInlineEditCommand.Execute(second);

        Assert.True(viewModel.IsSegmentInlineEditActive);
        Assert.Equal("segment-001", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal("   ", viewModel.SelectedSegmentEditText);
    }

    [Fact]
    public void RevertSegmentEditCommand_RestoresCommittedSegmentEdit()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        var repository = new TranscriptSegmentRepository(paths);
        repository.SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 5, "Speaker_0", "first")
        ]);
        new TranscriptEditService(paths).ApplySegmentEdit(job.JobId, "segment-001", "edited first");
        var viewModel = new MainWindowViewModel(paths);
        var segment = Assert.Single(viewModel.Segments);

        Assert.Equal("edited first", segment.Text);
        Assert.True(viewModel.RevertSegmentEditCommand.CanExecute(segment));
        viewModel.RevertSegmentEditCommand.Execute(segment);

        Assert.Equal("first", repository.ReadPreviews(job.JobId).Single().Text);
        Assert.Equal("first", viewModel.SelectedSegment?.Text);
        Assert.Contains("戻しました", viewModel.LatestLog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunSelectedJobAsync_StopsBeforeRunWhenSourceAudioIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.FasterWhisperScriptPath);
        CreateFasterWhisperRuntime(paths);
        Directory.CreateDirectory(paths.KotobaWhisperFasterModelPath);
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "kotoba-whisper-v2.2-faster", false));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            LicenseAccepted = true,
            SelectedAsrModelId = "kotoba-whisper-v2.2-faster"
        });
        var missingAudioPath = Path.Combine(root, "missing.wav");
        var viewModel = new MainWindowViewModel(paths);
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", missingAudioPath, "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];

        Assert.False(viewModel.RunSelectedJobCommand.CanExecute(null));
        await viewModel.RunSelectedJobAsync();

        Assert.False(viewModel.IsRunInProgress);
        Assert.True(viewModel.IsSetupWizardModalOpen);
        Assert.Contains("音声ファイルが見つかりません", viewModel.LatestLog, StringComparison.Ordinal);
        Assert.Contains(missingAudioPath, viewModel.RunPreflightDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunSelectedJobAsync_RechecksFfmpegFileBeforeRun()
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
            LicenseAccepted = true,
            SelectedAsrModelId = "kotoba-whisper-v2.2-faster"
        });
        var viewModel = new MainWindowViewModel(paths);
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", audioPath, "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];
        File.Delete(paths.FfmpegPath);

        await viewModel.RunSelectedJobAsync();

        Assert.False(viewModel.IsRunInProgress);
        Assert.True(viewModel.IsSetupWizardModalOpen);
        Assert.Contains("ffmpeg が見つかりません", viewModel.LatestLog, StringComparison.Ordinal);
        Assert.Contains(paths.FfmpegPath, viewModel.RunPreflightDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunSelectedJobAsync_GeneratesReadableDocumentByDefault()
    {
        var fixture = CreateRunReadyViewModel(
            enableReviewStage: true,
            new FakeTranscriptPolishingRuntime("[00:00 - 00:01] Speaker_0: 読める本文です。"));

        await fixture.ViewModel.RunSelectedJobAsync();

        Assert.True(fixture.ReviewStageRunner.RunWasCalled);
        Assert.True(fixture.PolishingRuntime.WasCalled);
        Assert.True(fixture.ViewModel.HasReadablePolishedContent);
        Assert.Contains("読める本文です。", fixture.ViewModel.ReadablePolishedContent, StringComparison.Ordinal);
        Assert.Equal(0, fixture.ViewModel.SelectedTranscriptTabIndex);
        Assert.Equal("整文が完了しました。", fixture.ViewModel.LatestLog);
    }

    [Fact]
    public async Task RunSelectedJobAsync_KeepsReadableFailureVisibleWhenDocumentGenerationFails()
    {
        var fixture = CreateRunReadyViewModel(
            enableReviewStage: true,
            new FakeTranscriptPolishingRuntime(
                "[00:00 - 00:01] Speaker_0: 読める本文です。",
                new ReviewWorkerException(ReviewFailureCategory.ProcessFailed, "runtime failed")));

        await fixture.ViewModel.RunSelectedJobAsync();

        Assert.True(fixture.ReviewStageRunner.RunWasCalled);
        Assert.True(fixture.PolishingRuntime.WasCalled);
        Assert.False(fixture.ViewModel.HasReadablePolishedContent);
        Assert.Contains("整文を作成できませんでした", fixture.ViewModel.LatestLog, StringComparison.Ordinal);
        Assert.Contains("整文を作成できませんでした", fixture.ViewModel.ReadablePolishedStatus, StringComparison.Ordinal);
    }
}
