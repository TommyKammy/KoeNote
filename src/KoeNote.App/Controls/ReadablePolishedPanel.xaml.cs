using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using KoeNote.App.Models;
using KoeNote.App.ViewModels;

namespace KoeNote.App.Controls;

public partial class ReadablePolishedPanel : UserControl
{
    private INotifyPropertyChanged? _subscribedViewModel;
    private readonly List<Paragraph> _readableBlockAnchors = [];

    public ReadablePolishedPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SubscribeToViewModel(DataContext as INotifyPropertyChanged);
        RebuildReadableDocument();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UnsubscribeFromViewModel(e.OldValue as INotifyPropertyChanged);
        SubscribeToViewModel(e.NewValue as INotifyPropertyChanged);
        RebuildReadableDocument();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnsubscribeFromViewModel(DataContext as INotifyPropertyChanged);
    }

    private void SubscribeToViewModel(INotifyPropertyChanged? viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
        {
            return;
        }

        UnsubscribeFromViewModel(_subscribedViewModel);
        _subscribedViewModel = viewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void UnsubscribeFromViewModel(INotifyPropertyChanged? viewModel)
    {
        if (_subscribedViewModel is null ||
            (viewModel is not null && !ReferenceEquals(_subscribedViewModel, viewModel)))
        {
            return;
        }

        _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribedViewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.TranscriptAutoScrollRequestId))
        {
            Dispatcher.BeginInvoke(ScrollReadableDocumentToPlaybackPosition);
        }

        if (e.PropertyName is nameof(MainWindowViewModel.ReadablePolishedContent) or
            nameof(MainWindowViewModel.HasReadableDocumentBlocks) or
            nameof(MainWindowViewModel.ReadableDocumentFontSize) or
            nameof(MainWindowViewModel.ReadableDocumentLineHeight))
        {
            Dispatcher.BeginInvoke(RebuildReadableDocument);
        }
    }

    private void ScrollReadableDocumentToPlaybackPosition()
    {
        if (DataContext is not MainWindowViewModel
            {
                IsTranscriptAutoScrollEnabled: true,
                SelectedTranscriptTabIndex: 0,
                SelectedSegment: { } segment,
                HasReadableDocumentBlocks: true
            } viewModel)
        {
            return;
        }

        var blockIndex = FindReadableBlockIndex(viewModel.ReadableDocumentBlocks, segment.StartSeconds);
        if (blockIndex < 0)
        {
            return;
        }

        if (blockIndex < _readableBlockAnchors.Count)
        {
            _readableBlockAnchors[blockIndex].BringIntoView();
        }
    }

    private void RebuildReadableDocument()
    {
        _readableBlockAnchors.Clear();

        if (DataContext is not MainWindowViewModel viewModel || !viewModel.HasReadableDocumentBlocks)
        {
            ReadableDocumentViewer.Document = null;
            return;
        }

        var textBrush = ResolveBrush("TextBrush", Brushes.Black);
        var mutedBrush = ResolveBrush("TextBrushMuted", Brushes.Gray);
        var separatorBrush = new SolidColorBrush(Color.FromRgb(0xF1, 0xEF, 0xE8));
        var speakerBackgroundBrush = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xFF));
        var speakerForegroundBrush = new SolidColorBrush(Color.FromRgb(0x37, 0x30, 0xA3));

        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            ColumnWidth = 760,
            FontFamily = new FontFamily("Yu Gothic UI"),
            FontSize = viewModel.ReadableDocumentFontSize,
            LineHeight = viewModel.ReadableDocumentLineHeight,
            Foreground = textBrush
        };

        var table = new Table
        {
            CellSpacing = 0
        };
        table.Columns.Add(new TableColumn { Width = new GridLength(122) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

        var rowGroup = new TableRowGroup();
        foreach (var block in viewModel.ReadableDocumentBlocks)
        {
            var row = new TableRow();
            row.Cells.Add(BuildMetaCell(block, mutedBrush, speakerBackgroundBrush, speakerForegroundBrush));
            row.Cells.Add(BuildBodyCell(block, viewModel, textBrush, separatorBrush));
            rowGroup.Rows.Add(row);
        }

        table.RowGroups.Add(rowGroup);
        document.Blocks.Add(table);
        ReadableDocumentViewer.Document = document;
    }

    private TableCell BuildMetaCell(
        ReadableDocumentBlock block,
        Brush mutedBrush,
        Brush speakerBackgroundBrush,
        Brush speakerForegroundBrush)
    {
        var cell = new TableCell
        {
            Padding = new Thickness(0, 3, 20, 0)
        };
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0),
            FontSize = 11,
            LineHeight = 16,
            Foreground = mutedBrush
        };

        if (block.HasSpeaker)
        {
            var speakerBorder = new Border
            {
                Background = speakerBackgroundBrush,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3),
                Child = new TextBlock
                {
                    Text = block.Speaker,
                    FontFamily = new FontFamily("Yu Gothic UI"),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = speakerForegroundBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 92
                }
            };
            paragraph.Inlines.Add(new InlineUIContainer(speakerBorder));
        }

        if (block.HasTimeRange)
        {
            if (paragraph.Inlines.Count > 0)
            {
                paragraph.Inlines.Add(new LineBreak());
            }

            paragraph.Inlines.Add(new Run(block.TimeRange)
            {
                Foreground = mutedBrush,
                FontSize = 11
            });
        }

        cell.Blocks.Add(paragraph);
        return cell;
    }

    private TableCell BuildBodyCell(
        ReadableDocumentBlock block,
        MainWindowViewModel viewModel,
        Brush textBrush,
        Brush separatorBrush)
    {
        var cell = new TableCell();
        var body = new Paragraph(new Run(block.Text))
        {
            Margin = new Thickness(0),
            FontFamily = new FontFamily("Yu Gothic UI"),
            FontSize = viewModel.ReadableDocumentFontSize,
            LineHeight = viewModel.ReadableDocumentLineHeight,
            Foreground = textBrush
        };
        _readableBlockAnchors.Add(body);

        cell.Blocks.Add(body);
        cell.Blocks.Add(new BlockUIContainer(new Border
        {
            Height = 1,
            Background = separatorBrush,
            Margin = new Thickness(0, 20, 0, 26)
        }));
        return cell;
    }

    private Brush ResolveBrush(string resourceKey, Brush fallback)
    {
        return TryFindResource(resourceKey) as Brush ?? fallback;
    }

    private static int FindReadableBlockIndex(IReadOnlyList<ReadableDocumentBlock> blocks, double positionSeconds)
    {
        var fallbackIndex = -1;
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            if (block.StartSeconds is not { } startSeconds)
            {
                continue;
            }

            var endSeconds = block.EndSeconds.GetValueOrDefault(double.MaxValue);
            if (positionSeconds >= startSeconds && positionSeconds < endSeconds)
            {
                return index;
            }

            if (positionSeconds >= startSeconds)
            {
                fallbackIndex = index;
            }
        }

        return fallbackIndex;
    }
}
