namespace KoeNote.App.Services.Llm;

public sealed record LlmSettingsDisplaySnapshot(
    string ActiveProfileSummary,
    string ReviewTaskSummary,
    string SummaryTaskSummary,
    string PolishingTaskSummary,
    string HeaderReviewSummary,
    string HeaderSummarySummary)
{
    public static LlmSettingsDisplaySnapshot NotConfigured { get; } = new(
        "Not configured",
        "Review: Not configured",
        "Summary: Not configured",
        "Polishing: Not configured",
        "Not configured",
        "Not configured");
}

public sealed class LlmSettingsDisplayService(LlmSettingsRepository repository)
{
    public LlmSettingsDisplaySnapshot LoadSnapshot()
    {
        var activeProfile = repository.FindActiveProfile();
        if (activeProfile is null)
        {
            return LlmSettingsDisplaySnapshot.NotConfigured;
        }

        var taskSettings = repository.ListTaskSettings(activeProfile.Profile.ProfileId)
            .ToDictionary(setting => setting.Settings.TaskKind);

        var review = GetTaskSummary(taskSettings, LlmTaskKind.Review);
        var summary = GetTaskSummary(taskSettings, LlmTaskKind.Summary);
        var polishing = GetTaskSummary(taskSettings, LlmTaskKind.Polishing);

        return new LlmSettingsDisplaySnapshot(
            FormatProfileSummary(activeProfile),
            review.DetailSummary,
            summary.DetailSummary,
            polishing.DetailSummary,
            review.HeaderSummary,
            summary.HeaderSummary);
    }

    private static string FormatProfileSummary(PersistedLlmRuntimeProfile profile)
    {
        var runtime = string.IsNullOrWhiteSpace(profile.Profile.RuntimePackageId)
            ? profile.Profile.RuntimeKind
            : $"{profile.Profile.RuntimeKind}/{profile.Profile.RuntimePackageId}";
        var gpu = profile.Profile.GpuLayers >= 900 ? "gpu auto" : $"gpu {profile.Profile.GpuLayers}";
        return $"{profile.Profile.DisplayName} ({profile.Profile.ModelId}) | {runtime} | ctx {profile.Profile.ContextSize} | {gpu}";
    }

    private static TaskDisplaySummary GetTaskSummary(
        IReadOnlyDictionary<LlmTaskKind, PersistedLlmTaskSettings> taskSettings,
        LlmTaskKind taskKind)
    {
        if (!taskSettings.TryGetValue(taskKind, out var persisted))
        {
            var taskName = FormatTaskName(taskKind);
            return new TaskDisplaySummary($"{taskName}: Not configured", "Not configured");
        }

        var settings = persisted.Settings;
        var detail = $"{FormatTaskName(taskKind)}: {settings.GenerationProfile} | prompt {settings.PromptTemplateId}/{settings.PromptVersion} | temp {settings.Temperature:0.###} | max {settings.MaxTokens} | chunk {settings.ChunkSegmentCount}+{settings.ChunkOverlap} | validation {settings.ValidationMode}";
        var header = $"{settings.GenerationProfile}, {settings.MaxTokens} tok";
        return new TaskDisplaySummary(detail, header);
    }

    private static string FormatTaskName(LlmTaskKind taskKind)
    {
        return taskKind switch
        {
            LlmTaskKind.Review => "Review",
            LlmTaskKind.Summary => "Summary",
            LlmTaskKind.Polishing => "Polishing",
            _ => taskKind.ToString()
        };
    }

    private sealed record TaskDisplaySummary(string DetailSummary, string HeaderSummary);
}
