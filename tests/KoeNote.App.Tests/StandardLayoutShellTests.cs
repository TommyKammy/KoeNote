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
        Assert.Contains("原文を確認", readablePanelXaml, StringComparison.Ordinal);
        Assert.Contains("差分を見る", readablePanelXaml, StringComparison.Ordinal);
        Assert.Contains("レビュー候補", readablePanelXaml, StringComparison.Ordinal);
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
