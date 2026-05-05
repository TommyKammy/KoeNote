using KoeNote.App.Models;

namespace KoeNote.App.Services.Diarization;

public sealed class DiarizationSegmentAssigner
{
    public IReadOnlyList<TranscriptSegment> Assign(
        IReadOnlyList<TranscriptSegment> segments,
        IReadOnlyList<DiarizationTurn> turns)
    {
        if (segments.Count == 0 || turns.Count == 0)
        {
            return segments;
        }

        return segments
            .Select(segment =>
            {
                var speaker = FindBestSpeaker(segment, turns);
                return speaker is null ? segment : segment with { SpeakerId = speaker };
            })
            .ToArray();
    }

    private static string? FindBestSpeaker(TranscriptSegment segment, IReadOnlyList<DiarizationTurn> turns)
    {
        var bestOverlap = 0.0;
        string? bestSpeaker = null;

        foreach (var turn in turns)
        {
            var overlap = Math.Min(segment.EndSeconds, turn.EndSeconds) - Math.Max(segment.StartSeconds, turn.StartSeconds);
            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                bestSpeaker = turn.SpeakerId;
            }
        }

        if (bestSpeaker is not null)
        {
            return bestSpeaker;
        }

        var midpoint = segment.StartSeconds + ((segment.EndSeconds - segment.StartSeconds) / 2);
        return turns.FirstOrDefault(turn => midpoint >= turn.StartSeconds && midpoint <= turn.EndSeconds)?.SpeakerId;
    }
}
