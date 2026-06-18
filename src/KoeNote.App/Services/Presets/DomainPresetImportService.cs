using System.IO;
using System.Text;
using KoeNote.App.Services.Asr;

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

public sealed record DomainPresetPreview(
    string SourcePath,
    string DisplayName,
    string? PresetId,
    int SchemaVersion,
    string Details,
    bool HasAsrContext,
    int HotwordCount,
    int CorrectionMemoryCount,
    int SpeakerAliasCount,
    int ReviewGuidelineCount)
{
    public string Summary =>
        $"読み込み済み: {DisplayName} / ホットワード {HotwordCount}件 / 補正メモリ {CorrectionMemoryCount}件 / 話者別名 {SpeakerAliasCount}件 / レビュー指針 {ReviewGuidelineCount}件";
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
    private readonly DomainPresetAsrSettingsMerger _asrSettingsMerger = new();
    private readonly DomainPresetImportRepository _repository = new(paths);
    private readonly DomainPresetParser _presetParser = new();

    public DomainPresetPreview LoadPreview(string presetPath)
    {
        var preset = _presetParser.Load(presetPath);
        return new DomainPresetPreview(
            Path.GetFullPath(presetPath),
            preset.DisplayName,
            preset.NormalizedPresetId,
            preset.SchemaVersion,
            BuildPreviewDetails(preset),
            !string.IsNullOrWhiteSpace(preset.AsrContext),
            preset.Hotwords.Count(static hotword => !string.IsNullOrWhiteSpace(hotword)),
            preset.CorrectionMemory.Count(static entry => !string.IsNullOrWhiteSpace(entry.WrongText) && !string.IsNullOrWhiteSpace(entry.CorrectText)),
            preset.SpeakerAliases.Count(static entry => !string.IsNullOrWhiteSpace(entry.SpeakerId) && !string.IsNullOrWhiteSpace(entry.DisplayName)),
            preset.ReviewGuidelines.Count(static guideline => !string.IsNullOrWhiteSpace(guideline)));
    }

    public DomainPresetImportResult Import(string presetPath, string? defaultJobId = null)
    {
        var preset = _presetParser.Load(presetPath);
        var current = asrSettingsRepository.Load();
        var nextContext = _asrSettingsMerger.MergeContext(current.ContextText, preset.AsrContext);
        var hotwordMerge = _asrSettingsMerger.MergeHotwords(current.Hotwords, preset.Hotwords);
        var next = current with
        {
            ContextText = nextContext,
            HotwordsText = string.Join(Environment.NewLine, hotwordMerge.Hotwords)
        };

        var importId = Guid.NewGuid().ToString("N");
        var databaseResult = _repository.ImportDatabaseEntries(preset, importId, defaultJobId);
        asrSettingsRepository.Save(next);

        var contextUpdated = !string.Equals(current.ContextText, nextContext, StringComparison.Ordinal);
        _repository.RecordImportHistory(importId, presetPath, preset, contextUpdated, hotwordMerge, databaseResult);

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
        return _repository.LoadHistory(limit);
    }

    public DomainPresetClearResult ClearImport(string importId)
    {
        if (string.IsNullOrWhiteSpace(importId))
        {
            throw new ArgumentException("プリセット履歴を選択してください。", nameof(importId));
        }

        var history = _repository.LoadHistoryById(importId)
            ?? throw new InvalidDataException("プリセット履歴が見つかりません。");

        if (history.DeactivatedAt is not null)
        {
            throw new InvalidDataException("このプリセット履歴はすでにクリア済みです。");
        }

        var preset = _presetParser.TryLoad(history.SourcePath);
        var presetId = preset?.NormalizedPresetId ?? history.PresetId ?? history.DisplayName;
        var contextRemoved = false;
        var removedHotwords = 0;

        if (preset is not null)
        {
            var current = asrSettingsRepository.Load();
            var nextContext = history.ContextUpdated
                ? _asrSettingsMerger.RemoveContextBlock(current.ContextText, preset.AsrContext)
                : current.ContextText.Trim();
            var nextHotwords = _asrSettingsMerger.ShouldRemovePresetHotwords(history.AddedHotwordCount, preset.Hotwords)
                ? _asrSettingsMerger.RemoveHotwords(current.Hotwords, preset.Hotwords, out removedHotwords)
                : current.Hotwords;
            contextRemoved = !string.Equals(current.ContextText.Trim(), nextContext.Trim(), StringComparison.Ordinal);
            if (contextRemoved || removedHotwords > 0)
            {
                asrSettingsRepository.Save(current with
                {
                    ContextText = nextContext,
                    HotwordsText = string.Join(Environment.NewLine, nextHotwords)
                });
            }
        }

        var databaseClear = _repository.ClearImportedDatabaseEntries(preset, history, presetId);

        return new DomainPresetClearResult(
            history.DisplayName,
            preset is not null,
            contextRemoved,
            removedHotwords,
            databaseClear.DisabledCorrectionMemoryCount,
            databaseClear.DisabledUserTermCount,
            databaseClear.DeletedSpeakerAliasCount,
            databaseClear.DisabledReviewGuidelineCount);
    }

    private static string BuildPreviewDetails(DomainPreset preset)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"表示名: {preset.DisplayName}");
        if (!string.IsNullOrWhiteSpace(preset.NormalizedPresetId))
        {
            builder.AppendLine($"プリセットID: {preset.NormalizedPresetId}");
        }

        if (!string.IsNullOrWhiteSpace(preset.Domain))
        {
            builder.AppendLine($"ドメイン: {preset.Domain.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(preset.Description))
        {
            builder.AppendLine();
            builder.AppendLine("説明:");
            builder.AppendLine(preset.Description.Trim());
        }

        if (!string.IsNullOrWhiteSpace(preset.AsrContext))
        {
            builder.AppendLine();
            builder.AppendLine("ASRコンテキスト:");
            builder.AppendLine(preset.AsrContext.Trim());
        }

        AppendPreviewSection(
            builder,
            "ホットワード",
            preset.Hotwords
                .Select(static hotword => hotword.Trim())
                .Where(static hotword => hotword.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase));

        AppendPreviewSection(
            builder,
            "補正メモリ",
            preset.CorrectionMemory
                .Where(static entry => !string.IsNullOrWhiteSpace(entry.WrongText) && !string.IsNullOrWhiteSpace(entry.CorrectText))
                .Select(static entry =>
                {
                    var issueType = string.IsNullOrWhiteSpace(entry.IssueType) ? "domain_preset" : entry.IssueType.Trim();
                    var scope = string.IsNullOrWhiteSpace(entry.Scope) ? "global" : entry.Scope.Trim();
                    return $"{entry.WrongText!.Trim()} -> {entry.CorrectText!.Trim()} ({issueType}, {scope})";
                }));

        AppendPreviewSection(
            builder,
            "話者別名",
            preset.SpeakerAliases
                .Where(static entry => !string.IsNullOrWhiteSpace(entry.SpeakerId) && !string.IsNullOrWhiteSpace(entry.DisplayName))
                .Select(static entry =>
                {
                    var jobId = string.IsNullOrWhiteSpace(entry.JobId) ? "選択中ジョブ" : entry.JobId.Trim();
                    return $"{jobId}: {entry.SpeakerId!.Trim()} -> {entry.DisplayName!.Trim()}";
                }));

        AppendPreviewSection(
            builder,
            "レビュー指針",
            preset.ReviewGuidelines
                .Select(static guideline => guideline.Trim())
                .Where(static guideline => guideline.Length > 0));

        return builder.ToString().Trim();
    }

    private static void AppendPreviewSection(StringBuilder builder, string title, IEnumerable<string> values)
    {
        var items = values.ToArray();
        if (items.Length == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine($"{title}:");
        foreach (var item in items.Take(20))
        {
            builder.AppendLine($"- {item}");
        }

        if (items.Length > 20)
        {
            builder.AppendLine($"- ...ほか {items.Length - 20} 件");
        }
    }

}