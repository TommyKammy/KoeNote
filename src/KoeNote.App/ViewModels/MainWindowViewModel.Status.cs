using KoeNote.App.Models;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task RefreshStatusBarInfoAsync()
    {
        if (_isStatusRefreshInProgress)
        {
            return;
        }

        try
        {
            _isStatusRefreshInProgress = true;
            _statusBarInfo = await Task.Run(_statusBarInfoService.GetStatusBarInfo);
            OnPropertyChanged(nameof(DiskFreeSummary));
            OnPropertyChanged(nameof(MemorySummary));
            OnPropertyChanged(nameof(CpuSummary));
            OnPropertyChanged(nameof(GpuUsageSummary));
        }
        finally
        {
            _isStatusRefreshInProgress = false;
        }
    }

    private Task ToggleReviewStageAsync(StageStatus? stage)
    {
        if (stage?.IsToggleable != true || IsRunInProgress)
        {
            return Task.CompletedTask;
        }

        EnableReviewStage = !EnableReviewStage;
        LatestLog = EnableReviewStage
            ? "推敲ステージを有効にしました。"
            : "推敲ステージをスキップします。";
        return Task.CompletedTask;
    }

    private void RefreshReviewStageToggleStatus()
    {
        var stage = StageStatuses.FirstOrDefault(static item => item.IsToggleable);
        if (stage is null)
        {
            return;
        }

        if (IsRunInProgress)
        {
            stage.ToggleToolTip = "処理中は切り替えできません";
            return;
        }

        stage.ToggleToolTip = EnableReviewStage
            ? "推敲を実行します。クリックでスキップ"
            : "推敲をスキップします。クリックで実行";

        if (EnableReviewStage)
        {
            if (stage.IsSkipped)
            {
                stage.Status = "未開始";
                stage.ProgressPercent = 0;
                stage.DurationText = "00:00:00";
            }
        }
        else
        {
            stage.Status = "スキップ";
            stage.ProgressPercent = 0;
            stage.DurationText = "00:00:00";
        }
    }

    private static IEnumerable<StageStatus> CreateStageStatuses()
    {
        yield return new StageStatus(
            "音声変換",
            "M4,12 C5.1,12 5.1,7 6.2,7 C7.4,7 7.3,17 8.5,17 C9.7,17 9.7,9 10.9,9 C12.1,9 12.1,15 13.3,15 C14.5,15 14.5,10 15.7,10 C16.9,10 16.9,14 18.1,14 C19.1,14 19.3,12 20,12 M4,5 L7,5 M5.5,3.5 L5.5,6.5 M17,5 L20,5 M18.5,3.5 L18.5,6.5",
            "#2F8F5B",
            "#EAF6EF",
            showConnectorBefore: false);

        yield return new StageStatus(
            "ASR",
            "M8,6 C8,3.8 9.8,2.5 12,2.5 C14.2,2.5 16,3.8 16,6 L16,11 C16,13.2 14.2,14.5 12,14.5 C9.8,14.5 8,13.2 8,11 Z M5.5,10 C5.5,14 8.2,17 12,17 C15.8,17 18.5,14 18.5,10 M12,17 L12,21 M9,21 L15,21",
            "#2563EB",
            "#EFF6FF");

        yield return new StageStatus(
            "推敲",
            "M5,16.5 L4,20 L7.5,19 L17.8,8.7 C18.6,7.9 18.6,6.7 17.8,5.9 L16.1,4.2 C15.3,3.4 14.1,3.4 13.3,4.2 Z M12.5,5 L17,9.5 M17.5,15 L20,15 M18.75,13.75 L18.75,16.25 M6.5,6.5 L8.5,6.5 M7.5,5.5 L7.5,7.5",
            "#7C3AED",
            "#F3E8FF",
            isToggleable: true);

        yield return new StageStatus(
            "レビュー",
            "M6,3.5 L15,3.5 L19,7.5 L19,20.5 L6,20.5 Z M15,3.5 L15,7.5 L19,7.5 M8.5,13 L11,15.5 L15.8,10.7 M8.5,18 L15.5,18",
            "#D97706",
            "#FEF3C7",
            showConnectorAfter: false);
    }
}
