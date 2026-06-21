using System.IO;
using KoeNote.App.Services.Asr;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Presets;

internal sealed class DomainPresetImportHistoryRepository(AppPaths paths)
{
    public IReadOnlyList<DomainPresetImportHistoryItem> Load(int limit)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                import_id,
                preset_id,
                display_name,
                source_path,
                imported_at,
                deactivated_at,
                context_updated,
                added_hotword_count,
                added_correction_memory_count,
                added_review_guideline_count
            FROM domain_preset_imports
            ORDER BY imported_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var items = new List<DomainPresetImportHistoryItem>();
        while (reader.Read())
        {
            items.Add(ReadHistoryItem(reader));
        }

        return items;
    }

    public DomainPresetImportHistoryItem? LoadById(string importId)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                import_id,
                preset_id,
                display_name,
                source_path,
                imported_at,
                deactivated_at,
                context_updated,
                added_hotword_count,
                added_correction_memory_count,
                added_review_guideline_count
            FROM domain_preset_imports
            WHERE import_id = $import_id;
            """;
        command.Parameters.AddWithValue("$import_id", importId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadHistoryItem(reader) : null;
    }

    public void Record(
        string importId,
        string presetPath,
        DomainPreset preset,
        bool contextUpdated,
        DomainPresetHotwordMergeResult hotwordMerge,
        DomainPresetDatabaseImportResult databaseResult)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO domain_preset_imports (
                import_id,
                preset_id,
                display_name,
                schema_version,
                source_path,
                context_updated,
                added_hotword_count,
                skipped_hotword_count,
                added_correction_memory_count,
                updated_correction_memory_count,
                added_speaker_alias_count,
                updated_speaker_alias_count,
                skipped_speaker_alias_count,
                added_review_guideline_count,
                updated_review_guideline_count,
                imported_at
            )
            VALUES (
                $import_id,
                $preset_id,
                $display_name,
                $schema_version,
                $source_path,
                $context_updated,
                $added_hotword_count,
                $skipped_hotword_count,
                $added_correction_memory_count,
                $updated_correction_memory_count,
                $added_speaker_alias_count,
                $updated_speaker_alias_count,
                $skipped_speaker_alias_count,
                $added_review_guideline_count,
                $updated_review_guideline_count,
                $imported_at
            );
            """;
        command.Parameters.AddWithValue("$import_id", importId);
        command.Parameters.AddWithValue("$preset_id", (object?)preset.NormalizedPresetId ?? DBNull.Value);
        command.Parameters.AddWithValue("$display_name", preset.DisplayName);
        command.Parameters.AddWithValue("$schema_version", preset.SchemaVersion);
        command.Parameters.AddWithValue("$source_path", Path.GetFullPath(presetPath));
        command.Parameters.AddWithValue("$context_updated", contextUpdated ? 1 : 0);
        command.Parameters.AddWithValue("$added_hotword_count", hotwordMerge.AddedCount);
        command.Parameters.AddWithValue("$skipped_hotword_count", hotwordMerge.SkippedCount);
        command.Parameters.AddWithValue("$added_correction_memory_count", databaseResult.AddedCorrectionMemoryCount);
        command.Parameters.AddWithValue("$updated_correction_memory_count", databaseResult.UpdatedCorrectionMemoryCount);
        command.Parameters.AddWithValue("$added_speaker_alias_count", databaseResult.AddedSpeakerAliasCount);
        command.Parameters.AddWithValue("$updated_speaker_alias_count", databaseResult.UpdatedSpeakerAliasCount);
        command.Parameters.AddWithValue("$skipped_speaker_alias_count", databaseResult.SkippedSpeakerAliasCount);
        command.Parameters.AddWithValue("$added_review_guideline_count", databaseResult.AddedReviewGuidelineCount);
        command.Parameters.AddWithValue("$updated_review_guideline_count", databaseResult.UpdatedReviewGuidelineCount);
        command.Parameters.AddWithValue("$imported_at", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();
    }

    public void MarkCleared(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string importId,
        string deactivatedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE domain_preset_imports
            SET deactivated_at = $deactivated_at
            WHERE import_id = $import_id
                AND deactivated_at IS NULL;
            """;
        command.Parameters.AddWithValue("$import_id", importId);
        command.Parameters.AddWithValue("$deactivated_at", deactivatedAt);
        command.ExecuteNonQuery();
    }

    private static DomainPresetImportHistoryItem ReadHistoryItem(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            DateTimeOffset.Parse(reader.GetString(4)),
            reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)),
            reader.GetInt32(6) != 0,
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9));
}
