namespace KoeNote.App.Tests;

public sealed class StandardLayoutShellTests
{
    [Fact]
    public void StandardShellSupplementalTranscriptRoutes_OpenDetailLayoutTabs()
    {
        var repoRoot = FindRepoRoot();
        var readablePanelXaml = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "Controls",
            "ReadablePolishedPanel.xaml"));
        var transcriptViewModel = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "ViewModels",
            "MainWindowViewModel.Transcript.cs"));

        Assert.Contains("ShowRawTranscriptTabCommand", readablePanelXaml, StringComparison.Ordinal);
        Assert.Contains("ShowDiffTranscriptTabCommand", readablePanelXaml, StringComparison.Ordinal);
        Assert.Contains("ShowReviewCandidateTranscriptTabCommand", readablePanelXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"原文\"", readablePanelXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"差分\"", readablePanelXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"レビュー候補\"", readablePanelXaml, StringComparison.Ordinal);
        Assert.Contains("tabIndex != ReadableTranscriptTabIndex && IsStandardLayout", transcriptViewModel, StringComparison.Ordinal);
        Assert.Contains("MainLayoutMode = MainLayoutMode.Detail;", transcriptViewModel, StringComparison.Ordinal);
        Assert.True(
            transcriptViewModel.IndexOf("MainLayoutMode = MainLayoutMode.Detail;", StringComparison.Ordinal) <
            transcriptViewModel.IndexOf("SelectedTranscriptTabIndex = tabIndex;", StringComparison.Ordinal));
    }

    [Fact]
    public void StandardLayoutMeta_IsBackedBySegmentsAndRefreshedAfterReplacement()
    {
        var repoRoot = FindRepoRoot();
        var layoutViewModel = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "ViewModels",
            "MainWindowViewModel.Layout.cs"));
        var runnerViewModel = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "ViewModels",
            "MainWindowViewModel.Runner.cs"));

        Assert.Contains("Segments.Count}セグメント", layoutViewModel, StringComparison.Ordinal);
        Assert.Contains("private void ReplaceSegments(IReadOnlyList<TranscriptSegment> segments)", runnerViewModel, StringComparison.Ordinal);
        Assert.Contains("NotifyStandardLayoutShellChanged();", runnerViewModel, StringComparison.Ordinal);
        Assert.True(
            runnerViewModel.IndexOf("FilteredSegments.Refresh();", StringComparison.Ordinal) <
            runnerViewModel.IndexOf("NotifyStandardLayoutShellChanged();", StringComparison.Ordinal));
    }

    [Fact]
    public void StandardLayout_UsesCollapsibleJobRailAndKeepsDetailJobList()
    {
        var repoRoot = FindRepoRoot();
        var mainWindowXaml = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "MainWindow.xaml"));
        var railXaml = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "Controls",
            "StandardJobRailPanel.xaml"));
        var railCode = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "Controls",
            "StandardJobRailPanel.xaml.cs"));
        var aiRailXaml = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "Controls",
            "StandardAiAssistRailPanel.xaml"));
        var reviewPanelXaml = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "Controls",
            "ReviewPanel.xaml"));
        var readablePanelXaml = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "Controls",
            "ReadablePolishedPanel.xaml"));
        var transcriptSegmentListXaml = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "Controls",
            "TranscriptSegmentList.xaml"));
        var transcriptSegmentListCode = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "Controls",
            "TranscriptSegmentList.xaml.cs"));
        var transcriptAudioPlayerXaml = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "Controls",
            "TranscriptAudioPlayer.xaml"));
        var transcriptAudioPlayerCode = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "Controls",
            "TranscriptAudioPlayer.xaml.cs"));
        var reviewPanelCode = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "Controls",
            "ReviewPanel.xaml.cs"));
        var layoutViewModel = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "ViewModels",
            "MainWindowViewModel.Layout.cs"));
        var commandsViewModel = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "ViewModels",
            "MainWindowViewModel.Commands.cs"));
        var mainViewModel = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "ViewModels",
            "MainWindowViewModel.cs"));
        var jobsViewModel = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "ViewModels",
            "MainWindowViewModel.Jobs.cs"));
        var transcriptViewModel = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "ViewModels",
            "MainWindowViewModel.Transcript.cs"));
        var postProcessViewModel = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "ViewModels",
            "MainWindowViewModel.PostProcess.cs"));
        var exportViewModel = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "KoeNote.App",
            "ViewModels",
            "MainWindowViewModel.Export.cs"));
        var layoutManualChecks = File.ReadAllText(Path.Combine(
            repoRoot,
            "docs",
            "development",
            "standard-detail-layout-manual-checks.md"));

        Assert.Contains(
            "Width=\"{Binding DataContext.StandardJobRailColumnWidth, ElementName=StandardWorkspaceGrid}\"",
            mainWindowXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "MinWidth=\"{Binding DataContext.StandardJobRailColumnMinWidth, ElementName=StandardWorkspaceGrid}\"",
            mainWindowXaml,
            StringComparison.Ordinal);
        Assert.Contains("<controls:StandardJobRailPanel Grid.RowSpan=\"3\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<controls:JobListPanel Grid.RowSpan=\"2\" Grid.Column=\"0\" />", mainWindowXaml, StringComparison.Ordinal);
        Assert.True(
            mainWindowXaml.IndexOf("<controls:StandardJobRailPanel Grid.RowSpan=\"3\"", StringComparison.Ordinal) <
            mainWindowXaml.IndexOf("Style=\"{StaticResource VisibleWhenDetailLayout}\"", StringComparison.Ordinal));
        Assert.Contains("Content=\"原文\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"整文\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsStandardRawTranscriptViewSelected", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsStandardReadableTranscriptViewSelected", mainWindowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("BasedOn=\"{StaticResource {x:Type RadioButton}}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource VisibleWhenStandardReadableTranscript}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource VisibleWhenStandardRawTranscript}\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("ShowCompactToolbar=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("SegmentSearchText", transcriptSegmentListXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedSpeakerFilter", transcriptSegmentListXaml, StringComparison.Ordinal);
        Assert.Contains("IsTranscriptAutoScrollEnabled", transcriptSegmentListXaml, StringComparison.Ordinal);
        Assert.Contains("public bool ShowCompactToolbar", transcriptSegmentListCode, StringComparison.Ordinal);
        Assert.Contains("ReadablePolishedPanel", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"920\"", readablePanelXaml, StringComparison.Ordinal);
        Assert.Contains("ReadableDocumentFontSize", readablePanelXaml, StringComparison.Ordinal);
        Assert.Contains("ReadableDocumentLineHeight", readablePanelXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding RawTranscriptText}\"", transcriptSegmentListXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource VisibleWhenPolishedTranscriptMode}\"", transcriptSegmentListXaml, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"64\" MinHeight=\"56\" />", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsSlim=\"True\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("VisibleWhenSlimPlayer", transcriptAudioPlayerXaml, StringComparison.Ordinal);
        Assert.Contains("VisibleWhenFullPlayer", transcriptAudioPlayerXaml, StringComparison.Ordinal);
        Assert.Contains("public bool IsSlim", transcriptAudioPlayerCode, StringComparison.Ordinal);

        Assert.Contains("IsStandardJobRailExpanded", railXaml, StringComparison.Ordinal);
        Assert.Contains("ToggleStandardJobRailCommand", railXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("BasedOn=\"{StaticResource StandardRailButton}\"", railXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding FilteredJobs}\"", railXaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedJob, Mode=TwoWay}\"", railXaml, StringComparison.Ordinal);
        Assert.Contains("RailInitial", railXaml, StringComparison.Ordinal);
        Assert.Contains("AddAudioCommand", railXaml, StringComparison.Ordinal);
        Assert.Contains("ClearAllJobsCommand", railXaml, StringComparison.Ordinal);
        Assert.Contains("UseDetailLayoutCommand", railXaml, StringComparison.Ordinal);
        Assert.Contains("JobSearchText", railXaml, StringComparison.Ordinal);
        Assert.Contains("AllowDrop=\"True\"", railXaml, StringComparison.Ordinal);
        Assert.Contains("UnreviewedDrafts", railXaml, StringComparison.Ordinal);
        Assert.Contains("ProgressPercent", railXaml, StringComparison.Ordinal);
        Assert.Contains("OnJobRailImportDropZoneClick", railCode, StringComparison.Ordinal);

        Assert.Contains(
            "Width=\"{Binding DataContext.StandardAiRailColumnWidth, ElementName=StandardWorkspaceGrid}\"",
            mainWindowXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "MinWidth=\"{Binding DataContext.StandardAiRailColumnMinWidth, ElementName=StandardWorkspaceGrid}\"",
            mainWindowXaml,
            StringComparison.Ordinal);
        Assert.Contains("<controls:StandardAiAssistRailPanel Grid.RowSpan=\"3\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("IsStandardAiRailExpanded", aiRailXaml, StringComparison.Ordinal);
        Assert.Contains("ToggleStandardAiRailCommand", aiRailXaml, StringComparison.Ordinal);
        Assert.Contains("StandardLayoutAiBadgeText", aiRailXaml, StringComparison.Ordinal);
        Assert.Contains("StandardLayoutAiAssistText", aiRailXaml, StringComparison.Ordinal);
        Assert.Contains("StandardLayoutAiReviewStatusText", aiRailXaml, StringComparison.Ordinal);
        Assert.Contains("StandardLayoutAiSummaryStatusText", aiRailXaml, StringComparison.Ordinal);
        Assert.Contains("HasStandardLayoutAiWarning", aiRailXaml, StringComparison.Ordinal);
        Assert.Contains("IsSummaryStale", aiRailXaml, StringComparison.Ordinal);
        Assert.Contains("<controls:ReviewPanel ShowCloseButton=\"False\" />", aiRailXaml, StringComparison.Ordinal);
        Assert.Contains("RunPostSummaryCommand", reviewPanelXaml, StringComparison.Ordinal);
        Assert.Contains("ReviewDraftActionBar", reviewPanelXaml, StringComparison.Ordinal);
        Assert.Contains("RememberCorrection", reviewPanelXaml, StringComparison.Ordinal);
        Assert.Contains("public bool ShowCloseButton", reviewPanelCode, StringComparison.Ordinal);
        Assert.Contains("IsReviewCandidateExportMenuVisible", exportViewModel, StringComparison.Ordinal);
        Assert.Contains("IsSummaryExportMenuVisible", exportViewModel, StringComparison.Ordinal);
        Assert.Contains("TranscriptExportSource.Polished", exportViewModel, StringComparison.Ordinal);
        Assert.Contains("整文ビュー", layoutManualChecks, StringComparison.Ordinal);
        Assert.Contains("本文幅", layoutManualChecks, StringComparison.Ordinal);
        Assert.Contains("メタ情報", layoutManualChecks, StringComparison.Ordinal);

        Assert.Contains("public bool IsStandardJobRailExpanded", layoutViewModel, StringComparison.Ordinal);
        Assert.Contains("public GridLength StandardJobRailColumnWidth", layoutViewModel, StringComparison.Ordinal);
        Assert.Contains("private Task ToggleStandardJobRailAsync()", layoutViewModel, StringComparison.Ordinal);
        Assert.Contains("_uiPreferencesService.SaveMainLayoutMode(_mainLayoutMode);", layoutViewModel, StringComparison.Ordinal);
        Assert.Contains("var uiPreferences = _uiPreferencesService.Load();", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("_mainLayoutMode = UiPreferencesService.NormalizeMainLayoutMode(uiPreferences.MainLayoutMode);", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("ToggleStandardJobRailCommand = new RelayCommand(ToggleStandardJobRailAsync);", commandsViewModel, StringComparison.Ordinal);
        Assert.Contains("public ICommand ToggleStandardJobRailCommand", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("public bool IsStandardAiRailExpanded", layoutViewModel, StringComparison.Ordinal);
        Assert.Contains("public GridLength StandardAiRailColumnWidth", layoutViewModel, StringComparison.Ordinal);
        Assert.Contains("private Task ToggleStandardAiRailAsync()", layoutViewModel, StringComparison.Ordinal);
        Assert.Contains("ToggleStandardAiRailCommand = new RelayCommand(ToggleStandardAiRailAsync);", commandsViewModel, StringComparison.Ordinal);
        Assert.Contains("public ICommand ToggleStandardAiRailCommand", mainViewModel, StringComparison.Ordinal);
        Assert.Contains("public bool IsStandardReadableTranscriptViewSelected", transcriptViewModel, StringComparison.Ordinal);
        Assert.Contains("public bool IsStandardRawTranscriptViewSelected", transcriptViewModel, StringComparison.Ordinal);
        Assert.Contains("SelectedTranscriptTabIndex = value", transcriptViewModel, StringComparison.Ordinal);
        Assert.Contains("RefreshSelectedSegmentEditBuffer();", transcriptViewModel, StringComparison.Ordinal);
        Assert.Contains("private int EffectiveExportTranscriptTabIndex => IsStandardLayout", exportViewModel, StringComparison.Ordinal);
        Assert.Contains("IsStandardRawTranscriptViewSelected", exportViewModel, StringComparison.Ordinal);
        Assert.Contains("ExportReviewCandidateTranscriptTabIndex => TranscriptExportSource.Polished", exportViewModel, StringComparison.Ordinal);
        Assert.Contains("IsStandardRawTranscriptView = false;", postProcessViewModel, StringComparison.Ordinal);
        Assert.Contains("GetSegmentEditableText(SelectedSegment)", jobsViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void StandardLayoutViewModelCoverage_IsCompiledByUnitTestProject()
    {
        var repoRoot = FindRepoRoot();
        var testProject = File.ReadAllText(Path.Combine(
            repoRoot,
            "tests",
            "KoeNote.App.Tests",
            "KoeNote.App.Tests.csproj"));
        var testFileName = Path.GetFileName(GetType().Name + ".cs");

        Assert.Contains("<Compile Remove=\"MainWindowViewModel*Tests.cs\" />", testProject, StringComparison.Ordinal);
        Assert.False(testFileName.StartsWith("MainWindowViewModel", StringComparison.Ordinal));
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
}
