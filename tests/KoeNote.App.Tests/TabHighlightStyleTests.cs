namespace KoeNote.App.Tests;

public sealed class TabHighlightStyleTests
{
    [Fact]
    public void CardTabItem_KeepsCompactSharedLayout()
    {
        var repoRoot = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Resources", "Tabs.xaml"));

        Assert.Contains("<Setter Property=\"Padding\" Value=\"12,7\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Margin\" Value=\"0,0,6,0\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MinWidth\" Value=\"54\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MinHeight\" Value=\"32\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"HighlightPulse\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ClipToBounds=\"False\"", xaml, StringComparison.Ordinal);
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
        Assert.Contains("<Setter Property=\"Padding\" Value=\"18,7\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MinWidth\" Value=\"108\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid ClipToBounds=\"False\" SnapsToDevicePixels=\"False\">", xaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"2,0,2,1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"10,0,10,0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid MinWidth=\"92\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<TabItem Header=\"整文\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<TabItem Header=\"素起こし\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<TabItem Header=\"差分\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<TextBlock Text=\"レビュー候補\"", xaml, StringComparison.Ordinal);
        Assert.True(
            xaml.IndexOf("<TabItem Header=\"整文\">", StringComparison.Ordinal) <
            xaml.IndexOf("<TabItem Header=\"素起こし\">", StringComparison.Ordinal));
        Assert.True(
            xaml.IndexOf("<TabItem Header=\"素起こし\">", StringComparison.Ordinal) <
            xaml.IndexOf("<TabItem Header=\"差分\">", StringComparison.Ordinal));
        Assert.True(
            xaml.IndexOf("<TabItem Header=\"差分\">", StringComparison.Ordinal) <
            xaml.IndexOf("<TextBlock Text=\"レビュー候補\"", StringComparison.Ordinal));
        Assert.Contains("Margin=\"4,-4,4,-4\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Opacity=\"0\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Setter Property=\"Padding\" Value=\"16,7\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Setter Property=\"Padding\" Value=\"16,7,20,7\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Setter Property=\"MinWidth\" Value=\"92\" />", xaml, StringComparison.Ordinal);
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
    public void ReadablePolishedPanel_ExposesSupplementalReviewRoutes()
    {
        var repoRoot = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "ReadablePolishedPanel.xaml"));

        Assert.Contains("ShowRawTranscriptTabCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowDiffTranscriptTabCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowReviewCandidateTranscriptTabCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("原文を確認", xaml, StringComparison.Ordinal);
        Assert.Contains("差分を見る", xaml, StringComparison.Ordinal);
        Assert.Contains("レビュー候補", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainScreen_UsesMockupAlignedHeaderAndRightPaneCtas()
    {
        var repoRoot = FindRepoRoot();
        var mainWindowXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "MainWindow.xaml"));
        var headerXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "HeaderToolbar.xaml"));
        var headerStyles = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "HeaderToolbar.Styles.xaml"));
        var controlsXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Resources", "Controls.xaml"));
        var transcriptXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "TranscriptPanel.xaml"));
        var reviewXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "ReviewPanel.xaml"));
        var audioPlayerXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "TranscriptAudioPlayer.xaml"));
        var listsXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Resources", "Lists.xaml"));
        var stageXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "StageProgressPanel.xaml"));
        var statusXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Controls", "StatusBarPanel.xaml"));

        Assert.Contains("Margin=\"0\"", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"1\" />", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"CornerRadius\" Value=\"0\" />", controlsXaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"0\"", audioPlayerXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"KoeNote\"", headerXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"実行\"", headerXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("整文まで実行", headerXaml, StringComparison.Ordinal);
        Assert.Contains("HeaderModelBadgeButton", headerXaml, StringComparison.Ordinal);
        Assert.Contains("HeaderToggleTrack", headerXaml, StringComparison.Ordinal);
        Assert.Contains("HeaderToggleThumb", headerXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"HeaderModelBadgeButton\"", headerStyles, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"HeaderToggleTrack\"", headerStyles, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"HeaderToggleThumb\"", headerStyles, StringComparison.Ordinal);

        Assert.Contains("ShowReadableTranscriptTabCommand", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("整文タブを開く", reviewXaml, StringComparison.Ordinal);
        Assert.Contains("RunPostSummaryCommand", reviewXaml, StringComparison.Ordinal);
        Assert.Single(AllIndexesOf(reviewXaml, "Command=\"{Binding RunPostSummaryCommand}\""));
        Assert.Contains("TranscriptInlineToggle", transcriptXaml, StringComparison.Ordinal);
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
        Assert.Contains("この原文から再整文", xaml, StringComparison.Ordinal);
        Assert.Contains("レビュー候補を再生成", xaml, StringComparison.Ordinal);
        Assert.Contains("BeginSegmentInlineEditCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("OnSpeakerMouseLeftButtonDown", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource VisibleWhenRawTranscriptMode}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("!string.Equals(DisplayMode, \"Raw\", StringComparison.Ordinal)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("!string.Equals(DisplayMode, \"Polished\", StringComparison.Ordinal)", code, StringComparison.Ordinal);
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
}
