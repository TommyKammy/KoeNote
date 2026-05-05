using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Presets;

public sealed class ReviewGuidelineRepository(AppPaths paths)
{
    public IReadOnlyList<string> LoadEnabled(int limit = 20)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT guideline_text, MAX(updated_at) AS latest_updated_at
            FROM review_guidelines
            WHERE enabled = 1
            GROUP BY guideline_text
            ORDER BY latest_updated_at DESC, guideline_text
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var guidelines = new List<string>();
        while (reader.Read())
        {
            guidelines.Add(reader.GetString(0));
        }

        return guidelines;
    }

    internal static UpsertResult Upsert(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string presetId,
        string guidelineText,
        string now)
    {
        var exists = Exists(connection, transaction, presetId, guidelineText);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO review_guidelines (
                guideline_id,
                preset_id,
                guideline_text,
                enabled,
                created_at,
                updated_at
            )
            VALUES (
                $guideline_id,
                $preset_id,
                $guideline_text,
                1,
                $now,
                $now
            )
            ON CONFLICT(preset_id, guideline_text) DO UPDATE SET
                enabled = 1,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$guideline_id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$preset_id", presetId);
        command.Parameters.AddWithValue("$guideline_text", guidelineText);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
        return exists ? UpsertResult.Updated : UpsertResult.Inserted;
    }

    private static bool Exists(SqliteConnection connection, SqliteTransaction transaction, string presetId, string guidelineText)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM review_guidelines
            WHERE preset_id = $preset_id
                AND guideline_text = $guideline_text;
            """;
        command.Parameters.AddWithValue("$preset_id", presetId);
        command.Parameters.AddWithValue("$guideline_text", guidelineText);
        return command.ExecuteScalar() is not null;
    }
}

internal enum UpsertResult
{
    Inserted,
    Updated
}
