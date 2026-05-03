using KoeNote.App.Services;

namespace KoeNote.App.Tests;

public sealed class ToolStatusServiceTests
{
    [Fact]
    public void GetStatusItems_ReportsMissingRuntimeFilesWithActionablePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var baseDirectory = Path.Combine(root, "app");
        var paths = new AppPaths(root, root, baseDirectory);
        paths.EnsureCreated();

        var items = new ToolStatusService(paths).GetStatusItems();

        AssertMissing(items, "crispasr", paths.CrispAsrPath);
        AssertMissing(items, "llama-completion", paths.LlamaCompletionPath);
        AssertMissing(items, "ASR model", paths.VibeVoiceAsrModelPath);
        AssertMissing(items, "Review model", paths.ReviewModelPath);
    }

    [Fact]
    public void GetStatusItems_MarksRuntimeFilesFoundWhenPlacedInExpectedLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var baseDirectory = Path.Combine(root, "app");
        var paths = new AppPaths(root, root, baseDirectory);
        paths.EnsureCreated();
        Touch(paths.CrispAsrPath);
        Touch(paths.LlamaCompletionPath);
        Touch(paths.VibeVoiceAsrModelPath);
        Touch(paths.ReviewModelPath);

        var items = new ToolStatusService(paths).GetStatusItems();

        AssertFound(items, "crispasr", paths.CrispAsrPath);
        AssertFound(items, "llama-completion", paths.LlamaCompletionPath);
        AssertFound(items, "ASR model", paths.VibeVoiceAsrModelPath);
        AssertFound(items, "Review model", paths.ReviewModelPath);
    }

    private static void AssertMissing(IReadOnlyList<Models.StatusItem> items, string name, string expectedPath)
    {
        var item = Assert.Single(items, item => item.Name == name);
        Assert.False(item.IsOk);
        Assert.Equal("Missing", item.Value);
        Assert.Contains(expectedPath, item.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertFound(IReadOnlyList<Models.StatusItem> items, string name, string expectedPath)
    {
        var item = Assert.Single(items, item => item.Name == name);
        Assert.True(item.IsOk);
        Assert.Equal("Found", item.Value);
        Assert.Equal(expectedPath, item.Detail);
    }

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
    }
}
