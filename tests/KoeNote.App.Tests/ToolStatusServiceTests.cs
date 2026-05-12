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

        AssertMissing(items, "ffmpeg", paths.FfmpegPath);
        AssertMissing(items, "llama-completion", paths.LlamaCompletionPath);
        AssertNotInstalled(items, "llama-completion-ternary", paths.TernaryLlamaCompletionPath);
        Assert.DoesNotContain(items, item => item.Name == "ASR model");
        Assert.DoesNotContain(items, item => item.Name == "Review model");
    }

    [Fact]
    public void GetStatusItems_MarksRuntimeFilesFoundWhenPlacedInExpectedLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var baseDirectory = Path.Combine(root, "app");
        var paths = new AppPaths(root, root, baseDirectory);
        paths.EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        Touch(paths.TernaryLlamaCompletionPath);

        var items = new ToolStatusService(paths).GetStatusItems();

        AssertFound(items, "ffmpeg", paths.FfmpegPath);
        AssertFound(items, "llama-completion", paths.LlamaCompletionPath);
        AssertFound(items, "llama-completion-ternary", paths.TernaryLlamaCompletionPath);
    }

    [Fact]
    public void GetStatusItems_DoesNotTreatDiarizeDistInfoAsInstalledRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        Directory.CreateDirectory(Path.Combine(paths.PythonPackages, "diarize-0.1.2.dist-info"));

        var items = new ToolStatusService(paths).GetStatusItems();

        var item = Assert.Single(items, item => item.Name == "diarize runtime");
        Assert.True(item.IsOk);
        Assert.Equal("Not installed yet", item.Value);
    }

    [Fact]
    public void GetStatusItems_MarksDiarizePackageDirectoryAsInstalledRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        Directory.CreateDirectory(Path.Combine(paths.PythonPackages, "diarize"));
        Touch(Path.Combine(paths.PythonPackages, "silero_vad", "data", "silero_vad.jit"));

        var items = new ToolStatusService(paths).GetStatusItems();

        AssertFound(items, "diarize runtime", paths.PythonPackages);
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

    private static void AssertNotInstalled(IReadOnlyList<Models.StatusItem> items, string name, string expectedPath)
    {
        var item = Assert.Single(items, item => item.Name == name);
        Assert.True(item.IsOk);
        Assert.Equal("Not installed yet", item.Value);
        Assert.Contains(expectedPath, item.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
    }
}
