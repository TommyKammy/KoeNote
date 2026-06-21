using System.Windows.Media;
using KoeNote.App.Models;

namespace KoeNote.App.Controls;

internal static class ReadablePolishedPanelRenderingHelper
{
    public static IReadOnlyList<ReadableHighlightedTextSegment> BuildHighlightedSegments(string text, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return [new ReadableHighlightedTextSegment(text, IsHighlighted: false)];
        }

        var segments = new List<ReadableHighlightedTextSegment>();
        var startIndex = 0;
        while (startIndex < text.Length)
        {
            var matchIndex = text.IndexOf(searchText, startIndex, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
            {
                segments.Add(new ReadableHighlightedTextSegment(text[startIndex..], IsHighlighted: false));
                return segments;
            }

            if (matchIndex > startIndex)
            {
                segments.Add(new ReadableHighlightedTextSegment(text[startIndex..matchIndex], IsHighlighted: false));
            }

            segments.Add(new ReadableHighlightedTextSegment(
                text.Substring(matchIndex, searchText.Length),
                IsHighlighted: true));
            startIndex = matchIndex + searchText.Length;
        }

        return segments;
    }

    public static bool ContainsSearchText(string text, string searchText)
    {
        return !string.IsNullOrWhiteSpace(searchText) &&
            text.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    public static int FindReadableBlockIndex(IReadOnlyList<ReadableDocumentBlock> blocks, double positionSeconds)
    {
        var fallbackIndex = -1;
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            if (block.StartSeconds is not { } startSeconds)
            {
                continue;
            }

            var endSeconds = block.EndSeconds.GetValueOrDefault(double.MaxValue);
            if (positionSeconds >= startSeconds && positionSeconds < endSeconds)
            {
                return index;
            }

            if (positionSeconds >= startSeconds)
            {
                fallbackIndex = index;
            }
        }

        return fallbackIndex;
    }

    public static ReadableSpeakerPalette GetSpeakerPalette(string speaker)
    {
        var palettes = new[]
        {
            new ReadableSpeakerPalette(Color.FromRgb(0xEA, 0xF1, 0xFA), Color.FromRgb(0x2F, 0x58, 0x96)),
            new ReadableSpeakerPalette(Color.FromRgb(0xF2, 0xEC, 0xFB), Color.FromRgb(0x5F, 0x44, 0xA0)),
            new ReadableSpeakerPalette(Color.FromRgb(0xFB, 0xF1, 0xE7), Color.FromRgb(0x95, 0x57, 0x1F)),
            new ReadableSpeakerPalette(Color.FromRgb(0xED, 0xF0, 0xF3), Color.FromRgb(0x5B, 0x64, 0x72))
        };

        if (string.IsNullOrWhiteSpace(speaker))
        {
            return palettes[^1];
        }

        var hash = 0;
        foreach (var character in speaker)
        {
            hash = unchecked((hash * 31) + character);
        }

        return palettes[(hash & int.MaxValue) % palettes.Length];
    }
}

internal readonly record struct ReadableHighlightedTextSegment(string Text, bool IsHighlighted);

internal sealed record ReadableSpeakerPalette(Color BackgroundColor, Color ForegroundColor)
{
    public Brush Background { get; } = new SolidColorBrush(BackgroundColor);

    public Brush Foreground { get; } = new SolidColorBrush(ForegroundColor);
}
