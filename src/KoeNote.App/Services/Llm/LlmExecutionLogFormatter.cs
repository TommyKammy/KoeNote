namespace KoeNote.App.Services.Llm;

public static class LlmExecutionLogFormatter
{
    public static string Format(LlmRuntimeProfile profile, LlmTaskSettings settings)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(settings);

        return string.Join(" ", [
            "LLM",
            $"task={settings.TaskKind.ToString().ToLowerInvariant()}",
            $"profile={profile.ProfileId}",
            $"model={profile.ModelId}",
            $"family={FormatValue(profile.ModelFamily)}",
            $"runtime={profile.RuntimeKind}/{profile.RuntimePackageId}",
            $"acceleration={profile.AccelerationMode}",
            $"runtime_path={FormatPath(profile.LlamaCompletionPath)}",
            $"ctx={profile.ContextSize}",
            $"gpu_layers={profile.GpuLayers}",
            $"threads={FormatValue(profile.Threads)}",
            $"threads_batch={FormatValue(profile.ThreadsBatch)}",
            $"timeout_sec={(int)Math.Ceiling(profile.Timeout.TotalSeconds)}",
            $"no_conversation={FormatBool(profile.NoConversation)}",
            $"prompt={settings.PromptTemplateId}",
            $"prompt_version={settings.PromptVersion}",
            $"generation={settings.GenerationProfile}",
            $"temp={FormatDouble(settings.Temperature)}",
            $"top_p={FormatValue(settings.TopP)}",
            $"top_k={FormatValue(settings.TopK)}",
            $"repeat_penalty={FormatValue(settings.RepeatPenalty)}",
            $"max_tokens={settings.MaxTokens}",
            $"chunk_segments={settings.ChunkSegmentCount}",
            $"chunk_overlap={settings.ChunkOverlap}",
            $"json_schema={FormatBool(settings.UseJsonSchema)}",
            $"repair={FormatBool(settings.EnableRepair)}",
            $"validation={settings.ValidationMode}",
            $"sanitizer={profile.OutputSanitizerProfile}"
        ]);
    }

    private static string FormatValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private static string FormatValue(int? value)
    {
        return value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-";
    }

    private static string FormatValue(double? value)
    {
        return value is null ? "-" : FormatDouble(value.Value);
    }

    private static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string FormatPath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? "-"
            : $"\"{path.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.###############", System.Globalization.CultureInfo.InvariantCulture);
    }
}
