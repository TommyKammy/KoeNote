namespace KoeNote.App.Services.Llm;

public sealed class LlmTaskSettingsResolver
{
    public LlmTaskSettings Resolve(LlmRuntimeProfile profile, LlmTaskKind taskKind)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return LlmPresetCatalog.ResolveTaskSettings(profile.ModelId, profile.ModelFamily, taskKind);
    }
}
