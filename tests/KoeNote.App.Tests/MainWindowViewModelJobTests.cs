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
        Assert.False(viewModel.CopyCurrentExportTargetCommand.CanExecute(null));
        Assert.False(viewModel.ExportTxtCommand.CanExecute(null));
        Assert.True(viewModel.ExportJsonCommand.CanExecute(null));
        Assert.True(viewModel.ExportSrtCommand.CanExecute(null));
        Assert.False(viewModel.ExportDocxCommand.CanExecute(null));
        Assert.True(viewModel.ExportRawXlsxCommand.CanExecute(null));
        Assert.True(viewModel.ExportPolishedXlsxCommand.CanExecute(null));
        Assert.False(viewModel.ExportReadablePolishedTxtCommand.CanExecute(null));

        viewModel.IsStandardRawTranscriptViewSelected = true;
        Assert.True(viewModel.ExportSelectedJobCommand.CanExecute(null));
        Assert.True(viewModel.CopyCurrentExportTargetCommand.CanExecute(null));

        viewModel.IsStandardReadableTranscriptViewSelected = true;
        Assert.False(viewModel.ExportSelectedJobCommand.CanExecute(null));
        Assert.False(viewModel.CopyCurrentExportTargetCommand.CanExecute(null));

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
        Assert.True(viewModel.CopyCurrentExportTargetCommand.CanExecute(null));
        Assert.True(viewModel.ExportTxtCommand.CanExecute(null));
        Assert.True(viewModel.ExportDocxCommand.CanExecute(null));
        Assert.True(viewModel.ExportReadablePolishedTxtCommand.CanExecute(null));
        Assert.True(viewModel.ExportReadablePolishedMarkdownCommand.CanExecute(null));
        Assert.True(viewModel.ExportReadablePolishedXlsxCommand.CanExecute(null));
        Assert.True(viewModel.ExportReadablePolishedDocxCommand.CanExecute(null));
    }

    [Fact]
    public void RawExportDialogFilterIncludesExcelFormat()
    {
        var createFilter = typeof(TranscriptExportDialogService).GetMethod(
            "CreateFilter",
            BindingFlags.NonPublic | BindingFlags.Static);
        var getFormatFromFilterIndex = typeof(TranscriptExportDialogService).GetMethod(
            "GetFormatFromFilterIndex",
            BindingFlags.NonPublic | BindingFlags.Static);

        var filter = Assert.IsType<string>(createFilter!.Invoke(
            null,
            [null, TranscriptExportSource.Raw]));
        var selectedFormat = Assert.IsType<TranscriptExportFormat>(getFormatFromFilterIndex!.Invoke(
            null,
            [7, TranscriptExportSource.Raw]));

        Assert.Contains("Excel workbook (*.xlsx)|*.xlsx", filter, StringComparison.Ordinal);
        Assert.Equal(TranscriptExportFormat.Xlsx, selectedFormat);
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

        viewModel.UseDetailLayoutCommand.Execute(null);
        viewModel.SelectedTranscriptTabIndex = 3;
        viewModel.ShowReadableTranscriptTabCommand.Execute(null);
        Assert.Equal(0, viewModel.SelectedTranscriptTabIndex);
        Assert.Equal("整文", viewModel.CurrentExportTargetDisplayName);
        Assert.True(viewModel.IsReadableExportMenuVisible);
        Assert.False(viewModel.IsRawExportMenuVisible);
        Assert.False(viewModel.IsMergeConsecutiveSpeakersExportOptionVisible);
        Assert.True(viewModel.IsCurrentExportTargetCopyVisible);

        viewModel.ShowRawTranscriptTabCommand.Execute(null);
        Assert.Equal(1, viewModel.SelectedTranscriptTabIndex);
        Assert.Equal("素起こし", viewModel.CurrentExportTargetDisplayName);
        Assert.True(viewModel.IsRawExportMenuVisible);
        Assert.False(viewModel.IsReadableExportMenuVisible);
        Assert.True(viewModel.IsMergeConsecutiveSpeakersExportOptionVisible);
        Assert.True(viewModel.IsCurrentExportTargetCopyVisible);

        viewModel.ShowDiffTranscriptTabCommand.Execute(null);
        Assert.Equal(2, viewModel.SelectedTranscriptTabIndex);
        Assert.Equal("差分", viewModel.CurrentExportTargetDisplayName);
        Assert.True(viewModel.IsDiffExportMenuVisible);
        Assert.False(viewModel.IsMergeConsecutiveSpeakersExportOptionVisible);
        Assert.False(viewModel.IsCurrentExportTargetCopyVisible);

        viewModel.ShowReviewCandidateTranscriptTabCommand.Execute(null);
        Assert.Equal(3, viewModel.SelectedTranscriptTabIndex);
        Assert.Equal("レビュー候補", viewModel.CurrentExportTargetDisplayName);
        Assert.True(viewModel.IsReviewCandidateExportMenuVisible);
        Assert.True(viewModel.IsMergeConsecutiveSpeakersExportOptionVisible);
        Assert.True(viewModel.IsCurrentExportTargetCopyVisible);
    }

    [Fact]
    public void ExportMenuTarget_TreatsStandardLayoutAsReadableView()
    {
        var viewModel = CreateViewModel();

        viewModel.UseDetailLayoutCommand.Execute(null);
        viewModel.SelectedTranscriptTabIndex = 3;
        Assert.Equal("レビュー候補", viewModel.CurrentExportTargetDisplayName);

        viewModel.UseStandardLayoutCommand.Execute(null);

        Assert.Equal(3, viewModel.SelectedTranscriptTabIndex);
        Assert.Equal("整文", viewModel.CurrentExportTargetDisplayName);
        Assert.True(viewModel.IsReadableExportMenuVisible);
        Assert.False(viewModel.IsReviewCandidateExportMenuVisible);
        Assert.False(viewModel.IsMergeConsecutiveSpeakersExportOptionVisible);
        Assert.True(viewModel.IsCurrentExportTargetCopyVisible);

        viewModel.IsStandardRawTranscriptViewSelected = true;
        Assert.Equal(1, viewModel.SelectedTranscriptTabIndex);
        Assert.Equal("素起こし", viewModel.CurrentExportTargetDisplayName);
        Assert.True(viewModel.IsRawExportMenuVisible);
        Assert.False(viewModel.IsReadableExportMenuVisible);
        Assert.True(viewModel.IsMergeConsecutiveSpeakersExportOptionVisible);
        Assert.True(viewModel.IsCurrentExportTargetCopyVisible);

        viewModel.IsStandardReadableTranscriptViewSelected = true;
        Assert.Equal(0, viewModel.SelectedTranscriptTabIndex);
        Assert.Equal("整文", viewModel.CurrentExportTargetDisplayName);
        Assert.True(viewModel.IsReadableExportMenuVisible);
        Assert.False(viewModel.IsRawExportMenuVisible);
        Assert.False(viewModel.IsMergeConsecutiveSpeakersExportOptionVisible);
        Assert.True(viewModel.IsCurrentExportTargetCopyVisible);
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
    public void InlineSegmentEdit_AutoSaveUsesModeFromEditStart()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        var repository = new TranscriptSegmentRepository(paths);
        repository.SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 5, "Speaker_0", "first raw", "first readable"),
            new TranscriptSegment("segment-002", job.JobId, 5, 10, "Speaker_1", "second raw", "second readable")
        ]);
        var viewModel = new MainWindowViewModel(paths);
        var first = viewModel.Segments.Single(segment => segment.SegmentId == "segment-001");
        var second = viewModel.Segments.Single(segment => segment.SegmentId == "segment-002");

        viewModel.IsStandardRawTranscriptViewSelected = true;
        viewModel.BeginSegmentInlineEditCommand.Execute(first);
        viewModel.SelectedSegmentEditText = "edited raw first";
        viewModel.IsStandardReadableTranscriptViewSelected = true;
        viewModel.SelectedSegment = second;

        Assert.False(viewModel.IsSegmentInlineEditActive);
        var updated = repository.ReadPreviews(job.JobId).Single(segment => segment.SegmentId == "segment-001");
        Assert.Equal("edited raw first", updated.RawTranscriptText);
        Assert.Equal("edited raw first", updated.Text);
    }

    [Fact]
    public void InlineRawSegmentEdit_AutoSaveDoesNotSelectOtherDraft()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 5, "Speaker_0", "first raw", "first readable"),
            new TranscriptSegment("segment-002", job.JobId, 5, 10, "Speaker_1", "second raw", "second readable")
        ]);
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", job.JobId, "segment-001", "wording", "first raw", "first fixed", "reason", 0.75),
            new CorrectionDraft("draft-002", job.JobId, "segment-002", "wording", "second raw", "second fixed", "reason", 0.75)
        ]);
        var viewModel = new MainWindowViewModel(paths);
        var first = viewModel.Segments.Single(segment => segment.SegmentId == "segment-001");
        var second = viewModel.Segments.Single(segment => segment.SegmentId == "segment-002");

        viewModel.IsStandardRawTranscriptViewSelected = true;
        viewModel.BeginSegmentInlineEditCommand.Execute(first);
        viewModel.SelectedCorrectionDraft = null;
        viewModel.SelectedSegmentEditText = "edited raw first";
        viewModel.SelectedSegment = second;

        Assert.Equal("segment-002", viewModel.SelectedSegment?.SegmentId);
        Assert.Null(viewModel.SelectedCorrectionDraft);
        Assert.Equal(
            "手修正済み",
            viewModel.Segments.Single(segment => segment.SegmentId == "segment-001").ReviewState);
    }

    [Fact]
    public void InlineRawSegmentEdit_AutoSaveClearsInvalidatedDraft()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 5, "Speaker_0", "first raw", "first readable"),
            new TranscriptSegment("segment-002", job.JobId, 5, 10, "Speaker_1", "second raw", "second readable")
        ]);
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", job.JobId, "segment-001", "wording", "first raw", "first fixed", "reason", 0.75)
        ]);
        var viewModel = new MainWindowViewModel(paths);
        var first = viewModel.Segments.Single(segment => segment.SegmentId == "segment-001");
        var second = viewModel.Segments.Single(segment => segment.SegmentId == "segment-002");
        Assert.Equal("draft-001", viewModel.SelectedCorrectionDraft?.DraftId);

        viewModel.IsStandardRawTranscriptViewSelected = true;
        viewModel.BeginSegmentInlineEditCommand.Execute(first);
        viewModel.SelectedSegmentEditText = "edited raw first";
        viewModel.SelectedSegment = second;

        Assert.Equal("segment-002", viewModel.SelectedSegment?.SegmentId);
        Assert.Null(viewModel.SelectedCorrectionDraft);
    }

    [Fact]
    public void SelectedTranscriptTabIndex_RefreshesSelectedSegmentEditBuffer()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 5, "Speaker_0", "first raw", "first readable")
        ]);
        var viewModel = new MainWindowViewModel(paths);

        Assert.Equal("first readable", viewModel.SelectedSegmentEditText);

        viewModel.SelectedTranscriptTabIndex = 1;

        Assert.Equal("first raw", viewModel.SelectedSegmentEditText);

        viewModel.SelectedTranscriptTabIndex = 0;

        Assert.Equal("first readable", viewModel.SelectedSegmentEditText);
    }

    [Fact]
    public void MainLayoutMode_RefreshesSelectedSegmentEditBuffer()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 5, "Speaker_0", "first raw", "first readable")
        ]);
        var viewModel = new MainWindowViewModel(paths);

        viewModel.UseDetailLayoutCommand.Execute(null);
        viewModel.ShowRawTranscriptTabCommand.Execute(null);
        Assert.Equal("first raw", viewModel.SelectedSegmentEditText);

        viewModel.UseStandardLayoutCommand.Execute(null);

        Assert.True(viewModel.IsStandardReadableTranscriptVisible);
        Assert.Equal("first readable", viewModel.SelectedSegmentEditText);
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
    public void RevertSegmentEditCommand_InRawViewDoesNotUndoFinalTextEdit()
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
        var editService = new TranscriptEditService(paths);
        editService.ApplyRawSegmentEdit(job.JobId, "segment-001", "raw edited");
        editService.ApplySegmentEdit(job.JobId, "segment-001", "final edited");
        var viewModel = new MainWindowViewModel(paths);
        var segment = Assert.Single(viewModel.Segments);

        viewModel.IsStandardRawTranscriptViewSelected = true;
        viewModel.RevertSegmentEditCommand.Execute(segment);

        var updated = repository.ReadPreviews(job.JobId).Single();
        Assert.Equal("raw edited", updated.RawTranscriptText);
        Assert.Equal("final edited", updated.Text);
    }

    [Fact]
    public void RevertSegmentEditCommand_RefreshesSelectedJobReviewBadges()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var jobRepository = new JobRepository(paths);
        var job = jobRepository.CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 5, "Speaker_0", "first raw")
        ]);
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", job.JobId, "segment-001", "wording", "first raw", "first fixed", "reason", 0.75)
        ]);
        jobRepository.MarkReviewCandidatesProcessed(job, pendingDraftCount: 1);
        new TranscriptEditService(paths).ApplyRawSegmentEdit(job.JobId, "segment-001", "first raw edited");
        var viewModel = new MainWindowViewModel(paths);
        var segment = Assert.Single(viewModel.Segments);

        Assert.Equal(0, viewModel.SelectedJobUnreviewedDrafts);

        viewModel.RevertSegmentEditCommand.Execute(segment);

        Assert.Equal(1, viewModel.SelectedJobUnreviewedDrafts);
        Assert.Equal(1, viewModel.ReviewQueue.Count);
    }

    [Fact]
    public void UndoLastOperationCommand_RefreshesSelectedJobReviewBadgesAfterRawUndo()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var jobRepository = new JobRepository(paths);
        var job = jobRepository.CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 5, "Speaker_0", "first raw")
        ]);
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", job.JobId, "segment-001", "wording", "first raw", "first fixed", "reason", 0.75)
        ]);
        jobRepository.MarkReviewCandidatesProcessed(job, pendingDraftCount: 1);
        new TranscriptEditService(paths).ApplyRawSegmentEdit(job.JobId, "segment-001", "first raw edited");
        var viewModel = new MainWindowViewModel(paths);

        Assert.Equal(0, viewModel.SelectedJobUnreviewedDrafts);

        viewModel.UndoLastOperationCommand.Execute(null);

        Assert.Equal(1, viewModel.SelectedJobUnreviewedDrafts);
        Assert.Equal(1, viewModel.ReviewQueue.Count);
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
    public async Task RunSelectedJobAsync_ConfirmsSpeakerNamesBeforeReadablePolishing()
    {
        var fixture = CreateRunReadyViewModel(
            enableReviewStage: true,
            new FakeTranscriptPolishingRuntime("[00:00 - 00:01] Speaker_0: readable text"));
        var job = fixture.ViewModel.SelectedJob ?? throw new InvalidOperationException("Selected job is required.");
        new TranscriptEditService(fixture.ViewModel.Paths).ApplySpeakerAlias(job.JobId, "Speaker_0", "佐藤");
        SpeakerNameConfirmationRequest? request = null;
        fixture.ViewModel.ConfirmSpeakerNamesDialog = dialogRequest =>
        {
            request = dialogRequest;
            return null;
        };

        await fixture.ViewModel.RunSelectedJobAsync();

        Assert.True(fixture.ReviewStageRunner.RunWasCalled);
        Assert.False(fixture.PolishingRuntime.WasCalled);
        Assert.NotNull(request);
        var speaker = Assert.Single(request.Speakers);
        Assert.Equal("佐藤", speaker.DisplayName);
        Assert.Equal("Speaker_0", speaker.SpeakerId);
        Assert.False(fixture.ViewModel.HasReadablePolishedContent);
    }

    [Fact]
    public async Task RunSelectedJobAsync_ShowsReviewCandidateConfirmationBeforeSpeakerNames()
    {
        var fixture = CreateRunReadyViewModel(
            enableReviewStage: true,
            new FakeTranscriptPolishingRuntime("[00:00 - 00:01] Speaker_0: readable text"));
        var job = fixture.ViewModel.SelectedJob ?? throw new InvalidOperationException("Selected job is required.");
        new CorrectionDraftRepository(fixture.ViewModel.Paths).SaveDrafts([
            new CorrectionDraft("draft-001", job.JobId, "segment-001", "wording", "raw text", "reviewed text", "候補", 0.8)
        ]);
        ReviewCandidateConfirmationRequest? request = null;
        var speakerConfirmationWasCalled = false;
        fixture.ViewModel.ConfirmReviewCandidatesDialog = dialogRequest =>
        {
            request = dialogRequest;
            return new ReviewCandidateConfirmationResult(
                ReviewCandidateConfirmationOutcome.Defer,
                dialogRequest.Candidates.Count,
                0,
                0,
                0,
                dialogRequest.Candidates.Count);
        };
        fixture.ViewModel.ConfirmSpeakerNamesDialog = dialogRequest =>
        {
            speakerConfirmationWasCalled = true;
            return SpeakerNameConfirmationResult.FromRequest(dialogRequest);
        };

        await fixture.ViewModel.RunSelectedJobAsync();

        Assert.True(fixture.ReviewStageRunner.RunWasCalled);
        Assert.NotNull(request);
        var candidate = Assert.Single(request.Candidates);
        Assert.Equal("draft-001", candidate.Draft.DraftId);
        Assert.Equal("Speaker_0", candidate.SpeakerName);
        Assert.Equal(0, candidate.StartSeconds);
        Assert.Equal(1, candidate.EndSeconds);
        Assert.False(speakerConfirmationWasCalled);
        Assert.False(fixture.PolishingRuntime.WasCalled);
        Assert.False(fixture.ViewModel.HasReadablePolishedContent);
        Assert.Contains("保留", fixture.ViewModel.LatestLog, StringComparison.Ordinal);
        Assert.Contains(
            new JobLogRepository(fixture.ViewModel.Paths).ReadForJob(job.JobId),
            entry => entry.Stage == "review_confirmation" &&
                entry.Message.Contains("outcome=Defer", StringComparison.Ordinal) &&
                entry.Message.Contains("remaining=1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunSelectedJobAsync_AppliesReviewCandidateBeforeReadablePolishing()
    {
        var fixture = CreateRunReadyViewModel(
            enableReviewStage: true,
            new FakeTranscriptPolishingRuntime("[00:00 - 00:01] Speaker_0: readable text"));
        var job = fixture.ViewModel.SelectedJob ?? throw new InvalidOperationException("Selected job is required.");
        new CorrectionDraftRepository(fixture.ViewModel.Paths).SaveDrafts([
            new CorrectionDraft("draft-001", job.JobId, "segment-001", "wording", "raw text", "reviewed text", "候補", 0.8)
        ]);
        fixture.ViewModel.ConfirmReviewCandidatesDialog = request =>
        {
            foreach (var candidate in request.Candidates)
            {
                var result = request.Operations.AcceptDraft(candidate.Draft.DraftId);
                request.RecordDecision?.Invoke(candidate.Draft, result, candidate.Draft.SuggestedText);
            }

            return new ReviewCandidateConfirmationResult(
                ReviewCandidateConfirmationOutcome.Continue,
                request.Candidates.Count,
                request.Candidates.Count,
                0,
                0,
                0);
        };
        fixture.ViewModel.ConfirmSpeakerNamesDialog = SpeakerNameConfirmationResult.FromRequest;

        await fixture.ViewModel.RunSelectedJobAsync();

        Assert.True(fixture.ReviewStageRunner.RunWasCalled);
        Assert.True(fixture.PolishingRuntime.WasCalled);
        Assert.Contains("reviewed text", fixture.PolishingRuntime.SeenSegmentTexts, StringComparer.Ordinal);
        Assert.Empty(fixture.ViewModel.ReviewQueue);
        Assert.True(fixture.ViewModel.HasReadablePolishedContent);
    }

    [Fact]
    public async Task RunSelectedJobAsync_CanceledReviewCandidateConfirmationSkipsReadablePolishing()
    {
        var fixture = CreateRunReadyViewModel(
            enableReviewStage: true,
            new FakeTranscriptPolishingRuntime("[00:00 - 00:01] Speaker_0: readable text"));
        var job = fixture.ViewModel.SelectedJob ?? throw new InvalidOperationException("Selected job is required.");
        new CorrectionDraftRepository(fixture.ViewModel.Paths).SaveDrafts([
            new CorrectionDraft("draft-001", job.JobId, "segment-001", "wording", "raw text", "reviewed text", "候補", 0.8)
        ]);
        fixture.ViewModel.ConfirmReviewCandidatesDialog = request => new ReviewCandidateConfirmationResult(
            ReviewCandidateConfirmationOutcome.Cancel,
            request.Candidates.Count,
            0,
            0,
            0,
            request.Candidates.Count);
        fixture.ViewModel.ConfirmSpeakerNamesDialog = SpeakerNameConfirmationResult.FromRequest;

        await fixture.ViewModel.RunSelectedJobAsync();

        Assert.False(fixture.PolishingRuntime.WasCalled);
        Assert.Contains("キャンセル", fixture.ViewModel.LatestLog, StringComparison.Ordinal);
        Assert.Contains(
            new JobLogRepository(fixture.ViewModel.Paths).ReadForJob(job.JobId),
            entry => entry.Stage == "review_confirmation" &&
                entry.Message.Contains("outcome=Cancel", StringComparison.Ordinal) &&
                entry.Message.Contains("remaining=1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunSelectedJobAsync_SkipsReviewCandidateConfirmationWhenNoPendingDrafts()
    {
        var fixture = CreateRunReadyViewModel(
            enableReviewStage: true,
            new FakeTranscriptPolishingRuntime("[00:00 - 00:01] Speaker_0: readable text"));
        var job = fixture.ViewModel.SelectedJob ?? throw new InvalidOperationException("Selected job is required.");
        var speakerConfirmationWasCalled = false;
        fixture.ViewModel.ConfirmReviewCandidatesDialog = _ => throw new InvalidOperationException("Review candidate dialog should not be shown.");
        fixture.ViewModel.ConfirmSpeakerNamesDialog = request =>
        {
            speakerConfirmationWasCalled = true;
            return SpeakerNameConfirmationResult.FromRequest(request);
        };

        await fixture.ViewModel.RunSelectedJobAsync();

        Assert.True(speakerConfirmationWasCalled);
        Assert.True(fixture.PolishingRuntime.WasCalled);
        Assert.Contains(
            new JobLogRepository(fixture.ViewModel.Paths).ReadForJob(job.JobId),
            entry => entry.Stage == "review_confirmation" &&
                entry.Message.Contains("skipped: no pending candidates", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunSelectedJobAsync_RejectedReviewCandidateIsNotUsedForReadablePolishing()
    {
        var fixture = CreateRunReadyViewModel(
            enableReviewStage: true,
            new FakeTranscriptPolishingRuntime("[00:00 - 00:01] Speaker_0: readable text"));
        var job = fixture.ViewModel.SelectedJob ?? throw new InvalidOperationException("Selected job is required.");
        new CorrectionDraftRepository(fixture.ViewModel.Paths).SaveDrafts([
            new CorrectionDraft("draft-001", job.JobId, "segment-001", "wording", "raw text", "rejected text", "候補", 0.8)
        ]);
        fixture.ViewModel.ConfirmReviewCandidatesDialog = request =>
        {
            var candidate = request.Candidates.Single();
            var result = request.Operations.RejectDraft(candidate.Draft.DraftId);
            request.RecordDecision?.Invoke(candidate.Draft, result, candidate.Draft.SuggestedText);
            return new ReviewCandidateConfirmationResult(
                ReviewCandidateConfirmationOutcome.Continue,
                request.Candidates.Count,
                0,
                1,
                0,
                0);
        };
        fixture.ViewModel.ConfirmSpeakerNamesDialog = SpeakerNameConfirmationResult.FromRequest;

        await fixture.ViewModel.RunSelectedJobAsync();

        Assert.True(fixture.PolishingRuntime.WasCalled);
        Assert.Contains("raw text", fixture.PolishingRuntime.SeenSegmentTexts, StringComparer.Ordinal);
        Assert.DoesNotContain("rejected text", fixture.PolishingRuntime.SeenSegmentTexts, StringComparer.Ordinal);
        Assert.Contains(
            new JobLogRepository(fixture.ViewModel.Paths).ReadForJob(job.JobId),
            entry => entry.Stage == "review_confirmation" &&
                entry.Message.Contains("outcome=Continue", StringComparison.Ordinal) &&
                entry.Message.Contains("rejected=1", StringComparison.Ordinal) &&
                entry.Message.Contains("remaining=0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunSelectedJobAsync_PartialReviewCandidateDecisionKeepsJobAndQueueInSync()
    {
        var fixture = CreateRunReadyViewModel(
            enableReviewStage: true,
            new FakeTranscriptPolishingRuntime("[00:00 - 00:01] Speaker_0: readable text"));
        var job = fixture.ViewModel.SelectedJob ?? throw new InvalidOperationException("Selected job is required.");
        new CorrectionDraftRepository(fixture.ViewModel.Paths).SaveDrafts([
            new CorrectionDraft("draft-001", job.JobId, "segment-001", "wording", "raw text", "reviewed text", "候補", 0.8),
            new CorrectionDraft("draft-002", job.JobId, "segment-001", "wording", "reviewed text", "reviewed text final", "候補", 0.7)
        ]);
        fixture.ViewModel.ConfirmReviewCandidatesDialog = request =>
        {
            var candidate = request.Candidates.First();
            var result = request.Operations.AcceptDraft(candidate.Draft.DraftId);
            request.RecordDecision?.Invoke(candidate.Draft, result, candidate.Draft.SuggestedText);
            return new ReviewCandidateConfirmationResult(
                ReviewCandidateConfirmationOutcome.Defer,
                request.Candidates.Count,
                1,
                0,
                0,
                1);
        };
        fixture.ViewModel.ConfirmSpeakerNamesDialog = SpeakerNameConfirmationResult.FromRequest;

        await fixture.ViewModel.RunSelectedJobAsync();

        Assert.False(fixture.PolishingRuntime.WasCalled);
        Assert.Equal(1, fixture.ViewModel.SelectedJobUnreviewedDrafts);
        var pendingDraft = Assert.Single(fixture.ViewModel.ReviewQueue);
        Assert.Equal("draft-002", pendingDraft.DraftId);
        Assert.Equal("draft-002", fixture.ViewModel.SelectedCorrectionDraftId);
        Assert.Equal("segment-001", fixture.ViewModel.SelectedSegment?.SegmentId);
        Assert.Contains("保留", fixture.ViewModel.LatestLog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunSelectedJobAsync_ReloadsStaleReadablePolishedContentWhenSpeakerConfirmationIsCanceled()
    {
        var fixture = CreateRunReadyViewModel(
            enableReviewStage: true,
            new FakeTranscriptPolishingRuntime("[00:00 - 00:01] Speaker_0: readable text"));
        var job = fixture.ViewModel.SelectedJob ?? throw new InvalidOperationException("Selected job is required.");
        var paths = fixture.ViewModel.Paths;
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "old raw text")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(paths);
        derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
            job.JobId,
            TranscriptDerivativeKinds.Polished,
            TranscriptDerivativeFormats.PlainText,
            "[00:00 - 00:01] Speaker_0: existing polished",
            TranscriptDerivativeSourceKinds.Raw,
            derivativeRepository.ComputeCurrentRawTranscriptHash(job.JobId),
            "segment-001..segment-001",
            null,
            "model",
            "prompt",
            "profile"));
        fixture.ViewModel.SelectedJob = null;
        fixture.ViewModel.SelectedJob = fixture.ViewModel.Jobs.Single(item => item.JobId == job.JobId);
        fixture.ViewModel.ConfirmSpeakerNamesDialog = _ => null;

        await fixture.ViewModel.RunSelectedJobAsync();

        Assert.True(fixture.ReviewStageRunner.RunWasCalled);
        Assert.False(fixture.PolishingRuntime.WasCalled);
        Assert.True(fixture.ViewModel.HasReadablePolishedContent);
        Assert.Contains("existing polished", fixture.ViewModel.ReadablePolishedContent, StringComparison.Ordinal);
        Assert.Contains("古い整文", fixture.ViewModel.ReadablePolishedStatus, StringComparison.Ordinal);
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
