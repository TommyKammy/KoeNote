using System.Windows;
using KoeNote.App.Services;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private Task SelectMainLayoutModeAsync(MainLayoutMode mode)
    {
        MainLayoutMode = mode;
        return Task.CompletedTask;
    }

    private void NotifyMainLayoutModeChanged()
    {
        OnPropertyChanged(nameof(MainLayoutMode));
        OnPropertyChanged(nameof(IsStandardLayout));
        OnPropertyChanged(nameof(IsDetailLayout));
        OnPropertyChanged(nameof(IsStandardLayoutSelected));
        OnPropertyChanged(nameof(IsDetailLayoutSelected));
        OnPropertyChanged(nameof(MainLayoutModeDisplayText));
        OnPropertyChanged(nameof(MainLayoutModeToolTip));
        OnPropertyChanged(nameof(JobListColumnWidth));
        OnPropertyChanged(nameof(TranscriptColumnWidth));
        OnPropertyChanged(nameof(ReviewColumnWidth));
        OnPropertyChanged(nameof(JobListColumnMinWidth));
        OnPropertyChanged(nameof(ReviewColumnMinWidth));
    }

    private static string GetMainLayoutModeDisplayName(MainLayoutMode mode)
    {
        return mode == MainLayoutMode.Detail ? "詳細" : "標準";
    }

    public MainLayoutMode MainLayoutMode
    {
        get => _mainLayoutMode;
        private set
        {
            var normalized = UiPreferencesService.NormalizeMainLayoutMode(value);
            if (_mainLayoutMode == normalized)
            {
                return;
            }

            _mainLayoutMode = normalized;
            _uiPreferencesService.SaveMainLayoutMode(_mainLayoutMode);
            NotifyMainLayoutModeChanged();
            LatestLog = $"レイアウトを{GetMainLayoutModeDisplayName(_mainLayoutMode)}に切り替えました。";
        }
    }

    public bool IsStandardLayout => MainLayoutMode == MainLayoutMode.Standard;

    public bool IsDetailLayout => MainLayoutMode == MainLayoutMode.Detail;

    public bool IsStandardLayoutSelected
    {
        get => IsStandardLayout;
        set
        {
            if (value)
            {
                MainLayoutMode = MainLayoutMode.Standard;
            }
        }
    }

    public bool IsDetailLayoutSelected
    {
        get => IsDetailLayout;
        set
        {
            if (value)
            {
                MainLayoutMode = MainLayoutMode.Detail;
            }
        }
    }

    public string MainLayoutModeDisplayText => GetMainLayoutModeDisplayName(MainLayoutMode);

    public string MainLayoutModeToolTip => IsStandardLayout
        ? "標準レイアウト: 校正に集中する既定の画面"
        : "詳細レイアウト: ジョブ、文字起こし、整文候補、要約を広く確認する画面";

    public GridLength JobListColumnWidth => IsStandardLayout
        ? new GridLength(3, GridUnitType.Star)
        : new GridLength(4, GridUnitType.Star);

    public GridLength TranscriptColumnWidth => IsStandardLayout
        ? new GridLength(17, GridUnitType.Star)
        : new GridLength(15, GridUnitType.Star);

    public GridLength ReviewColumnWidth => IsStandardLayout
        ? new GridLength(5, GridUnitType.Star)
        : new GridLength(6, GridUnitType.Star);

    public double JobListColumnMinWidth => IsStandardLayout ? 156 : 176;

    public double ReviewColumnMinWidth => IsStandardLayout ? 280 : 300;
}
