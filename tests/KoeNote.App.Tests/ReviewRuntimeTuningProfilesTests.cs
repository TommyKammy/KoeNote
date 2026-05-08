using KoeNote.App.Services.Review;

namespace KoeNote.App.Tests;

public sealed class ReviewRuntimeTuningProfilesTests
{
    [Fact]
    public void ForReviewModel_UsesBoundedCpuProfileForTernaryModels()
    {
        var tuning = ReviewRuntimeTuningProfiles.ForReviewModel("ternary-bonsai-8b-q2-0");

        Assert.Equal(TimeSpan.FromMinutes(20), tuning.Timeout);
        Assert.Equal(1024, tuning.ContextSize);
        Assert.Equal(0, tuning.GpuLayers);
        Assert.Equal(192, tuning.MaxTokens);
        Assert.Equal(3, tuning.ChunkSegmentCount);
        Assert.False(tuning.UseJsonSchema);
        Assert.False(tuning.EnableRepair);
        Assert.Equal("compact", tuning.PromptProfile);
        Assert.NotNull(tuning.Threads);
    }

    [Fact]
    public void ForReviewModel_KeepsExistingProfileForStandardModels()
    {
        var tuning = ReviewRuntimeTuningProfiles.ForReviewModel("gemma-4-e4b-it-q4-k-m");

        Assert.Equal(TimeSpan.FromHours(2), tuning.Timeout);
        Assert.Equal(8192, tuning.ContextSize);
        Assert.Equal(999, tuning.GpuLayers);
        Assert.Equal(4096, tuning.MaxTokens);
        Assert.Equal(80, tuning.ChunkSegmentCount);
        Assert.True(tuning.UseJsonSchema);
        Assert.True(tuning.EnableRepair);
        Assert.Equal("default", tuning.PromptProfile);
    }
}
