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
    private bool _isDeleted;
    private DateTimeOffset? _deletedAt;
    private string _deleteReason = string.Empty;
    private long _storageBytes;

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
        string? normalizedAudioPath = null,
        bool isDeleted = false,
        DateTimeOffset? deletedAt = null,
        string? deleteReason = null,
        long storageBytes = 0)
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
        _isDeleted = isDeleted;
        _deletedAt = deletedAt;
        _deleteReason = deleteReason ?? string.Empty;
        _storageBytes = storageBytes;
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

    public string DeletedAtDisplay => DeletedAt?.ToString("yyyy/MM/dd HH:mm") ?? "";

    public string StorageSizeDisplay => FormatBytes(StorageBytes);

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

    public bool IsDeleted
    {
        get => _isDeleted;
        set => SetField(ref _isDeleted, value);
    }

    public DateTimeOffset? DeletedAt
    {
        get => _deletedAt;
        set
        {
            if (SetField(ref _deletedAt, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DeletedAtDisplay)));
            }
        }
    }

    public string DeleteReason
    {
        get => _deleteReason;
        set => SetField(ref _deleteReason, value ?? string.Empty);
    }

    public long StorageBytes
    {
        get => _storageBytes;
        set
        {
            if (SetField(ref _storageBytes, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StorageSizeDisplay)));
            }
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{bytes} {units[unitIndex]}"
            : $"{value:0.0} {units[unitIndex]}";
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
