using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Transcript;
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
    public void ApplyRawSegmentEdit_UpdatesRawTextAndUndoRestoresOriginalState()
    {
        var paths = ArrangeSegment();
        var service = new TranscriptEditService(paths);

        SetReviewWaitingJobState(paths, "job-001");
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", "job-001", "segment-001", "wording", "ミギワ", "右側", "候補", 0.75)
        ]);
        SetNormalizedText(paths, "job-001", "segment-001", "正規化テキスト");
        service.ApplySegmentEdit("job-001", "segment-001", "手修正文");
        service.ApplyRawSegmentEdit("job-001", "segment-001", "修正した素起こし");

        AssertSegment(
            paths,
            expectedFinalText: null,
            expectedReviewState: "manually_edited",
            expectedPending: 0,
            expectedRawText: "修正した素起こし",
            expectedNormalizedText: null);
        AssertDraftStatus(paths, "draft-001", "invalidated");
        var preview = Assert.Single(new TranscriptSegmentRepository(paths).ReadPreviews("job-001"));
        Assert.Equal("修正した素起こし", preview.RawTranscriptText);
        Assert.Equal("修正した素起こし", preview.Text);

        AssertJobReviewCompleted(paths, "job-001");

        InsertPendingDraft(paths, "draft-002", "job-001", "segment-001");
        Assert.True(service.UndoLastSegmentEdit("job-001", "segment-001"));
        AssertSegment(
            paths,
            expectedFinalText: "手修正文",
            expectedReviewState: "manually_edited",
            expectedPending: 1,
            expectedRawText: "ミギワ",
            expectedNormalizedText: "正規化テキスト");
        AssertDraftStatus(paths, "draft-001", "pending");
        AssertDraftStatus(paths, "draft-002", "invalidated");
        AssertJobReviewReady(paths, "job-001", expectedPending: 1);
    }

    [Fact]
    public void ApplyRawSegmentEdit_MovesZeroPendingJobOutOfReviewWaiting()
    {
        var paths = ArrangeSegment();
        SetReviewWaitingJobState(paths, "job-001");
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", "job-001", "segment-001", "wording", "ミギワ", "右側", "候補", 0.75)
        ]);

        new TranscriptEditService(paths).ApplyRawSegmentEdit("job-001", "segment-001", "修正した素起こし");

        AssertJobReviewCompleted(paths, "job-001");
    }

    [Fact]
    public void UndoLastSegmentEdit_RecomputesPendingCountAfterOtherReviewDecision()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        InsertJob(paths, "job-001");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", "job-001", 0, 1, "Speaker_0", "first raw"),
            new TranscriptSegment("segment-002", "job-001", 1, 2, "Speaker_1", "second raw")
        ]);
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", "job-001", "segment-001", "wording", "first raw", "first fixed", "reason", 0.75),
            new CorrectionDraft("draft-002", "job-001", "segment-002", "wording", "second raw", "second fixed", "reason", 0.75)
        ]);
        SetReviewWaitingJobState(paths, "job-001");
        var service = new TranscriptEditService(paths);

        service.ApplyRawSegmentEdit("job-001", "segment-001", "first raw edited");
        new ReviewOperationService(paths).AcceptDraft("draft-002");

        Assert.True(service.UndoLastSegmentEdit("job-001", "segment-001"));

        AssertDraftStatus(paths, "draft-001", "pending");
        AssertDraftStatus(paths, "draft-002", "accepted");
        AssertJobReviewReady(paths, "job-001", expectedPending: 1);
    }

    [Fact]
    public void UndoLastSegmentEdit_DoesNotBypassNewerReviewDecision()
    {
        var paths = ArrangeSegment();
        var service = new TranscriptEditService(paths);
        service.ApplyRawSegmentEdit("job-001", "segment-001", "修正した素起こし");
        InsertPendingDraft(paths, "draft-001", "job-001", "segment-001");
        new ReviewOperationService(paths).AcceptDraft("draft-001");

        Assert.False(service.UndoLastSegmentEdit("job-001", "segment-001"));

        AssertSegment(
            paths,
            expectedFinalText: "再生成候補",
            expectedReviewState: "reviewed",
            expectedRawText: "修正した素起こし");
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
    public void ApplySpeakerAlias_IsRestoredAfterReplacingTranscriptSegments()
    {
        var paths = ArrangeSegment();
        new TranscriptEditService(paths).ApplySpeakerAlias("job-001", "Speaker_0", "佐藤");

        new TranscriptSegmentRepository(paths).ReplaceSegments("job-001", [
            new TranscriptSegment("segment-002", "job-001", 2, 3, "Speaker_0", "new raw", "new normalized")
        ]);

        var preview = Assert.Single(new TranscriptSegmentRepository(paths).ReadPreviews("job-001"));
        Assert.Equal("佐藤", preview.Speaker);
        Assert.Equal("Speaker_0", preview.SpeakerId);
        Assert.Equal("new normalized", preview.Text);

        var readModel = Assert.Single(new TranscriptReadRepository(paths).ReadForJob("job-001"));
        Assert.Equal("佐藤", readModel.Speaker);
        Assert.Equal("Speaker_0", readModel.SpeakerId);
    }

    [Fact]
    public void ReadPreviews_FallsBackToSpeakerIdWhenStoredAliasIsBlank()
    {
        var paths = ArrangeSegment();
        using (var connection = Open(paths))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO speaker_aliases (job_id, speaker_id, display_name, updated_at)
                VALUES ('job-001', 'Speaker_0', '   ', $updated_at);
                """;
            command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
            command.ExecuteNonQuery();
        }

        var preview = Assert.Single(new TranscriptSegmentRepository(paths).ReadPreviews("job-001"));
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

    [Fact]
    public void UndoLastSegmentEdit_RestoresOnlyTargetSegmentEdit()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        InsertJob(paths, "job-001");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", "job-001", 0, 1, "Speaker_0", "first"),
            new TranscriptSegment("segment-002", "job-001", 1, 2, "Speaker_1", "second")
        ]);
        var service = new TranscriptEditService(paths);
        service.ApplySegmentEdit("job-001", "segment-001", "first edited");
        service.ApplySegmentEdit("job-001", "segment-002", "second edited");

        Assert.True(service.UndoLastSegmentEdit("job-001", "segment-001"));

        AssertSegment(paths, "job-001", "segment-001", expectedFinalText: null, expectedReviewState: "none");
        AssertSegment(paths, "job-001", "segment-002", expectedFinalText: "second edited", expectedReviewState: "manually_edited");

        Assert.True(service.UndoLast("job-001"));
        AssertSegment(paths, "job-001", "segment-002", expectedFinalText: null, expectedReviewState: "none");
    }

    [Fact]
    public void UndoLastSegmentEdit_ReturnsFalseWhenSegmentHasNoEditHistory()
    {
        var paths = ArrangeSegment();

        Assert.False(new TranscriptEditService(paths).UndoLastSegmentEdit("job-001", "segment-001"));
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
        int expectedPending = 0,
        string? expectedRawText = null,
        string? expectedNormalizedText = null)
    {
        AssertSegment(
            paths,
            "job-001",
            "segment-001",
            expectedFinalText,
            expectedReviewState,
            expectedPending,
            expectedRawText,
            expectedNormalizedText);
    }

    private static void AssertSegment(
        AppPaths paths,
        string jobId,
        string segmentId,
        string? expectedFinalText,
        string expectedReviewState,
        int expectedPending = 0,
        string? expectedRawText = null,
        string? expectedNormalizedText = null)
    {
        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.final_text, s.review_state, j.unreviewed_draft_count, s.raw_text, s.normalized_text
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
        if (expectedRawText is not null)
        {
            Assert.Equal(expectedRawText, reader.GetString(3));
        }

        if (expectedNormalizedText is null)
        {
            Assert.True(reader.IsDBNull(4));
        }
        else
        {
            Assert.Equal(expectedNormalizedText, reader.GetString(4));
        }
    }

    private static void SetNormalizedText(AppPaths paths, string jobId, string segmentId, string normalizedText)
    {
        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE transcript_segments
            SET normalized_text = $normalized_text
            WHERE job_id = $job_id AND segment_id = $segment_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$segment_id", segmentId);
        command.Parameters.AddWithValue("$normalized_text", normalizedText);
        command.ExecuteNonQuery();
    }

    private static void AssertDraftStatus(AppPaths paths, string draftId, string expectedStatus)
    {
        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM correction_drafts WHERE draft_id = $draft_id;";
        command.Parameters.AddWithValue("$draft_id", draftId);
        Assert.Equal(expectedStatus, command.ExecuteScalar() as string);
    }

    private static void InsertPendingDraft(AppPaths paths, string draftId, string jobId, string segmentId)
    {
        using var connection = Open(paths);
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO correction_drafts (
                    draft_id,
                    job_id,
                    segment_id,
                    issue_type,
                    original_text,
                    suggested_text,
                    reason,
                    confidence,
                    status,
                    created_at,
                    source,
                    source_ref_id
                )
                VALUES (
                    $draft_id,
                    $job_id,
                    $segment_id,
                    'wording',
                    '修正した素起こし',
                    '再生成候補',
                    '候補',
                    0.5,
                    'pending',
                    $created_at,
                    'test',
                    NULL
                );
                """;
            command.Parameters.AddWithValue("$draft_id", draftId);
            command.Parameters.AddWithValue("$job_id", jobId);
            command.Parameters.AddWithValue("$segment_id", segmentId);
            command.Parameters.AddWithValue("$created_at", DateTimeOffset.Now.ToString("o"));
            command.ExecuteNonQuery();
        }

        using (var jobCommand = connection.CreateCommand())
        {
            jobCommand.Transaction = transaction;
            jobCommand.CommandText = """
                UPDATE jobs
                SET unreviewed_draft_count = (
                    SELECT COUNT(*)
                    FROM correction_drafts
                    WHERE job_id = $job_id AND status = 'pending'
                )
                WHERE job_id = $job_id;
                """;
            jobCommand.Parameters.AddWithValue("$job_id", jobId);
            jobCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void SetReviewWaitingJobState(AppPaths paths, string jobId)
    {
        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE jobs
            SET status = '整文待ち',
                current_stage = 'review_ready',
                progress_percent = 70
            WHERE job_id = $job_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.ExecuteNonQuery();
    }

    private static void AssertJobReviewCompleted(AppPaths paths, string jobId)
    {
        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status, current_stage, progress_percent, unreviewed_draft_count
            FROM jobs
            WHERE job_id = $job_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("完成文書作成待ち", reader.GetString(0));
        Assert.Equal("review_completed", reader.GetString(1));
        Assert.Equal(JobRunProgressPlan.ReviewSucceeded, reader.GetInt32(2));
        Assert.Equal(0, reader.GetInt32(3));
    }

    private static void AssertJobReviewReady(AppPaths paths, string jobId, int expectedPending)
    {
        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT current_stage, progress_percent, unreviewed_draft_count
            FROM jobs
            WHERE job_id = $job_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("review_ready", reader.GetString(0));
        Assert.Equal(JobRunProgressPlan.ReviewSucceeded, reader.GetInt32(1));
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
