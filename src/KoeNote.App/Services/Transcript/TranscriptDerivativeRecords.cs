namespace KoeNote.App.Services.Transcript;

public static class TranscriptDerivativeKinds
{
    public const string Polished = "polished";
    public const string Summary = "summary";
    public const string Minutes = "minutes";
}

public static class TranscriptDerivativeSourceKinds
{
    public const string Raw = "raw";
    public const string Polished = "polished";
    public const string Summary = "summary";
}

public static class TranscriptDerivativeFormats
{
    public const string PlainText = "plain_text";
    public const string Markdown = "markdown";
    public const string Json = "json";
}

public static class TranscriptDerivativeStatuses
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Stale = "stale";
}

public sealed record TranscriptDerivative(
    string DerivativeId,
    string JobId,
    string Kind,
    string ContentFormat,
    string Content,
    string SourceKind,
    string SourceTranscriptHash,
    string? SourceSegmentRange,
    string? SourceChunkIds,
    string? ModelId,
    string PromptVersion,
    string GenerationProfile,
    string Status,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TranscriptDerivativeChunk(
    string ChunkId,
    string DerivativeId,
    string JobId,
    int ChunkIndex,
    string SourceKind,
    string SourceSegmentIds,
    double? SourceStartSeconds,
    double? SourceEndSeconds,
    string SourceTranscriptHash,
    string ContentFormat,
    string Content,
    string? ModelId,
    string PromptVersion,
    string GenerationProfile,
    string Status,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TranscriptDerivativeSaveRequest(
    string JobId,
    string Kind,
    string ContentFormat,
    string Content,
    string SourceKind,
    string SourceTranscriptHash,
    string? SourceSegmentRange,
    string? SourceChunkIds,
    string? ModelId,
    string PromptVersion,
    string GenerationProfile,
    string Status = TranscriptDerivativeStatuses.Succeeded,
    string? ErrorMessage = null,
    string? DerivativeId = null);

public sealed record TranscriptDerivativeChunkSaveRequest(
    string DerivativeId,
    string JobId,
    int ChunkIndex,
    string SourceKind,
    string SourceSegmentIds,
    double? SourceStartSeconds,
    double? SourceEndSeconds,
    string SourceTranscriptHash,
    string ContentFormat,
    string Content,
    string? ModelId,
    string PromptVersion,
    string GenerationProfile,
    string Status = TranscriptDerivativeStatuses.Succeeded,
    string? ErrorMessage = null,
    string? ChunkId = null);
