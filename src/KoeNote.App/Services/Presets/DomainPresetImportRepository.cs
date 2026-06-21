using System.IO;
using KoeNote.App.Services.Asr;

namespace KoeNote.App.Services.Presets;

internal sealed class DomainPresetImportRepository(AppPaths paths)
{
    private readonly DomainPresetImportHistoryRepository _historyRepository = new(paths);
    private readonly DomainPresetSpeakerAliasRepository _speakerAliasRepository = new();
    private readonly DomainPresetTermRepository _termRepository = new();

    public IReadOnlyList<DomainPresetImportHistoryItem> LoadHistory(int limit) =>
        _historyRepository.Load(limit);

    public DomainPresetImportHistoryItem? LoadHistoryById(string importId) =>
        _historyRepository.LoadById(importId);

    public DomainPresetDatabaseImportResult ImportDatabaseEntries(DomainPreset preset, string importId, string? defaultJobId)
    {
        if (preset.CorrectionMemory.Count == 0 &&
            preset.SpeakerAliases.Count == 0 &&
            preset.ReviewGuidelines.Count == 0)
        {
            return new DomainPresetDatabaseImportResult(0, 0, 0, 0, 0, 0, 0);
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.Now.ToString("o");
        var termResult = _termRepository.Import(connection, transaction, preset, now);

        var aliasResult = _speakerAliasRepository.Import(
            connection,
            transaction,
            preset.SpeakerAliases,
            importId,
            defaultJobId,
            now);

        if (preset.CorrectionMemory.Count == 0 &&
            preset.ReviewGuidelines.Count == 0 &&
            preset.SpeakerAliases.Count > 0 &&
            aliasResult.AddedSpeakerAliasCount == 0 &&
            aliasResult.UpdatedSpeakerAliasCount == 0 &&
            aliasResult.SkippedSpeakerAliasCount > 0)
        {
            throw new InvalidDataException("話者別名を適用するジョブがありません。ジョブを選択してからインポートするか、プリセットに job_id を指定してください。");
        }

        transaction.Commit();
        return new DomainPresetDatabaseImportResult(
            termResult.AddedCorrectionMemoryCount,
            termResult.UpdatedCorrectionMemoryCount,
            aliasResult.AddedSpeakerAliasCount,
            aliasResult.UpdatedSpeakerAliasCount,
            aliasResult.SkippedSpeakerAliasCount,
            termResult.AddedReviewGuidelineCount,
            termResult.UpdatedReviewGuidelineCount);
    }

    public void RecordImportHistory(
        string importId,
        string presetPath,
        DomainPreset preset,
        bool contextUpdated,
        DomainPresetHotwordMergeResult hotwordMerge,
        DomainPresetDatabaseImportResult databaseResult) =>
        _historyRepository.Record(importId, presetPath, preset, contextUpdated, hotwordMerge, databaseResult);

    public DomainPresetDatabaseClearResult ClearImportedDatabaseEntries(
        DomainPreset? preset,
        DomainPresetImportHistoryItem history,
        string presetId)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();
        var termClear = _termRepository.Clear(connection, transaction, preset, presetId);

        var deletedAliases = _speakerAliasRepository.RevertTracked(connection, transaction, history.ImportId);
        _historyRepository.MarkCleared(connection, transaction, history.ImportId, DateTimeOffset.Now.ToString("o"));
        transaction.Commit();
        return new DomainPresetDatabaseClearResult(
            termClear.DisabledCorrectionMemoryCount,
            termClear.DisabledUserTermCount,
            deletedAliases,
            termClear.DisabledReviewGuidelineCount);
    }

}

internal sealed record DomainPresetDatabaseImportResult(
    int AddedCorrectionMemoryCount,
    int UpdatedCorrectionMemoryCount,
    int AddedSpeakerAliasCount,
    int UpdatedSpeakerAliasCount,
    int SkippedSpeakerAliasCount,
    int AddedReviewGuidelineCount,
    int UpdatedReviewGuidelineCount);

internal sealed record DomainPresetDatabaseClearResult(
    int DisabledCorrectionMemoryCount,
    int DisabledUserTermCount,
    int DeletedSpeakerAliasCount,
    int DisabledReviewGuidelineCount);
