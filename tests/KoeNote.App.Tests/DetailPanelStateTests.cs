using KoeNote.App.ViewModels;

namespace KoeNote.App.Tests;

public sealed class DetailPanelStateTests
{
    [Theory]
    [InlineData(-1, 0, "設定")]
    [InlineData(0, 0, "設定")]
    [InlineData(1, 1, "辞書プリセット")]
    [InlineData(2, 2, "モデル")]
    [InlineData(3, 3, "セットアップ / モデル導入")]
    [InlineData(4, 4, "ログ")]
    [InlineData(5, 4, "ログ")]
    public void SetSelectedTabIndex_ClampsIndexAndUpdatesTitle(int value, int expectedIndex, string expectedTitle)
    {
        var state = new DetailPanelState();

        state.SetSelectedTabIndex(value);

        Assert.Equal(expectedIndex, state.SelectedTabIndex);
        Assert.Equal(expectedTitle, state.Title);
    }

    [Fact]
    public void SetOpen_ReturnsWhetherOpenStateChanged()
    {
        var state = new DetailPanelState();

        Assert.True(state.SetOpen(true));
        Assert.True(state.IsOpen);
        Assert.False(state.SetOpen(true));
        Assert.True(state.SetOpen(false));
        Assert.False(state.IsOpen);
    }
}
