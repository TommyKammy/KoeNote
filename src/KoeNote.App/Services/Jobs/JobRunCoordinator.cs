using KoeNote.App.Models;
using KoeNote.App.Services.Asr;

namespace KoeNote.App.Services.Jobs;

public sealed class JobRunCoordinator(
    IPreprocessStageRunner preprocessStageRunner,
    IAsrStageRunner asrStageRunner,
    IReviewStageRunner reviewStageRunner,
    ISummaryStageRunner summaryStageRunner)
{
    public async Task RunAsync(
        JobSummary job,
        AsrSettings asrSettings,
        Action<JobRunUpdate> report,
        CancellationToken cancellationToken)
    {
        var normalizedAudioPath = await preprocessStageRunner.RunAsync(job, report, cancellationToken);
        if (normalizedAudioPath is null)
        {
            return;
        }

        var segments = await asrStageRunner.RunAsync(job, normalizedAudioPath, asrSettings, report, cancellationToken);
        if (segments is null)
        {
            return;
        }

        if (asrSettings.EnableReviewStage)
        {
            var reviewSucceeded = await reviewStageRunner.RunAsync(job, segments, report, cancellationToken);
            if (!reviewSucceeded)
            {
                if (!cancellationToken.IsCancellationRequested && asrSettings.EnableSummaryStage)
                {
                    summaryStageRunner.Skip(job, report, "review_not_succeeded");
                }

                return;
            }

        }
        else
        {
            reviewStageRunner.Skip(job, report);
        }

        if (asrSettings.EnableSummaryStage)
        {
            await summaryStageRunner.RunAsync(job, report, cancellationToken);
            return;
        }

        summaryStageRunner.Skip(job, report, "disabled_by_user");
    }

    public Task<bool> RunReviewOnlyAsync(
        JobSummary job,
        IReadOnlyList<TranscriptSegment> segments,
        Action<JobRunUpdate> report,
        CancellationToken cancellationToken)
    {
        return reviewStageRunner.RunAsync(job, segments, report, cancellationToken);
    }

    public Task RunSummaryOnlyAsync(
        JobSummary job,
        Action<JobRunUpdate> report,
        CancellationToken cancellationToken)
    {
        return summaryStageRunner.RunAsync(job, report, cancellationToken);
    }

    public async Task RunReviewAndSummaryAsync(
        JobSummary job,
        IReadOnlyList<TranscriptSegment> segments,
        Action<JobRunUpdate> report,
        CancellationToken cancellationToken)
    {
        var reviewSucceeded = await reviewStageRunner.RunAsync(job, segments, report, cancellationToken);
        if (!reviewSucceeded)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                summaryStageRunner.Skip(job, report, "review_not_succeeded");
            }

            return;
        }

        await summaryStageRunner.RunAsync(job, report, cancellationToken);
    }
}
