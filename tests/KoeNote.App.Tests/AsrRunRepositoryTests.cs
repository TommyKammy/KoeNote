using KoeNote.App.Services.Asr;

namespace KoeNote.App.Tests;

public sealed class AsrRunRepositoryTests
{
    [Fact]
    public void StartAndMarkSucceeded_PersistAsrRunLifecycle()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        var paths = fixture.Paths;
        var repository = new AsrRunRepository(paths);

        var asrRunId = repository.Start("job-001", "faster-whisper-large-v3-turbo", "faster-whisper-large-v3-turbo");
        repository.MarkSucceeded(asrRunId, TimeSpan.FromSeconds(1.25), "raw.json", "segments.json");

        using var connection = fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT engine_id, model_id, status, duration_seconds, raw_output_path, normalized_output_path
            FROM asr_runs
            WHERE asr_run_id = $asr_run_id;
            """;
        command.Parameters.AddWithValue("$asr_run_id", asrRunId);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("faster-whisper-large-v3-turbo", reader.GetString(0));
        Assert.Equal("faster-whisper-large-v3-turbo", reader.GetString(1));
        Assert.Equal("succeeded", reader.GetString(2));
        Assert.Equal(1.25, reader.GetDouble(3));
        Assert.Equal("raw.json", reader.GetString(4));
        Assert.Equal("segments.json", reader.GetString(5));
    }

    [Fact]
    public void MarkFailed_PersistsErrorCategory()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        var paths = fixture.Paths;
        var repository = new AsrRunRepository(paths);

        var asrRunId = repository.Start("job-001", "faster-whisper-large-v3-turbo", "faster-whisper-large-v3-turbo");
        repository.MarkFailed(asrRunId, "MissingModel");

        using var connection = fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status, error_category
            FROM asr_runs
            WHERE asr_run_id = $asr_run_id;
            """;
        command.Parameters.AddWithValue("$asr_run_id", asrRunId);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("failed", reader.GetString(0));
        Assert.Equal("MissingModel", reader.GetString(1));
    }

    [Fact]
    public void MarkCancelled_PersistsCancelledStatus()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        var paths = fixture.Paths;
        var repository = new AsrRunRepository(paths);

        var asrRunId = repository.Start("job-001", "faster-whisper-large-v3-turbo", "faster-whisper-large-v3-turbo");
        repository.MarkCancelled(asrRunId);

        using var connection = fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status, error_category
            FROM asr_runs
            WHERE asr_run_id = $asr_run_id;
            """;
        command.Parameters.AddWithValue("$asr_run_id", asrRunId);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("cancelled", reader.GetString(0));
        Assert.Equal("cancelled", reader.GetString(1));
    }

}
