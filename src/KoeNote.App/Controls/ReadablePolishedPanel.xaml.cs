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
    private Paragraph? _firstSearchMatchAnchor;

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
            nameof(MainWindowViewModel.ReadableDocumentLineHeight) or
            nameof(MainWindowViewModel.ReadableDocumentSearchText))
        {
            Dispatcher.BeginInvoke(RebuildReadableDocument);
        }
    }

    private void ScrollReadableDocumentToPlaybackPosition()
    {
        if (DataContext is not MainWindowViewModel
            {
                IsTranscriptAutoScrollEnabled: true,
                SelectedSegment: { } segment,
                HasReadableDocumentBlocks: true
            } viewModel ||
            (!viewModel.IsStandardReadableTranscriptVisible &&
             viewModel.SelectedTranscriptTabIndex != 0))
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
        _firstSearchMatchAnchor = null;

        if (DataContext is not MainWindowViewModel viewModel || !viewModel.HasReadableDocumentBlocks)
        {
            ReadableDocumentViewer.Document = null;
            return;
        }

        var textBrush = new SolidColorBrush(Color.FromRgb(0x17, 0x1C, 0x27));
        var mutedBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x6C, 0x7B));
        var separatorBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xE9, 0xEE));

        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            ColumnWidth = 10000,
            FontFamily = new FontFamily("Yu Gothic UI"),
            FontSize = viewModel.ReadableDocumentFontSize,
            LineHeight = viewModel.ReadableDocumentLineHeight,
            Foreground = textBrush
        };
        var searchText = viewModel.ReadableDocumentSearchText;

        var table = new Table
        {
            CellSpacing = 0
        };
        table.Columns.Add(new TableColumn { Width = new GridLength(122) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

        var rowGroup = new TableRowGroup();
        foreach (var block in viewModel.ReadableDocumentBlocks)
        {
            var speakerPalette = GetSpeakerPalette(block.Speaker);
            var row = new TableRow();
            row.Cells.Add(BuildMetaCell(block, mutedBrush, speakerPalette));
            row.Cells.Add(BuildBodyCell(block, viewModel, textBrush, separatorBrush, searchText));
            rowGroup.Rows.Add(row);
        }

        table.RowGroups.Add(rowGroup);
        document.Blocks.Add(table);
        ReadableDocumentViewer.Document = document;
        if (_firstSearchMatchAnchor is { } firstSearchMatch)
        {
            Dispatcher.BeginInvoke(new Action(firstSearchMatch.BringIntoView));
        }
    }

    private TableCell BuildMetaCell(
        ReadableDocumentBlock block,
        Brush mutedBrush,
        SpeakerPalette speakerPalette)
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
                Background = speakerPalette.Background,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3),
                Child = new TextBlock
                {
                    Text = block.Speaker,
                    FontFamily = new FontFamily("Yu Gothic UI"),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = speakerPalette.Foreground,
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

    private static SpeakerPalette GetSpeakerPalette(string speaker)
    {
        var palettes = new[]
        {
            new SpeakerPalette(Color.FromRgb(0xEA, 0xF1, 0xFA), Color.FromRgb(0x2F, 0x58, 0x96)),
            new SpeakerPalette(Color.FromRgb(0xF2, 0xEC, 0xFB), Color.FromRgb(0x5F, 0x44, 0xA0)),
            new SpeakerPalette(Color.FromRgb(0xFB, 0xF1, 0xE7), Color.FromRgb(0x95, 0x57, 0x1F)),
            new SpeakerPalette(Color.FromRgb(0xED, 0xF0, 0xF3), Color.FromRgb(0x5B, 0x64, 0x72))
        };

        if (string.IsNullOrWhiteSpace(speaker))
        {
            return palettes[^1];
        }

        var hash = 0;
        foreach (var character in speaker)
        {
            hash = unchecked((hash * 31) + character);
        }

        return palettes[(hash & int.MaxValue) % palettes.Length];
    }

    private sealed record SpeakerPalette(Color BackgroundColor, Color ForegroundColor)
    {
        public Brush Background { get; } = new SolidColorBrush(BackgroundColor);

        public Brush Foreground { get; } = new SolidColorBrush(ForegroundColor);
    }

    private TableCell BuildBodyCell(
        ReadableDocumentBlock block,
        MainWindowViewModel viewModel,
        Brush textBrush,
        Brush separatorBrush,
        string searchText)
    {
        var cell = new TableCell();
        var body = new Paragraph
        {
            Margin = new Thickness(0),
            FontFamily = new FontFamily("Yu Gothic UI"),
            FontSize = viewModel.ReadableDocumentFontSize,
            LineHeight = viewModel.ReadableDocumentLineHeight,
            Foreground = textBrush
        };
        AddHighlightedRuns(body, block.Text, searchText);
        _readableBlockAnchors.Add(body);
        if (_firstSearchMatchAnchor is null && ContainsSearchText(block.Text, searchText))
        {
            _firstSearchMatchAnchor = body;
        }

        cell.Blocks.Add(body);
        cell.Blocks.Add(new BlockUIContainer(new Border
        {
            Height = 1,
            Background = separatorBrush,
            Margin = new Thickness(0, 20, 0, 26)
        }));
        return cell;
    }

    private static bool ContainsSearchText(string text, string searchText) =>
        !string.IsNullOrWhiteSpace(searchText) &&
        text.Contains(searchText, StringComparison.OrdinalIgnoreCase);

    private static void AddHighlightedRuns(Paragraph paragraph, string text, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            paragraph.Inlines.Add(new Run(text));
            return;
        }

        var startIndex = 0;
        while (startIndex < text.Length)
        {
            var matchIndex = text.IndexOf(searchText, startIndex, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
            {
                paragraph.Inlines.Add(new Run(text[startIndex..]));
                return;
            }

            if (matchIndex > startIndex)
            {
                paragraph.Inlines.Add(new Run(text[startIndex..matchIndex]));
            }

            paragraph.Inlines.Add(new Run(text.Substring(matchIndex, searchText.Length))
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7)),
                FontWeight = FontWeights.SemiBold
            });
            startIndex = matchIndex + searchText.Length;
        }
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
