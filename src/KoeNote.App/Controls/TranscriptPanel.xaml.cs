using System.Windows.Controls;
using System.ComponentModel;
using KoeNote.App.ViewModels;

namespace KoeNote.App.Controls;

public partial class TranscriptPanel : UserControl
{
    public TranscriptPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ReviewSegmentFocusRequestId))
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
