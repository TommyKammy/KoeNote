using KoeNote.App.Services.Asr;

namespace KoeNote.App.Tests;

public sealed class AsrEngineRegistryTests
{
    [Fact]
    public void GetRequired_ReturnsRegisteredEngineById()
    {
        var engine = new FakeAsrEngine("fake-engine");
        var registry = new AsrEngineRegistry([engine]);

        Assert.Same(engine, registry.GetRequired("FAKE-ENGINE"));
    }

    [Fact]
    public void GetRequired_ThrowsForMissingEngine()
    {
        var registry = new AsrEngineRegistry([]);

        Assert.Throws<InvalidOperationException>(() => registry.GetRequired("missing"));
    }

    private sealed class FakeAsrEngine(string engineId) : IAsrEngine
    {
        public string EngineId => engineId;

        public string DisplayName => engineId;

        public Task<AsrEngineCheckResult> CheckAsync(AsrEngineConfig config, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AsrEngineCheckResult(true, []));
        }

        public Task<AsrResult> TranscribeAsync(
            AsrInput input,
            AsrEngineConfig config,
            AsrOptions options,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
