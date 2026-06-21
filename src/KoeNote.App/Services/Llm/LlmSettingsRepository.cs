using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Llm;

public sealed record PersistedLlmRuntimeProfile(
    LlmRuntimeProfile Profile,
    bool IsActive,
    string Source,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PersistedLlmTaskSettings(
    string SettingsId,
    string ProfileId,
    LlmTaskSettings Settings,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class LlmSettingsRepository(AppPaths paths)
{
    public void UpsertProfile(LlmRuntimeProfile profile, bool isActive, string source)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var now = DateTimeOffset.Now;
        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();

        if (isActive)
        {
            using var clearCommand = connection
                .CreateCommand(transaction, "UPDATE llm_profiles SET is_active = 0, updated_at = $updated_at;")
                .AddIsoDateTimeOffset("$updated_at", now);
            clearCommand.ExecuteNonQuery();
        }

        using var command = connection.CreateCommand(transaction, """
            INSERT INTO llm_profiles (
                profile_id, model_id, model_family, display_name, runtime_kind, runtime_package_id,
                model_path, llama_completion_path, context_size, gpu_layers, threads, threads_batch,
                no_conversation, output_sanitizer_profile, timeout_seconds, is_active, source, created_at, updated_at
            )
            VALUES (
                $profile_id, $model_id, $model_family, $display_name, $runtime_kind, $runtime_package_id,
                $model_path, $llama_completion_path, $context_size, $gpu_layers, $threads, $threads_batch,
                $no_conversation, $output_sanitizer_profile, $timeout_seconds, $is_active, $source, $created_at, $updated_at
            )
            ON CONFLICT(profile_id) DO UPDATE SET
                model_id = excluded.model_id,
                model_family = excluded.model_family,
                display_name = excluded.display_name,
                runtime_kind = excluded.runtime_kind,
                runtime_package_id = excluded.runtime_package_id,
                model_path = excluded.model_path,
                llama_completion_path = excluded.llama_completion_path,
                context_size = excluded.context_size,
                gpu_layers = excluded.gpu_layers,
                threads = excluded.threads,
                threads_batch = excluded.threads_batch,
                no_conversation = excluded.no_conversation,
                output_sanitizer_profile = excluded.output_sanitizer_profile,
                timeout_seconds = excluded.timeout_seconds,
                is_active = excluded.is_active,
                source = excluded.source,
                updated_at = excluded.updated_at;
            """);
        AddProfileParameters(command, profile, isActive, source, now);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public PersistedLlmRuntimeProfile? FindProfile(string profileId)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection
            .CreateCommand($"{ProfileSelectSql} WHERE profile_id = $profile_id;")
            .AddValue("$profile_id", profileId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadProfile(reader) : null;
    }

    public PersistedLlmRuntimeProfile? FindActiveProfile()
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand(
            $"{ProfileSelectSql} WHERE is_active = 1 ORDER BY updated_at DESC LIMIT 1;");

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadProfile(reader) : null;
    }

    public IReadOnlyList<PersistedLlmRuntimeProfile> ListProfiles()
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand(
            $"{ProfileSelectSql} ORDER BY is_active DESC, updated_at DESC;");

        using var reader = command.ExecuteReader();
        var profiles = new List<PersistedLlmRuntimeProfile>();
        while (reader.Read())
        {
            profiles.Add(ReadProfile(reader));
        }

        return profiles;
    }

    public void UpsertTaskSettings(string profileId, LlmTaskSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var now = DateTimeOffset.Now;
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand("""
            INSERT INTO llm_task_settings (
                settings_id, profile_id, task_kind, prompt_template_id, prompt_version, generation_profile,
                temperature, top_p, top_k, repeat_penalty, max_tokens, chunk_segment_count, chunk_overlap,
                use_json_schema, enable_repair, validation_mode, created_at, updated_at
            )
            VALUES (
                $settings_id, $profile_id, $task_kind, $prompt_template_id, $prompt_version, $generation_profile,
                $temperature, $top_p, $top_k, $repeat_penalty, $max_tokens, $chunk_segment_count, $chunk_overlap,
                $use_json_schema, $enable_repair, $validation_mode, $created_at, $updated_at
            )
            ON CONFLICT(profile_id, task_kind) DO UPDATE SET
                prompt_template_id = excluded.prompt_template_id,
                prompt_version = excluded.prompt_version,
                generation_profile = excluded.generation_profile,
                temperature = excluded.temperature,
                top_p = excluded.top_p,
                top_k = excluded.top_k,
                repeat_penalty = excluded.repeat_penalty,
                max_tokens = excluded.max_tokens,
                chunk_segment_count = excluded.chunk_segment_count,
                chunk_overlap = excluded.chunk_overlap,
                use_json_schema = excluded.use_json_schema,
                enable_repair = excluded.enable_repair,
                validation_mode = excluded.validation_mode,
                updated_at = excluded.updated_at;
            """);
        AddTaskSettingsParameters(command, profileId, settings, now);
        command.ExecuteNonQuery();
    }

    public PersistedLlmTaskSettings? FindTaskSettings(string profileId, LlmTaskKind taskKind)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection
            .CreateCommand($"{TaskSettingsSelectSql} WHERE profile_id = $profile_id AND task_kind = $task_kind;")
            .AddValue("$profile_id", profileId)
            .AddValue("$task_kind", ToDbTaskKind(taskKind));

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadTaskSettings(reader) : null;
    }

    public IReadOnlyList<PersistedLlmTaskSettings> ListTaskSettings(string profileId)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection
            .CreateCommand($"{TaskSettingsSelectSql} WHERE profile_id = $profile_id ORDER BY task_kind;")
            .AddValue("$profile_id", profileId);

        using var reader = command.ExecuteReader();
        var settings = new List<PersistedLlmTaskSettings>();
        while (reader.Read())
        {
            settings.Add(ReadTaskSettings(reader));
        }

        return settings;
    }

    private const string ProfileSelectSql = """
        SELECT profile_id, model_id, model_family, display_name, runtime_kind, runtime_package_id,
               model_path, llama_completion_path, context_size, gpu_layers, threads, threads_batch,
               no_conversation, output_sanitizer_profile, timeout_seconds, is_active, source, created_at, updated_at
        FROM llm_profiles
        """;

    private const string TaskSettingsSelectSql = """
        SELECT settings_id, profile_id, task_kind, prompt_template_id, prompt_version, generation_profile,
               temperature, top_p, top_k, repeat_penalty, max_tokens, chunk_segment_count, chunk_overlap,
               use_json_schema, enable_repair, validation_mode, created_at, updated_at
        FROM llm_task_settings
        """;

    private static PersistedLlmRuntimeProfile ReadProfile(SqliteDataReader reader)
    {
        var profile = new LlmRuntimeProfile(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            reader.IsDBNull(10) ? null : reader.GetInt32(10),
            reader.IsDBNull(11) ? null : reader.GetInt32(11),
            reader.GetInt32(12) != 0,
            reader.GetString(13),
            TimeSpan.FromSeconds(reader.GetInt32(14)));
        return new PersistedLlmRuntimeProfile(
            profile,
            reader.GetInt32(15) != 0,
            reader.GetString(16),
            DateTimeOffset.Parse(reader.GetString(17)),
            DateTimeOffset.Parse(reader.GetString(18)));
    }

    private static PersistedLlmTaskSettings ReadTaskSettings(SqliteDataReader reader)
    {
        var settings = new LlmTaskSettings(
            FromDbTaskKind(reader.GetString(2)),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetDouble(6),
            reader.IsDBNull(7) ? null : reader.GetDouble(7),
            reader.IsDBNull(8) ? null : reader.GetInt32(8),
            reader.IsDBNull(9) ? null : reader.GetDouble(9),
            reader.GetInt32(10),
            reader.GetInt32(11),
            reader.GetInt32(12),
            reader.GetInt32(13) != 0,
            reader.GetInt32(14) != 0,
            reader.GetString(15));
        return new PersistedLlmTaskSettings(
            reader.GetString(0),
            reader.GetString(1),
            settings,
            DateTimeOffset.Parse(reader.GetString(16)),
            DateTimeOffset.Parse(reader.GetString(17)));
    }

    private static void AddProfileParameters(
        SqliteCommand command,
        LlmRuntimeProfile profile,
        bool isActive,
        string source,
        DateTimeOffset now)
    {
        command
            .AddValue("$profile_id", profile.ProfileId)
            .AddValue("$model_id", profile.ModelId)
            .AddValue("$model_family", profile.ModelFamily)
            .AddValue("$display_name", profile.DisplayName)
            .AddValue("$runtime_kind", profile.RuntimeKind)
            .AddValue("$runtime_package_id", profile.RuntimePackageId)
            .AddValue("$model_path", profile.ModelPath)
            .AddValue("$llama_completion_path", profile.LlamaCompletionPath)
            .AddValue("$context_size", profile.ContextSize)
            .AddValue("$gpu_layers", profile.GpuLayers)
            .AddValue("$threads", profile.Threads)
            .AddValue("$threads_batch", profile.ThreadsBatch)
            .AddValue("$no_conversation", profile.NoConversation ? 1 : 0)
            .AddValue("$output_sanitizer_profile", profile.OutputSanitizerProfile)
            .AddValue("$timeout_seconds", (int)Math.Ceiling(profile.Timeout.TotalSeconds))
            .AddValue("$is_active", isActive ? 1 : 0)
            .AddValue("$source", source)
            .AddIsoDateTimeOffset("$created_at", now)
            .AddIsoDateTimeOffset("$updated_at", now);
    }

    private static void AddTaskSettingsParameters(
        SqliteCommand command,
        string profileId,
        LlmTaskSettings settings,
        DateTimeOffset now)
    {
        command
            .AddValue("$settings_id", $"{profileId}:{ToDbTaskKind(settings.TaskKind)}")
            .AddValue("$profile_id", profileId)
            .AddValue("$task_kind", ToDbTaskKind(settings.TaskKind))
            .AddValue("$prompt_template_id", settings.PromptTemplateId)
            .AddValue("$prompt_version", settings.PromptVersion)
            .AddValue("$generation_profile", settings.GenerationProfile)
            .AddValue("$temperature", settings.Temperature)
            .AddValue("$top_p", settings.TopP)
            .AddValue("$top_k", settings.TopK)
            .AddValue("$repeat_penalty", settings.RepeatPenalty)
            .AddValue("$max_tokens", settings.MaxTokens)
            .AddValue("$chunk_segment_count", settings.ChunkSegmentCount)
            .AddValue("$chunk_overlap", settings.ChunkOverlap)
            .AddValue("$use_json_schema", settings.UseJsonSchema ? 1 : 0)
            .AddValue("$enable_repair", settings.EnableRepair ? 1 : 0)
            .AddValue("$validation_mode", settings.ValidationMode)
            .AddIsoDateTimeOffset("$created_at", now)
            .AddIsoDateTimeOffset("$updated_at", now);
    }

    private static string ToDbTaskKind(LlmTaskKind taskKind)
    {
        return taskKind.ToString().ToLowerInvariant();
    }

    private static LlmTaskKind FromDbTaskKind(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "review" => LlmTaskKind.Review,
            "summary" => LlmTaskKind.Summary,
            "polishing" => LlmTaskKind.Polishing,
            _ => throw new InvalidOperationException($"Unknown LLM task kind: {value}")
        };
    }
}
