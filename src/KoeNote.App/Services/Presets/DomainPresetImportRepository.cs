using System.IO;
using KoeNote.App.Services.Asr;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Presets;

internal sealed class DomainPresetImportRepository(AppPaths paths)
{
    private readonly DomainPresetAsrSettingsMerger _asrSettingsMerger = new();
    private readonly DomainPresetImportHistoryRepository _historyRepository = new(paths);

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
        var addedMemory = 0;
        var updatedMemory = 0;
        foreach (var entry in preset.CorrectionMemory)
        {
            if (string.IsNullOrWhiteSpace(entry.WrongText) ||
                string.IsNullOrWhiteSpace(entry.CorrectText))
            {
                continue;
            }

            var wrongText = entry.WrongText?.Trim();
            var correctText = entry.CorrectText?.Trim();
            if (wrongText is null || correctText is null)
            {
                continue;
            }

            var result = UpsertCorrectionMemory(connection, transaction, entry, wrongText, correctText, now);
            if (result == UpsertResult.Inserted)
            {
                addedMemory++;
            }
            else
            {
                updatedMemory++;
            }

            if (_asrSettingsMerger.IsAsrHotwordCandidate(correctText))
            {
                UpsertUserTerm(connection, transaction, correctText, now);
            }
        }

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

        var addedReviewGuideline = 0;
        var updatedReviewGuideline = 0;
        foreach (var guideline in preset.ReviewGuidelines)
        {
            if (string.IsNullOrWhiteSpace(guideline))
            {
                continue;
            }

            var result = ReviewGuidelineRepository.Upsert(
                connection,
                transaction,
                preset.GuidelinePresetId,
                guideline.Trim(),
                now);
            if (result == UpsertResult.Inserted)
            {
                addedReviewGuideline++;
            }
            else
            {
                updatedReviewGuideline++;
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
            addedMemory,
            updatedMemory,
            addedAlias,
            updatedAlias,
            skippedAlias,
            addedReviewGuideline,
            updatedReviewGuideline);
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
        var disabledMemory = 0;
        var disabledUserTerms = 0;

        if (preset is not null)
        {
            foreach (var entry in preset.CorrectionMemory)
            {
                if (string.IsNullOrWhiteSpace(entry.WrongText) || string.IsNullOrWhiteSpace(entry.CorrectText))
                {
                    continue;
                }

                disabledMemory += DisableCorrectionMemory(
                    connection,
                    transaction,
                    entry.WrongText.Trim(),
                    entry.CorrectText.Trim(),
                    string.IsNullOrWhiteSpace(entry.Scope) ? "global" : entry.Scope.Trim());

                if (_asrSettingsMerger.IsAsrHotwordCandidate(entry.CorrectText))
                {
                    disabledUserTerms += DisableUserTerm(connection, transaction, entry.CorrectText.Trim());
                }
            }
        }

        var deletedAliases = RevertTrackedSpeakerAliases(connection, transaction, history.ImportId);
        var disabledGuidelines = DisableReviewGuidelines(connection, transaction, presetId);
        _historyRepository.MarkCleared(connection, transaction, history.ImportId, DateTimeOffset.Now.ToString("o"));
        transaction.Commit();
        return new DomainPresetDatabaseClearResult(disabledMemory, disabledUserTerms, deletedAliases, disabledGuidelines);
    }

    private static int DisableCorrectionMemory(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string wrongText,
        string correctText,
        string scope)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE correction_memory
            SET enabled = 0,
                updated_at = $updated_at
            WHERE wrong_text = $wrong_text
                AND correct_text = $correct_text
                AND scope = $scope
                AND enabled = 1
                AND accepted_count <= 1
                AND rejected_count = 0;
            """;
        command.Parameters.AddWithValue("$wrong_text", wrongText);
        command.Parameters.AddWithValue("$correct_text", correctText);
        command.Parameters.AddWithValue("$scope", scope);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        return command.ExecuteNonQuery();
    }

    private static int DisableUserTerm(SqliteConnection connection, SqliteTransaction transaction, string surface)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE user_terms
            SET enabled = 0,
                updated_at = $updated_at
            WHERE surface = $surface
                AND category = 'domain_preset'
                AND enabled = 1;
            """;
        command.Parameters.AddWithValue("$surface", surface);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        return command.ExecuteNonQuery();
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

    private static int DisableReviewGuidelines(SqliteConnection connection, SqliteTransaction transaction, string presetId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE review_guidelines
            SET enabled = 0,
                updated_at = $updated_at
            WHERE preset_id = $preset_id
                AND enabled = 1;
            """;
        command.Parameters.AddWithValue("$preset_id", presetId);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        return command.ExecuteNonQuery();
    }

    private static UpsertResult UpsertCorrectionMemory(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CorrectionMemoryPresetEntry entry,
        string wrongText,
        string correctText,
        string now)
    {
        var scope = string.IsNullOrWhiteSpace(entry.Scope) ? "global" : entry.Scope.Trim();
        var exists = CorrectionMemoryExists(connection, transaction, wrongText, correctText, scope);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO correction_memory (
                memory_id,
                wrong_text,
                correct_text,
                issue_type,
                scope,
                accepted_count,
                rejected_count,
                enabled,
                created_at,
                updated_at
            )
            VALUES (
                $memory_id,
                $wrong_text,
                $correct_text,
                $issue_type,
                $scope,
                1,
                0,
                1,
                $now,
                $now
            )
            ON CONFLICT(wrong_text, correct_text, scope) DO UPDATE SET
                issue_type = excluded.issue_type,
                enabled = 1,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$memory_id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$wrong_text", wrongText);
        command.Parameters.AddWithValue("$correct_text", correctText);
        command.Parameters.AddWithValue("$issue_type", string.IsNullOrWhiteSpace(entry.IssueType) ? "domain_preset" : entry.IssueType.Trim());
        command.Parameters.AddWithValue("$scope", scope);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
        return exists ? UpsertResult.Updated : UpsertResult.Inserted;
    }

    private static bool CorrectionMemoryExists(SqliteConnection connection, SqliteTransaction transaction, string wrongText, string correctText, string scope)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM correction_memory
            WHERE wrong_text = $wrong_text
                AND correct_text = $correct_text
                AND scope = $scope;
            """;
        command.Parameters.AddWithValue("$wrong_text", wrongText);
        command.Parameters.AddWithValue("$correct_text", correctText);
        command.Parameters.AddWithValue("$scope", scope);
        return command.ExecuteScalar() is not null;
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

    private static void UpsertUserTerm(SqliteConnection connection, SqliteTransaction transaction, string surface, string now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO user_terms (
                term_id,
                surface,
                category,
                enabled,
                created_at,
                updated_at
            )
            VALUES (
                $term_id,
                $surface,
                'domain_preset',
                1,
                $now,
                $now
            )
            ON CONFLICT(surface, category) DO UPDATE SET
                enabled = 1,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$term_id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$surface", surface);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
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
