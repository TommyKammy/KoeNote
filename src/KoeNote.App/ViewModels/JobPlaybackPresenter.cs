using System.Globalization;
using System.IO;
using KoeNote.App.Models;

namespace KoeNote.App.ViewModels;

internal static class JobPlaybackPresenter
{
    public static bool MatchesJobSearch(JobSummary job, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        return job.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || job.FileName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || job.Status.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    public static TranscriptSegmentPreview[] OrderSegmentsForPlayback(IEnumerable<TranscriptSegmentPreview> segments)
    {
        return segments
            .OrderBy(static segment => segment.StartSeconds)
            .ToArray();
    }

    public static TranscriptSegmentPreview? FindSegmentForPlaybackPosition(
        IEnumerable<TranscriptSegmentPreview> visibleSegments,
        double positionSeconds)
    {
        var segments = visibleSegments.ToArray();
        var segment = segments
            .LastOrDefault(segment =>
                positionSeconds >= segment.StartSeconds &&
                (segment.EndSeconds <= segment.StartSeconds || positionSeconds < segment.EndSeconds));

        return segment ?? segments.LastOrDefault(segment => positionSeconds >= segment.StartSeconds);
    }

    public static string? ResolvePlaybackPath(JobSummary? job)
    {
        if (job is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(job.NormalizedAudioPath) && File.Exists(job.NormalizedAudioPath))
        {
            return job.NormalizedAudioPath;
        }

        return File.Exists(job.SourceAudioPath) ? job.SourceAudioPath : null;
    }

    public static double ResolveDurationSeconds(
        TimeSpan playbackDuration,
        IEnumerable<TranscriptSegmentPreview> segments)
    {
        var durationSeconds = playbackDuration.TotalSeconds;
        var segmentEndSeconds = segments.Any()
            ? segments.Max(static segment => segment.EndSeconds)
            : 0;

        return durationSeconds > 0 ? durationSeconds : segmentEndSeconds;
    }

    public static string FormatPlaybackTime(TimeSpan value)
    {
        if (value.TotalHours >= 1)
        {
            return value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        return value.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    public static string FormatByteSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{bytes} {units[unitIndex]}"
            : $"{value:0.0} {units[unitIndex]}";
    }
}
