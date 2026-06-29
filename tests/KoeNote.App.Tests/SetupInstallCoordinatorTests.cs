using KoeNote.App.Services.Setup;

namespace KoeNote.App.Tests;

public sealed class SetupInstallCoordinatorTests
{
    [Fact]
    public async Task RunPresetInstallAsync_RunsPresetInstallStepsInOrder()
    {
        var coordinator = new SetupInstallCoordinator();
        var steps = new List<string>();
        using var cancellation = new CancellationTokenSource();

        var result = await coordinator.RunPresetInstallAsync(
            new SetupInstallSequence(
                Step("models", steps),
                Step("asr-runtime", steps),
                () =>
                {
                    steps.Add("review-runtime");
                    return true;
                },
                Step("asr-cuda-runtime", steps),
                Step("diarization-runtime", steps),
                Step("cuda-review-runtime", steps),
                Step("ternary-review-runtime", steps)),
            cancellation.Token);

        Assert.True(result);
        Assert.Equal(
            [
                "models",
                "asr-runtime",
                "cuda-review-runtime",
                "review-runtime",
                "asr-cuda-runtime",
                "diarization-runtime",
                "ternary-review-runtime"
            ],
            steps);
    }

    [Fact]
    public async Task RunPresetInstallAsync_StopsAtFirstFailedStep()
    {
        var coordinator = new SetupInstallCoordinator();
        var steps = new List<string>();

        var result = await coordinator.RunPresetInstallAsync(
            new SetupInstallSequence(
                Step("models", steps),
                Step("asr-runtime", steps),
                () =>
                {
                    steps.Add("review-runtime");
                    return false;
                },
                Step("asr-cuda-runtime", steps),
                Step("diarization-runtime", steps),
                Step("cuda-review-runtime", steps),
                Step("ternary-review-runtime", steps)));

        Assert.False(result);
        Assert.Equal(["models", "asr-runtime", "cuda-review-runtime", "review-runtime"], steps);
    }

    private static Func<CancellationToken, Task<bool>> Step(string name, ICollection<string> steps)
    {
        return _ =>
        {
            steps.Add(name);
            return Task.FromResult(true);
        };
    }
}
