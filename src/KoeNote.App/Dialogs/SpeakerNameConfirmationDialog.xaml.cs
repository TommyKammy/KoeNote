using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using KoeNote.App.Services.Audio;
using KoeNote.App.Services.Dialogs;
using KoeNote.App.ViewModels;

namespace KoeNote.App.Dialogs;

public partial class SpeakerNameConfirmationDialog : Window
{
    private SpeakerNameConfirmationDialogViewModel ViewModel => (SpeakerNameConfirmationDialogViewModel)DataContext;

    public SpeakerNameConfirmationDialog(SpeakerNameConfirmationRequest request)
        : this(request, new AudioPlaybackService())
    {
    }

    public SpeakerNameConfirmationDialog(SpeakerNameConfirmationRequest request, IAudioPlaybackService audioPlaybackService)
    {
        InitializeComponent();
        DataContext = new SpeakerNameConfirmationDialogViewModel(request, audioPlaybackService);
    }

    public SpeakerNameConfirmationResult? Result { get; private set; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ViewModel.StopPlayback();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshValidation();
        if (!ViewModel.CanConfirm)
        {
            return;
        }

        Result = ViewModel.CreateResult();
        DialogResult = true;
    }

    private void OnDisplayNameTextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.RefreshValidation();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OnConfirmClick(sender, e);
            e.Handled = true;
        }
    }
}

internal sealed class SpeakerNameConfirmationDialogViewModel : INotifyPropertyChanged
{
    private readonly SpeakerNameConfirmationPreviewPlaybackController _playbackController;
    private readonly DispatcherTimer _playbackTimer;
    private SpeakerNameConfirmationPreviewDialogItem? _activePreviewItem;

    public SpeakerNameConfirmationDialogViewModel(
        SpeakerNameConfirmationRequest request,
        IAudioPlaybackService audioPlaybackService)
    {
        AudioPath = request.AudioPath;
        _playbackController = new SpeakerNameConfirmationPreviewPlaybackController(audioPlaybackService);
        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _playbackTimer.Tick += (_, _) => RefreshPlayback();

        LeadText = $"{request.JobTitle} の話者名を確認してから整文を開始します。";
        Items = new ObservableCollection<SpeakerNameConfirmationDialogItem>(
            request.Speakers.Select(speaker => new SpeakerNameConfirmationDialogItem(speaker, this)));
        RefreshValidation();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string LeadText { get; }

    public string? AudioPath { get; }

    public bool CanPlayPreview => _playbackController.CanPlay(AudioPath);

    public ObservableCollection<SpeakerNameConfirmationDialogItem> Items { get; }

    public bool CanConfirm => Items.Count > 0 && Items.All(static item => !item.HasError);

    public string ValidationSummary => CanConfirm
        ? string.Empty
        : "空の話者名は保存できません。80文字以内で入力してください。";

    public void RefreshValidation()
    {
        foreach (var item in Items)
        {
            item.RefreshValidation();
        }

        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(ValidationSummary));
    }

    public SpeakerNameConfirmationResult CreateResult()
    {
        return new SpeakerNameConfirmationResult(
            Items.ToDictionary(
                static item => item.SpeakerId,
                static item => item.DisplayName.Trim(),
                StringComparer.OrdinalIgnoreCase));
    }

    public Task TogglePreviewAsync(SpeakerNameConfirmationPreviewDialogItem previewItem)
    {
        if (_activePreviewItem is not null && !ReferenceEquals(_activePreviewItem, previewItem))
        {
            _activePreviewItem.SetPlaybackState(false, 0);
        }

        var isPlaying = _playbackController.Toggle(AudioPath, previewItem.Preview);
        _activePreviewItem = isPlaying ? previewItem : null;
        previewItem.SetPlaybackState(isPlaying, _playbackController.ProgressPercent);

        if (isPlaying)
        {
            _playbackTimer.Start();
        }
        else
        {
            _playbackTimer.Stop();
        }

        return Task.CompletedTask;
    }

    public void StopPlayback()
    {
        _playbackTimer.Stop();
        _playbackController.Stop();
        _activePreviewItem?.SetPlaybackState(false, 0);
        _activePreviewItem = null;
    }

