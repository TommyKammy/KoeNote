using KoeNote.App.Services.Llm;

namespace KoeNote.App.Services.Transcript;

public static class ReadablePolishingPromptSettingsResolver
{
    public static ReadablePolishingPromptSettings Resolve(
        LlmRuntimeProfile profile,
        ReadablePolishingPromptSettingsRepository repository)
    {
        var modelFamily = ReadablePolishingPromptModelFamilies.ResolveForModel(profile.ModelId, profile.ModelFamily);
        var settings = repository.Load(modelFamily).Settings.Normalize();
        return settings with
        {
            PromptTemplateId = string.IsNullOrWhiteSpace(settings.PromptTemplateId)
                ? ReadablePolishingPromptSettings.ResolveDefaultPromptTemplateId(modelFamily)
                : settings.PromptTemplateId
        };
    }
}
