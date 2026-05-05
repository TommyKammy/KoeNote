using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Presets;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Tests;

public sealed class DomainPresetImportServiceTests
{
    [Fact]
    public void Import_MergesAsrContextAndHotwordsIdempotently()
    {
        var root = CreateRoot();
        var paths = CreatePaths(root);
        var repository = new AsrSettingsRepository(paths);
        repository.Save(new AsrSettings("existing context", "postpartum care\r\nexisting term", "kotoba-whisper-v2.2-faster", true));
        var presetPath = Path.Combine(root, "preset.json");
        File.WriteAllText(presetPath, """
            {
              "schema_version": 1,
              "preset_id": "obstetrics-postpartum-care",
              "display_name": "Obstetrics preset",
              "asr_context": "obstetrics interview context",
              "hotwords": ["postpartum care", "midwife", "EPDS"]
            }
            """);
        var service = new DomainPresetImportService(paths, repository);

        var first = service.Import(presetPath);
        var second = service.Import(presetPath);

        var settings = repository.Load();
        Assert.Contains("existing context", settings.ContextText, StringComparison.Ordinal);
        Assert.Contains("obstetrics interview context", settings.ContextText, StringComparison.Ordinal);
        Assert.Equal(["postpartum care", "existing term", "midwife", "EPDS"], settings.Hotwords);
        Assert.True(first.ContextUpdated);
        Assert.Equal(2, first.AddedHotwordCount);
        Assert.False(second.ContextUpdated);
        Assert.Equal(0, second.AddedHotwordCount);
        Assert.Equal(3, second.SkippedHotwordCount);
    }

