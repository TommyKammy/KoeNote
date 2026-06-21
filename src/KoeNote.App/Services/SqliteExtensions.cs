using System.Globalization;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services;

internal static class SqliteExtensions
{
    public static SqliteCommand CreateCommand(this SqliteConnection connection, string commandText)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        return command;
    }

    public static SqliteCommand AddValue(this SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddValue(name, value);
        return command;
    }

    public static SqliteCommand AddIsoDateTimeOffset(this SqliteCommand command, string name, DateTimeOffset? value)
    {
        return command.AddValue(name, value?.ToString("o"));
    }

    public static void AddValue(this SqliteParameterCollection parameters, string name, object? value)
    {
        parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    public static string? GetNullableString(this SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static double? GetNullableDouble(this SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
    }

    public static DateTimeOffset? GetNullableDateTimeOffset(this SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);
    }

    public static DateTimeOffset GetDateTimeOffset(this SqliteDataReader reader, int ordinal)
    {
        return DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);
    }
}
