using KoeNote.App.Models;
using KoeNote.App.Services.Asr;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Review;

public sealed class CorrectionMemoryService(AppPaths paths)
{
    private const int MaxStoredTextLength = 120;
    private const int MaxAsrHotwordLength = 24;

    public AsrSettings EnrichAsrSettings(AsrSettings settings)
    {
        var terms = ReadEnabledTerms(50);
        if (terms.Count == 0)
        {
            return settings;
        }

        var hotwords = settings.Hotwords
            .Concat(terms
                .Select(static term => term.Surface)
                .Where(IsAsrHotwordCandidate))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new AsrSettings(settings.ContextText, string.Join(Environment.NewLine, hotwords), settings.EngineId, settings.EnableReviewStage);
    }

    public IReadOnlyList<CorrectionDraft> BuildMemoryDrafts(string jobId, IReadOnlyList<TranscriptSegment> segments)
    {
        if (string.IsNullOrWhiteSpace(jobId) || segments.Count == 0)
        {
            return [];
        }

        var memories = ReadEnabledMemories(100);
        if (memories.Count == 0)
        {
            return [];
        }

        var drafts = new List<CorrectionDraft>();
        foreach (var segment in segments)
        {
            var text = segment.NormalizedText ?? segment.RawText;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (var memory in memories)
            {
                if (!text.Contains(memory.WrongText, StringComparison.Ordinal) ||
                    string.Equals(memory.WrongText, memory.CorrectText, StringComparison.Ordinal))
                {
                    continue;
                }

                var suggested = text.Replace(memory.WrongText, memory.CorrectText, StringComparison.Ordinal);
                if (string.Equals(text, suggested, StringComparison.Ordinal))
                {
                    continue;
                }

                drafts.Add(new CorrectionDraft(
                    $"{segment.SegmentId}-memory-{memory.MemoryId[..Math.Min(10, memory.MemoryId.Length)]}",
                    jobId,
                    segment.SegmentId,
                    "過去修正候補",
                    text,
                    suggested,
                    $"過去に採用された修正: {memory.WrongText} -> {memory.CorrectText}",
                    CalculateConfidence(memory),
                    Source: "memory",
                    SourceRefId: memory.MemoryId));
                RecordEvent(memory.MemoryId, drafts[^1].DraftId, jobId, segment.SegmentId, "suggested");
                break;
            }
        }

        return drafts;
    }

    public void RememberCorrection(CorrectionDraft draft, string finalText)
    {
        if (string.IsNullOrWhiteSpace(finalText) ||
            string.Equals(draft.OriginalText, finalText, StringComparison.Ordinal) ||
            draft.OriginalText.Length > MaxStoredTextLength ||
            finalText.Length > MaxStoredTextLength)
        {
            RecordDraftDecision(draft, "accepted_without_memory");
            return;
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.Now.ToString("o");
        var memoryId = UpsertMemory(connection, transaction, draft.OriginalText, finalText, draft.IssueType, now);
        if (IsAsrHotwordCandidate(finalText))
        {
            UpsertUserTerm(connection, transaction, finalText, now);
        }

        InsertEvent(connection, transaction, memoryId, draft.DraftId, draft.JobId, draft.SegmentId, "remembered", now);
        transaction.Commit();
    }

    public void RecordDraftDecision(CorrectionDraft draft, string action)
    {
        if (!string.Equals(draft.Source, "memory", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(draft.SourceRefId))
        {
            return;
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.Now.ToString("o");
        var counterColumn = action switch
        {
            "accepted" => "accepted_count",
            "rejected" => "rejected_count",
            _ => null
        };

        if (counterColumn is not null)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                UPDATE correction_memory
                SET {counterColumn} = {counterColumn} + 1,
                    updated_at = $updated_at
                WHERE memory_id = $memory_id;
                """;
            command.Parameters.AddWithValue("$memory_id", draft.SourceRefId);
            command.Parameters.AddWithValue("$updated_at", now);
            command.ExecuteNonQuery();
        }

        InsertEvent(connection, transaction, draft.SourceRefId, draft.DraftId, draft.JobId, draft.SegmentId, action, now);
        transaction.Commit();
    }

    private IReadOnlyList<UserTermRow> ReadEnabledTerms(int limit)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT surface
            FROM user_terms
            WHERE enabled = 1
            ORDER BY updated_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var terms = new List<UserTermRow>();
        while (reader.Read())
        {
            terms.Add(new UserTermRow(reader.GetString(0)));
        }

        return terms;
    }

    private IReadOnlyList<MemoryRow> ReadEnabledMemories(int limit)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT memory_id, wrong_text, correct_text, issue_type, accepted_count, rejected_count
            FROM correction_memory
            WHERE enabled = 1
            ORDER BY accepted_count DESC, updated_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var memories = new List<MemoryRow>();
        while (reader.Read())
        {
            memories.Add(new MemoryRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5)));
        }

        return memories;
    }

    private void RecordEvent(string memoryId, string draftId, string jobId, string segmentId, string eventType)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();
        InsertEvent(connection, transaction, memoryId, draftId, jobId, segmentId, eventType, DateTimeOffset.Now.ToString("o"));
        transaction.Commit();
    }

    private static string UpsertMemory(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string wrongText,
        string correctText,
        string issueType,
        string now)
    {
        var memoryId = Guid.NewGuid().ToString("N");
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
                'global',
                1,
                0,
                1,
                $now,
                $now
            )
            ON CONFLICT(wrong_text, correct_text, scope) DO UPDATE SET
                accepted_count = accepted_count + 1,
                issue_type = excluded.issue_type,
                enabled = 1,
                updated_at = excluded.updated_at
            RETURNING memory_id;
            """;
        command.Parameters.AddWithValue("$memory_id", memoryId);
        command.Parameters.AddWithValue("$wrong_text", wrongText);
        command.Parameters.AddWithValue("$correct_text", correctText);
        command.Parameters.AddWithValue("$issue_type", issueType);
        command.Parameters.AddWithValue("$now", now);
        return Convert.ToString(command.ExecuteScalar()) ?? memoryId;
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
                'correction_memory',
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

    private static void InsertEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string? memoryId,
        string? draftId,
        string jobId,
        string? segmentId,
        string eventType,
        string now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO correction_memory_events (
                event_id,
                memory_id,
                draft_id,
                job_id,
                segment_id,
                event_type,
                created_at
            )
            VALUES (
                $event_id,
                $memory_id,
                $draft_id,
                $job_id,
                $segment_id,
                $event_type,
                $created_at
            );
            """;
        command.Parameters.AddWithValue("$event_id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$memory_id", (object?)memoryId ?? DBNull.Value);
        command.Parameters.AddWithValue("$draft_id", (object?)draftId ?? DBNull.Value);
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$segment_id", (object?)segmentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$event_type", eventType);
        command.Parameters.AddWithValue("$created_at", now);
        command.ExecuteNonQuery();
    }

    private static double CalculateConfidence(MemoryRow memory)
    {
        var total = memory.AcceptedCount + memory.RejectedCount;
        if (total <= 0)
        {
            return 0.72;
        }

        return Math.Clamp(0.62 + (memory.AcceptedCount / (double)total) * 0.28, 0.62, 0.90);
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

    private sealed record UserTermRow(string Surface);

    private sealed record MemoryRow(
        string MemoryId,
        string WrongText,
        string CorrectText,
        string IssueType,
        int AcceptedCount,
        int RejectedCount);
}
