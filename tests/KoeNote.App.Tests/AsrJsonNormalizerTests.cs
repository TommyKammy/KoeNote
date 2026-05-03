using KoeNote.App.Services.Asr;

namespace KoeNote.App.Tests;

public sealed class AsrJsonNormalizerTests
{
    [Fact]
    public void Normalize_ReadsSegmentsObjectShape()
    {
        const string rawJson = """
            {
              "segments": [
                {
                  "start": 0.0,
                  "end": 5.81,
                  "speaker": "Speaker_0",
                  "text": "今日は会議の議事録を作成するために音声認識をテストしています。"
                }
              ]
            }
            """;

        var segments = new AsrJsonNormalizer().Normalize("job-001", rawJson);

        var segment = Assert.Single(segments);
        Assert.Equal("000001", segment.SegmentId);
        Assert.Equal("job-001", segment.JobId);
        Assert.Equal(0.0, segment.StartSeconds);
        Assert.Equal(5.81, segment.EndSeconds);
        Assert.Equal("Speaker_0", segment.SpeakerId);
        Assert.Equal("今日は会議の議事録を作成するために音声認識をテストしています。", segment.RawText);
    }

    [Fact]
    public void Normalize_ReadsArrayShapeAndExplicitIds()
    {
        const string rawJson = """
            [
              {
                "segment_id": "seg-a",
                "start_seconds": "1.5",
                "end_seconds": "2.5",
                "speaker_id": "Speaker_1",
                "raw_text": "テストです。",
                "confidence": 0.91
              }
            ]
            """;

        var segments = new AsrJsonNormalizer().Normalize("job-001", rawJson);

        var segment = Assert.Single(segments);
        Assert.Equal("seg-a", segment.SegmentId);
        Assert.Equal(1.5, segment.StartSeconds);
        Assert.Equal(2.5, segment.EndSeconds);
        Assert.Equal(0.91, segment.AsrConfidence);
    }

    [Fact]
    public void Normalize_ThrowsForInvalidJson()
    {
        var exception = Assert.Throws<AsrWorkerException>(() => new AsrJsonNormalizer().Normalize("job-001", "not-json"));

        Assert.Equal(AsrFailureCategory.JsonParseFailed, exception.Category);
    }
}
