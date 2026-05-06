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

        Title = "KoeNote クリーンアップ";
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
            Text = "KoeNote データのクリーンアップ",
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
        _logs = NewCheckBox("一時ファイルとログ", initialPlan.RemoveLogs);
        _downloads = NewCheckBox("一時モデルダウンロードと任意の Python パッケージ", initialPlan.RemoveDownloads);
        _models = NewCheckBox("ダウンロード済みユーザーモデル", initialPlan.RemoveUserModels);
        _machineModels = NewCheckBox("共有マシンモデル", initialPlan.RemoveMachineModels);
        _userData = NewCheckBox("ジョブ、文字起こし、セットアップ状態、設定", initialPlan.RemoveUserData);
        options.Children.Add(_logs);
        options.Children.Add(_downloads);
        options.Children.Add(_models);
        options.Children.Add(_machineModels);
        options.Children.Add(_userData);
        options.Children.Add(new TextBlock
        {
            Text = "モデルとジョブデータは、ここで選択しない限り保持されます。再インストール後も再利用できます。",
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
        var preview = new Button { Content = "プレビュー", MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
        preview.Click += (_, _) => Run(dryRun: true);
        var cleanup = new Button { Content = "削除する", MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
        cleanup.Click += (_, _) => Run(_dryRun);
        var close = new Button { Content = "閉じる", MinWidth = 96 };
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
        var plan = BuildPlan();
        if (!dryRun && RequiresDestructiveConfirmation(plan) && !ConfirmDestructiveCleanup(plan))
        {
            _output.Text = "削除をキャンセルしました。";
            return;
        }

        var result = _service.Execute(plan, dryRun);
        _output.Text = result.ToConsoleText();
    }

    private static bool RequiresDestructiveConfirmation(CleanupPlan plan)
    {
        return plan.RemoveUserModels || plan.RemoveMachineModels || plan.RemoveUserData;
    }

    private bool ConfirmDestructiveCleanup(CleanupPlan plan)
    {
        var targets = new List<string>();
        if (plan.RemoveUserModels)
        {
            targets.Add("ダウンロード済みユーザーモデル");
        }

        if (plan.RemoveMachineModels)
        {
            targets.Add("共有マシンモデル");
        }

        if (plan.RemoveUserData)
        {
            targets.Add("ジョブ、文字起こし、セットアップ状態、設定");
        }

        var message = string.Join(Environment.NewLine, targets.Select(static target => $"・{target}")) +
            Environment.NewLine +
            Environment.NewLine +
            "選択したデータを削除します。この操作は元に戻せません。KoeNote 本体を開いている場合は、先に終了してください。";
        return MessageBox.Show(
            this,
            message,
            "クリーンアップの確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }
}
