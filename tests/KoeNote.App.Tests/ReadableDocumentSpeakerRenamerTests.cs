using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

public sealed class ReadableDocumentSpeakerRenamerTests
{
    [Fact]
    public void Rename_UpdatesOnlyMatchingSpeakerLines()
    {
        const string content = """
            [00:00 - 00:10] 川田: ここは山梨です。
            川田という名前は本文にも残します。

            [00:10 - 00:20] 佐藤: 別の話者です。

            [00:20 - 00:30] 川田: もう一度話します。
            """;

        var result = ReadableDocumentSpeakerRenamer.Rename(content, "川田", "山田");

        Assert.True(result.Changed);
        Assert.Equal(2, result.UpdatedBlockCount);
        Assert.Contains("[00:00 - 00:10] 山田: ここは山梨です。", result.Content, StringComparison.Ordinal);
        Assert.Contains("川田という名前は本文にも残します。", result.Content, StringComparison.Ordinal);
        Assert.Contains("[00:10 - 00:20] 佐藤: 別の話者です。", result.Content, StringComparison.Ordinal);
        Assert.Contains("[00:20 - 00:30] 山田: もう一度話します。", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Rename_PreservesCrLfLineEndings()
    {
        const string content = "[00:00 - 00:10] Speaker_0: Hello\r\n\r\n[00:10 - 00:20] Speaker_1: Hi";

        var result = ReadableDocumentSpeakerRenamer.Rename(content, "Speaker_0", "Alice");

        Assert.Equal("[00:00 - 00:10] Alice: Hello\r\n\r\n[00:10 - 00:20] Speaker_1: Hi", result.Content);
    }

    [Fact]
    public void Rename_ReturnsUnchangedWhenSpeakerDoesNotMatch()
    {
        const string content = "[00:00 - 00:10] Speaker_0: Hello";

        var result = ReadableDocumentSpeakerRenamer.Rename(content, "Speaker_2", "Alice");

        Assert.False(result.Changed);
        Assert.Equal(0, result.UpdatedBlockCount);
        Assert.Equal(content, result.Content);
    }
}
