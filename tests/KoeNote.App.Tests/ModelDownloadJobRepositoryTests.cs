using KoeNote.App.Services;
using KoeNote.App.Services.Models;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Tests;

public sealed class ModelDownloadJobRepositoryTests
{
    [Fact]
    public void StartMarkPausedAndMarkFailed_PersistsDownloadJob()
    {
        var paths = new AppPaths(CreateRoot(), CreateRoot(), AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var repository = new ModelDownloadJobRepository(paths);

        var downloadId = repository.Start("model-001", "https://example.com/model.bin", "target.bin", "target.bin.partial", "abc");
        repository.UpdateProgress(downloadId, 12, 100);
        repository.MarkPaused(downloadId);
        Assert.Equal("paused", repository.Find(downloadId)?.Status);
        repository.MarkFailed(downloadId, "network unavailable", "def");

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status, bytes_downloaded, bytes_total, error_message, sha256_actual
            FROM model_download_jobs
            WHERE download_id = $download_id;
            """;
        command.Parameters.AddWithValue("$download_id", downloadId);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("failed", reader.GetString(0));
        Assert.Equal(12, reader.GetInt64(1));
        Assert.Equal(100, reader.GetInt64(2));
        Assert.Equal("network unavailable", reader.GetString(3));
        Assert.Equal("def", reader.GetString(4));
        Assert.Equal(downloadId, repository.FindLatestForModel("model-001")?.DownloadId);
    }

    [Fact]
    public void MarkCancelled_PersistsCancelledState()
    {
        var paths = new AppPaths(CreateRoot(), CreateRoot(), AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var repository = new ModelDownloadJobRepository(paths);

        var downloadId = repository.Start("model-001", "https://example.com/model.bin", "target.bin", "target.bin.partial", null);
        repository.MarkCancelled(downloadId);

        Assert.Equal("cancelled", repository.Find(downloadId)?.Status);
    }

    private static string CreateRoot()
    {
        return Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
    }
}
