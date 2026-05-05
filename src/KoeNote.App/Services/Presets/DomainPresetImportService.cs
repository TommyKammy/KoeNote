using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using KoeNote.App.Services.Asr;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Presets;

public sealed record DomainPresetImportResult(
    string DisplayName,
    bool ContextUpdated,
    int AddedHotwordCount,
    int SkippedHotwordCount,
    int AddedCorrectionMemoryCount,
    int UpdatedCorrectionMemoryCount,
    int AddedSpeakerAliasCount,
    int UpdatedSpeakerAliasCount,
    int SkippedSpeakerAliasCount,
    int AddedReviewGuidelineCount,
    int UpdatedReviewGuidelineCount)
{
    public string Summary =>
        $"プリセットをインポートしました: {DisplayName} (コンテキスト: {(ContextUpdated ? "更新" : "変更なし")}, ホットワード: {AddedHotwordCount}件追加 / {SkippedHotwordCount}件スキップ, 補正メモリ: {AddedCorrectionMemoryCount}件追加 / {UpdatedCorrectionMemoryCount}件更新, 話者別名: {AddedSpeakerAliasCount}件追加 / {UpdatedSpeakerAliasCount}件更新 / {SkippedSpeakerAliasCount}件スキップ, レビュー指針: {AddedReviewGuidelineCount}件追加 / {UpdatedReviewGuidelineCount}件更新)";
}

public sealed record DomainPresetImportHistoryItem(
    string ImportId,
    string? PresetId,
    string DisplayName,
    string SourcePath,
    DateTimeOffset ImportedAt,
    DateTimeOffset? DeactivatedAt,
    bool ContextUpdated,
    int AddedHotwordCount,
    int AddedCorrectionMemoryCount,
    int AddedReviewGuidelineCount)
{
    public string StatusText => DeactivatedAt is null ? "Active" : "Cleared";

    public string Summary =>
        $"{DisplayName} / {ImportedAt:yyyy/MM/dd HH:mm} / {StatusText}";
}

public sealed record DomainPresetClearResult(
    string DisplayName,
    bool PresetFileLoaded,
    bool ContextRemoved,
    int RemovedHotwordCount,
    int DisabledCorrectionMemoryCount,
    int DisabledUserTermCount,
    int DeletedSpeakerAliasCount,
    int DisabledReviewGuidelineCount)
{
    public string Summary =>
        $"プリセットをクリアしました: {DisplayName} (JSON: {(PresetFileLoaded ? "読込" : "未検出")}, コンテキスト: {(ContextRemoved ? "削除" : "変更なし")}, ホットワード: {RemovedHotwordCount}件削除, 補正メモリ: {DisabledCorrectionMemoryCount}件無効化, ユーザー語彙: {DisabledUserTermCount}件無効化, 話者別名: {DeletedSpeakerAliasCount}件削除, レビュー指針: {DisabledReviewGuidelineCount}件無効化)";
}

public sealed class DomainPresetImportService(AppPaths paths, AsrSettingsRepository asrSettingsRepository)
{
    private const int SupportedSchemaVersion = 1;
    private const int MaxAsrHotwordLength = 24;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public DomainPresetImportResult Import(string presetPath, string? defaultJobId = null)
    {
        if (string.IsNullOrWhiteSpace(presetPath))
        {
            throw new ArgumentException("プリセットファイルを指定してください。", nameof(presetPath));
        }

        if (!File.Exists(presetPath))
        {
            throw new FileNotFoundException("プリセットファイルが見つかりません。", presetPath);
        }

        var json = File.ReadAllText(presetPath);
        var preset = JsonSerializer.Deserialize<DomainPreset>(json, JsonOptions)
            ?? throw new InvalidDataException("プリセットJSONを読み込めませんでした。");

        preset.Validate();

        var current = asrSettingsRepository.Load();
        var nextContext = MergeContext(current.ContextText, preset.AsrContext);
        var hotwordMerge = MergeHotwords(current.Hotwords, preset.Hotwords);
        var next = new AsrSettings(
            nextContext,
            string.Join(Environment.NewLine, hotwordMerge.Hotwords),
            current.EngineId,
            current.EnableReviewStage);

        var databaseResult = ImportDatabaseEntries(preset, defaultJobId);
        asrSettingsRepository.Save(next);

        var contextUpdated = !string.Equals(current.ContextText, nextContext, StringComparison.Ordinal);
        RecordImportHistory(presetPath, preset, contextUpdated, hotwordMerge, databaseResult);

        return new DomainPresetImportResult(
            preset.DisplayName,
            contextUpdated,
            hotwordMerge.AddedCount,
            hotwordMerge.SkippedCount,
            databaseResult.AddedCorrectionMemoryCount,
            databaseResult.UpdatedCorrectionMemoryCount,
            databaseResult.AddedSpeakerAliasCount,
            databaseResult.UpdatedSpeakerAliasCount,
            databaseResult.SkippedSpeakerAliasCount,
            databaseResult.AddedReviewGuidelineCount,
            databaseResult.UpdatedReviewGuidelineCount);
    }

