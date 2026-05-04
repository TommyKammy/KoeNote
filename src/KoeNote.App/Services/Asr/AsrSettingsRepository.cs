namespace KoeNote.App.Services.Asr;

public sealed record AsrSettings(
    string ContextText,
    string HotwordsText,
    string EngineId = VibeVoiceCrispAsrEngine.Id,
    bool EnableReviewStage = true)
{
    public IReadOnlyList<string> Hotwords => HotwordsText
        .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

public sealed class AsrSettingsRepository(AppPaths paths)
{
    public AsrSettings Load()
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT context_text, hotwords_text, engine_id, enable_review_stage
            FROM asr_settings
            WHERE settings_id = 1;
            """;

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return new AsrSettings(string.Empty, string.Empty);
        }

        return new AsrSettings(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3) != 0);
    }

    public void Save(AsrSettings settings)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO asr_settings (settings_id, context_text, hotwords_text, engine_id, enable_review_stage, updated_at)
            VALUES (1, $context_text, $hotwords_text, $engine_id, $enable_review_stage, $updated_at)
            ON CONFLICT(settings_id) DO UPDATE SET
                context_text = excluded.context_text,
                hotwords_text = excluded.hotwords_text,
                engine_id = excluded.engine_id,
                enable_review_stage = excluded.enable_review_stage,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$context_text", settings.ContextText);
        command.Parameters.AddWithValue("$hotwords_text", settings.HotwordsText);
        command.Parameters.AddWithValue("$engine_id", settings.EngineId);
        command.Parameters.AddWithValue("$enable_review_stage", settings.EnableReviewStage ? 1 : 0);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();
    }

}
