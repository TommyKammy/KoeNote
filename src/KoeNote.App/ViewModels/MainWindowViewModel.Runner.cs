using KoeNote.App.Models;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Review;
using System.IO;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public async Task RunSelectedJobAsync()
    {
        if (SelectedJob is null || IsRunInProgress)
        {
            return;
        }

        var job = SelectedJob;
        using var cancellation = new CancellationTokenSource();
        _runCancellation = cancellation;
        IsRunInProgress = true;
        var stage = StageStatuses.First(item => item.Name == "音声変換");
        stage.Status = "実行中";
        stage.ProgressPercent = 10;
        _jobRepository.MarkPreprocessRunning(job);
        RefreshJobViews();
        LatestLog = $"Running ffmpeg for {job.FileName}";

        try
        {
            var result = await _audioPreprocessWorker.NormalizeAsync(job, "ffmpeg", Paths, cancellation.Token);
            stage.Status = "成功";
            stage.ProgressPercent = 100;
            _jobRepository.MarkPreprocessSucceeded(job, result.NormalizedAudioPath);
            RefreshJobViews();
            LatestLog = $"Generated normalized WAV: {result.NormalizedAudioPath}";

            await RunAsrAsync(job, result.NormalizedAudioPath, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            stage.Status = "中止";
            stage.ProgressPercent = 100;
            _jobRepository.MarkCancelled(job, "preprocess");
            RefreshJobViews();
            _jobLogRepository.AddEvent(job.JobId, "preprocess", "info", "Run was cancelled.");
            LatestLog = "実行をキャンセルしました。";
            RefreshLogs();
        }
        catch (Exception exception)
        {
            stage.Status = "失敗";
            stage.ProgressPercent = 100;
            _jobRepository.MarkPreprocessFailed(job, "ffmpeg_failed");
            RefreshJobViews();
            _jobLogRepository.AddEvent(job.JobId, "preprocess", "error", exception.Message);
            LatestLog = exception.Message;
            RefreshLogs();
        }
        finally
        {
            _runCancellation = null;
            IsRunInProgress = false;
        }
    }

    private async Task RunAsrAsync(JobSummary job, string normalizedAudioPath, CancellationToken cancellationToken)
    {
        var stage = StageStatuses.First(item => item.Name == "ASR");
        var startedAt = DateTimeOffset.Now;
        stage.Status = "実行中";
        stage.ProgressPercent = 10;
        _jobRepository.MarkAsrRunning(job);
        RefreshJobViews();
        _stageProgressRepository.Upsert(job.JobId, "asr", "running", 10, startedAt: startedAt);
        LatestLog = $"Running ASR for {job.FileName}";

        try
        {
            var outputDirectory = Path.Combine(Paths.Jobs, job.JobId, "asr");
            SaveAsrSettings();
            var asrSettings = new AsrSettings(AsrContextText, AsrHotwordsText);
            var result = await _asrWorker.RunAsync(new AsrRunOptions(
                job.JobId,
                normalizedAudioPath,
                Paths.CrispAsrPath,
                Paths.VibeVoiceAsrModelPath,
                outputDirectory,
                asrSettings.Hotwords,
                string.IsNullOrWhiteSpace(asrSettings.ContextText) ? null : asrSettings.ContextText,
                Timeout: TimeSpan.FromHours(2)),
                cancellationToken);

            Segments.Clear();
            foreach (var segment in result.Segments)
            {
                Segments.Add(new TranscriptSegmentPreview(
                    FormatTimestamp(segment.StartSeconds),
                    FormatTimestamp(segment.EndSeconds),
                    segment.SpeakerId ?? "",
                    segment.NormalizedText ?? segment.RawText,
                    "候補なし",
                    segment.SegmentId));
            }
            RefreshSpeakerFilters();
            FilteredSegments.Refresh();

            var finishedAt = DateTimeOffset.Now;
            stage.Status = "成功";
            stage.ProgressPercent = 100;
            _stageProgressRepository.Upsert(
                job.JobId,
                "asr",
                "succeeded",
                100,
                startedAt,
                finishedAt,
                result.Duration.TotalSeconds,
                logPath: result.RawOutputPath);
            _jobRepository.MarkAsrSucceeded(job);
            RefreshJobViews();
            _jobLogRepository.AddEvent(job.JobId, "asr", "info", $"Generated {result.Segments.Count} ASR segments: {result.NormalizedSegmentsPath}");
            LatestLog = $"ASR completed: {result.Segments.Count} segments";
            RefreshLogs();

            await RunReviewAsync(job, result.Segments, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            var finishedAt = DateTimeOffset.Now;
            stage.Status = "中止";
            stage.ProgressPercent = 100;
            _stageProgressRepository.Upsert(
                job.JobId,
                "asr",
                "cancelled",
                100,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: "cancelled");
            _jobRepository.MarkCancelled(job, "asr");
            RefreshJobViews();
            _jobLogRepository.AddEvent(job.JobId, "asr", "info", "Run was cancelled.");
            LatestLog = "ASRをキャンセルしました。";
            RefreshLogs();
        }
        catch (AsrWorkerException exception)
        {
            var finishedAt = DateTimeOffset.Now;
            stage.Status = $"失敗: {exception.Category}";
            stage.ProgressPercent = 100;
            _stageProgressRepository.Upsert(
                job.JobId,
                "asr",
                "failed",
                100,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: exception.Category.ToString());
            _jobRepository.MarkAsrFailed(job, exception.Category.ToString());
            RefreshJobViews();
            _jobLogRepository.AddEvent(job.JobId, "asr", "error", $"{exception.Category}: {exception.Message}");
            LatestLog = $"ASR failed ({exception.Category}): {exception.Message}";
            RefreshLogs();
        }
        catch (Exception exception)
        {
            var finishedAt = DateTimeOffset.Now;
            stage.Status = "失敗: Unknown";
            stage.ProgressPercent = 100;
            _stageProgressRepository.Upsert(
                job.JobId,
                "asr",
                "failed",
                100,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: AsrFailureCategory.Unknown.ToString());
            _jobRepository.MarkAsrFailed(job, AsrFailureCategory.Unknown.ToString());
            RefreshJobViews();
            _jobLogRepository.AddEvent(job.JobId, "asr", "error", $"{AsrFailureCategory.Unknown}: {exception.Message}");
            LatestLog = $"ASR failed ({AsrFailureCategory.Unknown}): {exception.Message}";
            RefreshLogs();
        }
    }

    private async Task RunReviewAsync(JobSummary job, IReadOnlyList<TranscriptSegment> segments, CancellationToken cancellationToken)
    {
        var stage = StageStatuses.First(item => item.Name == "推敲");
        var startedAt = DateTimeOffset.Now;
        stage.Status = "実行中";
        stage.ProgressPercent = 10;
        _jobRepository.MarkReviewRunning(job);
        RefreshJobViews();
        _stageProgressRepository.Upsert(job.JobId, "review", "running", 10, startedAt: startedAt);
        LatestLog = $"Running review for {job.FileName}";

        try
        {
            var outputDirectory = Path.Combine(Paths.Jobs, job.JobId, "review");
            var result = await _reviewWorker.RunAsync(new ReviewRunOptions(
                job.JobId,
                Paths.LlamaCompletionPath,
                Paths.ReviewModelPath,
                outputDirectory,
                segments,
                MinConfidence: 0.5,
                Timeout: TimeSpan.FromHours(2)),
                cancellationToken);

            var finishedAt = DateTimeOffset.Now;
            stage.Status = "成功";
            stage.ProgressPercent = 100;
            _stageProgressRepository.Upsert(
                job.JobId,
                "review",
                "succeeded",
                100,
                startedAt,
                finishedAt,
                result.Duration.TotalSeconds,
                logPath: result.RawOutputPath);
            _jobRepository.MarkReviewSucceeded(job, result.Drafts.Count);
            job.UnreviewedDrafts = result.Drafts.Count;
            RefreshJobViews();
            _jobLogRepository.AddEvent(job.JobId, "review", "info", $"Generated {result.Drafts.Count} correction drafts: {result.NormalizedDraftsPath}");
            LatestLog = $"Review completed: {result.Drafts.Count} drafts";
            RefreshLogs();

            var firstDraft = result.Drafts.FirstOrDefault();
            if (firstDraft is not null)
            {
                ReviewIssueType = firstDraft.IssueType;
                OriginalText = firstDraft.OriginalText;
                SuggestedText = firstDraft.SuggestedText;
                ReviewReason = firstDraft.Reason;
                Confidence = firstDraft.Confidence;
                UpdateSegmentReviewStates(result.Drafts);
            }
            else
            {
                ClearReviewPreview();
            }
        }
        catch (OperationCanceledException)
        {
            var finishedAt = DateTimeOffset.Now;
            stage.Status = "中止";
            stage.ProgressPercent = 100;
            _stageProgressRepository.Upsert(
                job.JobId,
                "review",
                "cancelled",
                100,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: "cancelled");
            _jobRepository.MarkCancelled(job, "review");
            RefreshJobViews();
            _jobLogRepository.AddEvent(job.JobId, "review", "info", "Run was cancelled.");
            LatestLog = "推敲をキャンセルしました。";
            RefreshLogs();
        }
        catch (ReviewWorkerException exception)
        {
            var finishedAt = DateTimeOffset.Now;
            stage.Status = $"失敗: {exception.Category}";
            stage.ProgressPercent = 100;
            _stageProgressRepository.Upsert(
                job.JobId,
                "review",
                "failed",
                100,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: exception.Category.ToString());
            _jobRepository.MarkReviewFailed(job, exception.Category.ToString());
            RefreshJobViews();
            _jobLogRepository.AddEvent(job.JobId, "review", "error", $"{exception.Category}: {exception.Message}");
            LatestLog = $"Review failed ({exception.Category}): {exception.Message}";
            RefreshLogs();
        }
        catch (Exception exception)
        {
            var finishedAt = DateTimeOffset.Now;
            stage.Status = "失敗: Unknown";
            stage.ProgressPercent = 100;
            _stageProgressRepository.Upsert(
                job.JobId,
                "review",
                "failed",
                100,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: ReviewFailureCategory.Unknown.ToString());
            _jobRepository.MarkReviewFailed(job, ReviewFailureCategory.Unknown.ToString());
            RefreshJobViews();
            _jobLogRepository.AddEvent(job.JobId, "review", "error", $"{ReviewFailureCategory.Unknown}: {exception.Message}");
            LatestLog = $"Review failed ({ReviewFailureCategory.Unknown}): {exception.Message}";
            RefreshLogs();
        }
    }

    private Task CancelRunAsync()
    {
        _runCancellation?.Cancel();
        LatestLog = "キャンセルを要求しました。";
        return Task.CompletedTask;
    }
}
