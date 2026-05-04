using KoeNote.App.Services.Asr;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void SaveAsrSettings()
    {
        _asrSettingsSaveDebounce?.Cancel();
        _asrSettingsSaveDebounce = null;
        _asrSettingsRepository.Save(new AsrSettings(AsrContextText, AsrHotwordsText, SelectedAsrEngineId, EnableReviewStage));
    }

    private void ScheduleSaveAsrSettings()
    {
        _asrSettingsSaveDebounce?.Cancel();
        var cancellation = new CancellationTokenSource();
        _asrSettingsSaveDebounce = cancellation;
        _ = SaveAsrSettingsAfterDelayAsync(cancellation.Token);
    }

    private async Task SaveAsrSettingsAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken).ConfigureAwait(false);
            _asrSettingsRepository.Save(new AsrSettings(AsrContextText, AsrHotwordsText, SelectedAsrEngineId, EnableReviewStage));
        }
        catch (OperationCanceledException)
        {
        }
    }
}
