using KoeNote.EvalBench;

namespace KoeNote.App.Tests;

public sealed class EvaluationBenchAsrManifestTests
{
    [Fact]
    public void Run_WithAsrManifest_ComparesEnginesAndRecommendsDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var outputRoot = Path.Combine(root, "out");
        var perfectOutput = Path.Combine(root, "perfect.json");
        var noisyOutput = Path.Combine(root, "noisy.json");
        var manifestPath = Path.Combine(root, "manifest.json");
        File.WriteAllText(perfectOutput, """{"segments":[{"start":0,"end":30,"text":"KoeNote"}]}""");
        File.WriteAllText(noisyOutput, """{"segments":[{"start":0,"end":30,"text":"Koe"}]}""");
        File.WriteAllText(manifestPath, $$"""
            {
              "cases": [
                {
                  "caseId": "case-30s",
                  "durationBucket": "30s",
                  "audioDurationSeconds": 30,
                  "referenceText": "KoeNote",
                  "results": [
                    {
                      "engineId": "faster-whisper-large-v3-turbo",
                      "outputJsonPath": "{{JsonPath(perfectOutput)}}",
                      "processingSeconds": 12,
                      "succeeded": true
                    },
                    {
                      "engineId": "reazonspeech-k2-v3",
                      "outputJsonPath": "{{JsonPath(noisyOutput)}}",
                      "processingSeconds": 9,
                      "succeeded": true
                    }
                  ]
                }
              ]
            }
            """);

        var report = new EvaluationBenchRunner().Run(new EvaluationBenchOptions(outputRoot, manifestPath));

        Assert.Equal(2, report.AsrBenchmarks.Count);
        Assert.Contains(report.AsrBenchmarks, item =>
            item.EngineId == "faster-whisper-large-v3-turbo" &&
            item.DurationBucket == "30s" &&
            item.CharacterErrorRate == 0);
        Assert.Equal("faster-whisper-large-v3-turbo", report.DefaultAsrRecommendation?.V01EngineId);
    }

    [Fact]
    public void Run_WithFailedAsrResult_SeparatesCerFromFailureRate()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var outputRoot = Path.Combine(root, "out");
        var manifestPath = Path.Combine(root, "manifest.json");
        File.WriteAllText(manifestPath, """
            {
              "cases": [
                {
                  "caseId": "case-5m",
                  "durationBucket": "5m",
                  "audioDurationSeconds": 300,
                  "referenceText": "KoeNote",
                  "results": [
                    {
                      "engineId": "failed-engine",
                      "outputJsonPath": "",
                      "processingSeconds": 0,
                      "succeeded": false
                    }
                  ]
                }
              ]
            }
            """);

        var report = new EvaluationBenchRunner().Run(new EvaluationBenchOptions(outputRoot, manifestPath));

        var result = Assert.Single(report.AsrBenchmarks);
        Assert.Equal("failed-engine", result.EngineId);
        Assert.Equal(1, result.FailureRate);
        Assert.Equal(1, result.CharacterErrorRate);
        Assert.Equal(0, result.RealTimeFactor);
    }

    private static string JsonPath(string path) => path.Replace("\\", "\\\\");
}
