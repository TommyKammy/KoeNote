using KoeNote.App.Services;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Tests;

internal static class TestDatabase
{
    public static AppPaths CreateReadyPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var local = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, local);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        return paths;
    }

    public static SqliteConnection Open(AppPaths paths)
    {
        return SqliteConnectionFactory.Open(paths);
    }

    public static void InsertReviewReadyJob(AppPaths paths, string jobId, string title)
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
                $title,
                'test.wav',
                'review_ready',
                90,
                $now,
                $now
            );
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$now", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();
    }
}
