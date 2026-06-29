using KoeNote.App.Services.Models;

namespace KoeNote.App.Services.Llm;

public static class ReviewModelSelectionResolver
{
    public const string DefaultReviewModelId = "gemma-4-e4b-it-q4-k-m";
    public const string LegacyReviewModelId = "llm-jp-4-8b-thinking-q4-k-m";

    public static string Resolve(ModelCatalog catalog, string? selectedModelId, string? selectedPresetId)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (IsSelectableReviewModel(catalog, selectedModelId))
        {
            return selectedModelId!;
        }

        if (string.IsNullOrWhiteSpace(selectedModelId))
        {
            var presetReviewModelId = (catalog.Presets ?? [])
                .FirstOrDefault(preset => !string.IsNullOrWhiteSpace(selectedPresetId) &&
                    preset.PresetId.Equals(selectedPresetId, StringComparison.OrdinalIgnoreCase))
                ?.ReviewModelId;
            if (IsSelectableReviewModel(catalog, presetReviewModelId))
            {
                return presetReviewModelId!;
            }
        }

        if (IsSelectableReviewModel(catalog, DefaultReviewModelId))
        {
            return DefaultReviewModelId;
        }

        if (IsSelectableReviewModel(catalog, LegacyReviewModelId))
        {
            return LegacyReviewModelId;
        }

        return catalog.Models
            .FirstOrDefault(model =>
                model.Role.Equals("review", StringComparison.OrdinalIgnoreCase) &&
                ModelCatalogCompatibility.IsSelectable(model))
            ?.ModelId ?? DefaultReviewModelId;
    }

    public static bool IsSelectableReviewModel(ModelCatalog catalog, string? modelId)
    {
        return !string.IsNullOrWhiteSpace(modelId) &&
            catalog.Models.Any(model =>
                model.Role.Equals("review", StringComparison.OrdinalIgnoreCase) &&
                model.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase) &&
                (ModelCatalogCompatibility.IsSelectable(model) ||
                 IsLocalValidationModel(model)));
    }

    private static bool IsLocalValidationModel(ModelCatalogItem model)
    {
        return Gemma12BLocalValidation.IsEnabled() &&
            Gemma12BLocalValidation.IsTargetModel(model.ModelId);
    }
}
