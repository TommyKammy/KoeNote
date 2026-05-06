using System.Windows.Controls;
using System.Windows;

namespace KoeNote.App.Controls;

public partial class HeaderToolbar : UserControl
{
    public HeaderToolbar()
    {
        InitializeComponent();
    }

    private void OnExportMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null)
        {
            return;
        }

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }
}
