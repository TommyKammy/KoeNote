using System.Windows;

namespace KoeNote.App.Dialogs;

public partial class ExitConfirmationDialog : Window
{
    public ExitConfirmationDialog(bool isRunInProgress)
    {
        InitializeComponent();
        DataContext = new ExitConfirmationDialogViewModel(isRunInProgress);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private sealed record ExitConfirmationDialogViewModel(
        string Message,
        string IconGlyph,
        string IconBackground,
        string IconForeground)
    {
        public ExitConfirmationDialogViewModel(bool isRunInProgress)
            : this(
                isRunInProgress
                    ? "処理が実行中です。終了すると現在の処理は中断されます。"
                    : "開いている作業を閉じて、アプリを終了します。",
                isRunInProgress ? "\uE7BA" : "\uE8BB",
                isRunInProgress ? "#FEF3C7" : "#ECFDF5",
                isRunInProgress ? "#B45309" : "#15803D")
        {
        }
    }
}
