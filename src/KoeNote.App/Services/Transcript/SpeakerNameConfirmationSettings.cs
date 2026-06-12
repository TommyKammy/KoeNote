namespace KoeNote.App.Services.Transcript;

public static class SpeakerNameConfirmationModes
{
    public const string Always = "always";
    public const string UnassignedOnly = "unassigned_only";

    public static string Normalize(string? mode)
    {
        return string.Equals(mode, UnassignedOnly, StringComparison.OrdinalIgnoreCase)
            ? UnassignedOnly
            : Always;
    }
}

public sealed record SpeakerNameConfirmationSettings(string Mode)
{
    public static SpeakerNameConfirmationSettings Default { get; } =
        new(SpeakerNameConfirmationModes.Always);

    public SpeakerNameConfirmationSettings Normalize()
    {
        return this with { Mode = SpeakerNameConfirmationModes.Normalize(Mode) };
    }
}

public sealed class SpeakerNameConfirmationSettingsRepository(AppPaths paths)
{
    public SpeakerNameConfirmationSettings Load()
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT mode
            FROM speaker_name_confirmation_settings
            WHERE settings_id = 1;
            """;

        var value = command.ExecuteScalar() as string;
        return string.IsNullOrWhiteSpace(value)
            ? SpeakerNameConfirmationSettings.Default
            : new SpeakerNameConfirmationSettings(value).Normalize();
    }

    public void Save(SpeakerNameConfirmationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = settings.Normalize();
        var now = DateTimeOffset.Now.ToString("o");

        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO speaker_name_confirmation_settings (settings_id, mode, created_at, updated_at)
            VALUES (1, $mode, $created_at, $updated_at)
            ON CONFLICT(settings_id) DO UPDATE SET
                mode = excluded.mode,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$mode", normalized.Mode);
        command.Parameters.AddWithValue("$created_at", now);
        command.Parameters.AddWithValue("$updated_at", now);
        command.ExecuteNonQuery();
    }
}
