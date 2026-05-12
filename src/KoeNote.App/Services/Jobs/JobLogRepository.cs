using KoeNote.App.Models;
using System.IO;
using System.Text;

namespace KoeNote.App.Services.Jobs;

public sealed class JobLogRepository(AppPaths paths)
{
    public string SaveWorkerLog(
        string jobId,
        string stage,
        string standardOutput,
        string standardError,
        IReadOnlyDictionary<string, string>? metadata = null,
        string? fileName = null)
    {
        var logDirectory = Path.Combine(paths.Jobs, jobId, "logs");
        Directory.CreateDirectory(logDirectory);

        var logPath = Path.Combine(logDirectory, fileName ?? $"{stage}.log");
        var builder = new StringBuilder();
        builder.AppendLine($"# {stage}");
        builder.AppendLine();
        if (metadata is not null && metadata.Count > 0)
        {
            builder.AppendLine("## metadata");
            foreach (var item in metadata)
            {
                builder.AppendLine($"{item.Key}: {item.Value}");
            }

            builder.AppendLine();
        }

        builder.AppendLine("## stdout");
        builder.AppendLine(standardOutput);
        builder.AppendLine();
        builder.AppendLine("## stderr");
        builder.AppendLine(standardError);

        File.WriteAllText(logPath, builder.ToString(), Encoding.UTF8);

        AddEvent(jobId, stage, "info", $"Saved {stage} worker log: {logPath}");
        return logPath;
    }

    public void AddEvent(string? jobId, string? stage, string level, string message)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
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

    public IReadOnlyList<JobLogEntry> ReadLatest(string? jobId, int limit = 80)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(jobId))
        {
            command.CommandText = """
                SELECT created_at, level, COALESCE(stage, ''), message
                FROM job_log_events
                ORDER BY created_at DESC
                LIMIT $limit;
                """;
        }
        else
        {
            command.CommandText = """
                SELECT created_at, level, COALESCE(stage, ''), message
                FROM job_log_events
                WHERE job_id = $job_id
                ORDER BY created_at DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$job_id", jobId);
        }

        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var entries = new List<JobLogEntry>();
        while (reader.Read())
        {
            entries.Add(new JobLogEntry(
                DateTimeOffset.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        entries.Reverse();
        return entries;
    }

    public IReadOnlyList<JobLogEntry> ReadForJob(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return [];
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT created_at, level, COALESCE(stage, ''), message
            FROM job_log_events
            WHERE job_id = $job_id
            ORDER BY created_at ASC;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);

        using var reader = command.ExecuteReader();
        var entries = new List<JobLogEntry>();
        while (reader.Read())
        {
            entries.Add(new JobLogEntry(
                DateTimeOffset.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return entries;
    }

    public IReadOnlyList<DiagnosticLogEntry> ReadForDiagnostics(string? jobId, int limit = 500)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(jobId))
        {
            command.CommandText = """
                SELECT created_at, job_id, level, COALESCE(stage, ''), message
                FROM job_log_events
                ORDER BY created_at DESC
                LIMIT $limit;
                """;
        }
        else
        {
            command.CommandText = """
                SELECT created_at, job_id, level, COALESCE(stage, ''), message
                FROM job_log_events
                WHERE job_id = $job_id
                ORDER BY created_at DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$job_id", jobId);
        }

        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var entries = new List<DiagnosticLogEntry>();
        while (reader.Read())
        {
            entries.Add(new DiagnosticLogEntry(
                DateTimeOffset.Parse(reader.GetString(0)),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        entries.Reverse();
        return entries;
    }

}
