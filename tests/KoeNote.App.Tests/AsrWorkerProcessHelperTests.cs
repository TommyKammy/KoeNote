using KoeNote.App.Services.Asr;

namespace KoeNote.App.Tests;

public sealed class AsrWorkerProcessHelperTests
{
    [Fact]
    public void BuildArgumentSummary_RedactsContextAndHotwords()
    {
        var summary = AsrWorkerCommandBuilder.BuildArgumentSummary([
            "worker.py",
            "--context",
            "customer name",
            "--hotword",
            "private phrase",
            "--device",
            "cuda"
        ]);

        Assert.Contains("--context (redacted)", summary);
        Assert.Contains("--hotword (redacted)", summary);
        Assert.Contains("--device cuda", summary);
        Assert.DoesNotContain("customer name", summary);
        Assert.DoesNotContain("private phrase", summary);
    }

    [Fact]
    public void ClassifyProcessFailure_ReturnsCudaRuntimeMissingForCudaDllLoadFailures()
    {
        var category = AsrWorkerFailureClassifier.ClassifyProcessFailure(
            1,
            "Could not load cublas64_12.dll: specified module could not be found");

        Assert.Equal(AsrFailureCategory.CudaRuntimeMissing, category);
    }

    [Fact]
    public void ClassifyProcessFailure_ReturnsNativeCrashForNegativeExitCodes()
    {
        var category = AsrWorkerFailureClassifier.ClassifyProcessFailure(-1073741819, string.Empty);

        Assert.Equal(AsrFailureCategory.NativeCrash, category);
        Assert.Contains("0xC0000005", AsrWorkerFailureClassifier.DescribeExitCode(-1073741819));
    }
}
