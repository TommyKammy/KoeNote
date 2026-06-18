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
public sealed class MainWindowViewModelUpdateTests : MainWindowViewModelTestBase
{
    [Fact]
    public void SettingsReviewModelSelection_UpdatesCommittedReviewModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            CurrentStep = SetupStep.ReviewModel,
            SelectedReviewModelId = "llm-jp-4-8b-thinking-q4-k-m"
        });
        var viewModel = new MainWindowViewModel(paths);
        var bonsai = viewModel.SetupReviewModelChoices.Single(entry => entry.ModelId == "bonsai-8b-q1-0");

        viewModel.SelectedSettingsReviewModel = bonsai;

        var state = new SetupStateService(paths).Load();
        Assert.Equal("bonsai-8b-q1-0", state.SelectedReviewModelId);
        Assert.Equal("custom", state.SetupMode);
        Assert.Equal("bonsai-8b-q1-0", viewModel.SelectedSettingsReviewModel?.ModelId);
        Assert.Equal("bonsai-8b-q1-0", viewModel.SelectedSetupReviewModel?.ModelId);
        Assert.Equal(ReadablePolishingPromptModelFamilies.Bonsai, viewModel.SelectedReadablePolishingPromptModelFamily?.ModelFamily);
    }

    [Fact]
    public async Task DeleteAndRestoreJob_UpdatesActiveAndHistoryLists()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        var viewModel = new MainWindowViewModel(new AppPaths(root, root, AppContext.BaseDirectory));
        var job = viewModel.RegisterAudioFile(audioPath);
        new JobRepository(viewModel.Paths).MarkPreprocessSucceeded(job, Path.Combine(root, "normalized.wav"));
        var confirmations = 0;
        viewModel.ConfirmAction = (_, _) =>
        {
            confirmations++;
            return true;
        };

        viewModel.DeleteJobCommand.Execute(job);
        for (var i = 0; i < 20 && viewModel.DeletedJobs.Count == 0; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.Empty(viewModel.Jobs);
        var deleted = Assert.Single(viewModel.DeletedJobs);
        Assert.True(deleted.IsDeleted);
        Assert.StartsWith("履歴 1 件 / ", viewModel.DeletedJobCountSummary, StringComparison.Ordinal);

        viewModel.RestoreJobCommand.Execute(deleted);
        for (var i = 0; i < 20 && viewModel.Jobs.Count == 0; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.Empty(viewModel.DeletedJobs);
        Assert.Single(viewModel.Jobs);
        Assert.Equal(job.JobId, viewModel.SelectedJob?.JobId);
        Assert.Equal(1, confirmations);
    }

    [Fact]
    public async Task DeleteJobCommand_UpdatesDeletedHistoryStorageSummary()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        var viewModel = new MainWindowViewModel(new AppPaths(root, root, AppContext.BaseDirectory));
        var job = viewModel.RegisterAudioFile(audioPath);
        new JobRepository(viewModel.Paths).MarkPreprocessSucceeded(job, Path.Combine(root, "normalized.wav"));
        var jobDirectory = Path.Combine(viewModel.Paths.Jobs, job.JobId);
        Directory.CreateDirectory(jobDirectory);
        File.WriteAllText(Path.Combine(jobDirectory, "artifact.txt"), "artifact");
        viewModel.ConfirmAction = (_, _) => true;

        viewModel.DeleteJobCommand.Execute(job);
        for (var i = 0; i < 20 && viewModel.DeletedJobs.Count == 0; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        var deleted = Assert.Single(viewModel.DeletedJobs);
        Assert.True(deleted.StorageBytes >= "artifact".Length);
        Assert.False(viewModel.DeletedJobCountSummary.EndsWith("/ 0 B", StringComparison.Ordinal), viewModel.DeletedJobCountSummary);
    }

    [Fact]
    public void ImportDomainPresetFromFile_UpdatesAsrSettingsInViewModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        var presetPath = Path.Combine(root, "preset.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(presetPath, """
            {
              "schema_version": 1,
              "display_name": "産科・産後ケア研究プリセット",
              "asr_context": "産後ケア研究のインタビューです。",
              "hotwords": ["産後ケア", "助産師"]
            }
            """);
        var viewModel = new MainWindowViewModel(paths)
        {
            AsrContextText = "既存設定",
            AsrHotwordsText = "産後ケア"
        };

        viewModel.ImportDomainPresetFromFile(presetPath);

        Assert.True(viewModel.HasLoadedDomainPreset);
        Assert.Contains("産科・産後ケア研究プリセット", viewModel.LoadedDomainPresetSummary, StringComparison.Ordinal);
        Assert.Contains("産後ケア研究のインタビューです。", viewModel.LoadedDomainPresetDetails, StringComparison.Ordinal);
        Assert.Contains("助産師", viewModel.LoadedDomainPresetDetails, StringComparison.Ordinal);
        Assert.Equal("既存設定", viewModel.AsrContextText);
        Assert.Equal("産後ケア", viewModel.AsrHotwordsText);

        viewModel.ApplyLoadedDomainPresetCommand.Execute(null);

        Assert.Contains("既存設定", viewModel.AsrContextText, StringComparison.Ordinal);
        Assert.Contains("産後ケア研究のインタビューです。", viewModel.AsrContextText, StringComparison.Ordinal);
        Assert.Equal(
            ["産後ケア", "助産師"],
            viewModel.AsrHotwordsText.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("プリセットをインポートしました", viewModel.LatestLog, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadablePolishedTab_UpdatesWhenSelectedJobChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var repository = new JobRepository(paths);
        var first = repository.CreateFromAudio(Path.Combine(root, "first.wav"));
        var second = repository.CreateFromAudio(Path.Combine(root, "second.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", first.JobId, 0, 1, "Speaker_0", "first raw"),
            new TranscriptSegment("segment-002", second.JobId, 0, 1, "Speaker_0", "second raw")
        ]);
        new TranscriptDerivativeRepository(paths).Save(new TranscriptDerivativeSaveRequest(
            second.JobId,
            TranscriptDerivativeKinds.Polished,
            TranscriptDerivativeFormats.PlainText,
            "[00:00 - 00:01] Speaker_0: second readable",
            TranscriptDerivativeSourceKinds.Raw,
            "hash",
            "segment-002..segment-002",
            null,
            "model",
            "prompt",
            "profile"));
        var viewModel = new MainWindowViewModel(paths);

        viewModel.SelectedJob = first;
        Assert.False(viewModel.HasReadablePolishedContent);
        Assert.Equal("読みやすい文書を生成", viewModel.ReadablePolishedActionText);

        viewModel.SelectedJob = second;
        Assert.True(viewModel.HasReadablePolishedContent);
        Assert.Contains("second readable", viewModel.ReadablePolishedContent, StringComparison.Ordinal);
        Assert.Equal("再生成", viewModel.ReadablePolishedActionText);
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
    public void ReviewStageSkippedRunUpdate_UsesLocalizedSkippedStatus()
    {
        var viewModel = CreateViewModel();
        var reviewStage = viewModel.StageStatuses.Single(stage => stage.IsToggleable);
        var method = typeof(MainWindowViewModel).GetMethod(
            "ApplyRunUpdate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        method?.Invoke(viewModel, [
            new JobRunUpdate(
                JobRunStage.Review,
                JobRunStageState.Skipped,
                100)
        ]);

        Assert.Equal("スキップ", reviewStage.Status);
        Assert.True(reviewStage.IsSkipped);
    }

    [Fact]
    public void ReviewSucceededRunUpdate_SelectsReadableTranscriptTabAndHighlightsReviewCandidateTab()
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedTranscriptTabIndex = 2;
        var method = typeof(MainWindowViewModel).GetMethod(
            "ApplyRunUpdate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        method?.Invoke(viewModel, [
            new JobRunUpdate(
                JobRunStage.Review,
                JobRunStageState.Succeeded,
                100)
        ]);

        Assert.Equal(0, viewModel.SelectedTranscriptTabIndex);
        Assert.True(viewModel.IsPolishedTranscriptTabHighlighted);
        Assert.Equal("Highlighted", viewModel.PolishedTranscriptTabHighlightTag);
    }

    [Fact]
    public void SummaryRunUpdate_SetsSummaryRunningFlagWithoutChangingTimeline()
    {
        var viewModel = CreateViewModel();
        var method = typeof(MainWindowViewModel).GetMethod(
            "ApplyRunUpdate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        method?.Invoke(viewModel, [
            new JobRunUpdate(
                JobRunStage.Summary,
                JobRunStageState.Running,
                0)
        ]);

        Assert.True(viewModel.IsSummaryStageRunning);
        Assert.Equal(3, viewModel.StageStatuses.Count);
        Assert.DoesNotContain(viewModel.StageStatuses, stage => stage.IsRunning);
    }

    [Fact]
    public void SummaryRunUpdate_ClearsSummaryRunningFlagWhenSucceeded()
    {
        var viewModel = CreateViewModel();
        var method = typeof(MainWindowViewModel).GetMethod(
            "ApplyRunUpdate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        method?.Invoke(viewModel, [
            new JobRunUpdate(
                JobRunStage.Summary,
                JobRunStageState.Running,
                100)
        ]);
        method?.Invoke(viewModel, [
            new JobRunUpdate(
                JobRunStage.Summary,
                JobRunStageState.Succeeded,
                100)
        ]);

        Assert.False(viewModel.IsSummaryStageRunning);
        Assert.DoesNotContain(viewModel.StageStatuses, stage => stage.IsDone);
    }

    [Fact]
    public void JobRunUpdate_SeparatesJobProgressFromStageProgress()
    {
        var viewModel = CreateViewModel();
        var job = viewModel.RegisterAudioFile(@"C:\audio\meeting.wav");
        var method = typeof(MainWindowViewModel).GetMethod(
            "ApplyRunUpdate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        method?.Invoke(viewModel, [
            new JobRunUpdate(
                JobRunStage.Asr,
                JobRunStageState.Running,
                StageProgressPercent: 35)
        ]);

        Assert.Equal(0, job.ProgressPercent);
        Assert.Equal(35, viewModel.StageStatuses.Single(stage => stage.Name == "ASR").ProgressPercent);

        method?.Invoke(viewModel, [
            new JobRunUpdate(JobProgressPercent: 55)
        ]);

        Assert.Equal(55, job.ProgressPercent);
        Assert.Equal(35, viewModel.StageStatuses.Single(stage => stage.Name == "ASR").ProgressPercent);
    }

    [Fact]
    public void StageRunUpdate_UsesCustomStageStatusText()
    {
        var viewModel = CreateViewModel();
        var method = typeof(MainWindowViewModel).GetMethod(
            "ApplyRunUpdate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        method?.Invoke(viewModel, [
            new JobRunUpdate(
                JobRunStage.Review,
                JobRunStageState.Running,
                45,
                StageStatusText: "custom polishing status")
        ]);

        var reviewStage = viewModel.StageStatuses.Last();
        Assert.Equal("custom polishing status", reviewStage.Status);
        Assert.Equal(45, reviewStage.ProgressPercent);
    }

    [Fact]
    public void StageRunUpdate_DoesNotMarkSkippedReviewStageDone()
    {
        var viewModel = CreateViewModel();
        var method = typeof(MainWindowViewModel).GetMethod(
            "ApplyRunUpdate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        method?.Invoke(viewModel, [
            new JobRunUpdate(
                JobRunStage.Review,
                JobRunStageState.Skipped,
                100)
        ]);

        var reviewStage = viewModel.StageStatuses.Last();
        Assert.True(reviewStage.IsSkipped);
        Assert.False(reviewStage.IsDone);
        Assert.False(reviewStage.IsDoing);
    }

    [Fact]
    public void AsrNativeCrashRunUpdate_ShowsActionableLatestLog()
    {
        var viewModel = CreateViewModel();
        var method = typeof(MainWindowViewModel).GetMethod(
            "ApplyRunUpdate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        method?.Invoke(viewModel, [
            new JobRunUpdate(
                JobRunStage.Asr,
                JobRunStageState.Failed,
                100,
                ErrorCategory: AsrFailureCategory.NativeCrash.ToString())
        ]);

        Assert.Contains("ASR native runtime crashed", viewModel.LatestLog, StringComparison.Ordinal);
        Assert.Contains("diagnostic package", viewModel.LatestLog, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallVerifiedUpdateCommand_IsEnabledOnlyWhenInstallerIsReadyAndNoJobIsRunning()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.InstallVerifiedUpdateCommand.CanExecute(null));

        SetPrivateProperty(viewModel, nameof(MainWindowViewModel.VerifiedUpdateInstallerPath), @"C:\updates\KoeNote.msi");

        Assert.True(viewModel.InstallVerifiedUpdateCommand.CanExecute(null));

        SetPrivateProperty(viewModel, nameof(MainWindowViewModel.IsRunInProgress), true);

        Assert.False(viewModel.InstallVerifiedUpdateCommand.CanExecute(null));
    }

    [Fact]
    public void UpToDateUpdateCheck_ClearsPreviouslyVerifiedInstaller()
    {
        var viewModel = CreateViewModel();
        SetPrivateProperty(viewModel, nameof(MainWindowViewModel.VerifiedUpdateInstallerPath), @"C:\updates\KoeNote.msi");
        Assert.True(viewModel.CanShowInstallUpdateAction);

        InvokePrivate(
            viewModel,
            "ApplyUpdateCheckResult",
            new UpdateCheckResult(
                true,
                false,
                false,
                "0.14.0",
                null,
                "KoeNote is up to date (0.14.0)."),
            true,
            false);

        Assert.Empty(viewModel.VerifiedUpdateInstallerPath);
        Assert.Empty(viewModel.UpdateDownloadProgressText);
        Assert.False(viewModel.CanShowInstallUpdateAction);
        Assert.False(viewModel.InstallVerifiedUpdateCommand.CanExecute(null));
    }

    [Fact]
    public void StartupUpToDateUpdateCheck_DoesNotOverwriteLatestLog()
    {
        var viewModel = CreateViewModel();
        SetPrivateProperty(viewModel, nameof(MainWindowViewModel.LatestLog), "User action completed.");

        InvokePrivate(
            viewModel,
            "ApplyUpdateCheckResult",
            new UpdateCheckResult(
                true,
                false,
                false,
                "0.14.0",
                null,
                "KoeNote is up to date (0.14.0)."),
            false,
            false);

        Assert.Equal("User action completed.", viewModel.LatestLog);
    }

    [Fact]
    public async Task InstallVerifiedUpdateCommand_DisablesInstallAfterStartingInstaller()
    {
        var viewModel = CreateViewModel();
        var installerPath = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "KoeNote.msi");
        var launcher = new RecordingUpdateInstallerLauncher();
        var shutdownRequested = false;
        Action requestShutdown = () => shutdownRequested = true;
        SetPrivateField(viewModel, "_updateInstallerLauncher", launcher);
        SetPrivateField(viewModel, "_shutdownApplication", requestShutdown);
        SetPrivateProperty(viewModel, nameof(MainWindowViewModel.VerifiedUpdateInstallerPath), installerPath);

        viewModel.InstallVerifiedUpdateCommand.Execute(null);
        await Task.Delay(50);

        Assert.Equal(installerPath, launcher.StartedInstallerPath);
        Assert.Empty(viewModel.VerifiedUpdateInstallerPath);
        Assert.False(viewModel.CanShowInstallUpdateAction);
        Assert.False(viewModel.InstallVerifiedUpdateCommand.CanExecute(null));
        Assert.Contains("Update and restart started", viewModel.UpdateDownloadProgressText, StringComparison.Ordinal);
        Assert.True(shutdownRequested);
    }

    [Fact]
    public void UpdateAndRestartCommand_IsSinglePrimaryActionAndBlocksDuringRuns()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.CanShowUpdateRestartAction);
        Assert.False(viewModel.UpdateAndRestartCommand.CanExecute(null));

        InvokePrivate(
            viewModel,
            "ApplyUpdateCheckResult",
            new UpdateCheckResult(
                true,
                true,
                false,
                "0.14.0",
                CreateUpdateRelease(),
                "KoeNote 0.15.0 is available."),
            true,
            false);

        Assert.True(viewModel.CanShowUpdateRestartAction);
        Assert.Equal("Update and restart", viewModel.UpdateRestartActionText);
        Assert.True(viewModel.UpdateAndRestartCommand.CanExecute(null));
        Assert.Contains("Update and restart", viewModel.UpdateNotificationMessage, StringComparison.Ordinal);

        SetPrivateProperty(viewModel, nameof(MainWindowViewModel.IsRunInProgress), true);

        Assert.False(viewModel.UpdateAndRestartCommand.CanExecute(null));
        Assert.True(viewModel.HasUpdateRestartBlockedReason);
        Assert.Contains("Finish or cancel", viewModel.UpdateRestartBlockedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateRestartBlockedReason_StaysHiddenWhenNoUpdateIsAvailable()
    {
        var viewModel = CreateViewModel();
        InvokePrivate(
            viewModel,
            "ApplyUpdateCheckResult",
            new UpdateCheckResult(
                true,
                false,
                false,
                "0.15.0",
                null,
                "KoeNote is up to date (0.15.0)."),
            true,
            false);

        SetPrivateProperty(viewModel, nameof(MainWindowViewModel.IsRunInProgress), true);

        Assert.False(viewModel.CanShowUpdateRestartAction);
        Assert.Empty(viewModel.UpdateRestartBlockedReason);
        Assert.False(viewModel.HasUpdateRestartBlockedReason);
    }

    [Fact]
    public async Task FailedUpdateCheck_ClearsUpdateRestartBlockedReason()
    {
        var viewModel = CreateViewModel();
        InvokePrivate(
            viewModel,
            "ApplyUpdateCheckResult",
            new UpdateCheckResult(
                true,
                true,
                false,
                "0.14.0",
                CreateUpdateRelease(),
                "KoeNote 0.15.0 is available."),
            true,
            false);
        SetPrivateProperty(viewModel, nameof(MainWindowViewModel.IsRunInProgress), true);
        Assert.True(viewModel.HasUpdateRestartBlockedReason);
        SetPrivateField(viewModel, "_updateCheckService", new ThrowingUpdateCheckService());

        await InvokePrivate<Task>(viewModel, "CheckForUpdatesAsync");

        Assert.False(viewModel.CanShowUpdateRestartAction);
        Assert.Empty(viewModel.UpdateRestartBlockedReason);
        Assert.False(viewModel.HasUpdateRestartBlockedReason);
        Assert.Equal("Update check failed", viewModel.UpdateNotificationTitle);
    }

    [Fact]
    public async Task UpdateAndRestartCommand_DownloadsUnverifiedUpdateThenStartsInstaller()
    {
        var viewModel = CreateViewModel();
        var installerPath = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "KoeNote.msi");
        var release = CreateUpdateRelease();
        var downloadService = new RecordingUpdateDownloadService(installerPath, release.Sha256);
        var launcher = new RecordingUpdateInstallerLauncher();
        var shutdownRequested = false;
        Action requestShutdown = () => shutdownRequested = true;
        SetPrivateField(viewModel, "_updateDownloadService", downloadService);
        SetPrivateField(viewModel, "_updateInstallerLauncher", launcher);
        SetPrivateField(viewModel, "_shutdownApplication", requestShutdown);
        InvokePrivate(
            viewModel,
            "ApplyUpdateCheckResult",
            new UpdateCheckResult(
                true,
                true,
                false,
                "0.14.0",
                release,
                "KoeNote 0.15.0 is available."),
            true,
            false);

        viewModel.UpdateAndRestartCommand.Execute(null);
        for (var i = 0; i < 20 && launcher.StartedInstallerPath is null; i++)
        {
            await Task.Delay(50);
        }

        Assert.Equal(1, downloadService.DownloadCount);
        Assert.Equal(installerPath, launcher.StartedInstallerPath);
        Assert.Empty(viewModel.VerifiedUpdateInstallerPath);
        Assert.True(shutdownRequested);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_StartsBackgroundDownloadAndMarksInstallerReady()
    {
        var viewModel = CreateViewModel();
        var installerPath = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "KoeNote.msi");
        var release = CreateUpdateRelease();
        var downloadService = new RecordingUpdateDownloadService(installerPath, release.Sha256);
        SetPrivateField(viewModel, "_updateDownloadService", downloadService);
        SetPrivateField(
            viewModel,
            "_updateCheckService",
            new ReturningUpdateCheckService(new UpdateCheckResult(
                true,
                true,
                false,
                "0.14.0",
                release,
                "KoeNote 0.15.0 is available.")));

        await InvokePrivate<Task>(viewModel, "CheckForUpdatesAsync");

        for (var i = 0; i < 20 && !viewModel.HasVerifiedUpdateInstaller; i++)
        {
            await Task.Delay(50);
        }

        Assert.Equal(1, downloadService.DownloadCount);
        Assert.Equal(installerPath, viewModel.VerifiedUpdateInstallerPath);
        Assert.Equal("Ready to update and restart: KoeNote 0.15.0", viewModel.UpdateNotificationTitle);
        Assert.False(viewModel.CanShowUpdateDownloadAction);
        Assert.True(viewModel.UpdateAndRestartCommand.CanExecute(null));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_BackgroundDownloadFailureKeepsManualRetryAvailable()
    {
        var viewModel = CreateViewModel();
        var release = CreateUpdateRelease();
        SetPrivateField(viewModel, "_updateDownloadService", new ThrowingUpdateDownloadService());
        SetPrivateField(
            viewModel,
            "_updateCheckService",
            new ReturningUpdateCheckService(new UpdateCheckResult(
                true,
                true,
                false,
                "0.14.0",
                release,
                "KoeNote 0.15.0 is available.")));

        await InvokePrivate<Task>(viewModel, "CheckForUpdatesAsync");

        for (var i = 0; i < 20 && viewModel.IsUpdateDownloadInProgress; i++)
        {
            await Task.Delay(50);
        }

        Assert.Equal("Update download failed", viewModel.UpdateNotificationTitle);
        Assert.Contains("download unavailable", viewModel.UpdateNotificationMessage, StringComparison.Ordinal);
        Assert.True(viewModel.CanShowUpdateDownloadAction);
        Assert.True(viewModel.DownloadUpdateCommand.CanExecute(null));
        Assert.True(viewModel.UpdateAndRestartCommand.CanExecute(null));
    }

    [Fact]
    public async Task DismissUpdateNotification_IgnoresInFlightBackgroundDownloadCompletion()
    {
        var viewModel = CreateViewModel();
        var installerPath = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "KoeNote.msi");
        var release = CreateUpdateRelease();
        var downloadService = new ControlledUpdateDownloadService(installerPath, release.Sha256);
        SetPrivateField(viewModel, "_updateDownloadService", downloadService);
        SetPrivateField(
            viewModel,
            "_updateCheckService",
            new ReturningUpdateCheckService(new UpdateCheckResult(
                true,
                true,
                false,
                "0.14.0",
                release,
                "KoeNote 0.15.0 is available.")));

        await InvokePrivate<Task>(viewModel, "CheckForUpdatesAsync");
        for (var i = 0; i < 20 && !viewModel.IsUpdateDownloadInProgress; i++)
        {
            await Task.Delay(50);
        }

        Assert.True(viewModel.IsUpdateDownloadInProgress);

        viewModel.DismissUpdateNotificationCommand.Execute(null);
        downloadService.Complete();
        for (var i = 0; i < 20 && viewModel.IsUpdateDownloadInProgress; i++)
        {
            await Task.Delay(50);
        }

        Assert.Empty(viewModel.UpdateNotificationTitle);
        Assert.Empty(viewModel.UpdateNotificationMessage);
        Assert.Empty(viewModel.UpdateDownloadProgressText);
        Assert.Empty(viewModel.VerifiedUpdateInstallerPath);
        Assert.False(viewModel.HasUpdateNotification);
    }

    [Fact]
    public void Constructor_SurfacesPendingUpdaterFailureResult()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        Directory.CreateDirectory(paths.UpdateDownloads);
        var resultPath = Path.Combine(paths.UpdateDownloads, "KoeNote-update-0.20.0.result.json");
        File.WriteAllText(resultPath, """
            {
              "Status": "InstallFailed",
              "ExitCode": 20,
              "Version": "0.20.0",
              "InstallerPath": "KoeNote.msi",
              "TargetExePath": "KoeNote.App.exe",
              "LogPath": "install.log",
              "CompletedAt": "2026-06-18T00:00:00Z",
              "Message": "msiexec exited with code 3010. Windows reported that a restart is required."
            }
            """);

        var viewModel = new MainWindowViewModel(paths);

        Assert.Equal("Update failed", viewModel.UpdateNotificationTitle);
        Assert.Contains("3010", viewModel.UpdateNotificationMessage, StringComparison.Ordinal);
        Assert.Contains("install.log", viewModel.UpdateNotificationMessage, StringComparison.Ordinal);
        Assert.Contains("Update failed", viewModel.LatestLog, StringComparison.Ordinal);
        Assert.False(File.Exists(resultPath));
        Assert.True(File.Exists(resultPath + ".seen"));
    }

    [Fact]
    public void Constructor_SurfacesPendingRebootResultAsRestartRequired()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        Directory.CreateDirectory(paths.UpdateDownloads);
        var resultPath = Path.Combine(paths.UpdateDownloads, "KoeNote-update-0.20.0.result.json");
        File.WriteAllText(resultPath, """
            {
              "Status": "PendingReboot",
              "ExitCode": 50,
              "Version": "0.20.0",
              "InstallerPath": "KoeNote.msi",
              "TargetExePath": "KoeNote.App.exe",
              "LogPath": "install.log",
              "CompletedAt": "2026-06-18T00:00:00Z",
              "Message": "Update installed, but Windows reported that a restart is required to complete installation."
            }
            """);

        var viewModel = new MainWindowViewModel(paths);

        Assert.Equal("Restart required to complete update: KoeNote 0.20.0", viewModel.UpdateNotificationTitle);
        Assert.Contains("restart is required", viewModel.UpdateNotificationMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Update failed", viewModel.LatestLog, StringComparison.Ordinal);
        Assert.False(File.Exists(resultPath));
        Assert.True(File.Exists(resultPath + ".seen"));
    }

    [Fact]
    public async Task StartupUpdateCheckAfterPendingFailurePreservesRetryAction()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        Directory.CreateDirectory(paths.UpdateDownloads);
        File.WriteAllText(Path.Combine(paths.UpdateDownloads, "KoeNote-update-0.20.0.result.json"), """
            {
              "Status": "InstallFailed",
              "ExitCode": 20,
              "Version": "0.20.0",
              "InstallerPath": "KoeNote.msi",
              "TargetExePath": "KoeNote.App.exe",
              "LogPath": "install.log",
              "CompletedAt": "2026-06-18T00:00:00Z",
              "Message": "msiexec exited with code 3010. Windows reported that a restart is required."
            }
            """);
        var viewModel = new MainWindowViewModel(paths);
        SetPrivateField(
            viewModel,
            "_updateCheckService",
            new ReturningUpdateCheckService(new UpdateCheckResult(
                true,
                true,
                false,
                "0.19.0",
                CreateUpdateRelease(),
                "KoeNote 0.15.0 is available.")));

        await InvokePrivate<Task>(viewModel, "CheckForUpdatesOnStartupAsync");

        Assert.Equal("Update failed", viewModel.UpdateNotificationTitle);
        Assert.Contains("3010", viewModel.UpdateNotificationMessage, StringComparison.Ordinal);
        Assert.True(viewModel.CanShowUpdateDownloadAction);
        Assert.True(viewModel.UpdateAndRestartCommand.CanExecute(null));
        Assert.Equal("0.15.0", viewModel.AvailableUpdateVersion);
    }

    private static LatestReleaseInfo CreateUpdateRelease()
    {
        return new LatestReleaseInfo(
            "0.15.0",
            new Uri("https://example.test/KoeNote-v0.15.0-win-x64.msi"),
            new string('a', 64),
            null,
            new Uri("https://example.test/releases/0.15.0"),
            false,
            "win-x64",
            DateTimeOffset.Parse("2026-06-18T00:00:00Z"));
    }

    private sealed class RecordingUpdateDownloadService(string installerPath, string sha256) : IUpdateDownloadService
    {
        public int DownloadCount { get; private set; }

        public Task<UpdateDownloadResult> DownloadAndVerifyAsync(
            LatestReleaseInfo release,
            IProgress<UpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DownloadCount++;
            progress?.Report(new UpdateDownloadProgress(10, 10));
            return Task.FromResult(new UpdateDownloadResult(
                installerPath,
                sha256,
                10,
                DateTimeOffset.Now));
        }
    }

    private sealed class ThrowingUpdateDownloadService : IUpdateDownloadService
    {
        public Task<UpdateDownloadResult> DownloadAndVerifyAsync(
            LatestReleaseInfo release,
            IProgress<UpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("download unavailable");
        }
    }

    private sealed class ControlledUpdateDownloadService(string installerPath, string sha256) : IUpdateDownloadService
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<UpdateDownloadResult> DownloadAndVerifyAsync(
            LatestReleaseInfo release,
            IProgress<UpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(new UpdateDownloadProgress(1, 10));
            await _completion.Task.WaitAsync(cancellationToken);
            progress?.Report(new UpdateDownloadProgress(10, 10));
            return new UpdateDownloadResult(
                installerPath,
                sha256,
                10,
                DateTimeOffset.Now);
        }

        public void Complete()
        {
            _completion.SetResult();
        }
    }

    private sealed class ThrowingUpdateCheckService : IUpdateCheckService
    {
        public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("latest.json unavailable");
        }
    }

    private sealed class ReturningUpdateCheckService(UpdateCheckResult result) : IUpdateCheckService
    {
        public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }

    [Fact]
    public void ReadablePolishingPromptSettings_SelectActiveModelFamilyCommandUpdatesStatusWhenSelectionDoesNotChange()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectActiveReadablePolishingPromptModelFamilyCommand.Execute(null);

        Assert.Equal(ReadablePolishingPromptModelFamilies.Gemma, viewModel.SelectedReadablePolishingPromptModelFamily?.ModelFamily);
        Assert.Contains("Gemma 4", viewModel.ReadablePolishingPromptSettingsStatus, StringComparison.Ordinal);
        Assert.Contains("対象モデル: Gemma 4", viewModel.ReadablePolishingPromptPreviewText, StringComparison.Ordinal);
        Assert.Equal(ReadablePolishingPromptPresets.StrongPunctuation, viewModel.SelectedReadablePolishingPromptPreset?.PresetId);
    }
}
