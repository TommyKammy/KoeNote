namespace KoeNote.App.ViewModels;

internal sealed class DetailPanelState
{
    private const int MinTabIndex = 0;
    private const int MaxTabIndex = 4;

    public int SelectedTabIndex { get; private set; }

    public bool IsOpen { get; private set; }

    public string Title => SelectedTabIndex switch
    {
        0 => "設定",
        1 => "辞書プリセット",
        2 => "モデル",
        3 => "セットアップ / モデル導入",
        4 => "ログ",
        _ => "詳細"
    };

    public bool SetSelectedTabIndex(int value)
    {
        var clamped = Math.Clamp(value, MinTabIndex, MaxTabIndex);
        if (SelectedTabIndex == clamped)
        {
            return false;
        }

        SelectedTabIndex = clamped;
        return true;
    }

    public bool SetOpen(bool value)
    {
        if (IsOpen == value)
        {
            return false;
        }

        IsOpen = value;
        return true;
    }
}
