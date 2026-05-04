using KoeNote.App.Services;
using KoeNote.App.Services.Asr;

namespace KoeNote.App.Tests;

public sealed class VibeVoiceCrispAsrEngineTests
{
    [Fact]
    public async Task CheckAsync_ReportsMissingRuntimeAndModel()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var engine = CreateEngine(paths);

        var result = await engine.CheckAsync(new AsrEngineConfig(
            Path.Combine(paths.RuntimeTools, "missing.exe"),
            Path.Combine(paths.Models, "missing.gguf"),
            paths.Jobs,
            "test-model"));

        Assert.False(result.IsAvailable);
        Assert.Contains(result.Messages, message => message.Contains("Missing ASR runtime", StringComparison.Ordinal));
        Assert.Contains(result.Messages, message => message.Contains("Missing ASR model", StringComparison.Ordinal));
    }

    private static VibeVoiceCrispAsrEngine CreateEngine(AppPaths paths)
    {
        var worker = new AsrWorker(
            new ExternalProcessRunner(),
            new AsrCommandBuilder(),
            new AsrJsonNormalizer(),
            new AsrResultStore(),
            new TranscriptSegmentRepository(paths));
        return new VibeVoiceCrispAsrEngine(worker, new AsrRunRepository(paths));
    }

    private static AppPaths CreatePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var local = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        return new AppPaths(root, local);
    }
}