    public IReadOnlyList<DomainPresetImportHistoryItem> LoadHistory(int limit = 20)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                import_id,
                preset_id,
                display_name,
                source_path,
                imported_at,
                deactivated_at,
                context_updated,
                added_hotword_count,
                added_correction_memory_count,
                added_review_guideline_count
            FROM domain_preset_imports
            ORDER BY imported_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var items = new List<DomainPresetImportHistoryItem>();
        while (reader.Read())
        {
            items.Add(new DomainPresetImportHistoryItem(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4)),
                reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)),
                reader.GetInt32(6) != 0,
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9)));
        }

        return items;
    }

    public DomainPresetClearResult ClearImport(string importId)
    {
        if (string.IsNullOrWhiteSpace(importId))
        {
            throw new ArgumentException("プリセット履歴を選択してください。", nameof(importId));
        }

        var history = LoadHistoryById(importId)
            ?? throw new InvalidDataException("プリセット履歴が見つかりません。");

        if (history.DeactivatedAt is not null)
        {
            throw new InvalidDataException("このプリセット履歴はすでにクリア済みです。");
        }

        var preset = TryLoadPreset(history.SourcePath);
        var presetId = preset?.NormalizedPresetId ?? history.PresetId ?? history.DisplayName;
        var contextRemoved = false;
        var removedHotwords = 0;
        var disabledMemory = 0;
        var disabledUserTerms = 0;
        var deletedAliases = 0;

        if (preset is not null)
        {
            var current = asrSettingsRepository.Load();
            var nextContext = history.ContextUpdated
                ? RemoveContextBlock(current.ContextText, preset.AsrContext)
                : current.ContextText.Trim();
            var nextHotwords = ShouldRemovePresetHotwords(history, preset)
                ? RemoveHotwords(current.Hotwords, preset.Hotwords, out removedHotwords)
                : current.Hotwords;
            contextRemoved = !string.Equals(current.ContextText.Trim(), nextContext.Trim(), StringComparison.Ordinal);
            if (contextRemoved || removedHotwords > 0)
            {
                asrSettingsRepository.Save(new AsrSettings(
                    nextContext,
                    string.Join(Environment.NewLine, nextHotwords),
                    current.EngineId,
                    current.EnableReviewStage));
            }
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();

        if (preset is not null)
        {
            foreach (var entry in preset.CorrectionMemory)
            {
                if (string.IsNullOrWhiteSpace(entry.WrongText) || string.IsNullOrWhiteSpace(entry.CorrectText))
                {
                    continue;
                }

                disabledMemory += DisableCorrectionMemory(
                    connection,
                    transaction,
                    entry.WrongText.Trim(),
                    entry.CorrectText.Trim(),
                    string.IsNullOrWhiteSpace(entry.Scope) ? "global" : entry.Scope.Trim());

                if (IsAsrHotwordCandidate(entry.CorrectText))
                {
                    disabledUserTerms += DisableUserTerm(connection, transaction, entry.CorrectText.Trim());
                }
            }

            foreach (var alias in preset.SpeakerAliases)
            {
                if (string.IsNullOrWhiteSpace(alias.JobId) ||
                    string.IsNullOrWhiteSpace(alias.SpeakerId))
                {
                    continue;
                }

                deletedAliases += DeleteSpeakerAlias(connection, transaction, alias.JobId.Trim(), alias.SpeakerId.Trim());
            }
        }

        var disabledGuidelines = DisableReviewGuidelines(connection, transaction, presetId);
        MarkPresetImportsCleared(connection, transaction, presetId, DateTimeOffset.Now.ToString("o"));
        transaction.Commit();

        return new DomainPresetClearResult(
            history.DisplayName,
            preset is not null,
            contextRemoved,
            removedHotwords,
            disabledMemory,
            disabledUserTerms,
            deletedAliases,
            disabledGuidelines);
    }

    private DomainPresetImportHistoryItem? LoadHistoryById(string importId)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                import_id,
                preset_id,
                display_name,
                source_path,
                imported_at,
                deactivated_at,
                context_updated,
                added_hotword_count,
                added_correction_memory_count,
                added_review_guideline_count
            FROM domain_preset_imports
            WHERE import_id = $import_id;
            """;
        command.Parameters.AddWithValue("$import_id", importId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new DomainPresetImportHistoryItem(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            DateTimeOffset.Parse(reader.GetString(4)),
            reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)),
            reader.GetInt32(6) != 0,
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9));
    }

    private static DomainPreset? TryLoadPreset(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                return null;
            }

            var preset = JsonSerializer.Deserialize<DomainPreset>(File.ReadAllText(sourcePath), JsonOptions);
            preset?.Validate();
            return preset;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private static string RemoveContextBlock(string currentContext, string? presetContext)
    {
        var preset = (presetContext ?? string.Empty).Trim();
        if (preset.Length == 0)
        {
            return currentContext.Trim();
        }

        var blocks = currentContext
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(block => !string.Equals(block, preset, StringComparison.Ordinal))
            .ToArray();
        return string.Join(Environment.NewLine + Environment.NewLine, blocks);
    }

    private static IReadOnlyList<string> RemoveHotwords(
        IReadOnlyList<string> currentHotwords,
        IReadOnlyList<string> presetHotwords,
        out int removedCount)
    {
        var presetSet = presetHotwords
            .Select(NormalizeHotword)
            .Where(static hotword => hotword.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var next = new List<string>();
        removedCount = 0;
        foreach (var hotword in currentHotwords)
        {
            var normalized = NormalizeHotword(hotword);
            if (normalized.Length == 0)
            {
                continue;
            }

            if (presetSet.Contains(normalized))
            {
                removedCount++;
                continue;
            }

            next.Add(normalized);
        }

        return next;
    }

    private static bool ShouldRemovePresetHotwords(DomainPresetImportHistoryItem history, DomainPreset preset)
    {
        var presetHotwordCount = preset.Hotwords
            .Select(NormalizeHotword)
            .Where(static hotword => hotword.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return presetHotwordCount > 0 && history.AddedHotwordCount == presetHotwordCount;
    }

    private static string MergeContext(string currentContext, string? presetContext)
    {
        var current = currentContext.Trim();
        var preset = (presetContext ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(preset))
        {
            return current;
        }

        if (string.IsNullOrWhiteSpace(current))
        {
            return preset;
        }

        if (current.Contains(preset, StringComparison.Ordinal))
        {
            return current;
        }

        return string.Join(Environment.NewLine + Environment.NewLine, current, preset);
    }

    private static HotwordMergeResult MergeHotwords(IReadOnlyList<string> currentHotwords, IReadOnlyList<string> presetHotwords)
    {
        var hotwords = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hotword in currentHotwords)
        {
            var normalized = NormalizeHotword(hotword);
            if (normalized.Length > 0 && seen.Add(normalized))
            {
                hotwords.Add(normalized);
            }
        }

        var added = 0;
        var skipped = 0;
        foreach (var hotword in presetHotwords)
        {
            var normalized = NormalizeHotword(hotword);
            if (normalized.Length == 0)
            {
                continue;
            }

            if (seen.Add(normalized))
            {
                hotwords.Add(normalized);
                added++;
            }
            else
            {
                skipped++;
            }
        }

        return new HotwordMergeResult(hotwords, added, skipped);
    }

    private static string NormalizeHotword(string hotword)
    {
        return hotword.Trim();
    }

    private DatabaseImportResult ImportDatabaseEntries(DomainPreset preset, string? defaultJobId)
    {
        if (preset.CorrectionMemory.Count == 0 &&
            preset.SpeakerAliases.Count == 0 &&
            preset.ReviewGuidelines.Count == 0)
        {
            return new DatabaseImportResult(0, 0, 0, 0, 0, 0, 0);
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.Now.ToString("o");
        var addedMemory = 0;
        var updatedMemory = 0;
        foreach (var entry in preset.CorrectionMemory)
        {
            if (string.IsNullOrWhiteSpace(entry.WrongText) ||
                string.IsNullOrWhiteSpace(entry.CorrectText))
            {
                continue;
            }

            var wrongText = entry.WrongText?.Trim();
            var correctText = entry.CorrectText?.Trim();
            if (wrongText is null || correctText is null)
            {
                continue;
            }

            var result = UpsertCorrectionMemory(connection, transaction, entry, wrongText, correctText, now);
            if (result == UpsertResult.Inserted)
            {
                addedMemory++;
            }
            else
            {
                updatedMemory++;
            }

            if (IsAsrHotwordCandidate(correctText))
            {
                UpsertUserTerm(connection, transaction, correctText, now);
            }
        }

        var addedAlias = 0;
        var updatedAlias = 0;
        var skippedAlias = 0;
        foreach (var alias in preset.SpeakerAliases)
        {
            var jobId = string.IsNullOrWhiteSpace(alias.JobId) ? defaultJobId : alias.JobId;
            if (string.IsNullOrWhiteSpace(jobId) ||
                string.IsNullOrWhiteSpace(alias.SpeakerId) ||
                string.IsNullOrWhiteSpace(alias.DisplayName))
            {
                skippedAlias++;
                continue;
            }

            var speakerId = alias.SpeakerId?.Trim();
            var displayName = alias.DisplayName?.Trim();
            if (speakerId is null || displayName is null)
            {
                skippedAlias++;
                continue;
            }

            var result = UpsertSpeakerAlias(connection, transaction, jobId.Trim(), speakerId, displayName, now);
            if (result == UpsertResult.Inserted)
            {
                addedAlias++;
            }
            else
            {
                updatedAlias++;
            }
        }

        var addedReviewGuideline = 0;
        var updatedReviewGuideline = 0;
        foreach (var guideline in preset.ReviewGuidelines)
        {
            if (string.IsNullOrWhiteSpace(guideline))
            {
                continue;
            }

            var result = ReviewGuidelineRepository.Upsert(
                connection,
                transaction,
                preset.GuidelinePresetId,
                guideline.Trim(),
                now);
            if (result == UpsertResult.Inserted)
            {
                addedReviewGuideline++;
            }
            else
            {
                updatedReviewGuideline++;
            }
        }

        if (preset.CorrectionMemory.Count == 0 &&
            preset.ReviewGuidelines.Count == 0 &&
            preset.SpeakerAliases.Count > 0 &&
            addedAlias == 0 &&
            updatedAlias == 0 &&
            skippedAlias > 0)
        {
            throw new InvalidDataException("話者別名を適用するジョブがありません。ジョブを選択してからインポートするか、プリセットに job_id を指定してください。");
        }

        transaction.Commit();
        return new DatabaseImportResult(
            addedMemory,
            updatedMemory,
            addedAlias,
            updatedAlias,
            skippedAlias,
            addedReviewGuideline,
            updatedReviewGuideline);
    }

    private void RecordImportHistory(
        string presetPath,
        DomainPreset preset,
        bool contextUpdated,
        HotwordMergeResult hotwordMerge,
        DatabaseImportResult databaseResult)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO domain_preset_imports (
                import_id,
                preset_id,
                display_name,
                schema_version,
                source_path,
                context_updated,
                added_hotword_count,
                skipped_hotword_count,
                added_correction_memory_count,
                updated_correction_memory_count,
                added_speaker_alias_count,
                updated_speaker_alias_count,
                skipped_speaker_alias_count,
                added_review_guideline_count,
                updated_review_guideline_count,
                imported_at
            )
            VALUES (
                $import_id,
                $preset_id,
                $display_name,
                $schema_version,
                $source_path,
                $context_updated,
                $added_hotword_count,
                $skipped_hotword_count,
                $added_correction_memory_count,
                $updated_correction_memory_count,
                $added_speaker_alias_count,
                $updated_speaker_alias_count,
                $skipped_speaker_alias_count,
                $added_review_guideline_count,
                $updated_review_guideline_count,
                $imported_at
            );
            """;
        command.Parameters.AddWithValue("$import_id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$preset_id", (object?)preset.NormalizedPresetId ?? DBNull.Value);
        command.Parameters.AddWithValue("$display_name", preset.DisplayName);
        command.Parameters.AddWithValue("$schema_version", preset.SchemaVersion);
        command.Parameters.AddWithValue("$source_path", Path.GetFullPath(presetPath));
        command.Parameters.AddWithValue("$context_updated", contextUpdated ? 1 : 0);
        command.Parameters.AddWithValue("$added_hotword_count", hotwordMerge.AddedCount);
        command.Parameters.AddWithValue("$skipped_hotword_count", hotwordMerge.SkippedCount);
        command.Parameters.AddWithValue("$added_correction_memory_count", databaseResult.AddedCorrectionMemoryCount);
        command.Parameters.AddWithValue("$updated_correction_memory_count", databaseResult.UpdatedCorrectionMemoryCount);
        command.Parameters.AddWithValue("$added_speaker_alias_count", databaseResult.AddedSpeakerAliasCount);
        command.Parameters.AddWithValue("$updated_speaker_alias_count", databaseResult.UpdatedSpeakerAliasCount);
        command.Parameters.AddWithValue("$skipped_speaker_alias_count", databaseResult.SkippedSpeakerAliasCount);
        command.Parameters.AddWithValue("$added_review_guideline_count", databaseResult.AddedReviewGuidelineCount);
        command.Parameters.AddWithValue("$updated_review_guideline_count", databaseResult.UpdatedReviewGuidelineCount);
        command.Parameters.AddWithValue("$imported_at", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();
    }

    private static int DisableCorrectionMemory(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string wrongText,
        string correctText,
        string scope)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE correction_memory
            SET enabled = 0,
                updated_at = $updated_at
            WHERE wrong_text = $wrong_text
                AND correct_text = $correct_text
                AND scope = $scope
                AND enabled = 1
                AND accepted_count <= 1
                AND rejected_count = 0;
            """;
        command.Parameters.AddWithValue("$wrong_text", wrongText);
        command.Parameters.AddWithValue("$correct_text", correctText);
        command.Parameters.AddWithValue("$scope", scope);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        return command.ExecuteNonQuery();
    }

    private static int DisableUserTerm(SqliteConnection connection, SqliteTransaction transaction, string surface)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE user_terms
            SET enabled = 0,
                updated_at = $updated_at
            WHERE surface = $surface
                AND category = 'domain_preset'
                AND enabled = 1;
            """;
        command.Parameters.AddWithValue("$surface", surface);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        return command.ExecuteNonQuery();
    }

    private static int DeleteSpeakerAlias(SqliteConnection connection, SqliteTransaction transaction, string jobId, string speakerId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM speaker_aliases
            WHERE job_id = $job_id
                AND speaker_id = $speaker_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$speaker_id", speakerId);
        return command.ExecuteNonQuery();
    }

    private static int DisableReviewGuidelines(SqliteConnection connection, SqliteTransaction transaction, string presetId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE review_guidelines
            SET enabled = 0,
                updated_at = $updated_at
            WHERE preset_id = $preset_id
                AND enabled = 1;
            """;
        command.Parameters.AddWithValue("$preset_id", presetId);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        return command.ExecuteNonQuery();
    }

    private static void MarkPresetImportsCleared(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string presetId,
        string deactivatedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE domain_preset_imports
            SET deactivated_at = $deactivated_at
            WHERE COALESCE(preset_id, display_name) = $preset_id
                AND deactivated_at IS NULL;
            """;
        command.Parameters.AddWithValue("$preset_id", presetId);
        command.Parameters.AddWithValue("$deactivated_at", deactivatedAt);
        command.ExecuteNonQuery();
    }

    private static UpsertResult UpsertCorrectionMemory(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CorrectionMemoryPresetEntry entry,
        string wrongText,
        string correctText,
        string now)
    {
        var scope = string.IsNullOrWhiteSpace(entry.Scope) ? "global" : entry.Scope.Trim();
        var exists = CorrectionMemoryExists(connection, transaction, wrongText, correctText, scope);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO correction_memory (
                memory_id,
                wrong_text,
                correct_text,
                issue_type,
                scope,
                accepted_count,
                rejected_count,
                enabled,
                created_at,
                updated_at
            )
            VALUES (
                $memory_id,
                $wrong_text,
                $correct_text,
                $issue_type,
                $scope,
                1,
                0,
                1,
                $now,
                $now
            )
            ON CONFLICT(wrong_text, correct_text, scope) DO UPDATE SET
                issue_type = excluded.issue_type,
                enabled = 1,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$memory_id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$wrong_text", wrongText);
        command.Parameters.AddWithValue("$correct_text", correctText);
        command.Parameters.AddWithValue("$issue_type", string.IsNullOrWhiteSpace(entry.IssueType) ? "domain_preset" : entry.IssueType.Trim());
        command.Parameters.AddWithValue("$scope", scope);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
        return exists ? UpsertResult.Updated : UpsertResult.Inserted;
    }

    private static bool CorrectionMemoryExists(SqliteConnection connection, SqliteTransaction transaction, string wrongText, string correctText, string scope)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM correction_memory
            WHERE wrong_text = $wrong_text
                AND correct_text = $correct_text
                AND scope = $scope;
            """;
        command.Parameters.AddWithValue("$wrong_text", wrongText);
        command.Parameters.AddWithValue("$correct_text", correctText);
        command.Parameters.AddWithValue("$scope", scope);
        return command.ExecuteScalar() is not null;
    }

    private static UpsertResult UpsertSpeakerAlias(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        string speakerId,
        string displayName,
        string now)
    {
        var exists = SpeakerAliasExists(connection, transaction, jobId, speakerId);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO speaker_aliases (job_id, speaker_id, display_name, updated_at)
            VALUES ($job_id, $speaker_id, $display_name, $updated_at)
            ON CONFLICT(job_id, speaker_id) DO UPDATE SET
                display_name = excluded.display_name,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$speaker_id", speakerId);
        command.Parameters.AddWithValue("$display_name", displayName);
        command.Parameters.AddWithValue("$updated_at", now);
        command.ExecuteNonQuery();
        return exists ? UpsertResult.Updated : UpsertResult.Inserted;
    }

    private static bool SpeakerAliasExists(SqliteConnection connection, SqliteTransaction transaction, string jobId, string speakerId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM speaker_aliases
            WHERE job_id = $job_id AND speaker_id = $speaker_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$speaker_id", speakerId);
        return command.ExecuteScalar() is not null;
    }

    private static void UpsertUserTerm(SqliteConnection connection, SqliteTransaction transaction, string surface, string now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO user_terms (
                term_id,
                surface,
                category,
                enabled,
                created_at,
                updated_at
            )
            VALUES (
                $term_id,
                $surface,
                'domain_preset',
                1,
                $now,
                $now
            )
            ON CONFLICT(surface, category) DO UPDATE SET
                enabled = 1,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$term_id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$surface", surface);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
    }

    private static bool IsAsrHotwordCandidate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > MaxAsrHotwordLength)
        {
            return false;
        }

        return !trimmed.Any(static character =>
            char.IsWhiteSpace(character) ||
            char.IsPunctuation(character) ||
            char.IsSymbol(character));
    }

    private sealed record HotwordMergeResult(IReadOnlyList<string> Hotwords, int AddedCount, int SkippedCount);

    private sealed record DatabaseImportResult(
        int AddedCorrectionMemoryCount,
        int UpdatedCorrectionMemoryCount,
        int AddedSpeakerAliasCount,
        int UpdatedSpeakerAliasCount,
        int SkippedSpeakerAliasCount,
        int AddedReviewGuidelineCount,
        int UpdatedReviewGuidelineCount);

    private sealed record DomainPreset(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("preset_id")] string? PresetId,
        [property: JsonPropertyName("display_name")] string? DisplayNameValue,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("domain")] string? Domain,
        [property: JsonPropertyName("asr_context")] string? AsrContext,
        [property: JsonPropertyName("hotwords")] IReadOnlyList<string>? HotwordsValue,
        [property: JsonPropertyName("correction_memory")] IReadOnlyList<CorrectionMemoryPresetEntry>? CorrectionMemoryValue,
        [property: JsonPropertyName("speaker_aliases")] IReadOnlyList<SpeakerAliasPresetEntry>? SpeakerAliasesValue,
        [property: JsonPropertyName("review_guidelines")] IReadOnlyList<string>? ReviewGuidelinesValue)
    {
        [JsonIgnore]
        public string DisplayName => string.IsNullOrWhiteSpace(DisplayNameValue)
            ? PresetId ?? Domain ?? "domain preset"
            : DisplayNameValue.Trim();

        [JsonIgnore]
        public string? NormalizedPresetId => string.IsNullOrWhiteSpace(PresetId)
            ? null
            : PresetId.Trim();

        [JsonIgnore]
        public string GuidelinePresetId => NormalizedPresetId ?? DisplayName;

        [JsonIgnore]
        public IReadOnlyList<string> Hotwords => HotwordsValue ?? [];

        [JsonIgnore]
        public IReadOnlyList<CorrectionMemoryPresetEntry> CorrectionMemory => CorrectionMemoryValue ?? [];

        [JsonIgnore]
        public IReadOnlyList<SpeakerAliasPresetEntry> SpeakerAliases => SpeakerAliasesValue ?? [];

        [JsonIgnore]
        public IReadOnlyList<string> ReviewGuidelines => ReviewGuidelinesValue ?? [];

        public void Validate()
        {
            if (SchemaVersion != SupportedSchemaVersion)
            {
                throw new InvalidDataException($"未対応のプリセット schema_version です: {SchemaVersion}");
            }

            if (string.IsNullOrWhiteSpace(AsrContext) &&
                !Hotwords.Any(static hotword => !string.IsNullOrWhiteSpace(hotword)) &&
                !CorrectionMemory.Any(static entry => !string.IsNullOrWhiteSpace(entry.WrongText) && !string.IsNullOrWhiteSpace(entry.CorrectText)) &&
                !SpeakerAliases.Any(static entry => !string.IsNullOrWhiteSpace(entry.SpeakerId) && !string.IsNullOrWhiteSpace(entry.DisplayName)) &&
                !ReviewGuidelines.Any(static guideline => !string.IsNullOrWhiteSpace(guideline)))
            {
                throw new InvalidDataException("プリセットには asr_context、hotwords、correction_memory、speaker_aliases、review_guidelines のいずれかが必要です。");
            }
        }
    }

    private sealed record CorrectionMemoryPresetEntry(
        [property: JsonPropertyName("wrong_text")] string? WrongText,
        [property: JsonPropertyName("correct_text")] string? CorrectText,
        [property: JsonPropertyName("issue_type")] string? IssueType,
        [property: JsonPropertyName("scope")] string? Scope);

    private sealed record SpeakerAliasPresetEntry(
        [property: JsonPropertyName("job_id")] string? JobId,
        [property: JsonPropertyName("speaker_id")] string? SpeakerId,
        [property: JsonPropertyName("display_name")] string? DisplayName);
}
