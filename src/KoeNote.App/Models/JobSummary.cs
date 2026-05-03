using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KoeNote.App.Models;

public sealed class JobSummary : INotifyPropertyChanged
{
    private string _status;
    private int _progressPercent;
    private DateTimeOffset _updatedAt;
    private string? _normalizedAudioPath;
    private int _unreviewedDrafts;

    public JobSummary(
        string jobId,
        string title,
        string fileName,
        string sourceAudioPath,
        string status,
        int progressPercent,
        int unreviewedDrafts,
        DateTimeOffset updatedAt,
        DateTimeOffset? createdAt = null,
        string? normalizedAudioPath = null)
    {
        JobId = jobId;
        Title = title;
        FileName = fileName;
        SourceAudioPath = sourceAudioPath;
        _status = status;
        _progressPercent = progressPercent;
        _unreviewedDrafts = unreviewedDrafts;
        _updatedAt = updatedAt;
        CreatedAt = createdAt ?? updatedAt;
        _normalizedAudioPath = normalizedAudioPath;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string JobId { get; }

    public string Title { get; }

    public string FileName { get; }

    public string SourceAudioPath { get; }

    public DateTimeOffset CreatedAt { get; }

    public int UnreviewedDrafts
    {
        get => _unreviewedDrafts;
        set => SetField(ref _unreviewedDrafts, value);
    }

    public string UpdatedAtDisplay => UpdatedAt.ToString("yyyy/MM/dd HH:mm");

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

    public DateTimeOffset UpdatedAt
    {
        get => _updatedAt;
        set
        {
            if (SetField(ref _updatedAt, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UpdatedAtDisplay)));
            }
        }
    }

    public string? NormalizedAudioPath
    {
        get => _normalizedAudioPath;
        set => SetField(ref _normalizedAudioPath, value);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
