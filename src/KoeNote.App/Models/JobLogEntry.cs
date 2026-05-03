namespace KoeNote.App.Models;

public sealed record JobLogEntry(
    DateTimeOffset CreatedAt,
    string Level,
    string Stage,
    string Message)
{
    public string CreatedAtDisplay => CreatedAt.ToString("HH:mm:ss");
}
