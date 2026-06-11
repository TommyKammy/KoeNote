using KoeNote.App.Services.Models;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void BeginModelDownloadProgress(string displayName)
    {
        _modelDownloadNotificationTimer.Stop();
        ApplyModelDownloadProgressState(_modelDownloadProgressPresenter.Begin(displayName));
    }

    private void UpdateModelDownloadProgress(string displayName, ModelDownloadProgress progress)
    {
        ApplyModelDownloadProgressState(_modelDownloadProgressPresenter.Update(displayName, progress));
    }

    private void RefreshModelCatalogForDownloadProgress(ModelDownloadProgress progress)
    {
        if (!_modelDownloadProgressPresenter.ShouldRefreshCatalog(progress, DateTimeOffset.UtcNow))
        {
            return;
        }

        RefreshModelCatalogKeepingSelection(progress.ModelId);
    }

    private void CompleteModelDownloadProgress(string displayName, bool succeeded, string? message = null)
    {
        ApplyModelDownloadProgressState(_modelDownloadProgressPresenter.Complete(displayName, succeeded, message));
        ScheduleModelDownloadNotificationDismiss();
    }

    private void ScheduleModelDownloadNotificationDismiss()
    {
        _modelDownloadNotificationTimer.Stop();
        _modelDownloadNotificationTimer.Start();
    }

    private void ClearModelDownloadNotification()
    {
        ModelDownloadNotification = string.Empty;
        IsModelDownloadNotificationError = false;
    }

    private void ApplyModelDownloadProgressState(ModelDownloadProgressViewState state)
    {
        if (state.IsInProgress is { } isInProgress)
        {
            IsModelDownloadInProgress = isInProgress;
        }

        if (state.IsIndeterminate is { } isIndeterminate)
        {
            IsModelDownloadProgressIndeterminate = isIndeterminate;
        }

        if (state.StageText is not null)
        {
            ModelDownloadProgressStageText = state.StageText;
        }

        if (state.Percent is { } percent)
        {
            ModelDownloadProgressPercent = percent;
        }

        if (state.Summary is not null)
        {
            ModelDownloadProgressSummary = state.Summary;
        }

        if (state.Notification is not null)
        {
            ModelDownloadNotification = state.Notification;
        }

        if (state.IsNotificationError is { } isNotificationError)
        {
            IsModelDownloadNotificationError = isNotificationError;
        }

        if (state.LatestLog is not null)
        {
            LatestLog = state.LatestLog;
        }
    }

    private static string FormatBytes(long sizeBytes)
    {
        return ModelDownloadProgressPresenter.FormatByteSize(sizeBytes);
    }
}
