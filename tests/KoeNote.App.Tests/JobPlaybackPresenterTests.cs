using KoeNote.App.Models;
using KoeNote.App.ViewModels;

namespace KoeNote.App.Tests;

public sealed class JobPlaybackPresenterTests
{
    [Fact]
    public void MatchesJobSearch_MatchesTitleFileNameAndStatus()
    {
        var job = CreateJob(
            title: "Planning meeting",
            fileName: "planning.wav",
            sourceAudioPath: @"C:\audio\planning.wav",
            status: "Cancelled");

        Assert.True(JobPlaybackPresenter.MatchesJobSearch(job, "planning"));
        Assert.True(JobPlaybackPresenter.MatchesJobSearch(job, "meeting"));
        Assert.True(JobPlaybackPresenter.MatchesJobSearch(job, "cancelled"));
        Assert.True(JobPlaybackPresenter.MatchesJobSearch(job, ""));
        Assert.False(JobPlaybackPresenter.MatchesJobSearch(job, "summary"));
    }

    [Fact]
    public void ResolvePlaybackPath_PrefersExistingNormalizedAudio()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "source.wav");
        var normalizedPath = Path.Combine(root, "normalized.wav");
        File.WriteAllText(sourcePath, "");
        File.WriteAllText(normalizedPath, "");
        var job = CreateJob(
            title: "Meeting",
            fileName: "source.wav",
            sourceAudioPath: sourcePath,
            status: "Registered",
            normalizedAudioPath: normalizedPath);

        var playbackPath = JobPlaybackPresenter.ResolvePlaybackPath(job);

        Assert.Equal(normalizedPath, playbackPath);
    }

    [Fact]
    public void FindSegmentForPlaybackPosition_UsesVisibleSegmentAtPosition()
    {
        var segments = new[]
        {
            CreateSegment("first", 0, 5),
            CreateSegment("second", 5, 10),
            CreateSegment("open-ended", 10, 10)
        };

        Assert.Equal("first", JobPlaybackPresenter.FindSegmentForPlaybackPosition(segments, 4.9)?.SegmentId);
        Assert.Equal("second", JobPlaybackPresenter.FindSegmentForPlaybackPosition(segments, 6)?.SegmentId);
        Assert.Equal("open-ended", JobPlaybackPresenter.FindSegmentForPlaybackPosition(segments, 12)?.SegmentId);
        Assert.Null(JobPlaybackPresenter.FindSegmentForPlaybackPosition(segments, -1));
    }

    [Fact]
    public void ResolveDurationSeconds_UsesPlaybackDurationBeforeSegmentFallback()
    {
        var segments = new[]
        {
            CreateSegment("first", 0, 5),
            CreateSegment("second", 5, 12)
        };

        Assert.Equal(20, JobPlaybackPresenter.ResolveDurationSeconds(TimeSpan.FromSeconds(20), segments));
        Assert.Equal(12, JobPlaybackPresenter.ResolveDurationSeconds(TimeSpan.Zero, segments));
        Assert.Equal(0, JobPlaybackPresenter.ResolveDurationSeconds(TimeSpan.Zero, []));
    }

    [Fact]
    public void FormatHelpers_ProduceStableDisplayText()
    {
        Assert.Equal("00:05", JobPlaybackPresenter.FormatPlaybackTime(TimeSpan.FromSeconds(5)));
        Assert.Equal("01:02:03", JobPlaybackPresenter.FormatPlaybackTime(new TimeSpan(1, 2, 3)));
        Assert.Equal("0 B", JobPlaybackPresenter.FormatByteSize(0));
        Assert.Equal("512 B", JobPlaybackPresenter.FormatByteSize(512));
        Assert.Equal("1.5 KB", JobPlaybackPresenter.FormatByteSize(1536));
    }

    private static JobSummary CreateJob(
        string title,
        string fileName,
        string sourceAudioPath,
        string status,
        string normalizedAudioPath = "")
    {
        return new JobSummary(
            Guid.NewGuid().ToString("N"),
            title,
            fileName,
            sourceAudioPath,
            status,
            0,
            0,
            DateTimeOffset.Now)
        {
            NormalizedAudioPath = normalizedAudioPath
        };
    }

    private static TranscriptSegmentPreview CreateSegment(string segmentId, double startSeconds, double endSeconds)
    {
        return new TranscriptSegmentPreview(
            TimeSpan.FromSeconds(startSeconds).ToString(@"hh\:mm\:ss\.fff"),
            TimeSpan.FromSeconds(endSeconds).ToString(@"hh\:mm\:ss\.fff"),
            "Speaker_0",
            "text",
            "",
            segmentId,
            StartSeconds: startSeconds,
            EndSeconds: endSeconds);
    }
}
