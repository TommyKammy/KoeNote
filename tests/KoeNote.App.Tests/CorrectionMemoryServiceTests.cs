using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Review;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Tests;

public sealed class CorrectionMemoryServiceTests
{
    [Fact]
    public void RememberCorrection_StoresMemoryAndUserTerm()
    {
        var paths = CreateReadyPaths();
        var service = new CorrectionMemoryService(paths);
        var draft = new CorrectionDraft(
            "draft-001",
            "job-001",
            "segment-001",
            "term",
            "プロジェクトA",
            "KoeNote",
            "reason",
            0.82);

        service.RememberCorrection(draft, "KoeNote");

        using var connection = Open(paths);
        Assert.Equal(1, ReadCount(connection, "correction_memory"));
        Assert.Equal(1, ReadCount(connection, "user_terms"));
        Assert.Equal(1, ReadCount(connection, "correction_memory_events"));
    }

    [Fact]
    public void EnrichAsrSettings_AddsUserTermsToHotwords()
    {
        var paths = CreateReadyPaths();
        var service = new CorrectionMemoryService(paths);
        service.RememberCorrection(new CorrectionDraft(
            "draft-001",
            "job-001",
            "segment-001",
            "term",
            "旧表記",
            "KoeNote",
            "reason",
            0.82), "KoeNote");

        var settings = service.EnrichAsrSettings(new AsrSettings("context", "既存"));

        Assert.Contains("既存", settings.Hotwords);
        Assert.Contains("KoeNote", settings.Hotwords);
    }

    [Fact]
    public void EnrichAsrSettings_DoesNotAddLongTermsToHotwords()
    {
        var paths = CreateReadyPaths();
        var service = new CorrectionMemoryService(paths);
        var longTerm = new string('長', 41);
        service.RememberCorrection(new CorrectionDraft(
            "draft-001",
            "job-001",
            "segment-001",
            "term",
            "旧表記",
            longTerm,
            "reason",
            0.82), longTerm);

        var settings = service.EnrichAsrSettings(new AsrSettings("context", "既存"));

        Assert.Contains("既存", settings.Hotwords);
        Assert.DoesNotContain(longTerm, settings.Hotwords);
    }

    [Fact]
    public void BuildMemoryDrafts_CreatesPastCorrectionCandidate()
    {
        var paths = CreateReadyPaths();
        var service = new CorrectionMemoryService(paths);
        service.RememberCorrection(new CorrectionDraft(
            "draft-001",
            "job-001",
            "segment-001",
            "term",
            "旧表記",
            "KoeNote",
            "reason",
            0.82), "KoeNote");

        var drafts = service.BuildMemoryDrafts("job-002", [
            new TranscriptSegment("segment-002", "job-002", 0, 1, "Speaker_0", "旧表記の説明です")
        ]);

        var draft = Assert.Single(drafts);
        Assert.Equal("memory", draft.Source);
        Assert.Equal("過去修正候補", draft.IssueType);
        Assert.Equal("KoeNoteの説明です", draft.SuggestedText);
        Assert.NotNull(draft.SourceRefId);
    }

    [Fact]
    public void RecordDraftDecision_DoesNotRewardManuallyEditedMemorySuggestion()
    {
        var paths = CreateReadyPaths();
        var service = new CorrectionMemoryService(paths);
        service.RememberCorrection(new CorrectionDraft(
            "draft-001",
            "job-001",
            "segment-001",
            "term",
            "旧表記",
            "KoeNote",
            "reason",
            0.82), "KoeNote");
        var memoryDraft = Assert.Single(service.BuildMemoryDrafts("job-002", [
            new TranscriptSegment("segment-002", "job-002", 0, 1, "Speaker_0", "旧表記です")
        ]));

        service.RecordDraftDecision(memoryDraft, "manual_edit");
        service.RememberCorrection(memoryDraft with { Source = "llm", SourceRefId = null }, "KoeNoteです");

        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT accepted_count, rejected_count
            FROM correction_memory
            WHERE memory_id = $memory_id;
            """;
        command.Parameters.AddWithValue("$memory_id", memoryDraft.SourceRefId);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(0, reader.GetInt32(1));
        Assert.Equal(2, ReadCount(connection, "correction_memory"));
    }

    private static AppPaths CreateReadyPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var local = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, local);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        return paths;
    }

    private static SqliteConnection Open(AppPaths paths)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();
        return connection;
    }

    private static int ReadCount(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
