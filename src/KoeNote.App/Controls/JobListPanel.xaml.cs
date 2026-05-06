using System.Windows.Controls;
using System.Windows.Input;
using KoeNote.App.ViewModels;

namespace KoeNote.App.Controls;

public partial class JobListPanel : UserControl
{
    public JobListPanel()
    {
        InitializeComponent();
    }

    private void OnImportDropZoneClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            !viewModel.AddAudioCommand.CanExecute(null))
        {
            return;
        }

        viewModel.AddAudioCommand.Execute(null);
        e.Handled = true;
    }
}
