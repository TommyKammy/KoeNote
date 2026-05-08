using System.ComponentModel;
using System.Windows;
using KoeNote.App.Dialogs;
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
        var dialog = new ExitConfirmationDialog(isRunning)
        {
            Owner = this
        };

        e.Cancel = dialog.ShowDialog() != true;
    }
}
