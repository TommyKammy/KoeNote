using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Review;

namespace KoeNote.EvalBench;

public sealed class EvaluationBenchRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public EvaluationBenchReport Run(EvaluationBenchOptions options)
    {
        var runStarted = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var runId = $"{runStarted:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..27];
        var runDirectory = Path.Combine(options.OutputRoot, runId);
        Directory.CreateDirectory(runDirectory);

        var cases = RunFixtureCases();
        var summary = BuildSummary(cases, stopwatch.Elapsed);
        var reportPath = Path.Combine(runDirectory, "report.json");
        var report = new EvaluationBenchReport(
            reportPath,
            runStarted,
            CreateHostMetadata(),
            summary,
            cases);

        var json = JsonSerializer.Serialize(report, JsonOptions);
        File.WriteAllText(reportPath, json);
        File.WriteAllText(Path.Combine(options.OutputRoot, "latest.json"), json);
        return report;
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
