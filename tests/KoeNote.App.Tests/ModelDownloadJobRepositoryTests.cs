using KoeNote.App.Services.Models;

namespace KoeNote.App.Tests;

public sealed class ModelDownloadJobRepositoryTests
{
    [Fact]
    public void StartMarkPausedAndMarkFailed_PersistsDownloadJob()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        var paths = fixture.Paths;
        var repository = new ModelDownloadJobRepository(paths);

        var downloadId = repository.Start("model-001", "https://example.com/model.bin", "target.bin", "target.bin.partial", "abc");
        repository.UpdateProgress(downloadId, 12, 100);
        repository.MarkPaused(downloadId);
        Assert.Equal("paused", repository.Find(downloadId)?.Status);
        repository.MarkFailed(downloadId, "network unavailable", "def");

        using var connection = fixture.Open();
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
        var paths = TestDatabase.CreateRepositoryFixture().Paths;
        var repository = new ModelDownloadJobRepository(paths);

        var downloadId = repository.Start("model-001", "https://example.com/model.bin", "target.bin", "target.bin.partial", null);
        repository.MarkCancelled(downloadId);

        Assert.Equal("cancelled", repository.Find(downloadId)?.Status);
    }

    [Fact]
    public void MarkRunningJobsInterrupted_MakesStaleDownloadsResumable()
    {
        var paths = TestDatabase.CreateRepositoryFixture().Paths;
        var repository = new ModelDownloadJobRepository(paths);
        var runningId = repository.Start("model-running", "https://example.com/running.bin", "running.bin", "running.bin.partial", null);
        repository.UpdateProgress(runningId, 62, 100);
        var pausedId = repository.Start("model-paused", "https://example.com/paused.bin", "paused.bin", "paused.bin.partial", null);
        repository.MarkPaused(pausedId);

        var changed = repository.MarkRunningJobsInterrupted();

        var running = repository.Find(runningId);
        var paused = repository.Find(pausedId);
        Assert.Equal(1, changed);
        Assert.Equal("paused", running?.Status);
        Assert.Equal(62, running?.BytesDownloaded);
        Assert.Equal(100, running?.BytesTotal);
        Assert.Contains("中断", running?.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal("paused", paused?.Status);
        Assert.Null(paused?.ErrorMessage);
    }

    [Fact]
    public void DeleteForModel_RemovesDownloadHistoryForModelOnly()
    {
        var paths = TestDatabase.CreateRepositoryFixture().Paths;
        var repository = new ModelDownloadJobRepository(paths);
        var firstId = repository.Start("model-delete", "https://example.com/1.bin", "1.bin", "1.bin.partial", null);
        var secondId = repository.Start("model-keep", "https://example.com/2.bin", "2.bin", "2.bin.partial", null);

        var deleted = repository.DeleteForModel("model-delete");

        Assert.Equal(1, deleted);
        Assert.Null(repository.Find(firstId));
        Assert.NotNull(repository.Find(secondId));
    }

}
