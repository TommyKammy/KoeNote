using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Tests;

public sealed class AsrRunRepositoryTests
{
    [Fact]
    public void StartAndMarkSucceeded_PersistAsrRunLifecycle()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var repository = new AsrRunRepository(paths);

        var asrRunId = repository.Start("job-001", "vibevoice-crispasr", "vibevoice-asr-q4_k");
        repository.MarkSucceeded(asrRunId, TimeSpan.FromSeconds(1.25), "raw.json", "segments.json");

        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT engine_id, model_id, status, duration_seconds, raw_output_path, normalized_output_path
            FROM asr_runs
            WHERE asr_run_id = $asr_run_id;
            """;
        command.Parameters.AddWithValue("$asr_run_id", asrRunId);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("vibevoice-crispasr", reader.GetString(0));
        Assert.Equal("vibevoice-asr-q4_k", reader.GetString(1));
        Assert.Equal("succeeded", reader.GetString(2));
        Assert.Equal(1.25, reader.GetDouble(3));
        Assert.Equal("raw.json", reader.GetString(4));
        Assert.Equal("segments.json", reader.GetString(5));
    }

    [Fact]
    public void MarkFailed_PersistsErrorCategory()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var repository = new AsrRunRepository(paths);

        var asrRunId = repository.Start("job-001", "vibevoice-crispasr", "vibevoice-asr-q4_k");
        repository.MarkFailed(asrRunId, "MissingModel");

        using var connection = Open(paths);
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
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var repository = new AsrRunRepository(paths);

        var asrRunId = repository.Start("job-001", "vibevoice-crispasr", "vibevoice-asr-q4_k");
        repository.MarkCancelled(asrRunId);

        using var connection = Open(paths);
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
}
