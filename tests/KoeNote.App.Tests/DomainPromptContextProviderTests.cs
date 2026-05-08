using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Presets;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Tests;

public sealed class DomainPromptContextProviderTests
{
    [Fact]
    public void LoadForSummary_LoadsEnabledPresetContext()
    {
        var paths = CreateReadyPaths();
        var presetPath = Path.Combine(paths.Root, "domain-preset.json");
        File.WriteAllText(presetPath, """
            {
              "schema_version": 1,
              "preset_id": "yamanashi-peach",
              "display_name": "山梨 桃農園",
              "hotwords": ["甲府盆地", "桃源郷"],
              "correction_memory": [
                {
                  "wrong_text": "幸福盆地",
                  "correct_text": "甲府盆地",
                  "issue_type": "ASR誤認識"
                }
              ],
              "review_guidelines": [
                "地名と観光表現は文脈に合う場合だけ正しい表記へ寄せる。"
              ]
            }
            """);
        new DomainPresetImportService(paths, new AsrSettingsRepository(paths)).Import(presetPath);
        var provider = new DomainPromptContextProvider(paths);

        var context = provider.LoadForSummary();

        Assert.False(context.IsEmpty);
        Assert.Contains(context.Terms, term => term.Surface == "甲府盆地");
        Assert.Contains(context.Terms, term => term.Surface == "桃源郷" && term.Category == "asr_hotword");
        var pair = Assert.Single(context.CorrectionPairs);
        Assert.Equal("幸福盆地", pair.WrongText);
        Assert.Equal("甲府盆地", pair.CorrectText);
        Assert.Contains("地名と観光表現", Assert.Single(context.ReviewGuidelines), StringComparison.Ordinal);
    }

    [Fact]
    public void LoadForSummary_ExcludesDisabledEntries()
    {
        var paths = CreateReadyPaths();
        InsertUserTerm(paths, "甲府盆地", true, "2026-05-08T00:00:00.0000000+09:00");
        InsertUserTerm(paths, "無効語", false, "2026-05-08T00:01:00.0000000+09:00");
        InsertCorrectionMemory(paths, "幸福盆地", "甲府盆地", true, "2026-05-08T00:02:00.0000000+09:00");
        InsertCorrectionMemory(paths, "無効誤り", "無効正解", false, "2026-05-08T00:03:00.0000000+09:00");
        InsertReviewGuideline(paths, "有効な指針", true, "2026-05-08T00:04:00.0000000+09:00");
        InsertReviewGuideline(paths, "無効な指針", false, "2026-05-08T00:05:00.0000000+09:00");
        var provider = new DomainPromptContextProvider(paths);

        var context = provider.LoadForSummary();

        Assert.Equal(["甲府盆地"], context.Terms.Select(static term => term.Surface).ToArray());
        Assert.Equal(["幸福盆地"], context.CorrectionPairs.Select(static pair => pair.WrongText).ToArray());
        Assert.Equal(["有効な指針"], context.ReviewGuidelines);
    }

    [Fact]
    public void LoadForSummary_AppliesLimits()
    {
        var paths = CreateReadyPaths();
        for (var index = 0; index < 5; index++)
        {
            var timestamp = $"2026-05-08T00:0{index}:00.0000000+09:00";
            InsertUserTerm(paths, $"用語{index}", true, timestamp);
            InsertCorrectionMemory(paths, $"誤り{index}", $"正解{index}", true, timestamp);
            InsertReviewGuideline(paths, $"指針{index}", true, timestamp);
        }
        var provider = new DomainPromptContextProvider(paths);

        var context = provider.LoadForSummary(new DomainPromptContextLimits(
            TermLimit: 2,
            CorrectionPairLimit: 3,
            ReviewGuidelineLimit: 1));

        Assert.Equal(2, context.Terms.Count);
        Assert.Equal(3, context.CorrectionPairs.Count);
        Assert.Single(context.ReviewGuidelines);
    }

    [Fact]
    public void BonsaiSummaryLimits_AreCompactForSmallModels()
    {
        var paths = CreateReadyPaths();
        for (var index = 0; index < 12; index++)
        {
            var timestamp = $"2026-05-08T00:{index:00}:00.0000000+09:00";
            InsertUserTerm(paths, $"用語{index}", true, timestamp);
            InsertCorrectionMemory(paths, $"誤り{index}", $"正解{index}", true, timestamp);
            InsertReviewGuideline(paths, $"指針{index}", true, timestamp);
        }
        var provider = new DomainPromptContextProvider(paths);

        var context = provider.LoadForSummary(DomainPromptContextLimits.BonsaiSummary);

        Assert.Equal(10, context.Terms.Count);
        Assert.Equal(10, context.CorrectionPairs.Count);
        Assert.Equal(5, context.ReviewGuidelines.Count);
    }

    [Fact]
    public void LoadForSummary_PrioritizesTermsAndPairsThatAppearInSourceText()
    {
        var paths = CreateReadyPaths();
        InsertUserTerm(paths, "無関係な新語", true, "2026-05-08T00:10:00.0000000+09:00");
        InsertUserTerm(paths, "甲府盆地", true, "2026-05-08T00:00:00.0000000+09:00");
        InsertCorrectionMemory(paths, "無関係誤り", "無関係正解", true, "2026-05-08T00:10:00.0000000+09:00");
        InsertCorrectionMemory(paths, "幸福盆地", "甲府盆地", true, "2026-05-08T00:00:00.0000000+09:00");
        var provider = new DomainPromptContextProvider(paths);

        var context = provider.LoadForSummary(
            "甲府盆地の紹介で、ASRでは幸福盆地と誤認識されることがあります",
            new DomainPromptContextLimits(TermLimit: 1, CorrectionPairLimit: 1, ReviewGuidelineLimit: 0));

        Assert.Equal(["甲府盆地"], context.Terms.Select(static term => term.Surface).ToArray());
        Assert.Equal(["幸福盆地"], context.CorrectionPairs.Select(static pair => pair.WrongText).ToArray());
    }

