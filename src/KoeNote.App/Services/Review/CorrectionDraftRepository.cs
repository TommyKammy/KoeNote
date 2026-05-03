using KoeNote.App.Models;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Review;

public sealed class CorrectionDraftRepository(AppPaths paths)
{
    public IReadOnlyList<CorrectionDraft> ReadPendingForJob(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return [];
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                d.draft_id,
                d.job_id,
                d.segment_id,
                d.issue_type,
                d.original_text,
                d.suggested_text,
                d.reason,
                d.confidence,
                d.status,
                d.created_at,
                d.source,
                d.source_ref_id
            FROM correction_drafts d
            JOIN transcript_segments s
                ON s.job_id = d.job_id AND s.segment_id = d.segment_id
            WHERE d.job_id = $job_id AND d.status = 'pending'
            ORDER BY s.start_seconds ASC, d.confidence DESC, d.created_at ASC;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);

        using var reader = command.ExecuteReader();
        var drafts = new List<CorrectionDraft>();
        while (reader.Read())
        {
            drafts.Add(ReadDraft(reader));
        }

        return drafts;
    }

    public void SaveDrafts(IReadOnlyList<CorrectionDraft> drafts)
    {
        if (drafts.Count == 0)
        {
            return;
        }

        ReplaceDrafts(drafts[0].JobId, drafts);
    }

    public void ReplaceDrafts(string jobId, IReadOnlyList<CorrectionDraft> drafts)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM correction_drafts WHERE job_id = $job_id AND status = 'pending';";
            deleteCommand.Parameters.AddWithValue("$job_id", jobId);
            deleteCommand.ExecuteNonQuery();
        }

        using (var segmentResetCommand = connection.CreateCommand())
        {
            segmentResetCommand.Transaction = transaction;
            segmentResetCommand.CommandText = """
                UPDATE transcript_segments
                SET review_state = CASE
                    WHEN final_text IS NULL THEN 'none'
                    ELSE 'reviewed'
                END
                WHERE job_id = $job_id AND review_state = 'has_draft';
                """;
            segmentResetCommand.Parameters.AddWithValue("$job_id", jobId);
            segmentResetCommand.ExecuteNonQuery();
        }

        foreach (var draft in drafts)
        {
            var persistedDraft = EnsureWritableDraftId(connection, transaction, draft);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO correction_drafts (
                    draft_id,
                    job_id,
                    segment_id,
                    issue_type,
                    original_text,
                    suggested_text,
                    reason,
                    confidence,
                    status,
                    created_at,
                    source,
                    source_ref_id
                )
                VALUES (
                    $draft_id,
                    $job_id,
                    $segment_id,
                    $issue_type,
                    $original_text,
                    $suggested_text,
                    $reason,
                    $confidence,
                    $status,
                    $created_at,
                    $source,
                    $source_ref_id
                )
                ON CONFLICT(draft_id) DO UPDATE SET
                    issue_type = excluded.issue_type,
                    original_text = excluded.original_text,
                    suggested_text = excluded.suggested_text,
                    reason = excluded.reason,
                    confidence = excluded.confidence,
                    status = excluded.status,
                    source = excluded.source,
                    source_ref_id = excluded.source_ref_id;
                """;
            command.Parameters.AddWithValue("$draft_id", persistedDraft.DraftId);
            command.Parameters.AddWithValue("$job_id", persistedDraft.JobId);
            command.Parameters.AddWithValue("$segment_id", persistedDraft.SegmentId);
            command.Parameters.AddWithValue("$issue_type", persistedDraft.IssueType);
            command.Parameters.AddWithValue("$original_text", persistedDraft.OriginalText);
            command.Parameters.AddWithValue("$suggested_text", persistedDraft.SuggestedText);
            command.Parameters.AddWithValue("$reason", persistedDraft.Reason);
            command.Parameters.AddWithValue("$confidence", persistedDraft.Confidence);
            command.Parameters.AddWithValue("$status", persistedDraft.Status);
            command.Parameters.AddWithValue("$created_at", (persistedDraft.CreatedAt ?? DateTimeOffset.Now).ToString("o"));
            command.Parameters.AddWithValue("$source", persistedDraft.Source);
            command.Parameters.AddWithValue("$source_ref_id", (object?)persistedDraft.SourceRefId ?? DBNull.Value);
            command.ExecuteNonQuery();

            using var segmentCommand = connection.CreateCommand();
            segmentCommand.Transaction = transaction;
            segmentCommand.CommandText = """
                UPDATE transcript_segments
                SET review_state = 'has_draft'
                WHERE job_id = $job_id AND segment_id = $segment_id;
                """;
            segmentCommand.Parameters.AddWithValue("$job_id", persistedDraft.JobId);
            segmentCommand.Parameters.AddWithValue("$segment_id", persistedDraft.SegmentId);
            segmentCommand.ExecuteNonQuery();
        }

        using var jobCommand = connection.CreateCommand();
        jobCommand.Transaction = transaction;
        jobCommand.CommandText = """
            UPDATE jobs
            SET unreviewed_draft_count = $draft_count,
                updated_at = $updated_at
            WHERE job_id = $job_id;
            """;
        jobCommand.Parameters.AddWithValue("$job_id", jobId);
        jobCommand.Parameters.AddWithValue("$draft_count", drafts.Count(static draft => draft.Status == "pending"));
        jobCommand.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        jobCommand.ExecuteNonQuery();

        transaction.Commit();
    }

    private static CorrectionDraft EnsureWritableDraftId(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CorrectionDraft draft)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                status,
                EXISTS(SELECT 1 FROM review_decisions WHERE draft_id = $draft_id)
            FROM correction_drafts
            WHERE draft_id = $draft_id;
            """;
        command.Parameters.AddWithValue("$draft_id", draft.DraftId);

        using var reader = command.ExecuteReader();
        var hasExistingDraft = reader.Read();
        var status = hasExistingDraft ? reader.GetString(0) : null;
        var hasDecision = hasExistingDraft && reader.GetInt32(1) != 0;
        reader.Close();

        if (!hasExistingDraft)
        {
            return draft;
        }

        if (string.Equals(status, "pending", StringComparison.Ordinal) && !hasDecision)
        {
            return draft;
        }

        return draft with { DraftId = CreateReplacementDraftId(connection, transaction, draft.DraftId) };
    }

    private static string CreateReplacementDraftId(SqliteConnection connection, SqliteTransaction transaction, string baseDraftId)
    {
        while (true)
        {
            var candidate = $"{baseDraftId}-rerun-{Guid.NewGuid():N}";
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM correction_drafts WHERE draft_id = $draft_id;";
            command.Parameters.AddWithValue("$draft_id", candidate);
            if (Convert.ToInt32(command.ExecuteScalar()) == 0)
            {
                return candidate;
            }
        }
    }

    private static CorrectionDraft ReadDraft(SqliteDataReader reader)
    {
        return new CorrectionDraft(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetDouble(7),
            reader.GetString(8),
            DateTimeOffset.TryParse(reader.GetString(9), out var createdAt) ? createdAt : null,
            reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11));
    }
}
