using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using KoeNote.App.Services.Dialogs;
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
        e.Cancel = !ConfirmationDialogService.Default.Confirm(
            this,
            ConfirmationDialogRequest.Exit(isRunning));
    }

    private void OnWorkspaceSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.JobListColumnWidth = JobListColumn.Width;
        viewModel.TranscriptColumnWidth = TranscriptColumn.Width;
        viewModel.ReviewColumnWidth = ReviewColumn.Width;
        RebindWorkspaceColumnWidth(JobListColumn, nameof(MainWindowViewModel.JobListColumnWidth), viewModel);
        RebindWorkspaceColumnWidth(TranscriptColumn, nameof(MainWindowViewModel.TranscriptColumnWidth), viewModel);
        RebindWorkspaceColumnWidth(ReviewColumn, nameof(MainWindowViewModel.ReviewColumnWidth), viewModel);
    }

    private static void RebindWorkspaceColumnWidth(
        ColumnDefinition column,
        string propertyName,
        MainWindowViewModel viewModel)
    {
        BindingOperations.SetBinding(
            column,
            ColumnDefinition.WidthProperty,
            new Binding(propertyName)
            {
                Source = viewModel,
                Mode = BindingMode.TwoWay
            });
    }
}
