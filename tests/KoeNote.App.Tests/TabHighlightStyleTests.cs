namespace KoeNote.App.Tests;

public sealed class TabHighlightStyleTests
{
    [Fact]
    public void CardTabItem_KeepsCompactSharedLayout()
    {
        var repoRoot = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Resources", "Tabs.xaml"));

        Assert.Contains("<Setter Property=\"Foreground\" Value=\"#9CA3AF\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Padding\" Value=\"14,6\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Margin\" Value=\"0,0,4,0\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MinWidth\" Value=\"54\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MinHeight\" Value=\"34\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Foreground\" Value=\"{StaticResource AccentBrush}\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"HighlightPulse\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ClipToBounds=\"False\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsTabs_UseSharedCardTabStyle()
    {
        var repoRoot = FindRepoRoot();
        var mainWindowXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "MainWindow.xaml"));
        var logPanelXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "LogPanel.xaml"));
        var settingsXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "SettingsTab.xaml"));
        var domainPresetXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "DomainPresetTab.xaml"));

        Assert.Contains("<TabControl Style=\"{StaticResource CardTabControl}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedIndex=\"{Binding SelectedDetailPanelTabIndex, Mode=TwoWay}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource CardTabControl}\" SelectedIndex=\"{Binding SelectedLogPanelTabIndex, Mode=TwoWay}\"", logPanelXaml, StringComparison.Ordinal);
        Assert.Contains("設定 / 辞書プリセット / モデル / セットアップ / ログ", logPanelXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ログ / 設定 / セットアップ / モデル", logPanelXaml, StringComparison.Ordinal);
        Assert.True(
            mainWindowXaml.IndexOf("Header=\"設定\"", StringComparison.Ordinal) <
            mainWindowXaml.IndexOf("Header=\"辞書プリセット\"", StringComparison.Ordinal));
        Assert.True(
            mainWindowXaml.IndexOf("Header=\"辞書プリセット\"", StringComparison.Ordinal) <
            mainWindowXaml.IndexOf("Header=\"モデル\"", StringComparison.Ordinal));
        Assert.True(
            mainWindowXaml.IndexOf("Header=\"モデル\"", StringComparison.Ordinal) <
            mainWindowXaml.IndexOf("Header=\"セットアップ\"", StringComparison.Ordinal));
        Assert.True(
            mainWindowXaml.IndexOf("Header=\"セットアップ\"", StringComparison.Ordinal) <
            mainWindowXaml.IndexOf("Header=\"ログ\"", StringComparison.Ordinal));
        Assert.Contains("<controls:DomainPresetTab />", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<controls:DomainPresetTab DataContext=\"{Binding DataContext, ElementName=Root}\" />", logPanelXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ImportDomainPresetCommand", settingsXaml, StringComparison.Ordinal);
        Assert.Contains("ImportDomainPresetCommand", domainPresetXaml, StringComparison.Ordinal);
        Assert.Contains("ApplyLoadedDomainPresetCommand", domainPresetXaml, StringComparison.Ordinal);
        Assert.Contains("ClearDomainPresetCommand", domainPresetXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("BorderThickness=\"0\"\r\n                                Background=\"Transparent\"\r\n                                SelectedIndex=\"{Binding SelectedDetailPanelTabIndex, Mode=TwoWay}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TabControl BorderThickness=\"0\" Background=\"Transparent\" SelectedIndex=\"{Binding SelectedLogPanelTabIndex, Mode=TwoWay}\"", logPanelXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsTab_HidesInternalTaskSettingsDetails()
    {
        var repoRoot = FindRepoRoot();
        var settingsXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "SettingsTab.xaml"));

        Assert.DoesNotContain("Text=\"現在のタスク設定\"", settingsXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding LlmReviewTaskSummary}\"", settingsXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding LlmSummaryTaskSummary}\"", settingsXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding LlmPolishingTaskSummary}\"", settingsXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Task settings\"", settingsXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupModelSettingsPanel_HidesProgressWhenNoSetupInstallProgressIsActive()
    {
        var repoRoot = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "SetupModelSettingsPanel.xaml"));

        Assert.Contains("Visibility=\"{Binding ShowSetupInstallProgress, Converter={StaticResource BooleanToVisibilityConverter}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ModelDownloadProgressSummary}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsIndeterminate=\"{Binding IsModelDownloadProgressIndeterminate}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void TranscriptPanel_UsesMeasuredLocalYellowPulseForPolishedTab()
    {
        var repoRoot = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "TranscriptPanel.xaml"));

        Assert.Contains("x:Key=\"TranscriptTabItem\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TranscriptCardTabControl\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource TranscriptCardTabControl}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemContainerStyle=\"{StaticResource TranscriptTabItem}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PolishedTabPulse\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"#FEF3C7\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"#FBBF24\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding IsPolishedTranscriptTabHighlighted}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("RepeatBehavior=\"3x\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutoReverse=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Padding\" Value=\"14,6\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MinWidth\" Value=\"72\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid ClipToBounds=\"False\" SnapsToDevicePixels=\"False\">", xaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"2,0,2,1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"10,0,10,0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid MinWidth=\"92\">", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"整文\" Style=\"{StaticResource TranscriptTabHeaderText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"素起こし\" Style=\"{StaticResource TranscriptTabHeaderText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"差分\" Style=\"{StaticResource TranscriptTabHeaderText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<TextBlock Text=\"レビュー候補\"", xaml, StringComparison.Ordinal);
        Assert.True(
            xaml.IndexOf("Text=\"整文\" Style=\"{StaticResource TranscriptTabHeaderText}\"", StringComparison.Ordinal) <
            xaml.IndexOf("Text=\"素起こし\" Style=\"{StaticResource TranscriptTabHeaderText}\"", StringComparison.Ordinal));
        Assert.True(
            xaml.IndexOf("Text=\"素起こし\" Style=\"{StaticResource TranscriptTabHeaderText}\"", StringComparison.Ordinal) <
            xaml.IndexOf("Text=\"差分\" Style=\"{StaticResource TranscriptTabHeaderText}\"", StringComparison.Ordinal));
        Assert.True(
            xaml.IndexOf("Text=\"差分\" Style=\"{StaticResource TranscriptTabHeaderText}\"", StringComparison.Ordinal) <
            xaml.IndexOf("<TextBlock Text=\"レビュー候補\"", StringComparison.Ordinal));
        Assert.Contains("Margin=\"4,-4,4,-4\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Opacity=\"0\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Setter Property=\"Padding\" Value=\"16,7\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Setter Property=\"Padding\" Value=\"16,7,20,7\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Setter Property=\"MinWidth\" Value=\"100\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth=\"42\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Grid MinWidth=\"70\">", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Grid MinWidth=\"78\">", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"0,-4,0,-4\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"2,-4,4,-4\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"-14,-4,-14,-4\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth=\"104\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth=\"86\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth=\"76\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadablePolishedPanel_RemovesSupplementalReviewRoutes()
    {
        var repoRoot = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "ReadablePolishedPanel.xaml"));

        Assert.DoesNotContain("CopyReadablePolishedContentCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowRawTranscriptTabCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDiffTranscriptTabCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowReviewCandidateTranscriptTabCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfirmSpeakerNamesAndRunReadablePolishingCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"原文\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"差分\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"レビュー候補\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"再整文\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadablePolishedPanel_UsesDocumentProofreadingSurface()
    {
        var repoRoot = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "ReadablePolishedPanel.xaml"));
        var code = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "ReadablePolishedPanel.xaml.cs"));
        var segmentListCode = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "TranscriptSegmentList.xaml.cs"));

        Assert.DoesNotContain("ScrollViewer x:Name=\"ReadableDocumentScrollViewer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("RichTextBox x:Name=\"ReadableDocumentRichTextBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsDocumentEnabled=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsTabStop=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FlowDocumentScrollViewer", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ReadableDocumentChrome\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MaxWidth\" Value=\"1080\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"CornerRadius\" Value=\"10\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<DataTrigger Binding=\"{Binding IsDetailLayout}\" Value=\"True\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MaxWidth\" Value=\"1600\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"BorderThickness\" Value=\"0\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"CornerRadius\" Value=\"0\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ReadableDocumentEmptyChrome\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FontFamily=\"Yu Gothic UI\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ReadableDocumentFontSize", xaml, StringComparison.Ordinal);
        Assert.Contains("ReadableDocumentLineHeight", xaml, StringComparison.Ordinal);
        Assert.Contains("ReadableDocumentStateTitle", xaml, StringComparison.Ordinal);
        Assert.Contains("ReadableDocumentStateDescription", xaml, StringComparison.Ordinal);
        Assert.Contains("viewModel.ReadableDocumentBlocks", code, StringComparison.Ordinal);
        Assert.Contains("BuildReadableDocument", code, StringComparison.Ordinal);
        Assert.Contains("BuildReadableBlockRow", code, StringComparison.Ordinal);
        Assert.Contains("BuildMetaPanel", code, StringComparison.Ordinal);
        Assert.Contains("new Table", code, StringComparison.Ordinal);
        Assert.Contains("ReadableMetaColumnWidth", code, StringComparison.Ordinal);
        Assert.Contains("UpdateReadableDocumentLayoutWidth", code, StringComparison.Ordinal);
        Assert.Contains("GetReadableBodyColumnWidth", code, StringComparison.Ordinal);
        Assert.Contains("document.PageWidth = contentWidth", code, StringComparison.Ordinal);
        Assert.Contains("document.ColumnWidth = contentWidth", code, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Left", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GridUnitType.Star", code, StringComparison.Ordinal);
        Assert.Contains("BuildBodyCell", code, StringComparison.Ordinal);
        Assert.Contains("ReadableBodyCellRightPadding", code, StringComparison.Ordinal);
        Assert.Contains("Padding = new Thickness(0, 0, ReadableBodyCellRightPadding, 26)", code, StringComparison.Ordinal);
        Assert.Contains("Padding = new Thickness(0, 0, ReadableBodyCellRightPadding, 20)", code, StringComparison.Ordinal);
        Assert.Contains("BlockUIContainer", code, StringComparison.Ordinal);
        Assert.Contains("_readableBodyParagraphs", code, StringComparison.Ordinal);
        Assert.Contains("new CommandBinding(ApplicationCommands.Copy", code, StringComparison.Ordinal);
        Assert.Contains("GetSelectedReadableBodyText", code, StringComparison.Ordinal);
        Assert.Contains("new TextRange(start, end).Text", code, StringComparison.Ordinal);
        Assert.DoesNotContain("OnBodyPreviewMouseWheel", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadableDocumentScrollViewer.ScrollToVerticalOffset", code, StringComparison.Ordinal);
        Assert.Contains("PagePadding = new Thickness(0)", code, StringComparison.Ordinal);
        Assert.Contains("FontWeight = FontWeights.Normal", code, StringComparison.Ordinal);
        Assert.Contains("LineHeight = viewModel.ReadableDocumentLineHeight", code, StringComparison.Ordinal);
        Assert.Contains("ReadableDocumentSearchText", code, StringComparison.Ordinal);
        Assert.Contains("AddHighlightedRuns", code, StringComparison.Ordinal);
        Assert.Contains("UpdateReadablePlaybackHighlight", code, StringComparison.Ordinal);
        Assert.Contains("ResolveReadablePlaybackBlockIndex", code, StringComparison.Ordinal);
        Assert.Contains("ApplyReadablePlaybackHighlight", code, StringComparison.Ordinal);
        Assert.Contains("viewModel.ReadableDocumentBlocks.Count != _readableMetaCells.Count", code, StringComparison.Ordinal);
        Assert.Contains("viewModel.ReadableDocumentBlocks.Count != _readableBodyCells.Count", code, StringComparison.Ordinal);
        Assert.Contains("ScrollReadableDocumentBlockIntoView", code, StringComparison.Ordinal);
        Assert.Contains("FindVisualChild<ScrollViewer>", code, StringComparison.Ordinal);
        Assert.Contains("ScrollToVerticalOffset", code, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.ContextIdle", segmentListCode, StringComparison.Ordinal);
        Assert.Contains("SegmentList.ItemContainerGenerator.ContainerFromItem(selectedItem)", segmentListCode, StringComparison.Ordinal);
        Assert.Contains("nameof(MainWindowViewModel.TranscriptAutoScrollRequestId)", code, StringComparison.Ordinal);
        Assert.Contains("nameof(MainWindowViewModel.SelectedSegment)", code, StringComparison.Ordinal);
        Assert.Contains("FindReadableBlockIndex", code, StringComparison.Ordinal);
        Assert.Contains("Color.FromRgb(0xEC, 0xFD, 0xF3)", code, StringComparison.Ordinal);
        Assert.Contains("new Thickness(3, 0, 0, 1)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding ReadablePolishedContent, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadableDocumentStateDescription_UsesRunningStatusWhenPolishing()
    {
        var repoRoot = FindRepoRoot();
        var viewModel = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "ViewModels", "MainWindowViewModel.cs"));

        Assert.Contains("public string ReadableDocumentStateDescription => IsReadablePolishingInProgress || HasReadablePolishedContent", viewModel, StringComparison.Ordinal);
        Assert.Contains("? ReadablePolishedStatus", viewModel, StringComparison.Ordinal);
        Assert.Contains(": ReadablePolishedContentDisplay;", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void MainScreen_UsesMockupAlignedHeaderAndRightPaneCtas()
    {
        var repoRoot = FindRepoRoot();
        var mainWindowXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "MainWindow.xaml"));
        var mainWindowCode = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "MainWindow.xaml.cs"));
        var headerXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "HeaderToolbar.xaml"));
        var headerStyles = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "HeaderToolbar.Styles.xaml"));
        var controlsXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Resources", "Controls.xaml"));
        var transcriptXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "TranscriptPanel.xaml"));
        var reviewXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "ReviewPanel.xaml"));
        var reviewDraftEmptyStateXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "ReviewDraftEmptyState.xaml"));
        var reviewDraftActionBarXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "ReviewDraftActionBar.xaml"));
        var audioPlayerXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "TranscriptAudioPlayer.xaml"));
        var listsXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Resources", "Lists.xaml"));
        var stageXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "StageProgressPanel.xaml"));
        var statusXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "StatusBarPanel.xaml"));
        var standardJobRailXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "StandardJobRailPanel.xaml"));
        var standardAiRailXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "StandardAiAssistRailPanel.xaml"));

        Assert.Contains("Margin=\"0\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"1\" />", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"VisibleWhenStandardLayout\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"VisibleWhenDetailLayout\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"StandardRailButton\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StandardWorkspaceGrid\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource VisibleWhenStandardLayout}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource VisibleWhenDetailLayout}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("StandardLayoutTitle", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("StandardLayoutMeta", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("StandardLayoutJobBadgeText", standardJobRailXaml, StringComparison.Ordinal);
        Assert.Contains("StandardLayoutAiBadgeText", standardAiRailXaml, StringComparison.Ordinal);
        Assert.Contains("AI アシスト", standardAiRailXaml, StringComparison.Ordinal);
        Assert.Contains("<controls:ReadablePolishedPanel Style=\"{StaticResource VisibleWhenStandardReadableTranscript}\" />", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"JobListColumn\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TranscriptColumn\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ReviewColumn\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Equal(2, AllIndexesOf(mainWindowXaml, "DragCompleted=\"OnWorkspaceSplitterDragCompleted\"").Count());
        Assert.Contains("BindingOperations.SetBinding", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinition.WidthProperty", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"CornerRadius\" Value=\"0\" />", controlsXaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"0\"", audioPlayerXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsSource=\"{Binding PlaybackRateOptions}\"", audioPlayerXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DisplayMemberPath=\"DisplayText\"", audioPlayerXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedValuePath=\"Value\"", audioPlayerXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedValue=\"{Binding PlaybackRate, Mode=TwoWay}\"", audioPlayerXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemStringFormat=\"{}{0:0.##}x\"", audioPlayerXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"PlayerPrimaryButton\"", audioPlayerXaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Width\" Value=\"44\" />", audioPlayerXaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"22\"", audioPlayerXaml, StringComparison.Ordinal);
        Assert.Contains("DropShadowEffect Color=\"#16A34A\"", audioPlayerXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"PlayerSlimPrimaryButton\"", audioPlayerXaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Width\" Value=\"40\" />", audioPlayerXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource PlayerSlimPrimaryButton}\"", audioPlayerXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"PlayerTimeText\"", audioPlayerXaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"132\" />", audioPlayerXaml, StringComparison.Ordinal);
        Assert.Contains("<Slider Width=\"52\"", audioPlayerXaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"56\"", audioPlayerXaml, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(audioPlayerXaml, "<controls:AudioWaveformControl Grid.Column=\"2\""));
        Assert.Equal(2, CountOccurrences(audioPlayerXaml, "Text=\"{Binding PlaybackVolumeIcon}\""));
        Assert.Contains("Text=\"KoeNote\"", headerXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"実行\"", headerXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("整文まで実行", headerXaml, StringComparison.Ordinal);
        Assert.Contains("ContextualExportMenuHeader", headerXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding CurrentExportTargetDisplayName}\"", headerXaml, StringComparison.Ordinal);
        Assert.Contains("ContextualExportMenuToolTip", headerXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource HeaderExportButton}\"", headerXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"書き出し形式\"", headerXaml, StringComparison.Ordinal);
        Assert.Contains("VisibleWhenReadableExportMenuItem", headerXaml, StringComparison.Ordinal);
        Assert.Contains("VisibleWhenRawExportMenuItem", headerXaml, StringComparison.Ordinal);
        Assert.Contains("VisibleWhenDiffExportMenuItem", headerXaml, StringComparison.Ordinal);
        Assert.Contains("VisibleWhenReviewCandidateExportMenuItem", headerXaml, StringComparison.Ordinal);
        Assert.Contains("VisibleWhenSummaryExportMenuItem", headerXaml, StringComparison.Ordinal);
        Assert.Contains("VisibleWhenSummaryExportSeparator", headerXaml, StringComparison.Ordinal);
        Assert.Contains("VisibleWhenCurrentExportTargetCopyMenuItem", headerXaml, StringComparison.Ordinal);
        Assert.Contains("VisibleWhenCurrentExportTargetToggleMenuItem", headerXaml, StringComparison.Ordinal);
        Assert.Contains("CopyCurrentExportTargetCommand", headerXaml, StringComparison.Ordinal);
        Assert.Contains("ExportTextIcon", headerXaml, StringComparison.Ordinal);
        Assert.Contains("ExportMarkdownIcon", headerXaml, StringComparison.Ordinal);
        Assert.Contains("ExportWordIcon", headerXaml, StringComparison.Ordinal);
        Assert.Contains("ExportCaptionIcon", headerXaml, StringComparison.Ordinal);
        Assert.Contains("ExportCopyIcon", headerXaml, StringComparison.Ordinal);
        Assert.Contains("HeaderExportImageIcon", headerXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"HeaderExportImageIcon\"", headerStyles, StringComparison.Ordinal);
        Assert.Contains("ExportRawJsonCommand", headerXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"字幕 SRT\" Tag=\".srt\"", headerXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"字幕 VTT\" Tag=\".vtt\"", headerXaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"差分ビューはエクスポート対象外です\" IsEnabled=\"False\"", headerXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<MenuItem Header=\"整文\" Style=\"{StaticResource VisibleWhenReadableExportMenuItem}\">", headerXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<MenuItem Header=\"素起こし\" Style=\"{StaticResource VisibleWhenRawExportMenuItem}\">", headerXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<MenuItem Header=\"レビュー候補\" Style=\"{StaticResource VisibleWhenReviewCandidateExportMenuItem}\">", headerXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<MenuItem Header=\"要約\" Style=\"{StaticResource VisibleWhenSummaryExportMenuItem}\">", headerXaml, StringComparison.Ordinal);
        Assert.True(
            headerXaml.IndexOf("Style=\"{StaticResource VisibleWhenReadableExportMenuItem}\"", StringComparison.Ordinal) <
            headerXaml.IndexOf("Style=\"{StaticResource VisibleWhenRawExportMenuItem}\"", StringComparison.Ordinal));
        Assert.True(
            headerXaml.IndexOf("Command=\"{Binding ExportReadablePolishedDocxCommand}\"", StringComparison.Ordinal) <
            headerXaml.IndexOf("Command=\"{Binding ExportReadablePolishedXlsxCommand}\"", StringComparison.Ordinal));
        Assert.Contains("x:Key=\"HeaderExportFormatMenuItem\"", headerStyles, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"HeaderExportToggleMenuItem\"", headerStyles, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"HeaderExportSectionLabel\"", headerStyles, StringComparison.Ordinal);
        Assert.Contains("HeaderModelBadgeButton", headerXaml, StringComparison.Ordinal);
        Assert.Contains("HeaderToggleTrack", headerXaml, StringComparison.Ordinal);
        Assert.Contains("HeaderToggleThumb", headerXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"HeaderModelBadgeButton\"", headerStyles, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"HeaderToggleTrack\"", headerStyles, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"HeaderToggleThumb\"", headerStyles, StringComparison.Ordinal);

        Assert.DoesNotContain("ShowReadableTranscriptTabCommand", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("<controls:ReviewDraftEmptyState />", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("<controls:ReviewDraftActionBar DockPanel.Dock=\"Bottom\"", reviewXaml, StringComparison.Ordinal);
        Assert.True(
            reviewXaml.IndexOf("<controls:ReviewDraftActionBar DockPanel.Dock=\"Bottom\"", StringComparison.Ordinal) <
            reviewXaml.IndexOf("<ScrollViewer VerticalScrollBarVisibility=\"Auto\">", StringComparison.Ordinal));
        Assert.Contains("整文候補はありません", reviewDraftEmptyStateXaml, StringComparison.Ordinal);
        Assert.Contains("「整文」タブで結果を確認", reviewDraftEmptyStateXaml, StringComparison.Ordinal);
        Assert.Contains("この候補への操作", reviewDraftActionBarXaml, StringComparison.Ordinal);
        Assert.Contains("レビュー候補を反映して、次の候補へ進みます", reviewDraftActionBarXaml, StringComparison.Ordinal);
        Assert.Contains("RunPostSummaryCommand", reviewXaml, StringComparison.Ordinal);
        Assert.Single(AllIndexesOf(reviewXaml, "Command=\"{Binding RunPostSummaryCommand}\""));
        Assert.Contains("AI アシスト", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("InspectorSectionCard", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("InspectorTabPill", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("表記ゆれ", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("DetailInspectorTargetText", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("DetailInspectorCurrentTabText", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("DetailInspectorSegmentText", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("Binding CanRunSelectedJob", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding RunPreflightSummary}\"", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("InspectorMetaLabel", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("TranscriptInlineToggle", transcriptXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"ビュー\"", transcriptXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"文字起こし\"", transcriptXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"話者:\"", transcriptXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"テキストを検索\"", transcriptXaml, StringComparison.Ordinal);
        Assert.Contains("TranscriptToggleTrack", transcriptXaml, StringComparison.Ordinal);
        Assert.Contains("IsTranscriptAutoScrollEnabled", transcriptXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<CheckBox Grid.Column=\"1\"", transcriptXaml, StringComparison.Ordinal);
        Assert.Contains("<Setter TargetName=\"RowChrome\" Property=\"Background\" Value=\"#ECFDF3\" />", listsXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DropShadowEffect", listsXaml, StringComparison.Ordinal);

        Assert.Contains("x:Key=\"StageBadgeTextStyle\"", stageXaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"処理中\"", stageXaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"完了\"", stageXaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding IsGpuUsageUnknown}\"", statusXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void TranscriptSegmentList_PlacesEditingRoutesInRawTranscriptMode()
    {
        var repoRoot = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "TranscriptSegmentList.xaml"));
        var code = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "TranscriptSegmentList.xaml.cs"));

        Assert.Contains("原文確認・修正", xaml, StringComparison.Ordinal);
        Assert.Contains("<TextBlock Text=\"再整文\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"#FFFFFF\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("この原文から再整文", xaml, StringComparison.Ordinal);
        Assert.Contains("レビュー候補を再生成", xaml, StringComparison.Ordinal);
        Assert.Contains("BeginSegmentInlineEditCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("OnSpeakerMouseLeftButtonDown", xaml, StringComparison.Ordinal);
        Assert.Contains("SpeakerChipBrushConverter", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"{Binding Speaker, Converter={StaticResource SpeakerChipBrushConverter}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ConverterParameter=Foreground", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource VisibleWhenRawTranscriptMode}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("!string.Equals(DisplayMode, \"Raw\", StringComparison.Ordinal)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("!string.Equals(DisplayMode, \"Polished\", StringComparison.Ordinal)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ZoomControls_TargetTranscriptContentFontsOnly()
    {
        var repoRoot = FindRepoRoot();
        var mainWindowXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "MainWindow.xaml"));
        var headerXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "HeaderToolbar.xaml"));
        var transcriptXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "TranscriptPanel.xaml"));
        var readableXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "ReadablePolishedPanel.xaml"));
        var segmentsXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "TranscriptSegmentList.xaml"));

        Assert.DoesNotContain("ScaleTransform ScaleX=\"{Binding MainContentZoomScale}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ZoomOutCommand", headerXaml, StringComparison.Ordinal);
        Assert.Contains("TranscriptZoomButton", transcriptXaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ZoomOutCommand}\"", transcriptXaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ResetZoomCommand}\"", transcriptXaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ZoomInCommand}\"", transcriptXaml, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"{Binding ReadableDocumentFontSize}\"", readableXaml, StringComparison.Ordinal);
        Assert.Contains("LineHeight=\"{Binding ReadableDocumentLineHeight}\"", readableXaml, StringComparison.Ordinal);
        Assert.Contains("DataContext.TranscriptBodyFontSize", segmentsXaml, StringComparison.Ordinal);
        Assert.Contains("DataContext.TranscriptBodyLineHeight", segmentsXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void TranscriptPanel_ColorsSpeakerFilterChips()
    {
        var repoRoot = FindRepoRoot();
        var appXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "App.xaml"));
        var transcriptXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "TranscriptPanel.xaml"));

        Assert.Contains("SpeakerChipBrushConverter x:Key=\"SpeakerChipBrushConverter\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("ItemTemplate", transcriptXaml, StringComparison.Ordinal);
        Assert.Contains("SelectionBoxItem", transcriptXaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"{Binding Converter={StaticResource SpeakerChipBrushConverter}}\"", transcriptXaml, StringComparison.Ordinal);
        Assert.Contains("ConverterParameter=Border", transcriptXaml, StringComparison.Ordinal);
        Assert.Contains("ConverterParameter=Foreground", transcriptXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchBars_UseSharedPillSizeAndBlueLensIcon()
    {
        var repoRoot = FindRepoRoot();
        var inputsXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Resources", "Inputs.xaml"));
        var jobXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "JobListPanel.xaml"));
        var transcriptXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "TranscriptPanel.xaml"));

        Assert.Contains("x:Key=\"SearchPillTextBox\"", inputsXaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Height\" Value=\"28\" />", inputsXaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Margin\" Value=\"32,0,32,0\" />", inputsXaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Padding\" Value=\"0\" />", inputsXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource SearchPillTextBox}\"", jobXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource SearchPillTextBox}\"", transcriptXaml, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"200\"", jobXaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", jobXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Width=\"200\"\r\n                  Height=\"28\"", jobXaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"14\"", jobXaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"14\"", transcriptXaml, StringComparison.Ordinal);
        Assert.Contains("PreviewMouseLeftButtonDown=\"OnSegmentSearchBoxPreviewMouseLeftButtonDown\"", transcriptXaml, StringComparison.Ordinal);
        Assert.Contains("Stroke=\"#2563EB\"", jobXaml, StringComparison.Ordinal);
        Assert.Contains("Stroke=\"#2563EB\"", transcriptXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"ジョブを検索\"", jobXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"テキストを検索\"", transcriptXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Style=\"{StaticResource PillSearchTextBox}\"", transcriptXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void JobDropZone_UsesFolderDropTargetCopy()
    {
        var repoRoot = FindRepoRoot();
        var jobXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "JobListPanel.xaml"));

        Assert.Contains("<Border Background=\"#FFFFFF\" Padding=\"12,14\">", jobXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Style=\"{StaticResource PanelBorder}\"", jobXaml, StringComparison.Ordinal);
        Assert.True(CountOccurrences(jobXaml, "BorderBrush=\"Transparent\"") >= 4);
        Assert.True(CountOccurrences(jobXaml, "BorderThickness=\"0\"") >= 4);
        Assert.Contains("StrokeDashArray=\"4 4\"", jobXaml, StringComparison.Ordinal);
        Assert.Contains("Fill=\"#FBBF24\"", jobXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"ファイルをドロップ\"", jobXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"またはクリックして選択\"", jobXaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{StaticResource AccentBrush}\"", jobXaml, StringComparison.Ordinal);
        Assert.Contains("TextDecorations=\"Underline\"", jobXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ファイルをドラッグ &amp; ドロップ", jobXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SummaryEmptyState_FillsSummaryCardAndCentersIcon()
    {
        var repoRoot = FindRepoRoot();
        var reviewXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "ReviewPanel.xaml"));

        Assert.Contains("<Grid Grid.Row=\"1\"", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource VisibleWhenNoSummaryStyle}\"", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("<Border Background=\"#FFFFFF\"", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("Padding=\"18\"", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("<Grid Width=\"34\"", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("ScaleTransform ScaleX=\"1.18\" ScaleY=\"1.18\"", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("Fill=\"#E9E2F4\"", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("Fill=\"#F97316\"", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("Fill=\"#7C3AED\"", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Center\"", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Center\"", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"要約はまだありません\"", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"「生成」を押すとAIが要約を作成します\"", reviewXaml, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KoeNote.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate KoeNote repository root.");
    }

    private static IEnumerable<int> AllIndexesOf(string value, string pattern)
    {
        var index = 0;
        while ((index = value.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            yield return index;
            index += pattern.Length;
        }
    }

    private static int CountOccurrences(string value, string pattern)
    {
        return AllIndexesOf(value, pattern).Count();
    }
}
