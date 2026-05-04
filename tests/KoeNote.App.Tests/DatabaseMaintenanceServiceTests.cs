using KoeNote.App.Services;

namespace KoeNote.App.Tests;

public sealed class DatabaseMaintenanceServiceTests
{
    [Fact]
    public void Run_PrunesLogsUndoHistoryAndMemoryEvents()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        var paths = fixture.Paths;
        InsertJob(paths, "job-001");
        InsertJob(paths, "job-002");
        for (var i = 0; i < 5; i++)
        {
            InsertLog(paths, "job-001", i);
            InsertLog(paths, "job-002", i);
            InsertReviewHistory(paths, "job-001", i);
            InsertMemoryEvent(paths, "job-001", i);
        }

        var summary = new DatabaseMaintenanceService(paths).Run(
            keepLogEventsPerJob: 2,
            keepGlobalLogEvents: 2,
            keepUndoHistoryPerJob: 2,
            keepCorrectionMemoryEventsPerJob: 2,
            vacuum: false);

        Assert.Equal(6, summary.DeletedLogEvents);
        Assert.Equal(3, summary.DeletedReviewHistory);
        Assert.Equal(3, summary.DeletedCorrectionMemoryEvents);
        Assert.False(summary.Vacuumed);
        Assert.Equal(2, CountRows(paths, "job_log_events", "job_id = 'job-001'"));
        Assert.Equal(2, CountRows(paths, "job_log_events", "job_id = 'job-002'"));
        Assert.Equal(2, CountRows(paths, "review_operation_history", "job_id = 'job-001'"));
        Assert.Equal(2, CountRows(paths, "correction_memory_events", "job_id = 'job-001'"));
    }

    private static void InsertJob(AppPaths paths, string jobId)
    {
        using var connection = TestDatabase.Open(paths);
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
            VALUES ($job_id, $job_id, 'test.wav', 'ready', 0, $now, $now);
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$now", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();
    }

    private static void InsertLog(AppPaths paths, string jobId, int index)
    {
        using var connection = TestDatabase.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO job_log_events (log_event_id, job_id, stage, level, message, created_at)
            VALUES ($id, $job_id, 'stage', 'info', 'message', $created_at);
            """;
        command.Parameters.AddWithValue("$id", $"{jobId}-log-{index:D3}");
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$created_at", DateTimeOffset.Now.AddMinutes(index).ToString("o"));
        command.ExecuteNonQuery();
    }

    private static void InsertReviewHistory(AppPaths paths, string jobId, int index)
    {
        using var connection = TestDatabase.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO review_operation_history (
                operation_id,
                job_id,
                draft_id,
                segment_id,
                operation_type,
                before_json,
                after_json,
                created_at
            )
            VALUES ($id, $job_id, NULL, NULL, 'edit', '{}', '{}', $created_at);
            """;
        command.Parameters.AddWithValue("$id", $"{jobId}-history-{index:D3}");
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$created_at", DateTimeOffset.Now.AddMinutes(index).ToString("o"));
        command.ExecuteNonQuery();
    }

    private static void InsertMemoryEvent(AppPaths paths, string jobId, int index)
    {
        using var connection = TestDatabase.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO correction_memory_events (
                event_id,
                memory_id,
                draft_id,
                job_id,
                segment_id,
                event_type,
                created_at
            )
            VALUES ($id, NULL, NULL, $job_id, NULL, 'accepted', $created_at);
            """;
        command.Parameters.AddWithValue("$id", $"{jobId}-memory-{index:D3}");
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$created_at", DateTimeOffset.Now.AddMinutes(index).ToString("o"));
        command.ExecuteNonQuery();
    }

    private static int CountRows(AppPaths paths, string table, string whereClause)
    {
        using var connection = TestDatabase.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {whereClause};";
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
