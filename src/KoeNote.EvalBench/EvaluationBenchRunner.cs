using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Review;

namespace KoeNote.EvalBench;

public sealed class EvaluationBenchRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public EvaluationBenchReport Run(EvaluationBenchOptions options)
    {
        var runStarted = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var runId = $"{runStarted:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..27];
        var runDirectory = Path.Combine(options.OutputRoot, runId);
        Directory.CreateDirectory(runDirectory);

        var cases = RunFixtureCases();
        var asrBenchmarks = string.IsNullOrWhiteSpace(options.AsrManifestPath)
            ? []
            : RunAsrManifest(options.AsrManifestPath);
        var summary = BuildSummary(cases, stopwatch.Elapsed);
        var reportPath = Path.Combine(runDirectory, "report.json");
        var report = new EvaluationBenchReport(
            reportPath,
            runStarted,
            CreateHostMetadata(),
            summary,
            cases,
            asrBenchmarks,
            RecommendDefaultAsr(asrBenchmarks));

        var json = JsonSerializer.Serialize(report, JsonOptions);
        File.WriteAllText(reportPath, json);
        File.WriteAllText(Path.Combine(options.OutputRoot, "latest.json"), json);
        return report;
    }

    private static IReadOnlyList<AsrBenchmarkResult> RunAsrManifest(string manifestPath)
    {
        var manifestJson = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<AsrBenchmarkManifest>(manifestJson, JsonOptions)
            ?? throw new InvalidOperationException($"ASR benchmark manifest could not be read: {manifestPath}");
        var normalizer = new AsrJsonNormalizer();

        return manifest.Cases
            .SelectMany(testCase => testCase.Results.Select(result => EvaluateAsrResult(testCase, result, normalizer)))
            .GroupBy(static item => (EngineId: item.EngineId.ToUpperInvariant(), DurationBucket: item.DurationBucket.ToUpperInvariant()))
            .Select(static group =>
            {
                var caseCount = group.Count();
                var failed = group.Count(static item => !item.Succeeded);
                var succeeded = group.Where(static item => item.Succeeded).ToArray();
                var referenceCharacters = succeeded.Sum(static item => item.ReferenceCharacterCount);
                var editDistance = succeeded.Sum(static item => item.EditDistance);
                var audioSeconds = succeeded.Sum(static item => item.AudioDurationSeconds);
                var processingSeconds = succeeded.Select(static item => item.ProcessingSeconds).ToArray();
                var averageProcessingSeconds = processingSeconds.Length == 0 ? 0 : processingSeconds.Average();
                var first = group.First();
                return new AsrBenchmarkResult(
                    first.EngineId,
                    first.DurationBucket,
                    caseCount,
                    failed,
                    caseCount == 0 ? 0 : failed / (double)caseCount,
                    referenceCharacters == 0 ? 1 : editDistance / (double)referenceCharacters,
                    averageProcessingSeconds,
                    audioSeconds <= 0 ? 0 : processingSeconds.Sum() / audioSeconds);
            })
            .OrderBy(static item => item.EngineId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.DurationBucket, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static AsrManifestEvaluation EvaluateAsrResult(
        AsrBenchmarkCase testCase,
        AsrBenchmarkOutput result,
        AsrJsonNormalizer normalizer)
    {
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.OutputJsonPath) || !File.Exists(result.OutputJsonPath))
        {
            return new AsrManifestEvaluation(
                result.EngineId,
                testCase.DurationBucket,
                false,
                0,
                testCase.ReferenceText.Length,
                testCase.AudioDurationSeconds,
                result.ProcessingSeconds);
        }

        var outputJson = File.ReadAllText(result.OutputJsonPath);
        var hypothesis = string.Concat(normalizer
            .Normalize(testCase.CaseId, outputJson)
            .Select(static segment => segment.NormalizedText ?? segment.RawText));
        return new AsrManifestEvaluation(
            result.EngineId,
            testCase.DurationBucket,
            true,
            TextMetrics.EditDistance(testCase.ReferenceText, hypothesis),
            testCase.ReferenceText.Length,
            testCase.AudioDurationSeconds,
            result.ProcessingSeconds);
    }

    private static DefaultAsrRecommendation? RecommendDefaultAsr(IReadOnlyList<AsrBenchmarkResult> benchmarks)
    {
        if (benchmarks.Count == 0)
        {
            return null;
        }

        var ranked = benchmarks
            .GroupBy(static item => item.EngineId, StringComparer.OrdinalIgnoreCase)
            .Select(static group =>
            {
                var totalCases = group.Sum(static item => item.CaseCount);
                var failures = group.Sum(static item => item.FailureCount);
                var averageCer = group.Average(static item => item.CharacterErrorRate);
                var averageRtf = group.Average(static item => item.RealTimeFactor);
                return new
                {
                    EngineId = group.Key,
                    Score = averageCer + (failures / (double)Math.Max(totalCases, 1)) + Math.Min(averageRtf, 10) * 0.01,
                    AverageCer = averageCer,
                    AverageRtf = averageRtf
                };
            })
            .OrderBy(static item => item.Score)
            .ToArray();

        var best = ranked[0];
        var second = ranked.Length > 1 ? ranked[1] : best;
        return new DefaultAsrRecommendation(
            best.EngineId,
            second.EngineId,
            $"v0.1 prioritizes lowest CER/failure score ({best.EngineId}: CER {best.AverageCer:P2}, RTF {best.AverageRtf:F2}); v0.2 keeps the next candidate for adapter hardening.");
    }

    private static IReadOnlyList<EvaluationCaseResult> RunFixtureCases()
    {
        var caseStopwatch = Stopwatch.StartNew();
        const string jobId = "phase8-fixture";
        const string hypothesisText = "今日は旧サービス名の評価ベンチを確認します。次のセグメントも品質を測ります。";
        const string referenceText = "今日はKoeNoteの評価ベンチを確認します。次のセグメントも品質を測ります。";
        var segments = new[]
        {
            new TranscriptSegment("000001", jobId, 0, 3, "Speaker_0", hypothesisText)
        };

        var asrEditDistance = TextMetrics.EditDistance(referenceText, hypothesisText);
        var asrCer = TextMetrics.CharacterErrorRate(referenceText, hypothesisText);
        var reviewEvaluation = EvaluateReviewJson(jobId, segments, """
            [
              {
                "segment_id": "000001",
                "issue_type": "固有名詞",
                "original_text": "旧サービス名",
                "suggested_text": "KoeNote",
                "reason": "製品名の表記ゆれを修正します。",
                "confidence": 0.91
              }
            ]
            """);

        var paths = CreateBenchPaths();
        var memoryService = new CorrectionMemoryService(paths);
        memoryService.RememberCorrection(new CorrectionDraft(
            "seed-draft",
            jobId,
            "000000",
            "固有名詞",
            "旧サービス名",
            "KoeNote",
            "fixture seed",
            0.95), "KoeNote");
        var memoryDrafts = memoryService.BuildMemoryDrafts(jobId, segments);

        caseStopwatch.Stop();
        return
        [
            new EvaluationCaseResult(
                "phase8-layer-c-proper-noun-memory",
                asrCer,
                asrEditDistance,
                referenceText.Length,
                reviewEvaluation.DraftCount,
                reviewEvaluation.JsonParseFailed,
                memoryDrafts.Count,
                caseStopwatch.Elapsed)
        ];
    }

    private static EvaluationSummary BuildSummary(IReadOnlyList<EvaluationCaseResult> cases, TimeSpan duration)
    {
        var referenceCharacterCount = cases.Sum(static item => item.ReferenceCharacterCount);
        var asrCer = referenceCharacterCount == 0
            ? 0
            : cases.Sum(static item => item.AsrEditDistance) / (double)referenceCharacterCount;
        var parseFailureRate = cases.Count == 0 ? 0 : cases.Count(static item => item.ReviewJsonParseFailed) / (double)cases.Count;
        var reviewDraftCount = cases.Sum(static item => item.ReviewDraftCount);
        var memoryDraftCount = cases.Sum(static item => item.MemoryDraftCount);
        var regressions = new List<string>();

        if (asrCer > 0.25)
        {
            regressions.Add($"ASR CER exceeded threshold: {asrCer:P2} > 25.00%");
        }

        if (parseFailureRate > 0)
        {
            regressions.Add($"Review JSON parse failure rate exceeded threshold: {parseFailureRate:P2} > 0.00%");
        }

        if (reviewDraftCount == 0)
        {
            regressions.Add("No review draft was produced by the fixture.");
        }

        if (memoryDraftCount == 0)
        {
            regressions.Add("No memory-derived draft was produced by the fixture.");
        }

        return new EvaluationSummary(
            regressions.Count == 0 ? "passed" : "failed",
            asrCer,
            parseFailureRate,
            reviewDraftCount > 0 ? 1 : 0,
            memoryDraftCount > 0 ? 1 : 0,
            0,
            memoryDraftCount,
            duration,
            regressions);
    }

    public static ReviewJsonEvaluation EvaluateReviewJson(
        string jobId,
        IReadOnlyList<TranscriptSegment> segments,
        string rawJson)
    {
        try
        {
            var drafts = new ReviewJsonNormalizer().Normalize(jobId, segments, rawJson, minConfidence: 0.5);
            return new ReviewJsonEvaluation(drafts.Count, false);
        }
        catch (ReviewWorkerException exception) when (exception.Category == ReviewFailureCategory.JsonParseFailed)
        {
            return new ReviewJsonEvaluation(0, true);
        }
    }

    private static HostMetadata CreateHostMetadata()
    {
        using var process = Process.GetCurrentProcess();
        return new HostMetadata(
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            Environment.ProcessorCount,
            process.WorkingSet64);
    }

    private static AppPaths CreateBenchPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.EvalBench", Guid.NewGuid().ToString("N"));
        var local = Path.Combine(Path.GetTempPath(), "KoeNote.EvalBench", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, local);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        return paths;
    }
}

public sealed record ReviewJsonEvaluation(int DraftCount, bool JsonParseFailed);

public sealed record AsrBenchmarkManifest(IReadOnlyList<AsrBenchmarkCase> Cases);

public sealed record AsrBenchmarkCase(
    string CaseId,
    string DurationBucket,
    double AudioDurationSeconds,
    string ReferenceText,
    IReadOnlyList<AsrBenchmarkOutput> Results);

public sealed record AsrBenchmarkOutput(
    string EngineId,
    string OutputJsonPath,
    double ProcessingSeconds,
    bool Succeeded = true);

internal sealed record AsrManifestEvaluation(
    string EngineId,
    string DurationBucket,
    bool Succeeded,
    int EditDistance,
    int ReferenceCharacterCount,
    double AudioDurationSeconds,
    double ProcessingSeconds);
