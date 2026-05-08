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
        return !UnsupportedStatuses.Contains(model.Status, StringComparer.OrdinalIgnoreCase);
    }
}
