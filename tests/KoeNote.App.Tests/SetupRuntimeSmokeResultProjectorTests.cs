using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Setup;

namespace KoeNote.App.Tests;

public sealed class SetupRuntimeSmokeResultProjectorTests
{
    [Fact]
    public void CreateSkippedAsrGpuSmokeCheck_UsesRuntimeSmokeDetail()
    {
        var runtimeSmoke = new SetupSmokeCheck("ASR runtime smoke", false, "ASR Python runtime is missing");

        var check = SetupRuntimeSmokeResultProjector.CreateSkippedAsrGpuSmokeCheck(runtimeSmoke);

        Assert.Equal("ASR GPU profile smoke", check.Name);
        Assert.False(check.IsOk);
        Assert.Equal("Skipped because ASR runtime smoke failed: ASR Python runtime is missing", check.Detail);
    }

    [Fact]
    public void CreatePassedAsrGpuSmokeCheck_UsesSelectedProfile()
    {
        var profile = AsrExecutionProfiles.Resolve(AsrExecutionProfiles.CudaInt8Float16);

        var check = SetupRuntimeSmokeResultProjector.CreatePassedAsrGpuSmokeCheck(profile);

        Assert.Equal("ASR GPU profile smoke", check.Name);
        Assert.True(check.IsOk);
        Assert.Equal("GPU ASR smoke passed with profile cuda-int8-float16.", check.Detail);
    }

    [Fact]
    public void SummarizeProfileAttempt_ReportsMissingOutputJsonForSuccessfulProcess()
    {
        var profile = AsrExecutionProfiles.Resolve(AsrExecutionProfiles.CudaFloat16);
        var result = new ProcessRunResult(0, TimeSpan.FromSeconds(1), "ok", string.Empty);

        var summary = SetupRuntimeSmokeResultProjector.SummarizeProfileAttempt(
            profile,
            result,
            outputJsonCreated: false);

        Assert.Equal("cuda-float16: process succeeded but output JSON was not created", summary);
    }

    [Fact]
    public void SummarizeProfileAttempt_ClassifiesCudaRuntimeLoadFailures()
    {
        var profile = AsrExecutionProfiles.Resolve(AsrExecutionProfiles.CudaFloat16);
        var result = new ProcessRunResult(
            1,
            TimeSpan.FromSeconds(1),
            string.Empty,
            "cublas64_12.dll failed to load");

        var summary = SetupRuntimeSmokeResultProjector.SummarizeProfileAttempt(
            profile,
            result,
            outputJsonCreated: false);

        Assert.Contains("cuda-float16: missing CUDA runtime (exit 1)", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void SummarizeProfileAttempt_NormalizesAndTrimsFailureOutput()
    {
        var profile = AsrExecutionProfiles.Resolve(AsrExecutionProfiles.CudaFloat32);
        var longOutput = $"line 1{Environment.NewLine}{new string('x', 520)}";
        var result = new ProcessRunResult(1, TimeSpan.FromSeconds(1), longOutput, string.Empty);

        var summary = SetupRuntimeSmokeResultProjector.SummarizeProfileAttempt(
            profile,
            result,
            outputJsonCreated: false);

        Assert.DoesNotContain(Environment.NewLine, summary, StringComparison.Ordinal);
        Assert.Contains("cuda-float32: process failed (exit 1) line 1", summary, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 501), summary, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateFailedAsrGpuSmokeCheck_JoinsProfileFailures()
    {
        var check = SetupRuntimeSmokeResultProjector.CreateFailedAsrGpuSmokeCheck(
            ["cuda-float16: process failed", "cuda-int8-float16: native crash"]);

        Assert.Equal("ASR GPU profile smoke", check.Name);
        Assert.False(check.IsOk);
        Assert.Equal(
            "GPU ASR smoke failed for all CUDA profiles. cuda-float16: process failed | cuda-int8-float16: native crash",
            check.Detail);
    }
}
