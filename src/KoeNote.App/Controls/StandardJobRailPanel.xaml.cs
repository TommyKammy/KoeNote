using System.Windows.Controls;
using System.Windows.Input;
using KoeNote.App.ViewModels;

namespace KoeNote.App.Controls;

public partial class StandardJobRailPanel : UserControl
{
    public StandardJobRailPanel()
    {
        InitializeComponent();
    }

    private void OnJobRailImportDropZoneClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            !viewModel.AddAudioCommand.CanExecute(null))
        {
            return;
        }

        viewModel.AddAudioCommand.Execute(null);
        e.Handled = true;
    }

    private void OnJobRailSearchBoxPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        JobRailSearchTextBox.Focus();
    }
}
