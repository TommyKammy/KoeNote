using KoeNote.App.Models;
using KoeNote.App.Services.Diarization;

namespace KoeNote.App.Tests;

public sealed class DiarizationSegmentAssignerTests
{
    [Fact]
    public void Assign_UsesLargestOverlapForEachTranscriptSegment()
    {
        var segments = new[]
        {
            new TranscriptSegment("000001", "job-001", 0, 5, null, "first"),
            new TranscriptSegment("000002", "job-001", 5, 10, null, "second")
        };
        var turns = new[]
        {
            new DiarizationTurn(0, 6, "Speaker_0"),
            new DiarizationTurn(6, 10, "Speaker_1")
        };

        var assigned = new DiarizationSegmentAssigner().Assign(segments, turns);

        Assert.Equal("Speaker_0", assigned[0].SpeakerId);
        Assert.Equal("Speaker_1", assigned[1].SpeakerId);
    }

    [Fact]
    public void Assign_KeepsExistingSpeakerWhenNoTurnMatches()
    {
        var segments = new[]
        {
            new TranscriptSegment("000001", "job-001", 20, 25, "Speaker_old", "text")
        };
        var turns = new[]
        {
            new DiarizationTurn(0, 5, "Speaker_0")
        };

        var assigned = new DiarizationSegmentAssigner().Assign(segments, turns);

        Assert.Equal("Speaker_old", assigned[0].SpeakerId);
    }
}
