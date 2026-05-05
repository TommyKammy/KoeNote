using KoeNote.App.Services.Diarization;

namespace KoeNote.App.Tests;

public sealed class DiarizationJsonNormalizerTests
{
    [Fact]
    public void Normalize_ReadsDiarizeWorkerTurns()
    {
        const string rawJson = """
            {
              "engine": "diarize",
              "status": "succeeded",
              "turns": [
                { "start": 0.0, "end": 2.5, "speaker": "SPEAKER_00" },
                { "start_seconds": "2.5", "end_seconds": "5.0", "speaker_id": "Speaker_1" }
              ]
            }
            """;

        var output = new DiarizationJsonNormalizer().Normalize(rawJson);

        Assert.Equal("succeeded", output.Status);
        Assert.Equal(2, output.Turns.Count);
        Assert.Equal("Speaker_00", output.Turns[0].SpeakerId);
        Assert.Equal("Speaker_1", output.Turns[1].SpeakerId);
        Assert.Equal(2.5, output.Turns[1].StartSeconds);
    }

    [Fact]
    public void Normalize_IgnoresInvalidTurns()
    {
        const string rawJson = """
            {
              "status": "succeeded",
              "turns": [
                { "start": 5.0, "end": 2.5, "speaker": "Speaker_0" },
                { "start": 2.5, "end": 5.0 }
              ]
            }
            """;

        var output = new DiarizationJsonNormalizer().Normalize(rawJson);

        Assert.Empty(output.Turns);
    }
}
