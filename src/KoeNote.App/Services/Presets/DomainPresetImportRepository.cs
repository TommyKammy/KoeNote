using System.IO;
using KoeNote.App.Services.Asr;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Presets;

internal sealed class DomainPresetImportRepository(AppPaths paths)
{
    private readonly DomainPresetImportHistoryRepository _historyRepository = new(paths);
    private readonly DomainPresetTermRepository _termRepository = new();

    public IReadOnlyList<DomainPresetImportHistoryItem> LoadHistory(int limit) =>
        _historyRepository.Load(limit);

    public DomainPresetImportHistoryItem? LoadHistoryById(string importId) =>
        _historyRepository.LoadById(importId);

    public DomainPresetDatabaseImportResult ImportDatabaseEntries(DomainPreset preset, string importId, string? defaultJobId)
    {
        if (preset.CorrectionMemory.Count == 0 &&
            preset.SpeakerAliases.Count == 0 &&
            preset.ReviewGuidelines.Count == 0)
        {
            return new DomainPresetDatabaseImportResult(0, 0, 0, 0, 0, 0, 0);
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.Now.ToString("o");
        var termResult = _termRepository.Import(connection, transaction, preset, now);

        var addedAlias = 0;
        var updatedAlias = 0;
        var skippedAlias = 0;
        foreach (var alias in preset.SpeakerAliases)
        {
            var jobId = string.IsNullOrWhiteSpace(alias.JobId) ? defaultJobId : alias.JobId;
            if (string.IsNullOrWhiteSpace(jobId) ||
                string.IsNullOrWhiteSpace(alias.SpeakerId) ||
                string.IsNullOrWhiteSpace(alias.DisplayName))
            {
                skippedAlias++;
                continue;
            }

            var speakerId = alias.SpeakerId?.Trim();
            var displayName = alias.DisplayName?.Trim();
            if (speakerId is null || displayName is null)
            {
                skippedAlias++;
                continue;
            }

            var normalizedJobId = jobId.Trim();
            var previousDisplayName = LoadSpeakerAliasDisplayName(connection, transaction, normalizedJobId, speakerId);
            var result = UpsertSpeakerAlias(connection, transaction, normalizedJobId, speakerId, displayName, now);
            TrackSpeakerAliasImport(connection, transaction, importId, normalizedJobId, speakerId, previousDisplayName, displayName);
            if (result == UpsertResult.Inserted)
            {
                addedAlias++;
            }
            else
            {
                updatedAlias++;
            }
        }

        if (preset.CorrectionMemory.Count == 0 &&
            preset.ReviewGuidelines.Count == 0 &&
            preset.SpeakerAliases.Count > 0 &&
            addedAlias == 0 &&
            updatedAlias == 0 &&
            skippedAlias > 0)
        {
            throw new InvalidDataException("話者別名を適用するジョブがありません。ジョブを選択してからインポートするか、プリセットに job_id を指定してください。");
        }

        transaction.Commit();
        return new DomainPresetDatabaseImportResult(
            termResult.AddedCorrectionMemoryCount,
            termResult.UpdatedCorrectionMemoryCount,
            addedAlias,
            updatedAlias,
            skippedAlias,
            termResult.AddedReviewGuidelineCount,
            termResult.UpdatedReviewGuidelineCount);
    }

    public void RecordImportHistory(
        string importId,
        string presetPath,
        DomainPreset preset,
        bool contextUpdated,
        DomainPresetHotwordMergeResult hotwordMerge,
        DomainPresetDatabaseImportResult databaseResult) =>
        _historyRepository.Record(importId, presetPath, preset, contextUpdated, hotwordMerge, databaseResult);

    public DomainPresetDatabaseClearResult ClearImportedDatabaseEntries(
        DomainPreset? preset,
        DomainPresetImportHistoryItem history,
        string presetId)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();
        var termClear = _termRepository.Clear(connection, transaction, preset, presetId);

        var deletedAliases = RevertTrackedSpeakerAliases(connection, transaction, history.ImportId);
        _historyRepository.MarkCleared(connection, transaction, history.ImportId, DateTimeOffset.Now.ToString("o"));
        transaction.Commit();
        return new DomainPresetDatabaseClearResult(
            termClear.DisabledCorrectionMemoryCount,
            termClear.DisabledUserTermCount,
            deletedAliases,
            termClear.DisabledReviewGuidelineCount);
    }

