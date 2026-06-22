using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KoeNote.Updater;

public sealed class UpdaterProgressWindow : Window
{
    private readonly TextBlock _titleText;
    private readonly TextBlock _messageText;
    private readonly TextBlock _logText;
    private readonly ProgressBar _progressBar;
    private readonly Button _closeButton;
    private bool _canClose;
    private bool _isProgrammaticClose;

    public UpdaterProgressWindow(string version)
    {
        Title = "KoeNote Update";
        Width = 430;
        Height = 210;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        ShowActivated = true;
        Background = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");

        var root = new Grid
        {
            Margin = new Thickness(24, 22, 24, 20)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var eyebrow = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(version) || string.Equals(version, "unknown", StringComparison.OrdinalIgnoreCase)
                ? "KoeNote update"
                : $"KoeNote {version}",
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(eyebrow, 0);
        root.Children.Add(eyebrow);

        _titleText = new TextBlock
        {
            Text = "KoeNoteを更新しています",
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            FontWeight = FontWeights.SemiBold,
            FontSize = 19,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(_titleText, 1);
        root.Children.Add(_titleText);

        _messageText = new TextBlock
        {
            Text = "完了後にKoeNoteを自動で再起動します。しばらくお待ちください。",
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        };
        Grid.SetRow(_messageText, 2);
        root.Children.Add(_messageText);

        _progressBar = new ProgressBar
        {
            Height = 8,
            IsIndeterminate = true,
            Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74)),
            Background = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(_progressBar, 3);
        root.Children.Add(_progressBar);

        var bottom = new DockPanel
        {
            LastChildFill = true
        };
        Grid.SetRow(bottom, 4);
        root.Children.Add(bottom);

        _closeButton = new Button
        {
            Content = "閉じる",
            MinWidth = 88,
            Padding = new Thickness(14, 6, 14, 7),
            Visibility = Visibility.Collapsed
        };
        _closeButton.Click += (_, _) => Close();
        DockPanel.SetDock(_closeButton, Dock.Right);
        bottom.Children.Add(_closeButton);

        _logText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        bottom.Children.Add(_logText);

        Content = root;
        Closing += (_, args) =>
        {
            if (!_canClose && !_isProgrammaticClose)
            {
                args.Cancel = true;
            }
        };
    }

    public void SetStatus(string title, string message)
    {
        _canClose = false;
        _titleText.Text = title;
        _messageText.Text = message;
        _progressBar.IsIndeterminate = true;
        _progressBar.Visibility = Visibility.Visible;
        _closeButton.Visibility = Visibility.Collapsed;
        _logText.Text = string.Empty;
    }

    public void SetTerminalStatus(string title, string message, string logPath)
    {
        _canClose = true;
        _titleText.Text = title;
        _messageText.Text = message;
        _progressBar.IsIndeterminate = false;
        _progressBar.Visibility = Visibility.Collapsed;
        _closeButton.Visibility = Visibility.Visible;
        _logText.Text = string.IsNullOrWhiteSpace(logPath)
            ? string.Empty
            : $"ログ: {logPath}";
        Activate();
    }

    public void CloseProgrammatically()
    {
        _isProgrammaticClose = true;
        Close();
    }
}