    [Fact]
    public void Import_RejectsUnsupportedSchemaVersion()
    {
        var root = CreateRoot();
        var paths = CreatePaths(root);
        var presetPath = Path.Combine(root, "preset.json");
        File.WriteAllText(presetPath, """
            {
              "schema_version": 99,
              "asr_context": "unsupported"
            }
            """);
        var service = new DomainPresetImportService(paths, new AsrSettingsRepository(paths));

        var exception = Assert.Throws<InvalidDataException>(() => service.Import(presetPath));
        Assert.Contains("schema_version", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RejectsPresetWithOnlyBlankHotwords()
    {
        var root = CreateRoot();
        var paths = CreatePaths(root);
        var presetPath = Path.Combine(root, "preset.json");
        File.WriteAllText(presetPath, """
            {
              "schema_version": 1,
              "hotwords": [" ", "\t"]
            }
            """);
        var service = new DomainPresetImportService(paths, new AsrSettingsRepository(paths));

        var exception = Assert.Throws<InvalidDataException>(() => service.Import(presetPath));
        Assert.Contains("asr_context", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_UpsertsCorrectionMemoryAndSpeakerAliases()
    {
        var root = CreateRoot();
        var paths = CreatePaths(root);
        var presetPath = Path.Combine(root, "preset.json");
        File.WriteAllText(presetPath, """
            {
              "schema_version": 1,
              "correction_memory": [
                {
                  "wrong_text": "wrong-term",
                  "correct_text": "correctterm",
                  "issue_type": "domain-term"
                }
              ],
              "speaker_aliases": [
                {
                  "speaker_id": "Speaker_0",
                  "display_name": "Interviewer"
                },
                {
                  "speaker_id": "Speaker_1",
                  "display_name": "Participant"
                }
              ]
            }
            """);
        var service = new DomainPresetImportService(paths, new AsrSettingsRepository(paths));

        var first = service.Import(presetPath, "job-001");
        var second = service.Import(presetPath, "job-001");

        Assert.Equal(1, first.AddedCorrectionMemoryCount);
        Assert.Equal(0, first.UpdatedCorrectionMemoryCount);
        Assert.Equal(2, first.AddedSpeakerAliasCount);
        Assert.Equal(0, first.SkippedSpeakerAliasCount);
        Assert.Equal(0, second.AddedCorrectionMemoryCount);
        Assert.Equal(1, second.UpdatedCorrectionMemoryCount);
        Assert.Equal(0, second.AddedSpeakerAliasCount);
        Assert.Equal(2, second.UpdatedSpeakerAliasCount);

        using var connection = OpenConnection(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM correction_memory WHERE wrong_text = 'wrong-term' AND correct_text = 'correctterm'),
                (SELECT COUNT(*) FROM user_terms WHERE surface = 'correctterm' AND category = 'domain_preset'),
                (SELECT COUNT(*) FROM speaker_aliases WHERE job_id = 'job-001');
            """;
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.Equal(2, reader.GetInt32(2));
    }

    [Fact]
    public void Import_RejectsAliasOnlyPresetWithoutJobIdOrDefaultJob()
    {
        var root = CreateRoot();
        var paths = CreatePaths(root);
        var presetPath = Path.Combine(root, "preset.json");
        File.WriteAllText(presetPath, """
            {
              "schema_version": 1,
              "speaker_aliases": [
                {
                  "speaker_id": "Speaker_0",
                  "display_name": "Interviewer"
                }
              ]
            }
            """);
        var service = new DomainPresetImportService(paths, new AsrSettingsRepository(paths));

        var exception = Assert.Throws<InvalidDataException>(() => service.Import(presetPath));

        Assert.Contains("ジョブ", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_UpsertsReviewGuidelinesAndRecordsHistory()
    {
        var root = CreateRoot();
        var paths = CreatePaths(root);
        var presetPath = Path.Combine(root, "preset.json");
        File.WriteAllText(presetPath, """
            {
              "schema_version": 1,
              "preset_id": "nursing-domain",
              "display_name": "Nursing preset",
              "review_guidelines": [
                "Keep domain terms unchanged when they are already natural."
              ]
            }
            """);
        var service = new DomainPresetImportService(paths, new AsrSettingsRepository(paths));

        var first = service.Import(presetPath);
        var second = service.Import(presetPath);

        Assert.Equal(1, first.AddedReviewGuidelineCount);
        Assert.Equal(0, first.UpdatedReviewGuidelineCount);
        Assert.Equal(0, second.AddedReviewGuidelineCount);
        Assert.Equal(1, second.UpdatedReviewGuidelineCount);

        using var connection = OpenConnection(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM review_guidelines WHERE preset_id = 'nursing-domain'),
                (SELECT COUNT(*) FROM domain_preset_imports WHERE preset_id = 'nursing-domain'),
                (SELECT added_review_guideline_count FROM domain_preset_imports WHERE preset_id = 'nursing-domain' ORDER BY imported_at LIMIT 1);
            """;
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(2, reader.GetInt32(1));
        Assert.Equal(1, reader.GetInt32(2));
    }

    [Fact]
    public void ReviewPromptBuilder_IncludesImportedReviewGuidelines()
    {
        var root = CreateRoot();
        var paths = CreatePaths(root);
        var presetPath = Path.Combine(root, "preset.json");
        File.WriteAllText(presetPath, """
            {
              "schema_version": 1,
              "preset_id": "nursing-domain",
              "review_guidelines": [
                "Keep participant wording unless an ASR error is obvious."
              ]
            }
            """);
        new DomainPresetImportService(paths, new AsrSettingsRepository(paths)).Import(presetPath);
        var builder = new ReviewPromptBuilder(new ReviewGuidelineRepository(paths));

        var prompt = builder.Build([
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "sample")
        ]);

        Assert.Contains("専門領域レビュー指針", prompt, StringComparison.Ordinal);
        Assert.Contains("Keep participant wording unless an ASR error is obvious.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewGuidelineRepository_LoadEnabled_DeduplicatesSharedGuidelines()
    {
        var root = CreateRoot();
        var paths = CreatePaths(root);
        var firstPresetPath = Path.Combine(root, "preset-1.json");
        var secondPresetPath = Path.Combine(root, "preset-2.json");
        File.WriteAllText(firstPresetPath, """
            {
              "schema_version": 1,
              "preset_id": "first-domain",
              "review_guidelines": [
                "Keep domain terms unchanged."
              ]
            }
            """);
        File.WriteAllText(secondPresetPath, """
            {
              "schema_version": 1,
              "preset_id": "second-domain",
              "review_guidelines": [
                "Keep domain terms unchanged."
              ]
            }
            """);
        var service = new DomainPresetImportService(paths, new AsrSettingsRepository(paths));
        service.Import(firstPresetPath);
        service.Import(secondPresetPath);

        var guidelines = new ReviewGuidelineRepository(paths).LoadEnabled();

        Assert.Equal(["Keep domain terms unchanged."], guidelines);
    }

    [Fact]
    public void ClearImport_DisablesPresetDataAndMarksHistory()
    {
        var root = CreateRoot();
        var paths = CreatePaths(root);
        var repository = new AsrSettingsRepository(paths);
        var presetPath = Path.Combine(root, "preset.json");
        File.WriteAllText(presetPath, """
            {
              "schema_version": 1,
              "preset_id": "clearable-domain",
              "display_name": "Clearable preset",
              "asr_context": "clearable context",
              "hotwords": ["clearword"],
              "correction_memory": [
                {
                  "wrong_text": "wrong clear",
                  "correct_text": "clearword"
                }
              ],
              "review_guidelines": [
                "Clearable guideline"
              ]
            }
            """);
        var service = new DomainPresetImportService(paths, repository);
        service.Import(presetPath);
        var history = service.LoadHistory().Single();

        var result = service.ClearImport(history.ImportId);

        Assert.True(result.PresetFileLoaded);
        Assert.True(result.ContextRemoved);
        Assert.Equal(1, result.RemovedHotwordCount);
        Assert.Equal(1, result.DisabledCorrectionMemoryCount);
        Assert.Equal(1, result.DisabledUserTermCount);
        Assert.Equal(1, result.DisabledReviewGuidelineCount);
        Assert.DoesNotContain("clearable context", repository.Load().ContextText, StringComparison.Ordinal);
        Assert.DoesNotContain("clearword", repository.Load().Hotwords);
        Assert.NotNull(service.LoadHistory().Single().DeactivatedAt);

        using var connection = OpenConnection(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT enabled FROM correction_memory WHERE wrong_text = 'wrong clear' AND correct_text = 'clearword'),
                (SELECT enabled FROM user_terms WHERE surface = 'clearword' AND category = 'domain_preset'),
                (SELECT enabled FROM review_guidelines WHERE preset_id = 'clearable-domain');
            """;
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.Equal(0, reader.GetInt32(1));
        Assert.Equal(0, reader.GetInt32(2));
    }

    [Fact]
    public void ClearImport_DoesNotRemoveAsrValuesThatWereAlreadyPresent()
    {
        var root = CreateRoot();
        var paths = CreatePaths(root);
        var repository = new AsrSettingsRepository(paths);
        repository.Save(new AsrSettings("shared context", "sharedword", "kotoba-whisper-v2.2-faster", true));
        var presetPath = Path.Combine(root, "preset.json");
        File.WriteAllText(presetPath, """
            {
              "schema_version": 1,
              "preset_id": "shared-domain",
              "display_name": "Shared preset",
              "asr_context": "shared context",
              "hotwords": ["sharedword"]
            }
            """);
        var service = new DomainPresetImportService(paths, repository);
        service.Import(presetPath);
        var history = service.LoadHistory().Single();

        var result = service.ClearImport(history.ImportId);

        Assert.False(result.ContextRemoved);
        Assert.Equal(0, result.RemovedHotwordCount);
        Assert.Equal("shared context", repository.Load().ContextText);
        Assert.Equal(["sharedword"], repository.Load().Hotwords);
        Assert.NotNull(service.LoadHistory().Single().DeactivatedAt);
    }

    [Fact]
    public void ClearImport_RejectsAlreadyClearedHistory()
    {
        var root = CreateRoot();
        var paths = CreatePaths(root);
        var repository = new AsrSettingsRepository(paths);
        var presetPath = Path.Combine(root, "preset.json");
        File.WriteAllText(presetPath, """
            {
              "schema_version": 1,
              "preset_id": "clear-once-domain",
              "display_name": "Clear once preset",
              "review_guidelines": ["Clear once guideline"]
            }
            """);
        var service = new DomainPresetImportService(paths, repository);
        service.Import(presetPath);
        var history = service.LoadHistory().Single();
        service.ClearImport(history.ImportId);

        var exception = Assert.Throws<InvalidDataException>(() => service.ClearImport(history.ImportId));

        Assert.Contains("クリア済み", exception.Message, StringComparison.Ordinal);
    }

    private static string CreateRoot()
    {
        return Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
    }

    private static AppPaths CreatePaths(string root)
    {
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        return paths;
    }

    private static Microsoft.Data.Sqlite.SqliteConnection OpenConnection(AppPaths paths)
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();
        return connection;
    }
}
