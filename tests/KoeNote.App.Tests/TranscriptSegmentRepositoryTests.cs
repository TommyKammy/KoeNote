using KoeNote.App.Models;
using KoeNote.App.Services.Asr;

namespace KoeNote.App.Tests;

public sealed class TranscriptSegmentRepositoryTests
{
    [Fact]
    public void SaveSegments_UpsertsTranscriptSegments()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        var paths = fixture.Paths;

        var repository = new TranscriptSegmentRepository(paths);
        repository.SaveSegments([
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "first", "first", Source: "asr", AsrRunId: "run-001")
        ]);
        repository.SaveSegments([
            new TranscriptSegment("000001", "job-001", 0, 1.5, "Speaker_1", "updated", "updated", Source: "asr", AsrRunId: "run-002")
        ]);

        using var connection = fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT speaker_id, raw_text, end_seconds, asr_run_id
            FROM transcript_segments
            WHERE job_id = 'job-001' AND segment_id = '000001';
            """;

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("Speaker_1", reader.GetString(0));
        Assert.Equal("updated", reader.GetString(1));
        Assert.Equal(1.5, reader.GetDouble(2));
        Assert.Equal("run-002", reader.GetString(3));
        Assert.False(reader.Read());
    }

    [Fact]
    public void ReplaceSegments_ReplacesExistingSegmentsAndClearsReviewState()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        var paths = fixture.Paths;
        var repository = new TranscriptSegmentRepository(paths);
        repository.SaveSegments([
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "old first", "old first", Source: "asr", AsrRunId: "run-old"),
            new TranscriptSegment("000002", "job-001", 1, 2, "Speaker_1", "stale second", "stale second", Source: "asr", AsrRunId: "run-old")
        ]);
        InsertReviewState(fixture);

        repository.ReplaceSegments([
            new TranscriptSegment("000001", "job-001", 0, 1.5, null, "new first", "new first", Source: "asr", AsrRunId: "run-new")
        ]);

        using var connection = fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM transcript_segments WHERE job_id = 'job-001'),
                (SELECT COUNT(*) FROM transcript_segments WHERE job_id = 'job-001' AND segment_id = '000002'),
                (SELECT raw_text FROM transcript_segments WHERE job_id = 'job-001' AND segment_id = '000001'),
                (SELECT speaker_id FROM transcript_segments WHERE job_id = 'job-001' AND segment_id = '000001'),
                (SELECT COUNT(*) FROM correction_drafts WHERE job_id = 'job-001'),
                (SELECT COUNT(*) FROM review_decisions WHERE draft_id = 'draft-001'),
                (SELECT COUNT(*) FROM review_operation_history WHERE job_id = 'job-001');
            """;

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(0, reader.GetInt32(1));
        Assert.Equal("new first", reader.GetString(2));
        Assert.True(reader.IsDBNull(3));
        Assert.Equal(0, reader.GetInt32(4));
        Assert.Equal(0, reader.GetInt32(5));
        Assert.Equal(0, reader.GetInt32(6));
    }

    [Fact]
    public void ReplaceSegments_WithJobIdClearsExistingSegmentsWhenNewResultIsEmpty()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        var paths = fixture.Paths;
        var repository = new TranscriptSegmentRepository(paths);
        repository.SaveSegments([
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "old first", "old first", Source: "asr", AsrRunId: "run-old"),
            new TranscriptSegment("000002", "job-001", 1, 2, "Speaker_1", "old second", "old second", Source: "asr", AsrRunId: "run-old")
        ]);
        InsertReviewState(fixture);

        repository.ReplaceSegments("job-001", []);

        using var connection = fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM transcript_segments WHERE job_id = 'job-001'),
                (SELECT COUNT(*) FROM correction_drafts WHERE job_id = 'job-001'),
                (SELECT COUNT(*) FROM review_decisions WHERE draft_id = 'draft-001'),
                (SELECT COUNT(*) FROM review_operation_history WHERE job_id = 'job-001');
            """;

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.Equal(0, reader.GetInt32(1));
        Assert.Equal(0, reader.GetInt32(2));
        Assert.Equal(0, reader.GetInt32(3));
    }

    [Fact]
    public void ReadSegments_ReturnsStoredSegmentsForPostProcessing()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        var repository = new TranscriptSegmentRepository(fixture.Paths);
        repository.SaveSegments([
            new TranscriptSegment("000002", "job-001", 2, 3, "Speaker_1", "second raw", "second normalized"),
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "first raw", "first normalized")
        ]);

        var segments = repository.ReadSegments("job-001");

        Assert.Equal(["000001", "000002"], segments.Select(segment => segment.SegmentId).ToArray());
        Assert.Equal("first raw", segments[0].RawText);
        Assert.Equal("first normalized", segments[0].NormalizedText);
        Assert.Equal("Speaker_0", segments[0].SpeakerId);
    }

    private static void InsertReviewState(RepositoryTestFixture fixture)
    {
        using var connection = fixture.Open();
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
                'draft-001',
                'job-001',
                '000001',
                'wording',
                'old first',
                'fixed first',
                'reason',
                0.8,
                'accepted',
                $now
            );

            INSERT INTO review_decisions (
                decision_id,
                draft_id,
                action,
                final_text,
                manual_note,
                decided_at
            )
            VALUES (
                'decision-001',
                'draft-001',
                'accept',
                'fixed first',
                NULL,
                $now
            );

            INSERT INTO review_operation_history (
                operation_id,
                job_id,
                draft_id,
                segment_id,
                operation_type,
                before_json,
                after_json,
                created_at
            )
            VALUES (
                'operation-001',
                'job-001',
                'draft-001',
                '000001',
                'accept',
                '{}',
                '{}',
                $now
            );
            """;
        command.Parameters.AddWithValue("$now", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();
    }

}
