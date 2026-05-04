using KoeNote.App.Models;
using KoeNote.App.Services.Asr;

namespace KoeNote.App.Tests;

public sealed class TranscriptSegmentRepositoryTests
{
    [Fact]
    public void SaveSegments_UpsertsTranscriptSegments()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        var paths = fixture.Paths;

        var repository = new TranscriptSegmentRepository(paths);
        repository.SaveSegments([
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "first", "first", Source: "asr", AsrRunId: "run-001")
        ]);
        repository.SaveSegments([
            new TranscriptSegment("000001", "job-001", 0, 1.5, "Speaker_1", "updated", "updated", Source: "asr", AsrRunId: "run-002")
        ]);

        using var connection = fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT speaker_id, raw_text, end_seconds, asr_run_id
            FROM transcript_segments
            WHERE job_id = 'job-001' AND segment_id = '000001';
            """;

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("Speaker_1", reader.GetString(0));
        Assert.Equal("updated", reader.GetString(1));
        Assert.Equal(1.5, reader.GetDouble(2));
        Assert.Equal("run-002", reader.GetString(3));
        Assert.False(reader.Read());
    }

}
