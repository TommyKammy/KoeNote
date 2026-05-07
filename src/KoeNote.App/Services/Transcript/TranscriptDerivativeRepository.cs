using System.Security.Cryptography;
using System.Text;

namespace KoeNote.App.Services.Transcript;

public sealed class TranscriptDerivativeRepository(AppPaths paths)
{
    public string ComputeCurrentRawTranscriptHash(string jobId)
    {
        var segments = new TranscriptReadRepository(paths).ReadForJob(jobId);
        return ComputeSourceTranscriptHash(segments);
    }

    public TranscriptDerivative Save(TranscriptDerivativeSaveRequest request)
    {
        ValidateRequest(request);

        var now = DateTimeOffset.Now;
        var derivativeId = string.IsNullOrWhiteSpace(request.DerivativeId)
            ? Guid.NewGuid().ToString("N")
            : request.DerivativeId;

        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
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
            """;
        command.Parameters.AddWithValue("$derivative_id", derivativeId);
        command.Parameters.AddWithValue("$job_id", request.JobId);
        command.Parameters.AddWithValue("$kind", request.Kind);
        command.Parameters.AddWithValue("$content_format", request.ContentFormat);
        command.Parameters.AddWithValue("$content", request.Content);
        command.Parameters.AddWithValue("$source_kind", request.SourceKind);
        command.Parameters.AddWithValue("$source_transcript_hash", request.SourceTranscriptHash);
        command.Parameters.AddWithValue("$source_segment_range", (object?)request.SourceSegmentRange ?? DBNull.Value);
        command.Parameters.AddWithValue("$source_chunk_ids", (object?)request.SourceChunkIds ?? DBNull.Value);
        command.Parameters.AddWithValue("$model_id", (object?)request.ModelId ?? DBNull.Value);
        command.Parameters.AddWithValue("$prompt_version", request.PromptVersion);
        command.Parameters.AddWithValue("$generation_profile", request.GenerationProfile);
        command.Parameters.AddWithValue("$status", request.Status);
        command.Parameters.AddWithValue("$error_message", (object?)request.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", now.ToString("o"));
        command.Parameters.AddWithValue("$updated_at", now.ToString("o"));
        command.ExecuteNonQuery();

        return ReadById(derivativeId)
            ?? throw new InvalidOperationException("Saved transcript derivative could not be read.");
    }

    public TranscriptDerivativeChunk SaveChunk(TranscriptDerivativeChunkSaveRequest request)
    {
        ValidateChunkRequest(request);

        var now = DateTimeOffset.Now;
        var chunkId = string.IsNullOrWhiteSpace(request.ChunkId)
            ? Guid.NewGuid().ToString("N")
            : request.ChunkId;

        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
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
            """;
        command.Parameters.AddWithValue("$chunk_id", chunkId);
        command.Parameters.AddWithValue("$derivative_id", request.DerivativeId);
        command.Parameters.AddWithValue("$job_id", request.JobId);
        command.Parameters.AddWithValue("$chunk_index", request.ChunkIndex);
        command.Parameters.AddWithValue("$source_kind", request.SourceKind);
        command.Parameters.AddWithValue("$source_segment_ids", request.SourceSegmentIds);
        command.Parameters.AddWithValue("$source_start_seconds", (object?)request.SourceStartSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$source_end_seconds", (object?)request.SourceEndSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$source_transcript_hash", request.SourceTranscriptHash);
        command.Parameters.AddWithValue("$content_format", request.ContentFormat);
        command.Parameters.AddWithValue("$content", request.Content);
        command.Parameters.AddWithValue("$model_id", (object?)request.ModelId ?? DBNull.Value);
        command.Parameters.AddWithValue("$prompt_version", request.PromptVersion);
        command.Parameters.AddWithValue("$generation_profile", request.GenerationProfile);
        command.Parameters.AddWithValue("$status", request.Status);
        command.Parameters.AddWithValue("$error_message", (object?)request.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", now.ToString("o"));
        command.Parameters.AddWithValue("$updated_at", now.ToString("o"));
        command.ExecuteNonQuery();

        return ReadChunkById(chunkId)
            ?? throw new InvalidOperationException("Saved transcript derivative chunk could not be read.");
    }

    public TranscriptDerivative? ReadById(string derivativeId)
    {
        if (string.IsNullOrWhiteSpace(derivativeId))
        {
            return null;
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
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
            FROM transcript_derivatives
            WHERE derivative_id = $derivative_id;
            """;
        command.Parameters.AddWithValue("$derivative_id", derivativeId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadDerivative(reader) : null;
    }

    public TranscriptDerivative? ReadLatestSuccessful(string jobId, string kind)
    {
        if (string.IsNullOrWhiteSpace(jobId) || string.IsNullOrWhiteSpace(kind))
        {
            return null;
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
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
            FROM transcript_derivatives
            WHERE job_id = $job_id
              AND kind = $kind
              AND status = $status
            ORDER BY updated_at DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$status", TranscriptDerivativeStatuses.Succeeded);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadDerivative(reader) : null;
    }

    public IReadOnlyList<TranscriptDerivativeChunk> ReadChunks(string derivativeId)
    {
        if (string.IsNullOrWhiteSpace(derivativeId))
        {
            return [];
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
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
            FROM transcript_derivative_chunks
            WHERE derivative_id = $derivative_id
            ORDER BY chunk_index ASC;
            """;
        command.Parameters.AddWithValue("$derivative_id", derivativeId);

        using var reader = command.ExecuteReader();
        var chunks = new List<TranscriptDerivativeChunk>();
        while (reader.Read())
        {
            chunks.Add(ReadChunk(reader));
        }

        return chunks;
    }

    public bool IsStale(TranscriptDerivative derivative)
    {
        ArgumentNullException.ThrowIfNull(derivative);
        if (!string.Equals(derivative.SourceKind, TranscriptDerivativeSourceKinds.Raw, StringComparison.Ordinal))
        {
            return false;
        }

        var currentHash = ComputeCurrentRawTranscriptHash(derivative.JobId);
        return !string.Equals(currentHash, derivative.SourceTranscriptHash, StringComparison.OrdinalIgnoreCase);
    }

    public int MarkStaleForJob(string jobId, string currentSourceTranscriptHash)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return 0;
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE transcript_derivatives
            SET status = $stale,
                updated_at = $updated_at
            WHERE job_id = $job_id
              AND source_kind = $source_kind
              AND status = $succeeded
              AND source_transcript_hash <> $source_transcript_hash;
            """;
        command.Parameters.AddWithValue("$stale", TranscriptDerivativeStatuses.Stale);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$source_kind", TranscriptDerivativeSourceKinds.Raw);
        command.Parameters.AddWithValue("$succeeded", TranscriptDerivativeStatuses.Succeeded);
        command.Parameters.AddWithValue("$source_transcript_hash", currentSourceTranscriptHash);
        return command.ExecuteNonQuery();
    }

    public static string ComputeSourceTranscriptHash(IReadOnlyList<TranscriptReadModel> segments)
    {
        var builder = new StringBuilder();
        foreach (var segment in segments.OrderBy(static segment => segment.StartSeconds).ThenBy(static segment => segment.EndSeconds))
        {
            builder
                .Append(segment.SegmentId).Append('\u001F')
                .Append(segment.StartSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\u001F')
                .Append(segment.EndSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\u001F')
                .Append(segment.SpeakerId).Append('\u001F')
                .Append(segment.Speaker).Append('\u001F')
                .Append(segment.Text).Append('\u001E');
        }

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private TranscriptDerivativeChunk? ReadChunkById(string chunkId)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
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
            FROM transcript_derivative_chunks
            WHERE chunk_id = $chunk_id;
            """;
        command.Parameters.AddWithValue("$chunk_id", chunkId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadChunk(reader) : null;
    }

    private static TranscriptDerivative ReadDerivative(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        return new TranscriptDerivative(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            DateTimeOffset.Parse(reader.GetString(14), System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(15), System.Globalization.CultureInfo.InvariantCulture));
    }

    private static TranscriptDerivativeChunk ReadChunk(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        return new TranscriptDerivativeChunk(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetDouble(6),
            reader.IsDBNull(7) ? null : reader.GetDouble(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14),
            reader.IsDBNull(15) ? null : reader.GetString(15),
            DateTimeOffset.Parse(reader.GetString(16), System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(17), System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void ValidateRequest(TranscriptDerivativeSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireValue(request.JobId, nameof(request.JobId));
        RequireValue(request.Kind, nameof(request.Kind));
        RequireValue(request.ContentFormat, nameof(request.ContentFormat));
        RequireContentWhenSucceeded(request.Content, request.Status, nameof(request.Content));
        RequireValue(request.SourceKind, nameof(request.SourceKind));
        RequireValue(request.SourceTranscriptHash, nameof(request.SourceTranscriptHash));
        RequireValue(request.PromptVersion, nameof(request.PromptVersion));
        RequireValue(request.GenerationProfile, nameof(request.GenerationProfile));
        RequireValue(request.Status, nameof(request.Status));
    }

    private static void ValidateChunkRequest(TranscriptDerivativeChunkSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireValue(request.DerivativeId, nameof(request.DerivativeId));
        RequireValue(request.JobId, nameof(request.JobId));
        RequireValue(request.SourceKind, nameof(request.SourceKind));
        RequireValue(request.SourceSegmentIds, nameof(request.SourceSegmentIds));
        RequireValue(request.SourceTranscriptHash, nameof(request.SourceTranscriptHash));
        RequireValue(request.ContentFormat, nameof(request.ContentFormat));
        RequireContentWhenSucceeded(request.Content, request.Status, nameof(request.Content));
        RequireValue(request.PromptVersion, nameof(request.PromptVersion));
        RequireValue(request.GenerationProfile, nameof(request.GenerationProfile));
        RequireValue(request.Status, nameof(request.Status));
    }

    private static void RequireValue(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }
    }

    private static void RequireContentWhenSucceeded(string? content, string status, string name)
    {
        if (string.Equals(status, TranscriptDerivativeStatuses.Succeeded, StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException($"{name} is required for successful transcript derivatives.", name);
        }
    }
}
