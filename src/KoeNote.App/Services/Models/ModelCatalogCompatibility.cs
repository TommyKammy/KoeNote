namespace KoeNote.App.Services.Models;

public static class ModelCatalogCompatibility
{
    private static readonly string[] UnsupportedStatuses =
    [
        "unsupported",
        "runtime-unsupported",
        "hidden"
    ];

    public static bool IsSelectable(ModelCatalogItem model)
    {
        return !UnsupportedStatuses.Contains(model.Status, StringComparer.OrdinalIgnoreCase) ||
            KoeNote.App.Services.Llm.Gemma12BLocalValidation.IsEnabled() &&
            KoeNote.App.Services.Llm.Gemma12BLocalValidation.IsTargetModel(model.ModelId);
    }
}
