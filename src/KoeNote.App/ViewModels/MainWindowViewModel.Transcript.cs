using KoeNote.App.Models;
using KoeNote.App.Services.Review;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static string FormatTimestamp(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
    }

    private void UpdateSegmentReviewStates(IReadOnlyList<CorrectionDraft> drafts)
    {
        var draftSegmentIds = drafts.Select(draft => draft.SegmentId).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < Segments.Count; i++)
        {
            var preview = Segments[i];
            if (draftSegmentIds.Contains(preview.SegmentId))
            {
                Segments[i] = preview with { ReviewState = "推敲候補あり" };
            }
        }
    }

    private void ClearReviewPreview()
    {
        ReviewIssueType = "候補なし";
        OriginalText = string.Empty;
        SuggestedText = string.Empty;
        ReviewReason = "推敲候補は生成されませんでした。";
        Confidence = 0;
    }

    private bool FilterSegment(object item)
    {
        if (item is not TranscriptSegmentPreview segment)
        {
            return false;
        }

        var speakerMatches = SelectedSpeakerFilter == "全話者"
            || string.Equals(segment.Speaker, SelectedSpeakerFilter, StringComparison.Ordinal);
        var textMatches = string.IsNullOrWhiteSpace(SegmentSearchText)
            || segment.Text.Contains(SegmentSearchText, StringComparison.OrdinalIgnoreCase)
            || segment.Speaker.Contains(SegmentSearchText, StringComparison.OrdinalIgnoreCase)
            || segment.ReviewState.Contains(SegmentSearchText, StringComparison.OrdinalIgnoreCase);

        return speakerMatches && textMatches;
    }

    private void RefreshSpeakerFilters()
    {
        var selected = SelectedSpeakerFilter;
        SpeakerFilters.Clear();
        SpeakerFilters.Add("全話者");
        foreach (var speaker in Segments.Select(segment => segment.Speaker).Where(static speaker => !string.IsNullOrWhiteSpace(speaker)).Distinct().Order())
        {
            SpeakerFilters.Add(speaker);
        }

        SelectedSpeakerFilter = SpeakerFilters.Contains(selected) ? selected : "全話者";
    }
}
