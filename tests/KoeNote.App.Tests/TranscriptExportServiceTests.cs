using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Export;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Tests;

public sealed class TranscriptExportServiceTests
{
    [Fact]
    public void ExportJob_WritesFilesUsingFallbackTextAndSpeakerAliases()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "meeting");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("seg-001", "job-001", 1, 2.5, "spk-1", "raw one", "normalized one"),
            new TranscriptSegment("seg-002", "job-001", 3, 4.25, "spk-2", "raw two")
        ]);
        SetFinalText(paths, "job-001", "seg-001", "final one");
        SetSpeakerAlias(paths, "job-001", "spk-1", "Alice");
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", "job-001", "seg-002", "wording", "raw", "fixed", "reason", 0.8)
        ]);
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports");

        var result = new TranscriptExportService(paths).ExportJob("job-001", output);

        Assert.True(result.HasUnresolvedDrafts);
        Assert.Equal(1, result.PendingDraftCount);
        Assert.Equal(5, result.FilePaths.Count);

        var text = File.ReadAllText(Path.Combine(output, "meeting.txt"));
        Assert.Contains("Alice: final one", text, StringComparison.Ordinal);
        Assert.Contains("spk-2: raw two", text, StringComparison.Ordinal);

        var json = File.ReadAllText(Path.Combine(output, "meeting.json"));
        Assert.Contains("\"PendingDraftCount\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"Text\": \"final one\"", json, StringComparison.Ordinal);

        var srt = File.ReadAllText(Path.Combine(output, "meeting.srt"));
        Assert.Contains("00:00:01,000 --> 00:00:02,500", srt, StringComparison.Ordinal);

        var vtt = File.ReadAllText(Path.Combine(output, "meeting.vtt"));
        Assert.StartsWith("WEBVTT", vtt, StringComparison.Ordinal);
        Assert.Contains("00:00:03.000 --> 00:00:04.250", vtt, StringComparison.Ordinal);

        var logs = new JobLogRepository(paths).ReadLatest("job-001");
        Assert.Contains(logs, entry => entry.Stage == "export" && entry.Level == "warning");
    }

    [Fact]
    public void ExportJob_CanWriteSelectedFormatsOnly()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "selected");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("seg-001", "job-001", 0, 1, "spk-1", "raw")
        ]);
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports");

        var result = new TranscriptExportService(paths).ExportJob("job-001", output, [TranscriptExportFormat.Text]);

        var file = Assert.Single(result.FilePaths);
        Assert.Equal(Path.Combine(output, "selected.txt"), file);
        Assert.True(File.Exists(file));
        Assert.False(File.Exists(Path.Combine(output, "selected.json")));
    }

    [Fact]
    public void ExportJob_NormalizesBlankLinesInsideSubtitleCues()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "subtitle");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("seg-001", "job-001", 0, 1, "spk-1", "first line\r\n\r\nsecond line")
        ]);
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports");

        new TranscriptExportService(paths).ExportJob("job-001", output, [TranscriptExportFormat.Srt, TranscriptExportFormat.Vtt]);

        var srt = File.ReadAllText(Path.Combine(output, "subtitle.srt"));
        Assert.Contains($"spk-1: first line{Environment.NewLine}second line", srt, StringComparison.Ordinal);
        Assert.DoesNotContain($"first line{Environment.NewLine}{Environment.NewLine}second line", srt, StringComparison.Ordinal);

        var vtt = File.ReadAllText(Path.Combine(output, "subtitle.vtt"));
        Assert.Contains($"spk-1: first line{Environment.NewLine}second line", vtt, StringComparison.Ordinal);
        Assert.DoesNotContain($"first line{Environment.NewLine}{Environment.NewLine}second line", vtt, StringComparison.Ordinal);
    }

    private static void SetFinalText(AppPaths paths, string jobId, string segmentId, string finalText)
    {
        using var connection = TestDatabase.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE transcript_segments
            SET final_text = $final_text
            WHERE job_id = $job_id AND segment_id = $segment_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$segment_id", segmentId);
        command.Parameters.AddWithValue("$final_text", finalText);
        command.ExecuteNonQuery();
    }

    private static void SetSpeakerAlias(AppPaths paths, string jobId, string speakerId, string displayName)
    {
        using var connection = TestDatabase.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO speaker_aliases (job_id, speaker_id, display_name, updated_at)
            VALUES ($job_id, $speaker_id, $display_name, $updated_at);
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$speaker_id", speakerId);
        command.Parameters.AddWithValue("$display_name", displayName);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();
    }

}
