using System.Windows;
using KoeNote.App.Services;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private GridLength _jobListColumnWidth = GetDefaultJobListColumnWidth(MainLayoutMode.Standard);
    private GridLength _transcriptColumnWidth = GetDefaultTranscriptColumnWidth(MainLayoutMode.Standard);
    private GridLength _reviewColumnWidth = GetDefaultReviewColumnWidth(MainLayoutMode.Standard);

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
            ResetMainLayoutColumns(_mainLayoutMode);
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

    public GridLength JobListColumnWidth
    {
        get => _jobListColumnWidth;
        set => SetField(ref _jobListColumnWidth, value);
    }

    public GridLength TranscriptColumnWidth
    {
        get => _transcriptColumnWidth;
        set => SetField(ref _transcriptColumnWidth, value);
    }

    public GridLength ReviewColumnWidth
    {
        get => _reviewColumnWidth;
        set => SetField(ref _reviewColumnWidth, value);
    }

    public double JobListColumnMinWidth => IsStandardLayout ? 156 : 176;

    public double ReviewColumnMinWidth => IsStandardLayout ? 280 : 300;

    private void ResetMainLayoutColumns(MainLayoutMode mode)
    {
        JobListColumnWidth = GetDefaultJobListColumnWidth(mode);
        TranscriptColumnWidth = GetDefaultTranscriptColumnWidth(mode);
        ReviewColumnWidth = GetDefaultReviewColumnWidth(mode);
    }

    private static GridLength GetDefaultJobListColumnWidth(MainLayoutMode mode)
    {
        return mode == MainLayoutMode.Detail
            ? new GridLength(4, GridUnitType.Star)
            : new GridLength(3, GridUnitType.Star);
    }

    private static GridLength GetDefaultTranscriptColumnWidth(MainLayoutMode mode)
    {
        return mode == MainLayoutMode.Detail
            ? new GridLength(15, GridUnitType.Star)
            : new GridLength(17, GridUnitType.Star);
    }

    private static GridLength GetDefaultReviewColumnWidth(MainLayoutMode mode)
    {
        return mode == MainLayoutMode.Detail
            ? new GridLength(6, GridUnitType.Star)
            : new GridLength(5, GridUnitType.Star);
    }
}
