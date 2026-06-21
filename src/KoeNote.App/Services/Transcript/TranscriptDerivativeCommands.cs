using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Transcript;

internal static class TranscriptDerivativeCommands
{
    private const string DerivativeSelectColumns = """
        derivative_id,
        job_id,
        kind,
        content_format,
        content,
        source_kind,
        source_transcript_hash,
        source_segment_range,
        source_chunk_ids,
        model_id,
        prompt_version,
        generation_profile,
        status,
        error_message,
        created_at,
        updated_at
        """;

    private const string ChunkSelectColumns = """
        chunk_id,
        derivative_id,
        job_id,
        chunk_index,
        source_kind,
        source_segment_ids,
        source_start_seconds,
        source_end_seconds,
        source_transcript_hash,
        content_format,
        content,
        model_id,
        prompt_version,
        generation_profile,
        status,
        error_message,
        created_at,
        updated_at
        """;

    public static SqliteCommand CreateSaveDerivativeCommand(
        SqliteConnection connection,
        string derivativeId,
        TranscriptDerivativeSaveRequest request,
        DateTimeOffset timestamp)
    {
        return connection.CreateCommand("""
            INSERT INTO transcript_derivatives (
                derivative_id,
                job_id,
                kind,
                content_format,
                content,
                source_kind,
                source_transcript_hash,
                source_segment_range,
                source_chunk_ids,
                model_id,
                prompt_version,
                generation_profile,
                status,
                error_message,
                created_at,
                updated_at
            )
            VALUES (
                $derivative_id,
                $job_id,
                $kind,
                $content_format,
                $content,
                $source_kind,
                $source_transcript_hash,
                $source_segment_range,
                $source_chunk_ids,
                $model_id,
                $prompt_version,
                $generation_profile,
                $status,
                $error_message,
                $created_at,
                $updated_at
            )
            ON CONFLICT(derivative_id) DO UPDATE SET
                kind = excluded.kind,
                content_format = excluded.content_format,
                content = excluded.content,
                source_kind = excluded.source_kind,
                source_transcript_hash = excluded.source_transcript_hash,
                source_segment_range = excluded.source_segment_range,
                source_chunk_ids = excluded.source_chunk_ids,
                model_id = excluded.model_id,
                prompt_version = excluded.prompt_version,
                generation_profile = excluded.generation_profile,
                status = excluded.status,
                error_message = excluded.error_message,
                updated_at = excluded.updated_at;
            """)
            .AddValue("$derivative_id", derivativeId)
            .AddValue("$job_id", request.JobId)
            .AddValue("$kind", request.Kind)
            .AddValue("$content_format", request.ContentFormat)
            .AddValue("$content", request.Content)
            .AddValue("$source_kind", request.SourceKind)
            .AddValue("$source_transcript_hash", request.SourceTranscriptHash)
            .AddValue("$source_segment_range", request.SourceSegmentRange)
            .AddValue("$source_chunk_ids", request.SourceChunkIds)
            .AddValue("$model_id", request.ModelId)
            .AddValue("$prompt_version", request.PromptVersion)
            .AddValue("$generation_profile", request.GenerationProfile)
            .AddValue("$status", request.Status)
            .AddValue("$error_message", request.ErrorMessage)
            .AddValue("$created_at", timestamp.ToString("o"))
            .AddValue("$updated_at", timestamp.ToString("o"));
    }

