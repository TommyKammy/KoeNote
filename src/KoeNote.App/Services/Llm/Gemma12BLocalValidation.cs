namespace KoeNote.App.Services.Llm;

public static class Gemma12BLocalValidation
{
    public const string ModelId = "gemma-4-12b-it-qat-q4-0";
    public const string EnableEnvironmentVariable = "KOENOTE_ENABLE_GEMMA12B_LOCAL_VALIDATION";

    public static bool IsTargetModel(string? modelId)
    {
        return !string.IsNullOrWhiteSpace(modelId) &&
            modelId.Equals(ModelId, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsEnabled()
    {
        var value = Environment.GetEnvironmentVariable(EnableEnvironmentVariable);
        return value is not null &&
            (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }
}
