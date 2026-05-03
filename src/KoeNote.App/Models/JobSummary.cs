using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KoeNote.App.Models;

public sealed class JobSummary : INotifyPropertyChanged
{
    private string _status;
    private int _progressPercent;
    private DateTimeOffset _updatedAt;
    private string? _normalizedAudioPath;

    public JobSummary(
        string jobId,
        string title,
        string fileName,
        string sourceAudioPath,
        string status,
        int progressPercent,
        int unreviewedDrafts,
        DateTimeOffset updatedAt,
        string? normalizedAudioPath = null)
    {
        JobId = jobId;
        Title = title;
        FileName = fileName;
        SourceAudioPath = sourceAudioPath;
        _status = status;
        _progressPercent = progressPercent;
        UnreviewedDrafts = unreviewedDrafts;
        _updatedAt = updatedAt;
        _normalizedAudioPath = normalizedAudioPath;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string JobId { get; }

    public string Title { get; }

    public string FileName { get; }

    public string SourceAudioPath { get; }

    public int UnreviewedDrafts { get; }

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
        set => SetField(ref _updatedAt, value);
    }

    public string? NormalizedAudioPath
    {
        get => _normalizedAudioPath;
        set => SetField(ref _normalizedAudioPath, value);
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
