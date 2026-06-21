using KoeNote.App.Services;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Tests;

public sealed class SqliteExtensionsTests
{
    [Fact]
    public void CreateCommand_SetsCommandText()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var command = connection.CreateCommand("SELECT 1;");

        Assert.Equal("SELECT 1;", command.CommandText);
    }

    [Fact]
    public void AddValue_BindsNullAsDbNullAndSupportsChaining()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand("SELECT $value;");

        var returnedCommand = command.AddValue("$value", null);

        Assert.Same(command, returnedCommand);
        Assert.Equal(DBNull.Value, command.Parameters["$value"].Value);
    }

    [Fact]
    public void AddIsoDateTimeOffset_FormatsValueUsingRoundTripFormat()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand("SELECT $created_at;");
        var timestamp = new DateTimeOffset(2026, 6, 21, 10, 30, 45, TimeSpan.FromHours(9));

        command.AddIsoDateTimeOffset("$created_at", timestamp);

        Assert.Equal("2026-06-21T10:30:45.0000000+09:00", command.Parameters["$created_at"].Value);
    }
}
