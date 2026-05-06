using System.ComponentModel;
using System.Windows.Controls;
using KoeNote.App.ViewModels;

namespace KoeNote.App.Controls;

public partial class TranscriptSegmentList : UserControl
{
    public TranscriptSegmentList()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
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
}
