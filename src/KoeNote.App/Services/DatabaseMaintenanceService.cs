using System.IO;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services;

public sealed record DatabaseMaintenanceSummary(
    long SizeBeforeBytes,
    long SizeAfterBytes,
    int DeletedLogEvents,
    int DeletedReviewHistory,
    int DeletedCorrectionMemoryEvents,
    bool Vacuumed)
{
    public long FreedBytes => Math.Max(0, SizeBeforeBytes - SizeAfterBytes);
}

public sealed class DatabaseMaintenanceService(AppPaths paths)
{
    public DatabaseMaintenanceSummary Run(
        int keepLogEventsPerJob = 500,
        int keepGlobalLogEvents = 500,
        int keepUndoHistoryPerJob = 200,
        int keepCorrectionMemoryEventsPerJob = 500,
        bool vacuum = true)
    {
        var sizeBefore = GetDatabaseSize();
        var deletedLogEvents = 0;
        var deletedReviewHistory = 0;
        var deletedCorrectionMemoryEvents = 0;

        using (var connection = SqliteConnectionFactory.Open(paths))
        {
            deletedLogEvents += PrunePartitionedRows(
                connection,
                "job_log_events",
                "log_event_id",
                "job_id",
                "created_at",
                keepLogEventsPerJob,
                "job_id IS NOT NULL");
            deletedLogEvents += PruneUnpartitionedRows(
                connection,
                "job_log_events",
                "log_event_id",
                "created_at",
                keepGlobalLogEvents,
                "job_id IS NULL");
            deletedReviewHistory += PrunePartitionedRows(
                connection,
                "review_operation_history",
                "operation_id",
                "job_id",
                "created_at",
                keepUndoHistoryPerJob,
                "job_id IS NOT NULL");
            deletedCorrectionMemoryEvents += PrunePartitionedRows(
                connection,
                "correction_memory_events",
                "event_id",
                "job_id",
                "created_at",
                keepCorrectionMemoryEventsPerJob,
                "job_id IS NOT NULL");

            Execute(connection, "PRAGMA optimize;");
        }

        if (vacuum && (deletedLogEvents > 0 || deletedReviewHistory > 0 || deletedCorrectionMemoryEvents > 0))
        {
            using var vacuumConnection = SqliteConnectionFactory.Open(paths);
            Execute(vacuumConnection, "VACUUM;");
        }

        return new DatabaseMaintenanceSummary(
            sizeBefore,
            GetDatabaseSize(),
            deletedLogEvents,
            deletedReviewHistory,
            deletedCorrectionMemoryEvents,
            vacuum && (deletedLogEvents > 0 || deletedReviewHistory > 0 || deletedCorrectionMemoryEvents > 0));
    }

    public long GetDatabaseSize()
    {
        return File.Exists(paths.DatabasePath)
            ? new FileInfo(paths.DatabasePath).Length
            : 0;
    }

    private static int PrunePartitionedRows(
        SqliteConnection connection,
        string table,
        string idColumn,
        string partitionColumn,
        string orderColumn,
        int keepPerPartition,
        string whereClause)
    {
        if (keepPerPartition < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(keepPerPartition), keepPerPartition, "Keep count must be greater than zero.");
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM {table}
            WHERE {whereClause}
              AND {idColumn} IN (
                  SELECT {idColumn}
                  FROM (
                      SELECT
                          {idColumn},
                          ROW_NUMBER() OVER (
                              PARTITION BY {partitionColumn}
                              ORDER BY {orderColumn} DESC, {idColumn} DESC
                          ) AS row_number
                      FROM {table}
                      WHERE {whereClause}
                  )
                  WHERE row_number > $keep
              );
            """;
        command.Parameters.AddWithValue("$keep", keepPerPartition);
        return command.ExecuteNonQuery();
    }

    private static int PruneUnpartitionedRows(
        SqliteConnection connection,
        string table,
        string idColumn,
        string orderColumn,
        int keepCount,
        string whereClause)
    {
        if (keepCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(keepCount), keepCount, "Keep count must be greater than zero.");
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM {table}
            WHERE {whereClause}
              AND {idColumn} IN (
                  SELECT {idColumn}
                  FROM (
                      SELECT
                          {idColumn},
                          ROW_NUMBER() OVER (ORDER BY {orderColumn} DESC, {idColumn} DESC) AS row_number
                      FROM {table}
                      WHERE {whereClause}
                  )
                  WHERE row_number > $keep
              );
            """;
        command.Parameters.AddWithValue("$keep", keepCount);
        return command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
