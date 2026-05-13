using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KoeNote.Cleanup;

public sealed class CleanupWindow : Window
{
    private readonly CleanupService _service;
    private readonly bool _dryRun;
    private readonly RadioButton _appOnly;
    private readonly RadioButton _allData;
    private readonly TextBox _output;

    public CleanupWindow(CleanupService service, CleanupPlan initialPlan, bool dryRun)
    {
        _service = service;
        _dryRun = dryRun;

        Title = "KoeNote アンインストール";
        Width = 660;
        Height = 520;
        MinWidth = 560;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new Grid
        {
            Margin = new Thickness(20)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 12)
        };

        var title = new TextBlock
        {
            Text = "KoeNote のアンインストール方法を選択",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        header.Children.Add(title);

        var lead = new TextBlock
        {
            Text = "アプリ本体はアンインストーラーによって削除されます。必要に応じて、KoeNote が保存したデータも一緒に削除できます。",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 0)
        };
        header.Children.Add(lead);
        root.Children.Add(header);

        var options = new StackPanel
        {
            Margin = new Thickness(0, 8, 0, 12)
        };
        Grid.SetRow(options, 1);

        _appOnly = NewRadioButton("アプリのみ削除", !initialPlan.RemoveAllData);
        _allData = NewRadioButton("アプリと KoeNote 関連データをすべて削除", initialPlan.RemoveAllData);
        _appOnly.Checked += (_, _) => Run(dryRun: true);
        _allData.Checked += (_, _) => Run(dryRun: true);

        options.Children.Add(_appOnly);
        options.Children.Add(new TextBlock
        {
            Text = "アプリ本体だけを削除します。ジョブ履歴、設定、モデル、ログ、更新ファイル、エクスポート済みファイルは残ります。",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, 0, 0, 10)
        });
        options.Children.Add(_allData);
        options.Children.Add(new TextBlock
        {
            Text = "AppData、LocalAppData、ProgramData 配下の KoeNote データを削除します。Documents\\KoeNote\\Exports は削除しません。",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, 0, 0, 0)
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
        var cleanup = new Button { Content = "選択して続行", MinWidth = 112, Margin = new Thickness(0, 0, 8, 0) };
        cleanup.Click += (_, _) => ContinueUninstall();
        var close = new Button { Content = "閉じる", MinWidth = 96 };
        close.Click += (_, _) => Close();
        buttons.Children.Add(preview);
        buttons.Children.Add(cleanup);
        buttons.Children.Add(close);
        root.Children.Add(buttons);

        Content = root;
        Run(dryRun: true);
    }

    private static RadioButton NewRadioButton(string text, bool isChecked)
    {
        return new RadioButton
        {
            Content = text,
            IsChecked = isChecked,
            GroupName = "CleanupMode",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 4)
        };
    }

    private CleanupPlan BuildPlan()
    {
        return _allData.IsChecked == true
            ? CleanupPlan.AllData
            : CleanupPlan.AppOnly;
    }

    private void Run(bool dryRun)
    {
        var plan = BuildPlan();
        if (!dryRun && plan.RemoveAllData && !ConfirmDeleteAllData())
        {
            _output.Text = "削除をキャンセルしました。";
            return;
        }

        var result = _service.Execute(plan, dryRun);
        if (plan.RemoveAllData || result.Actions.Any(static action => action.Removed))
        {
            _output.Text = result.ToConsoleText();
            return;
        }

        _output.Text = dryRun
            ? "アプリ本体のみ削除します。KoeNote のジョブ、設定、モデル、ログ、更新ファイルは保持されます。アプリ本体は、この画面を閉じた後にアンインストーラーによって削除されます。"
            : "選択を確定しました。この画面を閉じると、アンインストーラーがアプリ本体の削除を続行します。KoeNote のジョブ、設定、モデル、ログ、更新ファイルは保持されます。";
    }

    private void ContinueUninstall()
    {
        var plan = BuildPlan();
        if (!_dryRun && plan.RemoveAllData && !ConfirmDeleteAllData())
        {
            _output.Text = "削除をキャンセルしました。";
            return;
        }

        var result = _service.Execute(plan, _dryRun);
        if (_dryRun || !result.Succeeded)
        {
            _output.Text = result.ToConsoleText();
            return;
        }

        Close();
    }

    private bool ConfirmDeleteAllData()
    {
        const string message =
            "アプリ本体に加えて、KoeNote 関連データを削除します。\n\n" +
            "削除対象:\n" +
            "- %APPDATA%\\KoeNote\n" +
            "- %LOCALAPPDATA%\\KoeNote\n" +
            "- %ProgramData%\\KoeNote\n\n" +
            "Documents\\KoeNote\\Exports は削除しません。\n" +
            "この操作は元に戻せません。KoeNote を開いている場合は先に終了してください。";

        return MessageBox.Show(
            this,
            message,
            "KoeNote 関連データの全削除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }
}