    [Fact]
    public void LoadForSummary_FallsBackToRecentHintsWhenSourceTextDoesNotMatch()
    {
        var paths = CreateReadyPaths();
        InsertUserTerm(paths, "古い語", true, "2026-05-08T00:00:00.0000000+09:00");
        InsertUserTerm(paths, "新しい語", true, "2026-05-08T00:10:00.0000000+09:00");
        InsertCorrectionMemory(paths, "古い誤り", "古い正解", true, "2026-05-08T00:00:00.0000000+09:00");
        InsertCorrectionMemory(paths, "新しい誤り", "新しい正解", true, "2026-05-08T00:10:00.0000000+09:00");
        var provider = new DomainPromptContextProvider(paths);

        var context = provider.LoadForSummary(
            "本文には一致する辞書語がありません",
            new DomainPromptContextLimits(TermLimit: 1, CorrectionPairLimit: 1, ReviewGuidelineLimit: 0));

        Assert.Equal(["新しい語"], context.Terms.Select(static term => term.Surface).ToArray());
        Assert.Equal(["新しい誤り"], context.CorrectionPairs.Select(static pair => pair.WrongText).ToArray());
    }

    [Fact]
    public void LoadForSummary_MatchesFullWidthDigitsAndSimpleKanjiNumbers()
    {
        var paths = CreateReadyPaths();
        InsertUserTerm(paths, "無関係な新語", true, "2026-05-08T00:10:00.0000000+09:00");
        InsertUserTerm(paths, "日本一", true, "2026-05-08T00:00:00.0000000+09:00");
        InsertCorrectionMemory(paths, "無関係誤り", "無関係正解", true, "2026-05-08T00:10:00.0000000+09:00");
        InsertCorrectionMemory(paths, "2本1", "日本一", true, "2026-05-08T00:00:00.0000000+09:00");
        var provider = new DomainPromptContextProvider(paths);

        var context = provider.LoadForSummary(
            "山梨は桃の生産量も日本1で、ASRでは２本１と出ることがあります",
            new DomainPromptContextLimits(TermLimit: 1, CorrectionPairLimit: 1, ReviewGuidelineLimit: 0));

        Assert.Equal(["日本一"], context.Terms.Select(static term => term.Surface).ToArray());
        Assert.Equal(["2本1"], context.CorrectionPairs.Select(static pair => pair.WrongText).ToArray());
    }

    [Fact]
    public void LoadForSummary_MatchesAcrossPunctuationAndSpaces()
    {
        var paths = CreateReadyPaths();
        InsertUserTerm(paths, "無関係な新語", true, "2026-05-08T00:10:00.0000000+09:00");
        InsertUserTerm(paths, "KoeNote", true, "2026-05-08T00:00:00.0000000+09:00");
        var provider = new DomainPromptContextProvider(paths);

        var context = provider.LoadForSummary(
            "Koe-Note の要約機能について話しました",
            new DomainPromptContextLimits(TermLimit: 1, CorrectionPairLimit: 0, ReviewGuidelineLimit: 0));

        Assert.Equal(["KoeNote"], context.Terms.Select(static term => term.Surface).ToArray());
    }

    [Fact]
    public void LoadForSummary_DoesNotDropSpecializedSymbolsForMatching()
    {
        var paths = CreateReadyPaths();
        InsertUserTerm(paths, "C++", true, "2026-05-08T00:00:00.0000000+09:00");
        InsertUserTerm(paths, "C", true, "2026-05-08T00:10:00.0000000+09:00");
        var provider = new DomainPromptContextProvider(paths);

        var context = provider.LoadForSummary(
            "Cの話だけで、C++には触れていません",
            new DomainPromptContextLimits(TermLimit: 1, CorrectionPairLimit: 0, ReviewGuidelineLimit: 0));

        Assert.Equal(["C"], context.Terms.Select(static term => term.Surface).ToArray());
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

    private static void InsertUserTerm(AppPaths paths, string surface, bool enabled, string updatedAt)
    {
        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO user_terms (term_id, surface, category, enabled, created_at, updated_at)
            VALUES ($term_id, $surface, 'domain_preset', $enabled, $updated_at, $updated_at);
            """;
        command.Parameters.AddWithValue("$term_id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$surface", surface);
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$updated_at", updatedAt);
        command.ExecuteNonQuery();
    }

    private static void InsertCorrectionMemory(AppPaths paths, string wrongText, string correctText, bool enabled, string updatedAt)
    {
        using var connection = Open(paths);
        using var command = connection.CreateCommand();
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
                'ASR誤認識',
                'global',
                1,
                0,
                $enabled,
                $updated_at,
                $updated_at
            );
            """;
        command.Parameters.AddWithValue("$memory_id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$wrong_text", wrongText);
        command.Parameters.AddWithValue("$correct_text", correctText);
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$updated_at", updatedAt);
        command.ExecuteNonQuery();
    }

    private static void InsertReviewGuideline(AppPaths paths, string guidelineText, bool enabled, string updatedAt)
    {
        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO review_guidelines (guideline_id, preset_id, guideline_text, enabled, created_at, updated_at)
            VALUES ($guideline_id, 'test-domain', $guideline_text, $enabled, $updated_at, $updated_at);
            """;
        command.Parameters.AddWithValue("$guideline_id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$guideline_text", guidelineText);
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$updated_at", updatedAt);
        command.ExecuteNonQuery();
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
}
