namespace KoeNote.App.Services.Dialogs;

public sealed record SpeakerNameConfirmationRequest(
    string JobTitle,
    IReadOnlyList<SpeakerNameConfirmationItem> Speakers);

public sealed record SpeakerNameConfirmationItem(
    string SpeakerId,
    string DisplayName,
    int SegmentCount,
    IReadOnlyList<string> PreviewTexts)
{
    public string EffectiveDisplayName => string.IsNullOrWhiteSpace(DisplayName)
        ? SpeakerId
        : DisplayName.Trim();
}

public sealed record SpeakerNameConfirmationResult(IReadOnlyDictionary<string, string> DisplayNames)
{
    public static SpeakerNameConfirmationResult FromRequest(SpeakerNameConfirmationRequest request)
    {
        return new SpeakerNameConfirmationResult(
            request.Speakers.ToDictionary(
                static speaker => speaker.SpeakerId,
                static speaker => speaker.EffectiveDisplayName,
                StringComparer.OrdinalIgnoreCase));
    }
}
