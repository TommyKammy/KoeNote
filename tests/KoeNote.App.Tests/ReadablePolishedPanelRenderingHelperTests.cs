using System.Windows.Media;
using KoeNote.App.Controls;
using KoeNote.App.Models;

namespace KoeNote.App.Tests;

public sealed class ReadablePolishedPanelRenderingHelperTests
{
    [Fact]
    public void BuildHighlightedSegments_SplitsCaseInsensitiveMatches()
    {
        var segments = ReadablePolishedPanelRenderingHelper.BuildHighlightedSegments(
            "Alpha beta ALPHA",
            "alpha");

        Assert.Equal(3, segments.Count);
        Assert.Equal(new ReadableHighlightedTextSegment("Alpha", true), segments[0]);
        Assert.Equal(new ReadableHighlightedTextSegment(" beta ", false), segments[1]);
        Assert.Equal(new ReadableHighlightedTextSegment("ALPHA", true), segments[2]);
    }

    [Fact]
    public void BuildHighlightedSegments_ReturnsWholeTextWhenSearchIsBlank()
    {
        var segment = Assert.Single(ReadablePolishedPanelRenderingHelper.BuildHighlightedSegments("Alpha", " "));

        Assert.Equal("Alpha", segment.Text);
        Assert.False(segment.IsHighlighted);
    }

    [Fact]
    public void ContainsSearchText_IgnoresCaseAndBlankSearch()
    {
        Assert.True(ReadablePolishedPanelRenderingHelper.ContainsSearchText("Alpha beta", "BETA"));
        Assert.False(ReadablePolishedPanelRenderingHelper.ContainsSearchText("Alpha beta", " "));
    }

    [Fact]
    public void FindReadableBlockIndex_UsesRangeThenLastStartedFallback()
    {
        var blocks = new[]
        {
            Block(startSeconds: null, endSeconds: null),
            Block(startSeconds: 10, endSeconds: 20),
            Block(startSeconds: 25, endSeconds: 30),
            Block(startSeconds: 35, endSeconds: null)
        };

        Assert.Equal(-1, ReadablePolishedPanelRenderingHelper.FindReadableBlockIndex(blocks, 5));
        Assert.Equal(1, ReadablePolishedPanelRenderingHelper.FindReadableBlockIndex(blocks, 15));
        Assert.Equal(1, ReadablePolishedPanelRenderingHelper.FindReadableBlockIndex(blocks, 22));
        Assert.Equal(3, ReadablePolishedPanelRenderingHelper.FindReadableBlockIndex(blocks, 40));
    }

    [Fact]
    public void GetSpeakerPalette_UsesFallbackForBlankSpeaker()
    {
        var palette = ReadablePolishedPanelRenderingHelper.GetSpeakerPalette(" ");

        Assert.Equal(Color.FromRgb(0xED, 0xF0, 0xF3), palette.BackgroundColor);
        Assert.Equal(Color.FromRgb(0x5B, 0x64, 0x72), palette.ForegroundColor);
    }

    [Fact]
    public void LayoutCalculator_KeepsDocumentWidthInsideViewportWithRightGutter()
    {
        var contentWidth = ReadableDocumentLayoutCalculator.CalculateContentWidth(
            viewportWidth: 1000,
            metaColumnWidth: 122,
            rightGutter: 48);
        var bodyWidth = ReadableDocumentLayoutCalculator.CalculateBodyColumnWidth(
            contentWidth,
            metaColumnWidth: 122);

        Assert.Equal(952, contentWidth);
        Assert.Equal(830, bodyWidth);
        Assert.True(contentWidth < 1000);
        Assert.Equal(contentWidth, 122 + bodyWidth);
    }

    [Fact]
    public void LayoutCalculator_DoesNotForceMinimumBodyWidthPastNarrowViewport()
    {
        var contentWidth = ReadableDocumentLayoutCalculator.CalculateContentWidth(
            viewportWidth: 180,
            metaColumnWidth: 122,
            rightGutter: 48);
        var bodyWidth = ReadableDocumentLayoutCalculator.CalculateBodyColumnWidth(
            contentWidth,
            metaColumnWidth: 122);

        Assert.Equal(132, contentWidth);
        Assert.Equal(10, bodyWidth);
        Assert.Equal(contentWidth, 122 + bodyWidth);
    }

    private static ReadableDocumentBlock Block(double? startSeconds, double? endSeconds)
    {
        return new ReadableDocumentBlock(
            Speaker: string.Empty,
            TimeRange: string.Empty,
            Text: "text",
            SourceLineIndex: 0,
            StartSeconds: startSeconds,
            EndSeconds: endSeconds);
    }
}