    private void RefreshPlayback()
    {
        if (_activePreviewItem is null)
        {
            _playbackTimer.Stop();
            return;
        }

        var stillPlaying = _playbackController.Refresh();
        _activePreviewItem.SetPlaybackState(stillPlaying, _playbackController.ProgressPercent);
        if (!stillPlaying)
        {
            _activePreviewItem = null;
            _playbackTimer.Stop();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed class SpeakerNameConfirmationDialogItem : INotifyPropertyChanged
{
    private string _displayName;
    private string _errorText = string.Empty;

    public SpeakerNameConfirmationDialogItem(
        SpeakerNameConfirmationItem item,
        SpeakerNameConfirmationDialogViewModel owner)
    {
        SpeakerId = item.SpeakerId;
        _displayName = item.EffectiveDisplayName;
        OriginalLabel = string.Equals(item.EffectiveDisplayName, item.SpeakerId, StringComparison.OrdinalIgnoreCase)
            ? item.SpeakerId
            : $"{item.EffectiveDisplayName} / {item.SpeakerId}";
        SegmentCountText = $"{item.SegmentCount}件の発話";
        AssistanceText = string.Equals(item.EffectiveDisplayName, item.SpeakerId, StringComparison.OrdinalIgnoreCase)
            ? $"Speaker ID: {item.SpeakerId}"
            : item.EffectiveDisplayName;
        PreviewItems = new ObservableCollection<SpeakerNameConfirmationPreviewDialogItem>(
            item.PreviewSamples.Take(3).Select(preview => new SpeakerNameConfirmationPreviewDialogItem(preview, owner)));
        RefreshValidation();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SpeakerId { get; }

    public string OriginalLabel { get; }

    public string SegmentCountText { get; }

    public string AssistanceText { get; }

    public bool HasAssistanceText => !string.IsNullOrWhiteSpace(AssistanceText);

    public ObservableCollection<SpeakerNameConfirmationPreviewDialogItem> PreviewItems { get; }

    public bool HasPreviewItems => PreviewItems.Count > 0;

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (string.Equals(_displayName, value, StringComparison.Ordinal))
            {
                return;
            }

            _displayName = value;
            RefreshValidation();
            OnPropertyChanged();
        }
    }

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (string.Equals(_errorText, value, StringComparison.Ordinal))
            {
                return;
            }

            _errorText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public void RefreshValidation()
    {
        ErrorText = string.IsNullOrWhiteSpace(DisplayName)
            ? "話者名を入力してください。"
            : DisplayName.Trim().Length > 80
                ? "話者名は80文字以内で入力してください。"
                : string.Empty;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed class SpeakerNameConfirmationPreviewDialogItem : INotifyPropertyChanged
{
    private bool _isPlaying;
    private double _playbackProgressPercent;

    public SpeakerNameConfirmationPreviewDialogItem(
        SpeakerNameConfirmationPreview preview,
        SpeakerNameConfirmationDialogViewModel owner)
    {
        Preview = preview;
        TimestampText = $"[{FormatTimestamp(preview.StartSeconds)} - {FormatTimestamp(preview.EndSeconds)}]";
        Text = preview.Text.Trim();
        if (Text.Length > 120)
        {
            Text = string.Concat(Text.AsSpan(0, 120), "...");
        }

        PlayCommand = new RelayCommand(
            () => owner.TogglePreviewAsync(this),
            () => owner.CanPlayPreview);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SpeakerNameConfirmationPreview Preview { get; }

    public string TimestampText { get; }

    public string Text { get; }

    public ICommand PlayCommand { get; }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (_isPlaying == value)
            {
                return;
            }

            _isPlaying = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlayIcon));
            OnPropertyChanged(nameof(PlaybackStatusText));
        }
    }

    public string PlayIcon => IsPlaying ? "\uE769" : "\uE768";

    public string PlaybackStatusText => IsPlaying ? "再生中" : string.Empty;

    public double PlaybackProgressPercent
    {
        get => _playbackProgressPercent;
        private set
        {
            if (Math.Abs(_playbackProgressPercent - value) < 0.01)
            {
                return;
            }

            _playbackProgressPercent = value;
            OnPropertyChanged();
        }
    }

    public void SetPlaybackState(bool isPlaying, double playbackProgressPercent)
    {
        IsPlaying = isPlaying;
        PlaybackProgressPercent = isPlaying ? playbackProgressPercent : 0;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string FormatTimestamp(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            seconds = 0;
        }

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
            : time.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }
}
