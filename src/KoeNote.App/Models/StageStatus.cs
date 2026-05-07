using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KoeNote.App.Models;

public sealed class StageStatus(
    string name,
    string iconPathData,
    string iconStroke,
    string iconSoftBackground,
    bool showConnectorBefore = true,
    bool showConnectorAfter = true,
    bool isToggleable = false) : INotifyPropertyChanged
{
    private string _status = "未開始";
    private string _durationText = "00:00:00";
    private int _progressPercent;
    private bool _isRunning;
    private string _toggleToolTip = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; } = name;

    public string IconPathData { get; } = iconPathData;

    public string IconStroke { get; } = iconStroke;

    public string IconSoftBackground { get; } = iconSoftBackground;

    public bool ShowConnectorBefore { get; } = showConnectorBefore;

    public bool ShowConnectorAfter { get; } = showConnectorAfter;

    public bool IsToggleable { get; } = isToggleable;

    public string ToggleToolTip
    {
        get => _toggleToolTip;
        set => SetField(ref _toggleToolTip, value);
    }

    public string Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(IsSkipped));
                OnPropertyChanged(nameof(IsDone));
                OnPropertyChanged(nameof(IsDoing));
                OnPropertyChanged(nameof(IsPending));
            }
        }
    }

    public string DurationText
    {
        get => _durationText;
        set => SetField(ref _durationText, value);
    }

    public int ProgressPercent
    {
        get => _progressPercent;
        set
        {
            if (!SetField(ref _progressPercent, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsDone));
            OnPropertyChanged(nameof(IsDoing));
            OnPropertyChanged(nameof(IsPending));
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (!SetField(ref _isRunning, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsDoing));
            OnPropertyChanged(nameof(IsPending));
        }
    }

    public bool IsDone => !IsRunning && !IsSkipped && ProgressPercent >= 100;

    public bool IsDoing => !IsSkipped && (IsRunning || (ProgressPercent > 0 && ProgressPercent < 100));

    public bool IsPending => !IsRunning && !IsSkipped && ProgressPercent <= 0;

    public bool IsSkipped => string.Equals(Status, "スキップ", StringComparison.Ordinal);

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
