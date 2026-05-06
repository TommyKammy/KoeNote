using System.IO;
using System.Text;
using System.Text.Json;

namespace KoeNote.App.Services.Updates;

public sealed record UpdateHistoryEntry(
    DateTimeOffset Timestamp,
    string EventName,
    string Message,
    string? Version = null,
    string? InstallerPath = null,
    string? Sha256 = null);

public interface IUpdateHistoryService
{
    void Record(UpdateHistoryEntry entry);

    IReadOnlyList<UpdateHistoryEntry> ReadRecent(int maxEntries = 100);
}

public sealed class UpdateHistoryService(AppPaths paths) : IUpdateHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    private readonly object _sync = new();

    public void Record(UpdateHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Directory.CreateDirectory(paths.UpdateDownloads);
        var json = JsonSerializer.Serialize(entry, JsonOptions);
        lock (_sync)
        {
            File.AppendAllText(paths.UpdateHistoryPath, json + Environment.NewLine, Encoding.UTF8);
        }
    }

    public IReadOnlyList<UpdateHistoryEntry> ReadRecent(int maxEntries = 100)
    {
        if (maxEntries <= 0 || !File.Exists(paths.UpdateHistoryPath))
        {
            return [];
        }

        var entries = new List<UpdateHistoryEntry>();
        foreach (var line in File.ReadLines(paths.UpdateHistoryPath, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                if (JsonSerializer.Deserialize<UpdateHistoryEntry>(line, JsonOptions) is { } entry)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
            }
        }

        return entries.Count <= maxEntries
            ? entries
            : entries.Skip(entries.Count - maxEntries).ToArray();
    }
}

public sealed class NullUpdateHistoryService : IUpdateHistoryService
{
    public static NullUpdateHistoryService Instance { get; } = new();

    private NullUpdateHistoryService()
    {
    }

    public void Record(UpdateHistoryEntry entry)
    {
    }

    public IReadOnlyList<UpdateHistoryEntry> ReadRecent(int maxEntries = 100) => [];
}
