using System.ComponentModel;
using System.Windows;
using KoeNote.App.ViewModels;

namespace KoeNote.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }

    private void OnDropAudioFiles(object sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || !e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return;
        }

        viewModel.RegisterAudioFiles(files);
    }

    private void OnDragOverAudioFiles(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        var isRunning = DataContext is MainWindowViewModel { IsRunInProgress: true };
        var message = isRunning
            ? "処理が実行中です。KoeNote を終了すると、現在の処理は中断されます。終了しますか？"
            : "KoeNote を終了しますか？";
        var result = MessageBox.Show(
            this,
            message,
            "終了の確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        e.Cancel = result != MessageBoxResult.Yes;
    }
}
