using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using KoeNote.App.ViewModels;

namespace KoeNote.App.Controls;

public partial class ReadablePolishedPanel : UserControl
{
    private INotifyPropertyChanged? _subscribedViewModel;

    private static readonly Regex TimestampRangePattern = new(
        @"^\s*\[(?<start>\d{1,2}:\d{2}(?::\d{2})?)\s*(?:-|–|〜|~|－|ー)\s*(?<end>\d{1,2}:\d{2}(?::\d{2})?)\]",
        RegexOptions.Compiled);

    private static readonly Regex TimestampStartPattern = new(
        @"^\s*\[(?<start>\d{1,2}:\d{2}(?::\d{2})?)\]",
        RegexOptions.Compiled);

    public ReadablePolishedPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        SubscribeToViewModel(DataContext as INotifyPropertyChanged);
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        UnsubscribeFromViewModel(e.OldValue as INotifyPropertyChanged);
        SubscribeToViewModel(e.NewValue as INotifyPropertyChanged);
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        UnsubscribeFromViewModel(DataContext as INotifyPropertyChanged);
    }

    private void SubscribeToViewModel(INotifyPropertyChanged? viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
        {
            return;
        }

        UnsubscribeFromViewModel(_subscribedViewModel);
        _subscribedViewModel = viewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void UnsubscribeFromViewModel(INotifyPropertyChanged? viewModel)
    {
        if (_subscribedViewModel is null ||
            (viewModel is not null && !ReferenceEquals(_subscribedViewModel, viewModel)))
        {
            return;
        }

        _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribedViewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.TranscriptAutoScrollRequestId))
        {
            Dispatcher.BeginInvoke(ScrollReadableTextToPlaybackPosition);
        }
    }

    private void ScrollReadableTextToPlaybackPosition()
    {
        if (DataContext is not MainWindowViewModel
            {
                IsTranscriptAutoScrollEnabled: true,
                SelectedTranscriptTabIndex: 0,
                SelectedSegment: { } segment,
                HasReadablePolishedContent: true
            })
        {
            return;
        }

        var lineIndex = FindReadableLineIndex(segment.StartSeconds);
        if (lineIndex < 0)
        {
            return;
        }

        ReadablePolishedTextBox.ScrollToLine(lineIndex);
    }

    private int FindReadableLineIndex(double positionSeconds)
    {
        var lines = ReadablePolishedTextBox.Text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');

        var fallbackLine = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (TryParseTimestampRange(lines[i], out var startSeconds, out var endSeconds))
            {
                if (positionSeconds >= startSeconds && positionSeconds < endSeconds)
                {
                    return i;
                }

                if (positionSeconds >= startSeconds)
                {
                    fallbackLine = i;
                }
            }
        }

        return fallbackLine;
    }

    private static bool TryParseTimestampRange(string line, out double startSeconds, out double endSeconds)
    {
        var rangeMatch = TimestampRangePattern.Match(line);
        if (rangeMatch.Success &&
            TryParseTimestamp(rangeMatch.Groups["start"].Value, out startSeconds) &&
            TryParseTimestamp(rangeMatch.Groups["end"].Value, out endSeconds))
        {
            return true;
        }

        var startMatch = TimestampStartPattern.Match(line);
        if (startMatch.Success &&
            TryParseTimestamp(startMatch.Groups["start"].Value, out startSeconds))
        {
            endSeconds = double.MaxValue;
            return true;
        }

        startSeconds = 0;
        endSeconds = 0;
        return false;
    }

    private static bool TryParseTimestamp(string value, out double seconds)
    {
        var parts = value.Split(':');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var minutes) &&
            int.TryParse(parts[1], out var secondsPart))
        {
            seconds = minutes * 60 + secondsPart;
            return true;
        }

        if (parts.Length == 3 &&
            int.TryParse(parts[0], out var hours) &&
            int.TryParse(parts[1], out minutes) &&
            int.TryParse(parts[2], out secondsPart))
        {
            seconds = hours * 3600 + minutes * 60 + secondsPart;
            return true;
        }

        seconds = 0;
        return false;
    }
}
