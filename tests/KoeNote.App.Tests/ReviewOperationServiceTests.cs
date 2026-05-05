using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Review;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Tests;

public sealed class ReviewOperationServiceTests
{
    [Fact]
    public void AcceptDraft_AppliesSuggestedTextAndRecordsDecision()
    {
        var paths = CreatePaths();
        ArrangeDraft(paths, "draft-001", originalText: "繝溘ぐ繝ｯ", suggestedText: "蜿ｳ蛛ｴ");

        var result = new ReviewOperationService(paths).AcceptDraft("draft-001");

        Assert.Equal("accepted", result.Action);
        Assert.Equal("蜿ｳ蛛ｴ", result.FinalText);
        Assert.Equal(0, result.PendingDraftCount);
        AssertDraftDecision(paths, "draft-001", "accepted", "accepted", "蜿ｳ蛛ｴ", expectedNote: null);
        AssertSegment(paths, finalText: "蜿ｳ蛛ｴ", reviewState: "reviewed", pendingDraftCount: 0);
    }

    [Fact]
    public void AcceptDraft_ReplacesOriginalTextInsideSegment()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        InsertJob(paths, "job-001");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", "job-001", 0, 1, "Speaker_0", "今日は旧サービス名を確認します")
        ]);
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft(
                "draft-001",
                "job-001",
                "segment-001",
                "wording",
                "旧サービス名",
                "KoeNote",
                "suggestion",
                0.75)
        ]);

        var result = new ReviewOperationService(paths).AcceptDraft("draft-001");

        Assert.Equal("今日はKoeNoteを確認します", result.FinalText);
        AssertDraftDecision(paths, "draft-001", "accepted", "accepted", "今日はKoeNoteを確認します", expectedNote: null);
        AssertSegment(paths, finalText: "今日はKoeNoteを確認します", reviewState: "reviewed", pendingDraftCount: 0);
    }

    [Fact]
    public void AcceptDraft_ReplacesOnlyOneOccurrenceInsideSegment()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        InsertJob(paths, "job-001");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", "job-001", 0, 1, "Speaker_0", "alpha beta alpha")
        ]);
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft(
                "draft-001",
                "job-001",
                "segment-001",
                "wording",
                "alpha",
                "ALPHA",
                "suggestion",
                0.75)
        ]);

        var result = new ReviewOperationService(paths).AcceptDraft("draft-001");

        Assert.Equal("ALPHA beta alpha", result.FinalText);
        AssertDraftDecision(paths, "draft-001", "accepted", "accepted", "ALPHA beta alpha", expectedNote: null);
        AssertSegment(paths, finalText: "ALPHA beta alpha", reviewState: "reviewed", pendingDraftCount: 0);
    }

    [Fact]
    public void RejectDraft_LeavesFinalTextUnsetAndRecordsDecision()
    {
        var paths = CreatePaths();
        ArrangeDraft(paths, "draft-001", originalText: "繝溘ぐ繝ｯ", suggestedText: "蜿ｳ蛛ｴ");

        var result = new ReviewOperationService(paths).RejectDraft("draft-001");

        Assert.Equal("rejected", result.Action);
        Assert.Null(result.FinalText);
        AssertDraftDecision(paths, "draft-001", "rejected", "rejected", expectedFinalText: null, expectedNote: null);
        AssertSegment(paths, finalText: null, reviewState: "reviewed", pendingDraftCount: 0);
    }

    [Fact]
    public void ApplyManualEdit_UsesEditedTextAndStoresManualNote()
    {
        var paths = CreatePaths();
        ArrangeDraft(paths, "draft-001", originalText: "繝溘ぐ繝ｯ", suggestedText: "蜿ｳ蛛ｴ");

        var result = new ReviewOperationService(paths).ApplyManualEdit("draft-001", "蜿ｳ蛛ｴ API", "API蜷阪ｒ谿九☆");

        Assert.Equal("manual_edit", result.Action);
        Assert.Equal("蜿ｳ蛛ｴ API", result.FinalText);
        AssertDraftDecision(paths, "draft-001", "edited", "manual_edit", "蜿ｳ蛛ｴ API", expectedNote: "API蜷阪ｒ谿九☆");
        AssertSegment(paths, finalText: "蜿ｳ蛛ｴ API", reviewState: "reviewed", pendingDraftCount: 0);
    }

    [Fact]
    public void AcceptDraft_RejectsAlreadyDecidedDraft()
    {
        var paths = CreatePaths();
        ArrangeDraft(paths, "draft-001", originalText: "繝溘ぐ繝ｯ", suggestedText: "蜿ｳ蛛ｴ");
        var service = new ReviewOperationService(paths);
        service.AcceptDraft("draft-001");

        Assert.Throws<InvalidOperationException>(() => service.AcceptDraft("draft-001"));
    }

    [Fact]
    public void AcceptDraft_RollsBackWhenSegmentIsMissing()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        InsertJob(paths, "job-001");
        InsertDraftWithoutSegment(paths, "draft-001");

        Assert.Throws<KeyNotFoundException>(() => new ReviewOperationService(paths).AcceptDraft("draft-001"));

        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT status FROM correction_drafts WHERE draft_id = 'draft-001'),
                (SELECT COUNT(*) FROM review_decisions WHERE draft_id = 'draft-001'),
                (SELECT unreviewed_draft_count FROM jobs WHERE job_id = 'job-001');
            """;

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("pending", reader.GetString(0));
        Assert.Equal(0, reader.GetInt32(1));
        Assert.Equal(1, reader.GetInt32(2));
    }

    private static void ArrangeDraft(AppPaths paths, string draftId, string originalText, string suggestedText)
    {
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        InsertJob(paths, "job-001");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", "job-001", 0, 1, "Speaker_0", originalText)
        ]);
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft(
                draftId,
                "job-001",
                "segment-001",
                "wording",
                originalText,
                suggestedText,
                "suggestion",
                0.75)
        ]);
    }

    private static void AssertDraftDecision(
        AppPaths paths,
        string draftId,
        string expectedDraftStatus,
        string expectedAction,
        string? expectedFinalText,
        string? expectedNote)
    {
        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.status, r.action, r.final_text, r.manual_note
            FROM correction_drafts d
            JOIN review_decisions r ON r.draft_id = d.draft_id
            WHERE d.draft_id = $draft_id;
            """;
        command.Parameters.AddWithValue("$draft_id", draftId);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(expectedDraftStatus, reader.GetString(0));
        Assert.Equal(expectedAction, reader.GetString(1));
        if (expectedFinalText is null)
        {
            Assert.True(reader.IsDBNull(2));
        }
        else
        {
            Assert.Equal(expectedFinalText, reader.GetString(2));
        }
        if (expectedNote is null)
        {
            Assert.True(reader.IsDBNull(3));
        }
        else
        {
            Assert.Equal(expectedNote, reader.GetString(3));
        }
    }

    private static void AssertSegment(AppPaths paths, string? finalText, string reviewState, int pendingDraftCount)
    {
        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.final_text, s.review_state, j.unreviewed_draft_count
            FROM transcript_segments s
            JOIN jobs j ON j.job_id = s.job_id
            WHERE s.job_id = 'job-001' AND s.segment_id = 'segment-001';
            """;

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        if (finalText is null)
        {
            Assert.True(reader.IsDBNull(0));
        }
        else
        {
            Assert.Equal(finalText, reader.GetString(0));
        }
        Assert.Equal(reviewState, reader.GetString(1));
        Assert.Equal(pendingDraftCount, reader.GetInt32(2));
    }

    private static AppPaths CreatePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var local = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        return new AppPaths(root, local);
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
                'test',
                'test.wav',
                'asr_completed',
                70,
                $now,
                $now
            );
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$now", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();
    }

    private static void InsertDraftWithoutSegment(AppPaths paths, string draftId)
    {
        using var connection = Open(paths);
        using var command = connection.CreateCommand();
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
                created_at
            )
            VALUES (
                $draft_id,
                'job-001',
                'missing-segment',
                'wording',
                'original',
                'suggested',
                'reason',
                0.75,
                'pending',
                $now
            );
            """;
        command.Parameters.AddWithValue("$draft_id", draftId);
        command.Parameters.AddWithValue("$now", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();

        using var jobCommand = connection.CreateCommand();
        jobCommand.CommandText = "UPDATE jobs SET unreviewed_draft_count = 1 WHERE job_id = 'job-001';";
        jobCommand.ExecuteNonQuery();
    }
}
