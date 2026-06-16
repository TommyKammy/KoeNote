using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

public sealed class ReadableDocumentBlockBuilderTests
{
    [Fact]
    public void Build_SeparatesTimestampSpeakerAndBody()
    {
        var blocks = ReadableDocumentBlockBuilder.Build(
            "[00:00 - 00:05] Speaker_0: First paragraph.\n\n[00:05 - 00:09] Speaker_1: Second paragraph.");

        Assert.Collection(
            blocks,
            first =>
            {
                Assert.Equal("Speaker_0", first.Speaker);
                Assert.Equal("00:00 - 00:05", first.TimeRange);
                Assert.Equal("First paragraph.", first.Text);
                Assert.Equal(0, first.StartSeconds);
                Assert.Equal(5, first.EndSeconds);
                Assert.True(first.HasMeta);
            },
            second =>
            {
                Assert.Equal("Speaker_1", second.Speaker);
                Assert.Equal("00:05 - 00:09", second.TimeRange);
                Assert.Equal("Second paragraph.", second.Text);
                Assert.Equal(5, second.StartSeconds);
                Assert.Equal(9, second.EndSeconds);
                Assert.True(second.HasMeta);
            });
    }

    [Fact]
    public void Build_KeepsUntimestampedTextAsDocumentParagraph()
    {
        var blocks = ReadableDocumentBlockBuilder.Build("Plain paragraph.\ncontinued line.");

        var block = Assert.Single(blocks);
        Assert.Equal(string.Empty, block.Speaker);
        Assert.Equal(string.Empty, block.TimeRange);
        Assert.Equal("Plain paragraph.\ncontinued line.", block.Text.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.False(block.HasMeta);
    }

    [Fact]
    public void Build_DoesNotStripOrdinaryTimeExpression()
    {
        var blocks = ReadableDocumentBlockBuilder.Build("案内事項です。\n\n10:00に受付を開始します。");

        Assert.Collection(
            blocks,
            first =>
            {
                Assert.Equal("案内事項です。", first.Text);
                Assert.False(first.HasMeta);
            },
            second =>
            {
                Assert.Equal("10:00に受付を開始します。", second.Text);
                Assert.Equal(string.Empty, second.TimeRange);
                Assert.Null(second.StartSeconds);
                Assert.False(second.HasMeta);
            });
    }

    [Theory]
    [InlineData("－")]
    [InlineData("ー")]
    public void Build_AcceptsFullWidthRangeSeparators(string separator)
    {
        var blocks = ReadableDocumentBlockBuilder.Build($"[00:00 {separator} 00:05] Speaker_0: 本文です。");

        var block = Assert.Single(blocks);
        Assert.Equal("Speaker_0", block.Speaker);
        Assert.Equal("00:00 - 00:05", block.TimeRange);
        Assert.Equal("本文です。", block.Text);
        Assert.Equal(0, block.StartSeconds);
        Assert.Equal(5, block.EndSeconds);
    }
}
