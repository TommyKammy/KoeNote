using KoeNote.App.Models;
using KoeNote.App.Services.Asr;

namespace KoeNote.App.Services.Jobs;

public sealed class JobRunCoordinator(
    IPreprocessStageRunner preprocessStageRunner,
    IAsrStageRunner asrStageRunner,
    IReviewStageRunner reviewStageRunner)
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
            await reviewStageRunner.RunAsync(job, segments, report, cancellationToken);
            return;
        }

        reviewStageRunner.Skip(job, report);
    }
}
