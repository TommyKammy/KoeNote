using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KoeNote.App.ViewModels;

namespace KoeNote.App.Controls;

public partial class ReviewPanel : UserControl
{
    public static readonly DependencyProperty ShowCloseButtonProperty =
        DependencyProperty.Register(
            nameof(ShowCloseButton),
            typeof(bool),
            typeof(ReviewPanel),
            new PropertyMetadata(true));

    public ReviewPanel()
    {
        InitializeComponent();
    }

    public bool ShowCloseButton
    {
        get => (bool)GetValue(ShowCloseButtonProperty);
        set => SetValue(ShowCloseButtonProperty, value);
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
