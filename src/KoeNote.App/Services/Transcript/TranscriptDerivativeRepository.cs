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
        using var command = TranscriptDerivativeCommands.CreateSaveDerivativeCommand(
            connection,
            derivativeId,
            request,
            now);
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
        using var command = TranscriptDerivativeCommands.CreateSaveChunkCommand(
            connection,
            chunkId,
            request,
            now);
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
        using var command = TranscriptDerivativeCommands.CreateReadDerivativeByIdCommand(
            connection,
            derivativeId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? TranscriptDerivativeRowMapper.ReadDerivative(reader) : null;
    }

    public TranscriptDerivative? ReadLatestSuccessful(string jobId, string kind)
    {
        if (string.IsNullOrWhiteSpace(jobId) || string.IsNullOrWhiteSpace(kind))
        {
            return null;
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = TranscriptDerivativeCommands.CreateReadLatestSuccessfulCommand(
            connection,
            jobId,
            kind);

        using var reader = command.ExecuteReader();
        return reader.Read() ? TranscriptDerivativeRowMapper.ReadDerivative(reader) : null;
    }

    public TranscriptDerivative? ReadLatestDisplayable(string jobId, string kind)
    {
        if (string.IsNullOrWhiteSpace(jobId) || string.IsNullOrWhiteSpace(kind))
        {
            return null;
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = TranscriptDerivativeCommands.CreateReadLatestDisplayableCommand(
            connection,
            jobId,
            kind);

        using var reader = command.ExecuteReader();
        return reader.Read() ? TranscriptDerivativeRowMapper.ReadDerivative(reader) : null;
    }

    public IReadOnlyList<TranscriptDerivativeChunk> ReadChunks(string derivativeId)
    {
        if (string.IsNullOrWhiteSpace(derivativeId))
        {
            return [];
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = TranscriptDerivativeCommands.CreateReadChunksCommand(connection, derivativeId);

        using var reader = command.ExecuteReader();
        var chunks = new List<TranscriptDerivativeChunk>();
        while (reader.Read())
        {
            chunks.Add(TranscriptDerivativeRowMapper.ReadChunk(reader));
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
        using var command = TranscriptDerivativeCommands.CreateMarkStaleForJobCommand(
            connection,
            jobId,
            currentSourceTranscriptHash,
            DateTimeOffset.Now);
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
        using var command = TranscriptDerivativeCommands.CreateReadChunkByIdCommand(connection, chunkId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? TranscriptDerivativeRowMapper.ReadChunk(reader) : null;
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
