using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KoeNote.App.Models;
using KoeNote.App.ViewModels;

namespace KoeNote.App.Controls;

public partial class TranscriptSegmentList : UserControl
{
    public static readonly DependencyProperty DisplayModeProperty = DependencyProperty.Register(
        nameof(DisplayMode),
        typeof(string),
        typeof(TranscriptSegmentList),
        new PropertyMetadata("Polished"));

    private bool _suppressNextInlineAutoSave;

    public TranscriptSegmentList()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    public string DisplayMode
    {
        get => (string)GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldViewModel)
        {
            oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is INotifyPropertyChanged newViewModel)
        {
            newViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is INotifyPropertyChanged viewModel)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ReviewSegmentFocusRequestId))
        {
            Dispatcher.BeginInvoke(ScrollSelectedSegmentIntoView);
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.TranscriptAutoScrollRequestId))
        {
            Dispatcher.BeginInvoke(ScrollSelectedSegmentIntoView);
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.IsSegmentInlineEditActive))
        {
            if (DataContext is MainWindowViewModel { IsSegmentInlineEditActive: false })
            {
                _suppressNextInlineAutoSave = false;
            }

            Dispatcher.BeginInvoke(FocusInlineSegmentEditor);
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.IsSpeakerInlineEditActive))
        {
            Dispatcher.BeginInvoke(FocusInlineSpeakerEditor);
        }
    }

    private void OnInlineSegmentEditorLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_suppressNextInlineAutoSave ||
            IsWithinTaggedElement(e.NewFocus as DependencyObject, "InlineSegmentRevertButton"))
        {
            return;
        }

        SaveInlineSegmentEdit();
    }

    private void OnInlineSegmentRevertPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _suppressNextInlineAutoSave = DataContext is MainWindowViewModel { IsSegmentInlineEditActive: true };
    }

    private void OnInlineSpeakerEditorLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        SaveInlineSpeakerEdit();
    }

    private void OnSegmentTextMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2 || !string.Equals(DisplayMode, "Raw", StringComparison.Ordinal))
        {
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not FrameworkElement { DataContext: TranscriptSegmentPreview segment })
        {
            return;
        }

        if (viewModel.BeginSegmentInlineEditCommand.CanExecute(segment))
        {
            viewModel.BeginSegmentInlineEditCommand.Execute(segment);
            e.Handled = true;
        }
    }

    private void OnSpeakerMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!string.Equals(DisplayMode, "Raw", StringComparison.Ordinal))
        {
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not FrameworkElement { DataContext: TranscriptSegmentPreview segment })
        {
            return;
        }

        if (viewModel.BeginSpeakerInlineEditCommand.CanExecute(segment))
        {
            viewModel.BeginSpeakerInlineEditCommand.Execute(segment);
            e.Handled = true;
        }
    }

    private void ScrollSelectedSegmentIntoView()
    {
        if (SegmentList.SelectedItem is null)
        {
            return;
        }

        SegmentList.UpdateLayout();
        SegmentList.ScrollIntoView(SegmentList.SelectedItem);
    }

    private void FocusInlineSegmentEditor()
    {
        if (DataContext is not MainWindowViewModel { IsSegmentInlineEditActive: true } ||
            SegmentList.SelectedItem is null)
        {
            return;
        }

        SegmentList.UpdateLayout();
        SegmentList.ScrollIntoView(SegmentList.SelectedItem);
        SegmentList.UpdateLayout();
        if (FindVisualChild<TextBox>(SegmentList, textBox => textBox.IsVisible && Equals(textBox.Tag, "InlineSegmentEditor")) is { } editor)
        {
            editor.Focus();
            editor.SelectAll();
        }
    }

    private void FocusInlineSpeakerEditor()
    {
        if (DataContext is not MainWindowViewModel { IsSpeakerInlineEditActive: true } ||
            SegmentList.SelectedItem is null)
        {
            return;
        }

        SegmentList.UpdateLayout();
        SegmentList.ScrollIntoView(SegmentList.SelectedItem);
        SegmentList.UpdateLayout();
        if (FindVisualChild<TextBox>(SegmentList, textBox => textBox.IsVisible && Equals(textBox.Tag, "InlineSpeakerEditor")) is { } editor)
        {
            editor.Focus();
            editor.SelectAll();
        }
    }

    private void SaveInlineSegmentEdit()
    {
        if (DataContext is not MainWindowViewModel { IsSegmentInlineEditActive: true } viewModel)
        {
            return;
        }

        if (viewModel.SaveSegmentInlineEditCommand.CanExecute(null))
        {
            viewModel.SaveSegmentInlineEditCommand.Execute(null);
        }
    }

    private void SaveInlineSpeakerEdit()
    {
        if (DataContext is not MainWindowViewModel { IsSpeakerInlineEditActive: true } viewModel)
        {
            return;
        }

        if (viewModel.SaveSpeakerInlineEditCommand.CanExecute(null))
        {
            viewModel.SaveSpeakerInlineEditCommand.Execute(null);
        }
    }

    private static bool IsWithinTaggedElement(DependencyObject? current, object tag)
    {
        while (current is not null)
        {
            if (current is FrameworkElement element && Equals(element.Tag, tag))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static T? FindVisualChild<T>(DependencyObject parent, Predicate<T> predicate)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild && predicate(typedChild))
            {
                return typedChild;
            }

            var descendant = FindVisualChild(child, predicate);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
