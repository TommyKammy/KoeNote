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
        OnPropertyChanged(nameof(StandardJobRailColumnWidth));
        OnPropertyChanged(nameof(StandardJobRailColumnMinWidth));
        OnPropertyChanged(nameof(StandardAiRailColumnWidth));
        OnPropertyChanged(nameof(StandardAiRailColumnMinWidth));
        OnPropertyChanged(nameof(JobListColumnMinWidth));
        OnPropertyChanged(nameof(ReviewColumnMinWidth));
        OnPropertyChanged(nameof(IsStandardReadableTranscriptVisible));
        OnPropertyChanged(nameof(IsStandardRawTranscriptVisible));
        OnPropertyChanged(nameof(DetailInspectorCurrentTabText));
        NotifyExportMenuTargetChanged();
        RefreshSelectedSegmentEditBuffer();
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

    public bool IsStandardJobRailExpanded
    {
        get => _isStandardJobRailExpanded;
        private set
        {
            if (SetField(ref _isStandardJobRailExpanded, value))
            {
                OnPropertyChanged(nameof(StandardJobRailColumnWidth));
                OnPropertyChanged(nameof(StandardJobRailColumnMinWidth));
                OnPropertyChanged(nameof(StandardJobRailToggleText));
                OnPropertyChanged(nameof(StandardJobRailToggleToolTip));
            }
        }
    }

    public GridLength StandardJobRailColumnWidth => IsStandardJobRailExpanded
        ? new GridLength(244)
        : new GridLength(76);

    public double StandardJobRailColumnMinWidth => IsStandardJobRailExpanded ? 220 : 72;

    public string StandardJobRailToggleText => IsStandardJobRailExpanded ? "‹" : "›";

    public string StandardJobRailToggleToolTip => IsStandardJobRailExpanded
        ? "ジョブレールを折り畳みます。"
        : "ジョブレールを展開します。";

    private Task ToggleStandardJobRailAsync()
    {
        IsStandardJobRailExpanded = !IsStandardJobRailExpanded;
        return Task.CompletedTask;
    }

    public bool IsStandardAiRailExpanded
    {
        get => _isStandardAiRailExpanded;
        private set
        {
            if (SetField(ref _isStandardAiRailExpanded, value))
            {
                OnPropertyChanged(nameof(StandardAiRailColumnWidth));
                OnPropertyChanged(nameof(StandardAiRailColumnMinWidth));
                OnPropertyChanged(nameof(StandardAiRailToggleText));
                OnPropertyChanged(nameof(StandardAiRailToggleToolTip));
                NotifyExportMenuTargetChanged();
            }
        }
    }

    public GridLength StandardAiRailColumnWidth => IsStandardAiRailExpanded
        ? new GridLength(388)
        : new GridLength(64);

    public double StandardAiRailColumnMinWidth => IsStandardAiRailExpanded ? 340 : 56;

    public string StandardAiRailToggleText => IsStandardAiRailExpanded ? "›" : "‹";

    public string StandardAiRailToggleToolTip => IsStandardAiRailExpanded
        ? "AIアシストレールを折り畳みます。"
        : "AIアシストレールを展開します。";

    private Task ToggleStandardAiRailAsync()
    {
        IsStandardAiRailExpanded = !IsStandardAiRailExpanded;
        return Task.CompletedTask;
    }

    public string StandardLayoutTitle => SelectedJob?.Title ?? "ジョブを選択";

    public string StandardLayoutMeta
    {
        get
        {
            if (SelectedJob is null)
            {
                return "音声ファイルを追加すると整文ビューを確認できます。";
            }

            return $"{SelectedJob.FileName} · {SelectedJob.Status} · {Segments.Count}セグメント";
        }
    }

    private int EffectiveDetailInspectorTranscriptTabIndex => IsStandardLayout
        ? IsStandardRawTranscriptViewSelected
            ? RawTranscriptTabIndex
            : ReadableTranscriptTabIndex
        : SelectedTranscriptTabIndex;

    public string DetailInspectorCurrentTabText => EffectiveDetailInspectorTranscriptTabIndex switch
    {
        ReadableTranscriptTabIndex => "整文",
        RawTranscriptTabIndex => "素起こし",
        DiffTranscriptTabIndex => "差分",
        ReviewCandidateTranscriptTabIndex => "レビュー候補",
        _ => "文字起こし"
    };

    public string DetailInspectorTargetText => SelectedJob is null
        ? "ジョブ未選択"
        : $"{SelectedJob.Title} · {SelectedJob.Status}";

    public string DetailInspectorSegmentText => SelectedSegment is null
        ? "セグメント未選択"
        : $"{SelectedSegment.Start} - {SelectedSegment.End} · {SelectedSegment.Speaker}";

    public string StandardLayoutJobBadgeText => Jobs.Count > 99 ? "99+" : Jobs.Count.ToString("0");

    public string StandardLayoutAiBadgeText
    {
        get
        {
            if (SelectedJobUnreviewedDrafts > 0)
            {
                return Math.Min(SelectedJobUnreviewedDrafts, 99).ToString("0");
            }

            return HasSummaryContent ? "OK" : "0";
        }
    }

    public string StandardLayoutAiAssistText
    {
        get
        {
            if (IsSummaryStale)
            {
                return "本文更新により要約の再生成が必要です。";
            }

            if (SelectedJobUnreviewedDrafts > 0)
            {
                return $"未確認の整文候補が{SelectedJobUnreviewedDrafts}件あります。";
            }

            return HasSummaryContent
                ? "要約があります。"
                : "整文候補と要約を詳細レイアウトで確認できます。";
        }
    }

    public string StandardLayoutAiReviewStatusText => SelectedJobUnreviewedDrafts > 0
        ? $"未確認 {SelectedJobUnreviewedDrafts} 件"
        : HasReviewDraft
            ? "候補確認中"
            : "候補なし";

    public string StandardLayoutAiSummaryStatusText
    {
        get
        {
            if (IsSummaryStageRunning)
            {
                return "要約生成中";
            }

            if (IsSummaryStale)
            {
                return "本文更新あり";
            }

            return HasSummaryContent ? "要約あり" : "要約なし";
        }
    }

    public bool HasStandardLayoutAiWarning => IsSummaryStale || SelectedJobUnreviewedDrafts > 0;

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

    private void NotifyStandardLayoutShellChanged()
    {
        OnPropertyChanged(nameof(StandardLayoutTitle));
        OnPropertyChanged(nameof(StandardLayoutMeta));
        OnPropertyChanged(nameof(StandardLayoutJobBadgeText));
        OnPropertyChanged(nameof(StandardLayoutAiBadgeText));
        OnPropertyChanged(nameof(StandardLayoutAiAssistText));
        OnPropertyChanged(nameof(StandardLayoutAiReviewStatusText));
        OnPropertyChanged(nameof(StandardLayoutAiSummaryStatusText));
        OnPropertyChanged(nameof(HasStandardLayoutAiWarning));
    }

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
