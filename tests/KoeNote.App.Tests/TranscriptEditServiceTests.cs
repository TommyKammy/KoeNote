using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Review;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Tests;

public sealed class TranscriptEditServiceTests
{
    [Fact]
    public void ApplySegmentEdit_UpdatesFinalTextAndUndoRestoresOriginalState()
    {
        var paths = ArrangeSegment();
        var service = new TranscriptEditService(paths);

        service.ApplySegmentEdit("job-001", "segment-001", "手修正文");

        AssertSegment(paths, expectedFinalText: "手修正文", expectedReviewState: "manually_edited");

        Assert.True(service.UndoLast());
        AssertSegment(paths, expectedFinalText: null, expectedReviewState: "none");
    }

    [Fact]
    public void ApplySpeakerAlias_ChangesPreviewSpeakerAndUndoRestoresSpeakerId()
    {
        var paths = ArrangeSegment();
        var service = new TranscriptEditService(paths);

        service.ApplySpeakerAlias("job-001", "Speaker_0", "佐藤");

        var preview = Assert.Single(new TranscriptSegmentRepository(paths).ReadPreviews("job-001"));
        Assert.Equal("佐藤", preview.Speaker);

        Assert.True(service.UndoLast());
        preview = Assert.Single(new TranscriptSegmentRepository(paths).ReadPreviews("job-001"));
        Assert.Equal("Speaker_0", preview.Speaker);
    }

    [Fact]
    public void UndoLast_RestoresReviewDecision()
    {
        var paths = ArrangeSegment();
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", "job-001", "segment-001", "wording", "ミギワ", "右側", "候補", 0.75)
        ]);

        new ReviewOperationService(paths).AcceptDraft("draft-001");
        AssertSegment(paths, expectedFinalText: "右側", expectedReviewState: "reviewed", expectedPending: 0);

        Assert.True(new TranscriptEditService(paths).UndoLast());

        AssertSegment(paths, expectedFinalText: null, expectedReviewState: "has_draft", expectedPending: 1);
        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT status FROM correction_drafts WHERE draft_id = 'draft-001'),
                (SELECT COUNT(*) FROM review_decisions WHERE draft_id = 'draft-001');
            """;
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("pending", reader.GetString(0));
        Assert.Equal(0, reader.GetInt32(1));
    }

    [Fact]
    public void UndoLast_WithJobId_DoesNotUndoAnotherJobsOperation()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        InsertJob(paths, "job-001");
        InsertJob(paths, "job-002");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", "job-001", 0, 1, "Speaker_0", "job one"),
            new TranscriptSegment("segment-002", "job-002", 0, 1, "Speaker_0", "job two")
        ]);
        var service = new TranscriptEditService(paths);
        service.ApplySegmentEdit("job-002", "segment-002", "job two edited");

        Assert.False(service.UndoLast("job-001"));
        AssertSegment(paths, "job-002", "segment-002", expectedFinalText: "job two edited", expectedReviewState: "manually_edited");

        Assert.True(service.UndoLast("job-002"));
        AssertSegment(paths, "job-002", "segment-002", expectedFinalText: null, expectedReviewState: "none");
    }

    private static AppPaths ArrangeSegment()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));

        using (var connection = Open(paths))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE jobs SET job_id = 'job-001' WHERE source_audio_path = $source_audio_path;";
            command.Parameters.AddWithValue("$source_audio_path", Path.Combine(root, "meeting.wav"));
            command.ExecuteNonQuery();
        }

        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", "job-001", 0, 1, "Speaker_0", "ミギワ")
        ]);
        return paths;
    }

    private static void AssertSegment(
        AppPaths paths,
        string? expectedFinalText,
        string expectedReviewState,
        int expectedPending = 0)
    {
        AssertSegment(paths, "job-001", "segment-001", expectedFinalText, expectedReviewState, expectedPending);
    }

    private static void AssertSegment(
        AppPaths paths,
        string jobId,
        string segmentId,
        string? expectedFinalText,
        string expectedReviewState,
        int expectedPending = 0)
    {
        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.final_text, s.review_state, j.unreviewed_draft_count
            FROM transcript_segments s
            JOIN jobs j ON j.job_id = s.job_id
            WHERE s.job_id = $job_id AND s.segment_id = $segment_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$segment_id", segmentId);
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        if (expectedFinalText is null)
        {
            Assert.True(reader.IsDBNull(0));
        }
        else
        {
            Assert.Equal(expectedFinalText, reader.GetString(0));
        }

        Assert.Equal(expectedReviewState, reader.GetString(1));
        Assert.Equal(expectedPending, reader.GetInt32(2));
    }

    private static SqliteConnection Open(AppPaths paths)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void InsertJob(AppPaths paths, string jobId)
    {
        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO jobs (
                job_id,
                title,
                source_audio_path,
                status,
                progress_percent,
                created_at,
                updated_at
            )
            VALUES (
                $job_id,
                $job_id,
                $source_audio_path,
                'asr_completed',
                70,
                $now,
                $now
            );
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$source_audio_path", $"{jobId}.wav");
        command.Parameters.AddWithValue("$now", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();
    }
}
