using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Presets;

internal sealed class DomainPresetTermRepository
{
    private readonly DomainPresetAsrSettingsMerger _asrSettingsMerger = new();

    public DomainPresetTermImportResult Import(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DomainPreset preset,
        string now)
    {
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

        return new DomainPresetTermImportResult(
            addedMemory,
            updatedMemory,
            addedReviewGuideline,
            updatedReviewGuideline);
    }

    public DomainPresetTermClearResult Clear(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DomainPreset? preset,
        string presetId)
    {
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

        var disabledGuidelines = DisableReviewGuidelines(connection, transaction, presetId);
        return new DomainPresetTermClearResult(disabledMemory, disabledUserTerms, disabledGuidelines);
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

internal sealed record DomainPresetTermImportResult(
    int AddedCorrectionMemoryCount,
    int UpdatedCorrectionMemoryCount,
    int AddedReviewGuidelineCount,
    int UpdatedReviewGuidelineCount);

internal sealed record DomainPresetTermClearResult(
    int DisabledCorrectionMemoryCount,
    int DisabledUserTermCount,
    int DisabledReviewGuidelineCount);