    public static SqliteCommand CreateSaveChunkCommand(
        SqliteConnection connection,
        string chunkId,
        TranscriptDerivativeChunkSaveRequest request,
        DateTimeOffset timestamp)
    {
        return connection.CreateCommand("""
            INSERT INTO transcript_derivative_chunks (
                chunk_id,
                derivative_id,
                job_id,
                chunk_index,
                source_kind,
                source_segment_ids,
                source_start_seconds,
                source_end_seconds,
                source_transcript_hash,
                content_format,
                content,
                model_id,
                prompt_version,
                generation_profile,
                status,
                error_message,
                created_at,
                updated_at
            )
            VALUES (
                $chunk_id,
                $derivative_id,
                $job_id,
                $chunk_index,
                $source_kind,
                $source_segment_ids,
                $source_start_seconds,
                $source_end_seconds,
                $source_transcript_hash,
                $content_format,
                $content,
                $model_id,
                $prompt_version,
                $generation_profile,
                $status,
                $error_message,
                $created_at,
                $updated_at
            )
            ON CONFLICT(chunk_id) DO UPDATE SET
                chunk_index = excluded.chunk_index,
                source_kind = excluded.source_kind,
                source_segment_ids = excluded.source_segment_ids,
                source_start_seconds = excluded.source_start_seconds,
                source_end_seconds = excluded.source_end_seconds,
                source_transcript_hash = excluded.source_transcript_hash,
                content_format = excluded.content_format,
                content = excluded.content,
                model_id = excluded.model_id,
                prompt_version = excluded.prompt_version,
                generation_profile = excluded.generation_profile,
                status = excluded.status,
                error_message = excluded.error_message,
                updated_at = excluded.updated_at;
            """)
            .AddValue("$chunk_id", chunkId)
            .AddValue("$derivative_id", request.DerivativeId)
            .AddValue("$job_id", request.JobId)
            .AddValue("$chunk_index", request.ChunkIndex)
            .AddValue("$source_kind", request.SourceKind)
            .AddValue("$source_segment_ids", request.SourceSegmentIds)
            .AddValue("$source_start_seconds", request.SourceStartSeconds)
            .AddValue("$source_end_seconds", request.SourceEndSeconds)
            .AddValue("$source_transcript_hash", request.SourceTranscriptHash)
            .AddValue("$content_format", request.ContentFormat)
            .AddValue("$content", request.Content)
            .AddValue("$model_id", request.ModelId)
            .AddValue("$prompt_version", request.PromptVersion)
            .AddValue("$generation_profile", request.GenerationProfile)
            .AddValue("$status", request.Status)
            .AddValue("$error_message", request.ErrorMessage)
            .AddValue("$created_at", timestamp.ToString("o"))
            .AddValue("$updated_at", timestamp.ToString("o"));
    }

    public static SqliteCommand CreateReadDerivativeByIdCommand(SqliteConnection connection, string derivativeId)
    {
        return connection.CreateCommand($"""
            SELECT
                {DerivativeSelectColumns}
            FROM transcript_derivatives
            WHERE derivative_id = $derivative_id;
            """)
            .AddValue("$derivative_id", derivativeId);
    }

    public static SqliteCommand CreateReadLatestSuccessfulCommand(SqliteConnection connection, string jobId, string kind)
    {
        return connection.CreateCommand($"""
            SELECT
                {DerivativeSelectColumns}
            FROM transcript_derivatives
            WHERE job_id = $job_id
              AND kind = $kind
              AND status = $status
            ORDER BY updated_at DESC
            LIMIT 1;
            """)
            .AddValue("$job_id", jobId)
            .AddValue("$kind", kind)
            .AddValue("$status", TranscriptDerivativeStatuses.Succeeded);
    }

    public static SqliteCommand CreateReadLatestDisplayableCommand(SqliteConnection connection, string jobId, string kind)
    {
        return connection.CreateCommand($"""
            SELECT
                {DerivativeSelectColumns}
            FROM transcript_derivatives
            WHERE job_id = $job_id
              AND kind = $kind
              AND status IN ($succeeded, $stale)
            ORDER BY updated_at DESC
            LIMIT 1;
            """)
            .AddValue("$job_id", jobId)
            .AddValue("$kind", kind)
            .AddValue("$succeeded", TranscriptDerivativeStatuses.Succeeded)
            .AddValue("$stale", TranscriptDerivativeStatuses.Stale);
    }

    public static SqliteCommand CreateReadChunksCommand(SqliteConnection connection, string derivativeId)
    {
        return connection.CreateCommand($"""
            SELECT
                {ChunkSelectColumns}
            FROM transcript_derivative_chunks
            WHERE derivative_id = $derivative_id
            ORDER BY chunk_index ASC;
            """)
            .AddValue("$derivative_id", derivativeId);
    }

    public static SqliteCommand CreateMarkStaleForJobCommand(
        SqliteConnection connection,
        string jobId,
        string currentSourceTranscriptHash,
        DateTimeOffset timestamp)
    {
        return connection.CreateCommand("""
            UPDATE transcript_derivatives
            SET status = $stale,
                updated_at = $updated_at
            WHERE job_id = $job_id
              AND source_kind = $source_kind
              AND status = $succeeded
              AND source_transcript_hash <> $source_transcript_hash;
            """)
            .AddValue("$stale", TranscriptDerivativeStatuses.Stale)
            .AddValue("$updated_at", timestamp.ToString("o"))
            .AddValue("$job_id", jobId)
            .AddValue("$source_kind", TranscriptDerivativeSourceKinds.Raw)
            .AddValue("$succeeded", TranscriptDerivativeStatuses.Succeeded)
            .AddValue("$source_transcript_hash", currentSourceTranscriptHash);
    }

    public static SqliteCommand CreateReadChunkByIdCommand(SqliteConnection connection, string chunkId)
    {
        return connection.CreateCommand($"""
            SELECT
                {ChunkSelectColumns}
            FROM transcript_derivative_chunks
            WHERE chunk_id = $chunk_id;
            """)
            .AddValue("$chunk_id", chunkId);
    }
}
