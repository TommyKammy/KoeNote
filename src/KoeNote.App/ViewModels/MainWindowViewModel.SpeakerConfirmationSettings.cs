using System.Collections.ObjectModel;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.ViewModels;

public sealed record SpeakerNameConfirmationModeOption(string Mode, string DisplayName, string Description)
{
    public override string ToString() => DisplayName;
}

public sealed partial class MainWindowViewModel
{
    private readonly SpeakerNameConfirmationSettingsRepository _speakerNameConfirmationSettingsRepository;
    private bool _isLoadingSpeakerNameConfirmationSettings;
    private SpeakerNameConfirmationModeOption? _selectedSpeakerNameConfirmationMode;
    private string _speakerNameConfirmationSettingsStatus = string.Empty;

    public ObservableCollection<SpeakerNameConfirmationModeOption> SpeakerNameConfirmationModeOptions { get; } =
    [
        new(
            SpeakerNameConfirmationModes.Always,
            "毎回確認する",
            "整文前に毎回、話者名確認ダイアログを表示します。"),
        new(
            SpeakerNameConfirmationModes.UnassignedOnly,
            "未設定speakerがある時だけ確認",
            "Speaker_0 などのID表示が残っている時だけ、整文前に確認します。")
    ];

    public SpeakerNameConfirmationModeOption? SelectedSpeakerNameConfirmationMode
    {
        get => _selectedSpeakerNameConfirmationMode;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetField(ref _selectedSpeakerNameConfirmationMode, value) &&
                !_isLoadingSpeakerNameConfirmationSettings)
            {
                SaveSpeakerNameConfirmationSettings(value.Mode);
            }

            OnPropertyChanged(nameof(SpeakerNameConfirmationModeDescription));
        }
    }

    public string SpeakerNameConfirmationModeDescription =>
        SelectedSpeakerNameConfirmationMode?.Description ?? string.Empty;

    public string SpeakerNameConfirmationSettingsStatus
    {
        get => _speakerNameConfirmationSettingsStatus;
        private set => SetField(ref _speakerNameConfirmationSettingsStatus, value);
    }

    private void InitializeSpeakerNameConfirmationSettings()
    {
        _isLoadingSpeakerNameConfirmationSettings = true;
        try
        {
            var settings = _speakerNameConfirmationSettingsRepository.Load();
            SelectedSpeakerNameConfirmationMode = FindSpeakerNameConfirmationModeOption(settings.Mode);
            SpeakerNameConfirmationSettingsStatus = "話者名確認設定を読み込みました。";
        }
        finally
        {
            _isLoadingSpeakerNameConfirmationSettings = false;
        }
    }

    private void SaveSpeakerNameConfirmationSettings(string mode)
    {
        var normalized = SpeakerNameConfirmationModes.Normalize(mode);
        _speakerNameConfirmationSettingsRepository.Save(new SpeakerNameConfirmationSettings(normalized));
        SelectedSpeakerNameConfirmationMode = FindSpeakerNameConfirmationModeOption(normalized);
        SpeakerNameConfirmationSettingsStatus =
            $"{SelectedSpeakerNameConfirmationMode?.DisplayName ?? normalized} に更新しました。";
        OnPropertyChanged(nameof(SpeakerNameConfirmationModeDescription));
    }

    private SpeakerNameConfirmationModeOption FindSpeakerNameConfirmationModeOption(string mode)
    {
        var normalized = SpeakerNameConfirmationModes.Normalize(mode);
        return SpeakerNameConfirmationModeOptions.First(option =>
            option.Mode.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private string SelectedSpeakerNameConfirmationModeValue =>
        SelectedSpeakerNameConfirmationMode?.Mode ?? SpeakerNameConfirmationModes.Always;
}
