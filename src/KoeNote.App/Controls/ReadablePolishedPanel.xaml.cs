using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KoeNote.App.Dialogs;
using KoeNote.App.Models;
using KoeNote.App.Services.Clipboard;
using KoeNote.App.ViewModels;

namespace KoeNote.App.Controls;

public partial class ReadablePolishedPanel : UserControl
{
    private const double ReadableMetaColumnWidth = 122;
    private const double ReadableDocumentScrollbarReserve = 24;

    private INotifyPropertyChanged? _subscribedViewModel;
    private readonly List<FrameworkContentElement> _readableBlockAnchors = [];
    private readonly List<TableCell> _readableMetaCells = [];
    private readonly List<TableCell> _readableBodyCells = [];
    private readonly List<Paragraph> _readableBodyParagraphs = [];
    private readonly List<TextBox> _readableBodyEditors = [];
    private FrameworkContentElement? _firstSearchMatchAnchor;
    private int _activeReadablePlaybackBlockIndex = -1;
    private double _lastReadableDocumentWidth;
    private bool _isBuildingReadableDocument;

    public ReadablePolishedPanel()
    {
        InitializeComponent();
        ReadableDocumentRichTextBox.ContextMenu = BuildReadOnlyTextContextMenu(ReadableDocumentRichTextBox);
        ReadableDocumentRichTextBox.CommandBindings.Add(
            new CommandBinding(ApplicationCommands.Copy, OnCopyReadableDocumentSelection, CanCopyReadableDocumentSelection));
        ReadableDocumentRichTextBox.SizeChanged += OnReadableDocumentRichTextBoxSizeChanged;
        SpellCheck.SetIsEnabled(ReadableDocumentRichTextBox, false);
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
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateReadablePlaybackHighlight();
                ScrollReadableDocumentToPlaybackPosition();
            }));
        }
        else if (e.PropertyName is nameof(MainWindowViewModel.SelectedSegment) or
                 nameof(MainWindowViewModel.SelectedTranscriptTabIndex) or
                 nameof(MainWindowViewModel.IsStandardReadableTranscriptVisible) or
                 nameof(MainWindowViewModel.IsTranscriptAutoScrollEnabled))
        {
            Dispatcher.BeginInvoke(new Action(UpdateReadablePlaybackHighlight));
        }

        if (e.PropertyName is nameof(MainWindowViewModel.ReadablePolishedContent) or
            nameof(MainWindowViewModel.HasReadableDocumentBlocks) or
            nameof(MainWindowViewModel.ReadableDocumentFontSize) or
            nameof(MainWindowViewModel.ReadableDocumentLineHeight) or
            nameof(MainWindowViewModel.IsRunInProgress) or
            nameof(MainWindowViewModel.ReadableDocumentSearchText) or
            nameof(MainWindowViewModel.IsReadableDocumentEditMode))
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
             viewModel.SelectedTranscriptTabIndex != 0) ||
            (viewModel.IsReadableDocumentEditMode &&
             _readableBodyEditors.Any(static editor => editor.IsKeyboardFocusWithin)))
        {
            return;
        }

        var blockIndex = ReadablePolishedPanelRenderingHelper.FindReadableBlockIndex(
            viewModel.ReadableDocumentBlocks,
            segment.StartSeconds);
        if (blockIndex < 0)
        {
            return;
        }

        if (blockIndex < _readableBodyParagraphs.Count)
        {
            Dispatcher.BeginInvoke(
                new Action(() => ScrollReadableDocumentBlockIntoView(blockIndex)),
                DispatcherPriority.ContextIdle);
        }
        else if (blockIndex < _readableBlockAnchors.Count)
        {
            Dispatcher.BeginInvoke(
                new Action(_readableBlockAnchors[blockIndex].BringIntoView),
                DispatcherPriority.ContextIdle);
        }
    }

    private void ScrollReadableDocumentBlockIntoView(int blockIndex)
    {
        if (blockIndex < 0 || blockIndex >= _readableBodyParagraphs.Count)
        {
            return;
        }

        ReadableDocumentRichTextBox.UpdateLayout();
        var paragraph = _readableBodyParagraphs[blockIndex];
        var rect = paragraph.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        var scroller = FindVisualChild<ScrollViewer>(ReadableDocumentRichTextBox);
        if (scroller is null ||
            rect.IsEmpty ||
            double.IsNaN(rect.Top) ||
            double.IsInfinity(rect.Top))
        {
            paragraph.BringIntoView();
            return;
        }

        var contextOffset = Math.Min(80, Math.Max(24, scroller.ViewportHeight * 0.2));
        var targetOffset = Math.Max(0, scroller.VerticalOffset + rect.Top - contextOffset);
        scroller.ScrollToVerticalOffset(targetOffset);
    }

    private void RebuildReadableDocument()
    {
        _readableBlockAnchors.Clear();
        _readableMetaCells.Clear();
        _readableBodyCells.Clear();
        _readableBodyParagraphs.Clear();
        _readableBodyEditors.Clear();
        _firstSearchMatchAnchor = null;
        _activeReadablePlaybackBlockIndex = -1;
        ReadableDocumentRichTextBox.Document = new FlowDocument();

        if (DataContext is not MainWindowViewModel viewModel || !viewModel.HasReadableDocumentBlocks)
        {
            return;
        }

        var textBrush = new SolidColorBrush(Color.FromRgb(0x17, 0x1C, 0x27));
        var mutedBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x6C, 0x7B));
        var separatorBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xE9, 0xEE));
        var searchText = viewModel.ReadableDocumentSearchText;
        FlowDocument document;
        try
        {
            _isBuildingReadableDocument = true;
            document = BuildReadableDocument(viewModel, textBrush, mutedBrush, separatorBrush, searchText);
        }
        finally
        {
            _isBuildingReadableDocument = false;
        }

        ReadableDocumentRichTextBox.Document = document;
        UpdateReadableDocumentLayoutWidth();

        if (_firstSearchMatchAnchor is { } firstSearchMatch)
        {
            Dispatcher.BeginInvoke(new Action(firstSearchMatch.BringIntoView));
        }

        UpdateReadablePlaybackHighlight();
    }

    private FlowDocument BuildReadableDocument(
        MainWindowViewModel viewModel,
        Brush textBrush,
        Brush mutedBrush,
        Brush separatorBrush,
        string searchText)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Yu Gothic UI"),
            FontSize = viewModel.ReadableDocumentFontSize,
            FontWeight = FontWeights.Normal,
            LineHeight = viewModel.ReadableDocumentLineHeight,
            Foreground = textBrush,
            TextAlignment = TextAlignment.Left
        };
        var table = new Table
        {
            CellSpacing = 0
        };
        table.Columns.Add(new TableColumn { Width = new GridLength(ReadableMetaColumnWidth) });
        table.Columns.Add(new TableColumn { Width = new GridLength(GetReadableBodyColumnWidth()) });
        var rowGroup = new TableRowGroup();
        table.RowGroups.Add(rowGroup);
        document.Blocks.Add(table);

        for (var index = 0; index < viewModel.ReadableDocumentBlocks.Count; index++)
        {
            rowGroup.Rows.Add(
                BuildReadableBlockRow(index, viewModel.ReadableDocumentBlocks[index], viewModel, textBrush, mutedBrush, separatorBrush, searchText));
        }

        return document;
    }

    private TableRow BuildReadableBlockRow(
        int blockIndex,
        ReadableDocumentBlock block,
        MainWindowViewModel viewModel,
        Brush textBrush,
        Brush mutedBrush,
        Brush separatorBrush,
        string searchText)
    {
        var row = new TableRow();
        var metaCell = BuildMetaCell(block, viewModel, mutedBrush, separatorBrush);
        var bodyCell = BuildBodyCell(blockIndex, block, viewModel, textBrush, separatorBrush, searchText);
        row.Cells.Add(metaCell);
        row.Cells.Add(bodyCell);

        _readableBlockAnchors.Add(row);
        _readableMetaCells.Add(metaCell);
        _readableBodyCells.Add(bodyCell);
        if (_firstSearchMatchAnchor is null && ReadablePolishedPanelRenderingHelper.ContainsSearchText(block.Text, searchText))
        {
            _firstSearchMatchAnchor = row;
        }

        return row;
    }

    private TableCell BuildMetaCell(
        ReadableDocumentBlock block,
        MainWindowViewModel viewModel,
        Brush mutedBrush,
        Brush separatorBrush)
    {
        return new TableCell(new BlockUIContainer(BuildMetaPanel(
            block,
            viewModel,
            mutedBrush,
            ReadablePolishedPanelRenderingHelper.GetSpeakerPalette(block.Speaker))))
        {
            Padding = new Thickness(0, 3, 20, 26),
            BorderBrush = separatorBrush,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
    }

    private TableCell BuildBodyCell(
        int blockIndex,
        ReadableDocumentBlock block,
        MainWindowViewModel viewModel,
        Brush textBrush,
        Brush separatorBrush,
        string searchText)
    {
        if (viewModel.IsReadableDocumentEditMode)
        {
            return BuildEditableBodyCell(blockIndex, block, viewModel, textBrush, separatorBrush);
        }

        var paragraph = new Paragraph
        {
            Margin = new Thickness(0),
            LineHeight = viewModel.ReadableDocumentLineHeight,
            Foreground = textBrush
        };
        AddHighlightedRuns(paragraph.Inlines, block.Text, searchText);
        _readableBodyParagraphs.Add(paragraph);

        return new TableCell(paragraph)
        {
            Padding = new Thickness(0, 0, 0, 26),
            BorderBrush = separatorBrush,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
    }

    private TableCell BuildEditableBodyCell(
        int blockIndex,
        ReadableDocumentBlock block,
        MainWindowViewModel viewModel,
        Brush textBrush,
        Brush separatorBrush)
    {
        var editor = new TextBox
        {
            Text = block.Text,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xBF, 0xDB, 0xFE)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xFD, 0xFF)),
            Foreground = textBrush,
            FontFamily = new FontFamily("Yu Gothic UI"),
            FontSize = viewModel.ReadableDocumentFontSize,
            Padding = new Thickness(8, 6, 8, 6),
            MinHeight = Math.Max(42, viewModel.ReadableDocumentLineHeight * 2),
            ToolTip = "本文を編集"
        };
        editor.Tag = blockIndex;
        editor.TextChanged += OnReadableBodyEditorTextChanged;
        _readableBodyEditors.Add(editor);

        return new TableCell(new BlockUIContainer(editor))
        {
            Padding = new Thickness(0, 0, 0, 20),
            BorderBrush = separatorBrush,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
    }

    private StackPanel BuildMetaPanel(
        ReadableDocumentBlock block,
        MainWindowViewModel viewModel,
        Brush mutedBrush,
        ReadableSpeakerPalette speakerPalette)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(0, 3, 20, 0)
        };

        if (block.HasSpeaker)
        {
            var speakerBorder = new Border
            {
                Background = speakerPalette.Background,
                CornerRadius = new CornerRadius(6),
                Cursor = viewModel.IsRunInProgress ? Cursors.Arrow : Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 3, 8, 3),
                ToolTip = viewModel.IsRunInProgress ? "実行中は話者名を変更できません" : "話者名を変更",
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
            if (!viewModel.IsRunInProgress)
            {
                speakerBorder.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    OpenSpeakerRenameDialog(block, viewModel);
                };
            }

            panel.Children.Add(speakerBorder);
        }

        if (block.HasTimeRange)
        {
            panel.Children.Add(new TextBlock
            {
                Text = block.TimeRange,
                Margin = block.HasSpeaker ? new Thickness(0, 4, 0, 0) : new Thickness(0),
                FontFamily = new FontFamily("Yu Gothic UI"),
                FontSize = 11,
                LineHeight = 16,
                Foreground = mutedBrush
            });
        }

        return panel;
    }

    private void OpenSpeakerRenameDialog(ReadableDocumentBlock block, MainWindowViewModel viewModel)
    {
        if (!block.HasSpeaker)
        {
            return;
        }

        var dialog = new ReadableSpeakerRenameDialog(block.Speaker)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        viewModel.RenameReadableDocumentSpeaker(block.Speaker, dialog.SpeakerName);
    }

    private void OnReadableBodyEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isBuildingReadableDocument)
        {
            return;
        }

        if (sender is TextBox { Tag: int blockIndex } editor &&
            DataContext is MainWindowViewModel viewModel)
        {
            viewModel.UpdateReadableDocumentEditedText(blockIndex, editor.Text);
        }
    }

    private void OnReadableDocumentRichTextBoxSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.NewSize.Width - _lastReadableDocumentWidth) < 1)
        {
            return;
        }

        _lastReadableDocumentWidth = e.NewSize.Width;
        UpdateReadableDocumentLayoutWidth();
    }

    private void UpdateReadableDocumentLayoutWidth()
    {
        var contentWidth = GetReadableDocumentContentWidth();
        var document = ReadableDocumentRichTextBox.Document;
        document.PageWidth = contentWidth;
        document.ColumnWidth = contentWidth;

        foreach (var block in document.Blocks)
        {
            if (block is not Table table || table.Columns.Count < 2)
            {
                continue;
            }

            table.Columns[0].Width = new GridLength(ReadableMetaColumnWidth);
            table.Columns[1].Width = new GridLength(GetReadableBodyColumnWidth(contentWidth));
            return;
        }
    }

    private double GetReadableBodyColumnWidth() =>
        GetReadableBodyColumnWidth(GetReadableDocumentContentWidth());

    private static double GetReadableBodyColumnWidth(double contentWidth) =>
        Math.Max(320, contentWidth - ReadableMetaColumnWidth);

    private double GetReadableDocumentContentWidth()
    {
        var width = ReadableDocumentRichTextBox.ActualWidth;
        if (double.IsNaN(width) || width <= 0)
        {
            width = ReadableDocumentRichTextBox.RenderSize.Width;
        }

        if (double.IsNaN(width) || width <= 0)
        {
            width = 900;
        }

        return Math.Max(ReadableMetaColumnWidth + 320, width - ReadableDocumentScrollbarReserve);
    }

    private static ContextMenu BuildReadOnlyTextContextMenu(RichTextBox body)
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem
        {
            Header = "コピー",
            Command = ApplicationCommands.Copy,
            CommandTarget = body
        });
        return menu;
    }

    private void CanCopyReadableDocumentSelection(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = !ReadableDocumentRichTextBox.Selection.IsEmpty &&
                       !string.IsNullOrEmpty(GetSelectedReadableBodyText());
        e.Handled = true;
    }

    private void OnCopyReadableDocumentSelection(object sender, ExecutedRoutedEventArgs e)
    {
        var text = GetSelectedReadableBodyText();
        if (!string.IsNullOrEmpty(text))
        {
            var result = ClipboardHelper.TrySetText(text);
            if (!result.IsSucceeded && DataContext is MainWindowViewModel viewModel)
            {
                viewModel.ReportClipboardCopyFailure("整文の選択範囲", result);
            }
        }

        e.Handled = true;
    }

    private string GetSelectedReadableBodyText()
    {
        var selection = ReadableDocumentRichTextBox.Selection;
        if (selection.IsEmpty)
        {
            return string.Empty;
        }

        var selectedBlocks = new List<string>();
        foreach (var paragraph in _readableBodyParagraphs)
        {
            if (selection.Start.CompareTo(paragraph.ContentEnd) >= 0 ||
                selection.End.CompareTo(paragraph.ContentStart) <= 0)
            {
                continue;
            }

            var start = MaxTextPointer(selection.Start, paragraph.ContentStart);
            var end = MinTextPointer(selection.End, paragraph.ContentEnd);
            var text = new TextRange(start, end).Text.TrimEnd('\r', '\n');
            if (!string.IsNullOrEmpty(text))
            {
                selectedBlocks.Add(text);
            }
        }

        return string.Join(Environment.NewLine, selectedBlocks);
    }

    private void UpdateReadablePlaybackHighlight()
    {
        if (DataContext is MainWindowViewModel viewModel &&
            (viewModel.ReadableDocumentBlocks.Count != _readableMetaCells.Count ||
             viewModel.ReadableDocumentBlocks.Count != _readableBodyCells.Count))
        {
            return;
        }

        var activeIndex = ResolveReadablePlaybackBlockIndex();
        if (activeIndex == _activeReadablePlaybackBlockIndex)
        {
            return;
        }

        ApplyReadablePlaybackHighlight(_activeReadablePlaybackBlockIndex, isActive: false);
        ApplyReadablePlaybackHighlight(activeIndex, isActive: true);
        _activeReadablePlaybackBlockIndex = activeIndex;
    }

    private int ResolveReadablePlaybackBlockIndex()
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
            return -1;
        }

        return ReadablePolishedPanelRenderingHelper.FindReadableBlockIndex(
            viewModel.ReadableDocumentBlocks,
            segment.StartSeconds);
    }

    private void ApplyReadablePlaybackHighlight(int blockIndex, bool isActive)
    {
        if (blockIndex < 0 ||
            blockIndex >= _readableMetaCells.Count ||
            blockIndex >= _readableBodyCells.Count)
        {
            return;
        }

        var background = isActive
            ? new SolidColorBrush(Color.FromRgb(0xEC, 0xFD, 0xF3))
            : Brushes.Transparent;
        var borderBrush = isActive
            ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E))
            : new SolidColorBrush(Color.FromRgb(0xE6, 0xE9, 0xEE));
        var metaCell = _readableMetaCells[blockIndex];
        var bodyCell = _readableBodyCells[blockIndex];

        metaCell.Background = background;
        bodyCell.Background = background;
        metaCell.BorderBrush = borderBrush;
        bodyCell.BorderBrush = borderBrush;
        metaCell.BorderThickness = isActive
            ? new Thickness(3, 0, 0, 1)
            : new Thickness(0, 0, 0, 1);
        bodyCell.BorderThickness = new Thickness(0, 0, 0, 1);
    }

    private static TextPointer MaxTextPointer(TextPointer first, TextPointer second) =>
        first.CompareTo(second) >= 0 ? first : second;

    private static TextPointer MinTextPointer(TextPointer first, TextPointer second) =>
        first.CompareTo(second) <= 0 ? first : second;

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static void AddHighlightedRuns(InlineCollection inlines, string text, string searchText)
    {
        foreach (var segment in ReadablePolishedPanelRenderingHelper.BuildHighlightedSegments(text, searchText))
        {
            var run = new Run(segment.Text);
            if (segment.IsHighlighted)
            {
                run.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7));
                run.FontWeight = FontWeights.SemiBold;
            }

            inlines.Add(run);
        }
    }
}
