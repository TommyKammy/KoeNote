using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Review;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Tests;

public sealed class CorrectionDraftRepositoryTests
{
    [Fact]
    public void SaveDrafts_PersistsDraftsAndMarksSegments()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "この仕様はサーバーのミギワで処理します")
        ]);
        InsertJob(paths, "job-001");

        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft(
                "000001-01",
                "job-001",
                "000001",
                "意味不明語の疑い",
                "この仕様はサーバーのミギワで処理します",
                "この仕様はサーバーの右側で処理します",
                "音の近い語として右側が候補になる。",
                0.62)
        ]);

        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.issue_type, s.review_state, j.unreviewed_draft_count
            FROM correction_drafts d
            JOIN transcript_segments s ON s.job_id = d.job_id AND s.segment_id = d.segment_id
            JOIN jobs j ON j.job_id = d.job_id
            WHERE d.draft_id = '000001-01';
            """;

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("意味不明語の疑い", reader.GetString(0));
        Assert.Equal("has_draft", reader.GetString(1));
        Assert.Equal(1, reader.GetInt32(2));
    }

    [Fact]
    public void ReplaceDrafts_ClearsStaleDraftsWhenNoCandidatesRemain()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "この仕様はサーバーのミギワで処理します")
        ]);
        InsertJob(paths, "job-001");
        var repository = new CorrectionDraftRepository(paths);
        repository.SaveDrafts([
            new CorrectionDraft("000001-01", "job-001", "000001", "意味不明語", "ミギワ", "右側", "候補", 0.62)
        ]);

        repository.ReplaceDrafts("job-001", []);

        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM correction_drafts WHERE job_id = 'job-001'),
                (SELECT review_state FROM transcript_segments WHERE job_id = 'job-001' AND segment_id = '000001'),
                (SELECT unreviewed_draft_count FROM jobs WHERE job_id = 'job-001');
            """;

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.Equal("none", reader.GetString(1));
        Assert.Equal(0, reader.GetInt32(2));
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
                '文字起こし完了',
                70,
                $now,
                $now
            );
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$now", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();
    }
}
