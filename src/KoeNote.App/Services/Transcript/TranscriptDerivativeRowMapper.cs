using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Transcript;

public static class TranscriptDerivativeRowMapper
{
    public static TranscriptDerivative ReadDerivative(SqliteDataReader reader)
    {
        return new TranscriptDerivative(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetNullableString(7),
            reader.GetNullableString(8),
            reader.GetNullableString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetNullableString(13),
            reader.GetDateTimeOffset(14),
            reader.GetDateTimeOffset(15));
    }

    public static TranscriptDerivativeChunk ReadChunk(SqliteDataReader reader)
    {
        return new TranscriptDerivativeChunk(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetNullableDouble(6),
            reader.GetNullableDouble(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetNullableString(11),
            reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14),
            reader.GetNullableString(15),
            reader.GetDateTimeOffset(16),
            reader.GetDateTimeOffset(17));
    }
}
