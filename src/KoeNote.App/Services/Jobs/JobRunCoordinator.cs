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

            if (job.UnreviewedDrafts > 0)
            {
                if (asrSettings.EnableSummaryStage)
                {
                    summaryStageRunner.Skip(job, report, "manual_review_pending");
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
}
