namespace KoeNote.App.Services.Asr;

public sealed class AsrEngineRegistry(IEnumerable<IAsrEngine> engines)
{
    private readonly Dictionary<string, IAsrEngine> _engines = engines.ToDictionary(
        engine => engine.EngineId,
        StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<IAsrEngine> Engines => _engines.Values.OrderBy(engine => engine.DisplayName).ToArray();

    public bool Contains(string engineId) => _engines.ContainsKey(engineId);

    public IAsrEngine GetRequired(string engineId)
    {
        if (_engines.TryGetValue(engineId, out var engine))
        {
            return engine;
        }

        throw new InvalidOperationException($"ASR engine is not registered: {engineId}");
    }
}
