using Microsoft.Data.Sqlite;
using System.IO;
using System.Text;

namespace KoeNote.App.Services.Jobs;

public sealed class JobLogRepository(AppPaths paths)
{
    public string SaveWorkerLog(string jobId, string stage, string standardOutput, string standardError)
    {
        var logDirectory = Path.Combine(paths.Jobs, jobId, "logs");
        Directory.CreateDirectory(logDirectory);

        var logPath = Path.Combine(logDirectory, $"{stage}.log");
        var content = $"""
            # {stage}

            ## stdout
            {standardOutput}

            ## stderr
            {standardError}
            """;

        File.WriteAllText(logPath, content, Encoding.UTF8);

        AddEvent(jobId, stage, "info", $"Saved {stage} worker log: {logPath}");
        return logPath;
    }

    public void AddEvent(string? jobId, string? stage, string level, string message)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO job_log_events (
                log_event_id,
                job_id,
                stage,
                level,
                message,
                created_at
            )
            VALUES (
                $log_event_id,
                $job_id,
                $stage,
                $level,
                $message,
                $created_at
            );
            """;
        command.Parameters.AddWithValue("$log_event_id", $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("$job_id", (object?)jobId ?? DBNull.Value);
        command.Parameters.AddWithValue("$stage", (object?)stage ?? DBNull.Value);
        command.Parameters.AddWithValue("$level", level);
        command.Parameters.AddWithValue("$message", message);
        command.Parameters.AddWithValue("$created_at", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();
        return connection;
    }
}
