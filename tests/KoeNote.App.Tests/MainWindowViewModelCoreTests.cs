using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Input;
using System.Xml.Linq;
using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Diarization;
using KoeNote.App.Services.Export;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Setup;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.SystemStatus;
using KoeNote.App.Services.Transcript;
using KoeNote.App.Services.Updates;
using KoeNote.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Tests;

[Trait("Category", "Slow")]
[Trait("Category", "UiIntegration")]
public sealed class MainWindowViewModelCoreTests : MainWindowViewModelTestBase
{
    [Fact]
    public void SettingsModelSelections_IgnoreTransientNullFromComboBoxRefresh()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "whisper-base", true));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            CurrentStep = SetupStep.ReviewModel,
            SelectedAsrModelId = "whisper-base",
            SelectedReviewModelId = "llm-jp-4-8b-thinking-q4-k-m"
        });
        var viewModel = new MainWindowViewModel(paths);
        var selectedSettingsReview = viewModel.SelectedSettingsReviewModel;
        var selectedSettingsAsr = viewModel.SelectedSettingsAsrEngine;
        var selectedSetupReview = viewModel.SelectedSetupReviewModel;
        var selectedSetupAsr = viewModel.SelectedSetupAsrModel;
        var selectedPreset = viewModel.SelectedSetupModelPreset;

        viewModel.SelectedAsrEngineId = string.Empty;
        viewModel.SelectedSettingsAsrEngine = null;
        viewModel.SelectedSettingsReviewModel = null;
        viewModel.SelectedSetupReviewModel = null;
        viewModel.SelectedSetupAsrModel = null;
        viewModel.SelectedSetupModelPreset = null;

        Assert.Equal("whisper-base", viewModel.SelectedAsrEngineId);
        Assert.Same(selectedSettingsAsr, viewModel.SelectedSettingsAsrEngine);
        Assert.Same(selectedSettingsReview, viewModel.SelectedSettingsReviewModel);
        Assert.Same(selectedSetupReview, viewModel.SelectedSetupReviewModel);
        Assert.Same(selectedSetupAsr, viewModel.SelectedSetupAsrModel);
        Assert.Same(selectedPreset, viewModel.SelectedSetupModelPreset);
    }

    [Fact]
    public void StageStatuses_StartWithReadablePendingStatus()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(3, viewModel.StageStatuses.Count);
        Assert.All(viewModel.StageStatuses, stage => Assert.Equal("未開始", stage.Status));
    }

    [Fact]
    public void TranscriptAutoScroll_IsEnabledByDefault()
    {
        var viewModel = CreateViewModel();

        Assert.True(viewModel.IsTranscriptAutoScrollEnabled);
    }

    [Theory]
    [InlineData(JobRunStageState.Skipped)]
    [InlineData(JobRunStageState.Failed)]
    [InlineData(JobRunStageState.Cancelled)]
    public void ReviewNonSucceededRunUpdate_KeepsCurrentTranscriptTabAndHighlightState(JobRunStageState state)
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedTranscriptTabIndex = 2;
        var method = typeof(MainWindowViewModel).GetMethod(
            "ApplyRunUpdate",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method?.Invoke(viewModel, [
            new JobRunUpdate(
                JobRunStage.Review,
                state,
                100)
        ]);

        Assert.Equal(2, viewModel.SelectedTranscriptTabIndex);
        Assert.False(viewModel.IsPolishedTranscriptTabHighlighted);
        Assert.Equal(string.Empty, viewModel.PolishedTranscriptTabHighlightTag);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    public void SelectedTranscriptTabIndex_ClampsToAvailableTabs(int value, int expected)
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedTranscriptTabIndex = value;

        Assert.Equal(expected, viewModel.SelectedTranscriptTabIndex);
    }

    [Theory]
    [InlineData("GPU Unknown", true)]
    [InlineData("GPU 未検出", true)]
    [InlineData("GPU 12% / 4096 MB", false)]
    public void IsGpuUsageUnknown_ReflectsStatusBarGpuSummary(string summary, bool expected)
    {
        var viewModel = CreateViewModel();
        SetPrivateField(
            viewModel,
            "_statusBarInfo",
            new StatusBarInfo("空き容量 100 GB", "MEM 100 MB", "CPU 0%", summary));

        Assert.Equal(expected, viewModel.IsGpuUsageUnknown);
    }

    [Fact]
    public void DetailPanelCommands_OpenAndCloseWidePanel()
    {
        var viewModel = CreateViewModel();

        Assert.True(viewModel.OpenSelectedDetailPanelCommand.CanExecute(null));
        viewModel.OpenSelectedDetailPanelCommand.Execute(null);

        Assert.True(viewModel.IsDetailPanelOpen);
        Assert.Equal(0, viewModel.SelectedDetailPanelTabIndex);
        Assert.Equal("設定", viewModel.DetailPanelTitle);

        viewModel.SelectedLogPanelTabIndex = 2;
        Assert.True(viewModel.OpenSelectedDetailPanelCommand.CanExecute(null));
        viewModel.OpenSelectedDetailPanelCommand.Execute(null);

        Assert.True(viewModel.IsDetailPanelOpen);
        Assert.Equal(2, viewModel.SelectedDetailPanelTabIndex);
        Assert.Equal("モデル", viewModel.DetailPanelTitle);

        viewModel.SelectedLogPanelTabIndex = 1;
        viewModel.OpenSelectedDetailPanelCommand.Execute(null);

        Assert.True(viewModel.IsDetailPanelOpen);
        Assert.Equal(1, viewModel.SelectedDetailPanelTabIndex);
        Assert.Equal("辞書プリセット", viewModel.DetailPanelTitle);

        viewModel.CloseDetailPanelCommand.Execute(null);
        Assert.False(viewModel.IsDetailPanelOpen);
    }
}
