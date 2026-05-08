using System.Windows;
using KoeNote.App.Services.Dialogs;

namespace KoeNote.App.Dialogs;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog(ConfirmationDialogRequest request)
    {
        InitializeComponent();
        DataContext = request;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
