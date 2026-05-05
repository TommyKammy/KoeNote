using KoeNote.App.Models;
using KoeNote.App.Services.Asr;

namespace KoeNote.App.Services.Jobs;

public interface IPreprocessStageRunner
{
    Task<string?> RunAsync(
        JobSummary job,
        Action<JobRunUpdate> report,
        CancellationToken cancellationToken);
}

public interface IAsrStageRunner
{
    Task<IReadOnlyList<TranscriptSegment>?> RunAsync(
        JobSummary job,
        string normalizedAudioPath,
        AsrSettings asrSettings,
        Action<JobRunUpdate> report,
        CancellationToken cancellationToken);
}

public interface IReviewStageRunner
{
    Task RunAsync(
        JobSummary job,
        IReadOnlyList<TranscriptSegment> segments,
        Action<JobRunUpdate> report,
        CancellationToken cancellationToken);

    void Skip(JobSummary job, Action<JobRunUpdate> report);
}
