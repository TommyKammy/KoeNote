using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KoeNote.App.Controls;

public partial class TranscriptPanel : UserControl
{
    public TranscriptPanel()
    {
        InitializeComponent();
    }

    private void OnSegmentSearchBoxPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DependencyObject source)
        {
            return;
        }

        var searchTextBox = FindDescendant<TextBox>(source);
        if (searchTextBox is null ||
            !string.IsNullOrEmpty(searchTextBox.Text))
        {
            return;
        }

        searchTextBox.Focus();
        searchTextBox.CaretIndex = 0;
        e.Handled = true;
    }

    private void OnSegmentSearchTextBoxPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        FocusEmptySearchBoxAtStart(sender, e);
    }

    private void OnSegmentSearchBoxGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { Text.Length: 0 } textBox)
        {
            textBox.CaretIndex = 0;
        }
    }

    private static void FocusEmptySearchBoxAtStart(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox { Text.Length: 0 } textBox)
        {
            return;
        }

        textBox.Focus();
        textBox.CaretIndex = 0;
        e.Handled = true;
    }

    private static T? FindDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
