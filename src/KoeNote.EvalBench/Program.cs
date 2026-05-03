using KoeNote.EvalBench;

var options = EvaluationBenchOptions.FromArgs(args);
var report = new EvaluationBenchRunner().Run(options);

Console.WriteLine($"KoeNote evaluation bench: {report.Summary.Status}");
Console.WriteLine($"Report: {report.ReportPath}");
Console.WriteLine($"CER: {report.Summary.AsrCharacterErrorRate:P2}");
Console.WriteLine($"Review JSON parse failure rate: {report.Summary.ReviewJsonParseFailureRate:P2}");
Console.WriteLine($"Memory suggestions: {report.Summary.MemorySuggestionCount}");

if (report.Summary.Regressions.Count > 0)
{
    Console.WriteLine("Regressions:");
    foreach (var regression in report.Summary.Regressions)
    {
        Console.WriteLine($"- {regression}");
    }
}

return report.Summary.Status == "failed" ? 1 : 0;
