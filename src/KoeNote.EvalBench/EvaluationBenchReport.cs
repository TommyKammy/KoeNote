namespace KoeNote.EvalBench;

public sealed record EvaluationBenchReport(
    string ReportPath,
    DateTimeOffset GeneratedAt,
    HostMetadata Host,
    EvaluationSummary Summary,
    IReadOnlyList<EvaluationCaseResult> Cases,
    IReadOnlyList<AsrBenchmarkResult> AsrBenchmarks,
    DefaultAsrRecommendation? DefaultAsrRecommendation);

public sealed record HostMetadata(
    string MachineName,
    string OsDescription,
    string FrameworkDescription,
    int ProcessorCount,
    long WorkingSetBytes);

public sealed record EvaluationSummary(
    string Status,
    double AsrCharacterErrorRate,
    double ReviewJsonParseFailureRate,
    double ReviewCandidateAcceptanceRate,
    double MemoryCandidateAcceptanceRate,
    double MemorySuggestionRejectionRate,
    int MemorySuggestionCount,
    TimeSpan Duration,
    IReadOnlyList<string> Regressions);

public sealed record EvaluationCaseResult(
    string CaseId,
    double AsrCharacterErrorRate,
    int AsrEditDistance,
    int ReferenceCharacterCount,
    int ReviewDraftCount,
    bool ReviewJsonParseFailed,
    int MemoryDraftCount,
    TimeSpan Duration);

public sealed record AsrBenchmarkResult(
    string EngineId,
    string DurationBucket,
    int CaseCount,
    int FailureCount,
    double FailureRate,
    double CharacterErrorRate,
    double AverageProcessingSeconds,
    double RealTimeFactor);

public sealed record DefaultAsrRecommendation(
    string V01EngineId,
    string V02EngineId,
    string Rationale);
