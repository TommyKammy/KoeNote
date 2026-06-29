using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Models;

namespace KoeNote.App.Services.Jobs;

internal static class DirectLlmStageModelResolver
{
    public static string Resolve(
        ModelCatalog catalog,
        string? selectedModelId,
        string? selectedPresetId,
        bool allowGemma12BMtpServer = false)
    {
        var modelId = ReviewModelSelectionResolver.Resolve(catalog, selectedModelId, selectedPresetId);
        return Gemma12BLocalValidation.IsTargetModel(modelId) && !allowGemma12BMtpServer
            ? ReviewModelSelectionResolver.DefaultReviewModelId
            : modelId;
    }
}
