using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Tests;

public sealed class TranscriptSegmentRepositoryTests
{
    [Fact]
    public void SaveSegments_UpsertsTranscriptSegments()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();

        var repository = new TranscriptSegmentRepository(paths);
        repository.SaveSegments([
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "初回", "初回")
        ]);
        repository.SaveSegments([
            new TranscriptSegment("000001", "job-001", 0, 1.5, "Speaker_1", "更新", "更新")
        ]);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT speaker_id, raw_text, end_seconds
            FROM transcript_segments
            WHERE job_id = 'job-001' AND segment_id = '000001';
            """;

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("Speaker_1", reader.GetString(0));
        Assert.Equal("更新", reader.GetString(1));
        Assert.Equal(1.5, reader.GetDouble(2));
        Assert.False(reader.Read());
    }

    private static AppPaths CreatePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var local = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        return new AppPaths(root, local);
    }
}
