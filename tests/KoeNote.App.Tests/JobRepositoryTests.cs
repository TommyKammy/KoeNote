using KoeNote.App.Models;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Tests;

public sealed class JobRepositoryTests
{
    [Fact]
    public void CreateFromAudio_PersistsJobWithLongJapaneseFileName()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        var paths = fixture.Paths;

        var repository = new JobRepository(paths);
        var sourcePath = Path.Combine(
            Path.GetTempPath(),
            "これは非常に長い日本語ファイル名の会議録音サンプルです株式会社テスト開発定例2026年05月03日.m4a");

        var job = repository.CreateFromAudio(sourcePath);

        Assert.Contains("非常に長い日本語ファイル名", job.FileName);

        using var connection = fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT title, source_audio_path, status FROM jobs WHERE job_id = $job_id;";
        command.Parameters.AddWithValue("$job_id", job.JobId);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Contains("非常に長い日本語ファイル名", reader.GetString(0));
        Assert.Equal(sourcePath, reader.GetString(1));
        Assert.Equal("登録済み", reader.GetString(2));
    }

    [Fact]
    public void UpdatePreprocessResult_PersistsNormalizedAudioAndProgress()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        var paths = fixture.Paths;

        var repository = new JobRepository(paths);
        var job = repository.CreateFromAudio(@"C:\audio\meeting.wav");

        repository.MarkPreprocessSucceeded(job, @"C:\normalized\audio.wav");

        using var connection = fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT status, current_stage, progress_percent, normalized_audio_path FROM jobs WHERE job_id = $job_id;";
        command.Parameters.AddWithValue("$job_id", job.JobId);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("音声変換完了", reader.GetString(0));
        Assert.Equal("preprocessed", reader.GetString(1));
        Assert.Equal(100, reader.GetInt32(2));
        Assert.Equal(@"C:\normalized\audio.wav", reader.GetString(3));
    }

    [Fact]
    public void CreateFromAudio_GeneratesUniqueIdsForRapidRegistrations()
    {
        var paths = TestDatabase.CreateRepositoryFixture().Paths;

        var repository = new JobRepository(paths);

        var first = repository.CreateFromAudio(@"C:\audio\meeting.wav");
        var second = repository.CreateFromAudio(@"C:\audio\meeting.wav");

        Assert.NotEqual(first.JobId, second.JobId);
    }

    [Fact]
    public void MarkAsrFailed_PersistsVisibleFailureCategory()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        var paths = fixture.Paths;

        var repository = new JobRepository(paths);
        var job = repository.CreateFromAudio(@"C:\audio\meeting.wav");

        repository.MarkAsrFailed(job, "MissingRuntime");

        using var connection = fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT status, current_stage, last_error_category FROM jobs WHERE job_id = $job_id;";
        command.Parameters.AddWithValue("$job_id", job.JobId);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("ASR失敗: MissingRuntime", reader.GetString(0));
        Assert.Equal("asr_failed", reader.GetString(1));
        Assert.Equal("MissingRuntime", reader.GetString(2));
        Assert.Equal("ASR失敗: MissingRuntime", job.Status);
    }

    [Fact]
    public void LoadRecent_RestoresPersistedJobs()
    {
        var paths = TestDatabase.CreateRepositoryFixture().Paths;

        var repository = new JobRepository(paths);
        var created = repository.CreateFromAudio(@"C:\audio\meeting.wav");
        repository.MarkReviewSucceeded(created, draftCount: 2);

        var restored = Assert.Single(repository.LoadRecent());

        Assert.Equal(created.JobId, restored.JobId);
        Assert.Equal("レビュー待ち", restored.Status);
        Assert.Equal(@"C:\audio\meeting.wav", restored.SourceAudioPath);
        Assert.Equal(90, restored.ProgressPercent);
    }

    [Fact]
    public void MarkCancelled_PersistsCancelledState()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        var paths = fixture.Paths;

        var repository = new JobRepository(paths);
        var job = repository.CreateFromAudio(@"C:\audio\meeting.wav");
        repository.MarkCancelled(job, "asr");

        using var connection = fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT status, current_stage, last_error_category FROM jobs WHERE job_id = $job_id;";
        command.Parameters.AddWithValue("$job_id", job.JobId);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("キャンセル済み", reader.GetString(0));
        Assert.Equal("asr_cancelled", reader.GetString(1));
        Assert.Equal("cancelled", reader.GetString(2));
    }

    [Fact]
    public void MarkReviewSkippedAndClearDrafts_ClearsPendingReviewState()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        var paths = fixture.Paths;
        var repository = new JobRepository(paths);
        var job = repository.CreateFromAudio(@"C:\audio\meeting.wav");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "raw")
        ]);
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", job.JobId, "segment-001", "wording", "raw", "fixed", "reason", 0.8)
        ]);

        repository.MarkReviewSkippedAndClearDrafts(job);

        using var connection = fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                j.current_stage,
                j.progress_percent,
                j.unreviewed_draft_count,
                d.status,
                s.review_state
            FROM jobs j
            JOIN correction_drafts d ON d.job_id = j.job_id
            JOIN transcript_segments s ON s.job_id = j.job_id AND s.segment_id = d.segment_id
            WHERE j.job_id = $job_id;
            """;
        command.Parameters.AddWithValue("$job_id", job.JobId);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("review_skipped", reader.GetString(0));
        Assert.Equal(100, reader.GetInt32(1));
        Assert.Equal(0, reader.GetInt32(2));
        Assert.Equal("skipped", reader.GetString(3));
        Assert.Equal("none", reader.GetString(4));
        Assert.False(reader.Read());
        Assert.Equal(0, job.UnreviewedDrafts);
    }

}
