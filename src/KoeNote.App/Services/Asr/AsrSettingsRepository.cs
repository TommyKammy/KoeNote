using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Asr;

public sealed record AsrSettings(string ContextText, string HotwordsText)
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
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT context_text, hotwords_text
            FROM asr_settings
            WHERE settings_id = 1;
            """;

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return new AsrSettings(string.Empty, string.Empty);
        }

        return new AsrSettings(reader.GetString(0), reader.GetString(1));
    }

    public void Save(AsrSettings settings)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO asr_settings (settings_id, context_text, hotwords_text, updated_at)
            VALUES (1, $context_text, $hotwords_text, $updated_at)
            ON CONFLICT(settings_id) DO UPDATE SET
                context_text = excluded.context_text,
                hotwords_text = excluded.hotwords_text,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$context_text", settings.ContextText);
        command.Parameters.AddWithValue("$hotwords_text", settings.HotwordsText);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
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
