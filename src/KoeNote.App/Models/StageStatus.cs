using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KoeNote.App.Models;

public sealed class StageStatus(
    string name,
    string iconPathData,
    string iconStroke,
    string iconSoftBackground) : INotifyPropertyChanged
{
    private string _status = "未開始";
    private int _progressPercent;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; } = name;

    public string IconPathData { get; } = iconPathData;

    public string IconStroke { get; } = iconStroke;

    public string IconSoftBackground { get; } = iconSoftBackground;

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public int ProgressPercent
    {
        get => _progressPercent;
        set => SetField(ref _progressPercent, value);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
