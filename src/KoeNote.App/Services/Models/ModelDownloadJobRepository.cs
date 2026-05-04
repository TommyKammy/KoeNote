namespace KoeNote.App.Services.Models;

public sealed class ModelDownloadJobRepository(AppPaths paths)
{
    public ModelDownloadJob? Find(string downloadId)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT download_id, model_id, url, target_path, temp_path, status, bytes_total, bytes_downloaded,
                   sha256_expected, sha256_actual, error_message, created_at, updated_at
            FROM model_download_jobs
            WHERE download_id = $download_id;
            """;
        command.Parameters.AddWithValue("$download_id", downloadId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadJob(reader) : null;
    }

    public ModelDownloadJob? FindLatestForModel(string modelId)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT download_id, model_id, url, target_path, temp_path, status, bytes_total, bytes_downloaded,
                   sha256_expected, sha256_actual, error_message, created_at, updated_at
            FROM model_download_jobs
            WHERE model_id = $model_id
            ORDER BY updated_at DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$model_id", modelId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadJob(reader) : null;
    }

    public string Start(string modelId, string url, string targetPath, string tempPath, string? sha256Expected)
    {
        var downloadId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.Now.ToString("o");
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO model_download_jobs (
                download_id, model_id, url, target_path, temp_path, status,
                bytes_downloaded, sha256_expected, created_at, updated_at
            )
            VALUES (
                $download_id, $model_id, $url, $target_path, $temp_path, 'running',
                0, $sha256_expected, $created_at, $updated_at
            );
            """;
        command.Parameters.AddWithValue("$download_id", downloadId);
        command.Parameters.AddWithValue("$model_id", modelId);
        command.Parameters.AddWithValue("$url", url);
        command.Parameters.AddWithValue("$target_path", targetPath);
        command.Parameters.AddWithValue("$temp_path", tempPath);
        command.Parameters.AddWithValue("$sha256_expected", (object?)sha256Expected ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", now);
        command.Parameters.AddWithValue("$updated_at", now);
        command.ExecuteNonQuery();
        return downloadId;
    }

    public void UpdateProgress(string downloadId, long bytesDownloaded, long? bytesTotal)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE model_download_jobs
            SET bytes_downloaded = $bytes_downloaded,
                bytes_total = $bytes_total,
                updated_at = $updated_at
            WHERE download_id = $download_id;
            """;
        command.Parameters.AddWithValue("$download_id", downloadId);
        command.Parameters.AddWithValue("$bytes_downloaded", bytesDownloaded);
        command.Parameters.AddWithValue("$bytes_total", (object?)bytesTotal ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();
    }

    public void MarkSucceeded(string downloadId, string? sha256Actual)
    {
        Finish(downloadId, "succeeded", sha256Actual, null);
    }

    public void MarkFailed(string downloadId, string errorMessage, string? sha256Actual = null)
    {
        Finish(downloadId, "failed", sha256Actual, errorMessage);
    }

    public void MarkCancelled(string downloadId)
    {
        Finish(downloadId, "cancelled", null, "cancelled");
    }

    public void MarkPaused(string downloadId)
    {
        Finish(downloadId, "paused", null, null);
    }

    private void Finish(string downloadId, string status, string? sha256Actual, string? errorMessage)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE model_download_jobs
            SET status = $status,
                sha256_actual = $sha256_actual,
                error_message = $error_message,
                updated_at = $updated_at
            WHERE download_id = $download_id;
            """;
        command.Parameters.AddWithValue("$download_id", downloadId);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$sha256_actual", (object?)sha256Actual ?? DBNull.Value);
        command.Parameters.AddWithValue("$error_message", (object?)errorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();
    }

    private static ModelDownloadJob ReadJob(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        return new ModelDownloadJob(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetInt64(6),
            reader.GetInt64(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            DateTimeOffset.Parse(reader.GetString(11)),
            DateTimeOffset.Parse(reader.GetString(12)));
    }
}