    private static int DeleteSpeakerAlias(SqliteConnection connection, SqliteTransaction transaction, string jobId, string speakerId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM speaker_aliases
            WHERE job_id = $job_id
                AND speaker_id = $speaker_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$speaker_id", speakerId);
        return command.ExecuteNonQuery();
    }

    private static int RestoreSpeakerAlias(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        string speakerId,
        string displayName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE speaker_aliases
            SET display_name = $display_name,
                updated_at = $updated_at
            WHERE job_id = $job_id
                AND speaker_id = $speaker_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$speaker_id", speakerId);
        command.Parameters.AddWithValue("$display_name", displayName);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        return command.ExecuteNonQuery();
    }

    private static void TrackSpeakerAliasImport(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string importId,
        string jobId,
        string speakerId,
        string? previousDisplayName,
        string appliedDisplayName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO domain_preset_speaker_alias_imports (
                import_id,
                job_id,
                speaker_id,
                previous_display_name,
                applied_display_name
            )
            VALUES (
                $import_id,
                $job_id,
                $speaker_id,
                $previous_display_name,
                $applied_display_name
            );
            """;
        command.Parameters.AddWithValue("$import_id", importId);
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$speaker_id", speakerId);
        command.Parameters.AddWithValue("$previous_display_name", (object?)previousDisplayName ?? DBNull.Value);
        command.Parameters.AddWithValue("$applied_display_name", appliedDisplayName);
        command.ExecuteNonQuery();
    }

    private static int RevertTrackedSpeakerAliases(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string importId)
    {
        using var selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText = """
            SELECT job_id, speaker_id, previous_display_name
            FROM domain_preset_speaker_alias_imports
            WHERE import_id = $import_id;
            """;
        selectCommand.Parameters.AddWithValue("$import_id", importId);

        var aliases = new List<(string JobId, string SpeakerId, string? PreviousDisplayName)>();
        using (var reader = selectCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                aliases.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        var changed = 0;
        foreach (var alias in aliases)
        {
            changed += alias.PreviousDisplayName is null
                ? DeleteSpeakerAlias(connection, transaction, alias.JobId, alias.SpeakerId)
                : RestoreSpeakerAlias(connection, transaction, alias.JobId, alias.SpeakerId, alias.PreviousDisplayName);
        }

        using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = """
            DELETE FROM domain_preset_speaker_alias_imports
            WHERE import_id = $import_id;
            """;
        deleteCommand.Parameters.AddWithValue("$import_id", importId);
        deleteCommand.ExecuteNonQuery();
        return changed;
    }

    private static UpsertResult UpsertSpeakerAlias(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        string speakerId,
        string displayName,
        string now)
    {
        var exists = SpeakerAliasExists(connection, transaction, jobId, speakerId);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO speaker_aliases (job_id, speaker_id, display_name, updated_at)
            VALUES ($job_id, $speaker_id, $display_name, $updated_at)
            ON CONFLICT(job_id, speaker_id) DO UPDATE SET
                display_name = excluded.display_name,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$speaker_id", speakerId);
        command.Parameters.AddWithValue("$display_name", displayName);
        command.Parameters.AddWithValue("$updated_at", now);
        command.ExecuteNonQuery();
        return exists ? UpsertResult.Updated : UpsertResult.Inserted;
    }

    private static string? LoadSpeakerAliasDisplayName(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        string speakerId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT display_name
            FROM speaker_aliases
            WHERE job_id = $job_id AND speaker_id = $speaker_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$speaker_id", speakerId);
        return command.ExecuteScalar() as string;
    }

    private static bool SpeakerAliasExists(SqliteConnection connection, SqliteTransaction transaction, string jobId, string speakerId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM speaker_aliases
            WHERE job_id = $job_id AND speaker_id = $speaker_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$speaker_id", speakerId);
        return command.ExecuteScalar() is not null;
    }

}

internal sealed record DomainPresetDatabaseImportResult(
    int AddedCorrectionMemoryCount,
    int UpdatedCorrectionMemoryCount,
    int AddedSpeakerAliasCount,
    int UpdatedSpeakerAliasCount,
    int SkippedSpeakerAliasCount,
    int AddedReviewGuidelineCount,
    int UpdatedReviewGuidelineCount);

internal sealed record DomainPresetDatabaseClearResult(
    int DisabledCorrectionMemoryCount,
    int DisabledUserTermCount,
    int DeletedSpeakerAliasCount,
    int DisabledReviewGuidelineCount);
