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
        Assert.Contains("<Grid MinWidth=\"72\">", xaml, StringComparison.Ordinal);
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
