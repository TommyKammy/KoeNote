using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KoeNote.Cleanup;

public sealed class CleanupWindow : Window
{
    private readonly CleanupService _service;
    private readonly bool _dryRun;
    private readonly CheckBox _logs;
    private readonly CheckBox _downloads;
    private readonly CheckBox _models;
    private readonly CheckBox _machineModels;
    private readonly CheckBox _userData;
    private readonly TextBox _output;

    public CleanupWindow(CleanupService service, CleanupPlan initialPlan, bool dryRun)
    {
        _service = service;
        _dryRun = dryRun;

        Title = "KoeNote Cleanup";
        Width = 560;
        Height = 480;
        MinWidth = 480;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new Grid
        {
            Margin = new Thickness(20)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "KoeNote optional data cleanup",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        root.Children.Add(title);

        var options = new StackPanel
        {
            Margin = new Thickness(0, 8, 0, 12)
        };
        Grid.SetRow(options, 1);
        _logs = NewCheckBox("Temporary files and logs", initialPlan.RemoveLogs);
        _downloads = NewCheckBox("Temporary model downloads and optional Python packages", initialPlan.RemoveDownloads);
        _models = NewCheckBox("Downloaded user models", initialPlan.RemoveUserModels);
        _machineModels = NewCheckBox("Shared machine models", initialPlan.RemoveMachineModels);
        _userData = NewCheckBox("Jobs, transcripts, setup state, and settings", initialPlan.RemoveUserData);
        options.Children.Add(_logs);
        options.Children.Add(_downloads);
        options.Children.Add(_models);
        options.Children.Add(_machineModels);
        options.Children.Add(_userData);
        options.Children.Add(new TextBlock
        {
            Text = "Models and job data are kept unless you select them here, so reinstall can reuse them.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });
        root.Children.Add(options);

        _output = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12
        };
        Grid.SetRow(_output, 2);
        root.Children.Add(_output);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(buttons, 3);
        var preview = new Button { Content = "Preview", MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
        preview.Click += (_, _) => Run(dryRun: true);
        var cleanup = new Button { Content = "Clean up", MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
        cleanup.Click += (_, _) => Run(_dryRun);
        var close = new Button { Content = "Close", MinWidth = 96 };
        close.Click += (_, _) => Close();
        buttons.Children.Add(preview);
        buttons.Children.Add(cleanup);
        buttons.Children.Add(close);
        root.Children.Add(buttons);

        Content = root;
        Run(dryRun: true);
    }

    private static CheckBox NewCheckBox(string text, bool isChecked)
    {
        return new CheckBox
        {
            Content = text,
            IsChecked = isChecked,
            Margin = new Thickness(0, 4, 0, 4)
        };
    }

    private CleanupPlan BuildPlan()
    {
        return new CleanupPlan(
            _logs.IsChecked == true,
            _downloads.IsChecked == true,
            _models.IsChecked == true,
            _machineModels.IsChecked == true,
            _userData.IsChecked == true);
    }

    private void Run(bool dryRun)
    {
        var result = _service.Execute(BuildPlan(), dryRun);
        _output.Text = result.ToConsoleText();
    }
}
