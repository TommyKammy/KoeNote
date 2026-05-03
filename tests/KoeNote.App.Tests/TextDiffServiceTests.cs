using KoeNote.App.Models;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Tests;

public sealed class TextDiffServiceTests
{
    [Fact]
    public void BuildInlineDiff_MarksJapaneseReplacement()
    {
        var tokens = new TextDiffService().BuildInlineDiff(
            "この仕様はサーバーのミギワで処理します。",
            "この仕様はサーバーの右側で処理します。");

        Assert.Contains(tokens, token => token.Kind == DiffKind.Replaced && token.Text.Contains("ミギワ", StringComparison.Ordinal));
        Assert.Contains(tokens, token => token.Kind == DiffKind.Replaced && token.Text.Contains("右側", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildInlineDiff_ReturnsEqualTokenWhenTextIsSame()
    {
        var tokens = new TextDiffService().BuildInlineDiff("候補なし", "候補なし");

        var token = Assert.Single(tokens);
        Assert.Equal(DiffKind.Equal, token.Kind);
        Assert.Equal("候補なし", token.Text);
    }
}
