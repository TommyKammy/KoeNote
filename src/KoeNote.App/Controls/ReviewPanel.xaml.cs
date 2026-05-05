using System.Windows.Controls;
using System.Windows.Input;
using KoeNote.App.ViewModels;

namespace KoeNote.App.Controls;

public partial class ReviewPanel : UserControl
{
    public ReviewPanel()
    {
        InitializeComponent();
    }

    private void FocusDraftSegmentOnClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            !viewModel.FocusSelectedDraftSegmentCommand.CanExecute(null))
        {
            return;
        }

        viewModel.FocusSelectedDraftSegmentCommand.Execute(null);
    }
}
