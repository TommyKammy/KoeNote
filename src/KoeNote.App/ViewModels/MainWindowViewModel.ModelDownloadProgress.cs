using KoeNote.App.Services.Models;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const string InactiveModelDownloadProgressSummary = "No active model download.";

    private int BeginModelDownloadProgress(string displayName)
    {
        _modelDownloadProgressOperationId++;
        _modelDownloadNotificationTimer.Stop();
        ApplyModelDownloadProgressState(_modelDownloadProgressPresenter.Begin(displayName));
        return _modelDownloadProgressOperationId;
    }

    private bool IsCurrentModelDownloadProgressOperation(int operationId)
    {
        return operationId == _modelDownloadProgressOperationId && IsModelDownloadInProgress;
    }

    private void UpdateModelDownloadProgress(int operationId, string displayName, ModelDownloadProgress progress)
    {
        if (!IsCurrentModelDownloadProgressOperation(operationId))
        {
            return;
        }

        ApplyModelDownloadProgressState(_modelDownloadProgressPresenter.Update(displayName, progress, IsModelDownloadInProgress));
    }

    private void RefreshModelCatalogForDownloadProgress(ModelDownloadProgress progress)
    {
        if (!_modelDownloadProgressPresenter.ShouldRefreshCatalog(progress, DateTimeOffset.UtcNow))
        {
            return;
        }

        RefreshModelCatalogKeepingSelection(progress.ModelId);
    }

    private void CompleteModelDownloadProgress(int operationId, string displayName, bool succeeded, string? message = null)
    {
        if (!IsCurrentModelDownloadProgressOperation(operationId))
        {
            return;
        }

        ApplyModelDownloadProgressState(_modelDownloadProgressPresenter.Complete(displayName, succeeded, message));
        _modelDownloadProgressOperationId++;
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
