using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Transcript;

public sealed class ReadablePolishingPromptSettingsRepository(AppPaths paths)
{
    public PersistedReadablePolishingPromptSettings Load(string modelFamily)
    {
        var normalizedFamily = ReadablePolishingPromptModelFamilies.Normalize(modelFamily);
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT model_family, preset_id, additional_instruction, use_custom_prompt, custom_prompt,
                   prompt_template_id, prompt_version, created_at, updated_at
            FROM readable_polishing_prompt_settings
            WHERE model_family = $model_family;
            """;
        command.Parameters.AddWithValue("$model_family", normalizedFamily);

        using var reader = command.ExecuteReader();
        return reader.Read()
            ? ReadSettings(reader)
            : CreateDefaultPersisted(normalizedFamily);
    }

    public IReadOnlyList<PersistedReadablePolishingPromptSettings> LoadAll()
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT model_family, preset_id, additional_instruction, use_custom_prompt, custom_prompt,
                   prompt_template_id, prompt_version, created_at, updated_at
            FROM readable_polishing_prompt_settings
            ORDER BY model_family;
            """;

        using var reader = command.ExecuteReader();
        var persisted = new List<PersistedReadablePolishingPromptSettings>();
        while (reader.Read())
        {
            persisted.Add(ReadSettings(reader));
        }

        foreach (var family in ReadablePolishingPromptModelFamilies.Supported)
        {
            if (persisted.All(settings => !string.Equals(settings.Settings.ModelFamily, family, StringComparison.Ordinal)))
            {
                persisted.Add(CreateDefaultPersisted(family));
            }
        }

        return persisted
            .OrderBy(static settings => settings.Settings.ModelFamily, StringComparer.Ordinal)
            .ToArray();
    }

    public void Save(ReadablePolishingPromptSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = settings.Normalize();
        var now = DateTimeOffset.Now;

        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO readable_polishing_prompt_settings (
                model_family, preset_id, additional_instruction, use_custom_prompt, custom_prompt,
                prompt_template_id, prompt_version, created_at, updated_at
            )
            VALUES (
                $model_family, $preset_id, $additional_instruction, $use_custom_prompt, $custom_prompt,
                $prompt_template_id, $prompt_version, $created_at, $updated_at
            )
            ON CONFLICT(model_family) DO UPDATE SET
                preset_id = excluded.preset_id,
                additional_instruction = excluded.additional_instruction,
                use_custom_prompt = excluded.use_custom_prompt,
                custom_prompt = excluded.custom_prompt,
                prompt_template_id = excluded.prompt_template_id,
                prompt_version = excluded.prompt_version,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$model_family", normalized.ModelFamily);
        command.Parameters.AddWithValue("$preset_id", normalized.PresetId);
        command.Parameters.AddWithValue("$additional_instruction", normalized.AdditionalInstruction);
        command.Parameters.AddWithValue("$use_custom_prompt", normalized.UseCustomPrompt ? 1 : 0);
        command.Parameters.AddWithValue("$custom_prompt", normalized.CustomPrompt);
        command.Parameters.AddWithValue("$prompt_template_id", normalized.PromptTemplateId);
        command.Parameters.AddWithValue("$prompt_version", normalized.PromptVersion);
        command.Parameters.AddWithValue("$created_at", now.ToString("o"));
        command.Parameters.AddWithValue("$updated_at", now.ToString("o"));
        command.ExecuteNonQuery();
    }

    public void Reset(string modelFamily)
    {
        var normalizedFamily = ReadablePolishingPromptModelFamilies.Normalize(modelFamily);
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM readable_polishing_prompt_settings WHERE model_family = $model_family;";
        command.Parameters.AddWithValue("$model_family", normalizedFamily);
        command.ExecuteNonQuery();
    }

    private static PersistedReadablePolishingPromptSettings ReadSettings(SqliteDataReader reader)
    {
        var settings = new ReadablePolishingPromptSettings(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3) != 0,
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6)).Normalize();
        return new PersistedReadablePolishingPromptSettings(
            settings,
            DateTimeOffset.Parse(reader.GetString(7)),
            DateTimeOffset.Parse(reader.GetString(8)));
    }

    private static PersistedReadablePolishingPromptSettings CreateDefaultPersisted(string modelFamily)
    {
        var now = DateTimeOffset.Now;
        return new PersistedReadablePolishingPromptSettings(
            ReadablePolishingPromptSettings.CreateDefault(modelFamily),
            now,
            now);
    }
}
