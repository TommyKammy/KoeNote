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
public sealed class MainWindowViewModelSetupTests : MainWindowViewModelTestBase
{
    [Fact]
    public async Task SelectedAsrEngine_RestoresAfterRecreatingViewModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        var first = new MainWindowViewModel(paths)
        {
            SelectedAsrEngineId = "faster-whisper-large-v3-turbo"
        };
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        var second = new MainWindowViewModel(paths);

        Assert.Equal(first.SelectedAsrEngineId, second.SelectedAsrEngineId);
        Assert.Equal(
            ["faster-whisper-large-v3", "faster-whisper-large-v3-turbo", "kotoba-whisper-v2.2-faster", "whisper-base", "whisper-small"],
            second.AvailableAsrEngines.Select(engine => engine.EngineId).Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    [Fact]
    public void HeaderModels_ReflectInstalledReviewModelAfterSetupDownloadWithoutCatalogRefresh()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            SelectedModelPresetId = "recommended",
            SelectedReviewModelId = "gemma-4-e4b-it-q4-k-m",
            StorageRoot = paths.UserModels
        });
        var viewModel = new MainWindowViewModel(paths);
        Assert.Equal("未設定", viewModel.ReviewModel);

        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var installService = new ModelInstallService(paths, new InstalledModelRepository(paths), new ModelVerificationService());
        var reviewItem = catalog.Models.First(model => model.ModelId == "gemma-4-e4b-it-q4-k-m");
        var reviewPath = installService.GetDefaultInstallPath(reviewItem);
        Touch(reviewPath);
        installService.RegisterLocalModel(reviewItem, reviewPath, "download");

        Assert.Equal("Gemma 4 E4B it Q4_K_M", viewModel.ReviewModel);
    }

    [Fact]
    public void SettingsAsrEngine_ReflectsInstalledModelAfterSetupWizardRefresh()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        CreateFasterWhisperRuntime(paths);
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            SelectedModelPresetId = "high_accuracy",
            SelectedAsrModelId = "faster-whisper-large-v3-turbo",
            StorageRoot = paths.UserModels
        });
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "faster-whisper-large-v3-turbo"));
        var viewModel = new MainWindowViewModel(paths);
        Assert.False(viewModel.AvailableAsrEngines.Single(engine => engine.EngineId == "faster-whisper-large-v3-turbo").IsInstalled);

        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var installService = new ModelInstallService(paths, new InstalledModelRepository(paths), new ModelVerificationService());
        var asrItem = catalog.Models.First(model => model.ModelId == "faster-whisper-large-v3-turbo");
        var asrPath = installService.GetDefaultInstallPath(asrItem);
        Directory.CreateDirectory(asrPath);
        File.WriteAllText(Path.Combine(asrPath, "model.bin"), "asr");
        installService.RegisterLocalModel(asrItem, asrPath, "download");

        InvokePrivate(viewModel, "RefreshSetupWizard", true);

        var option = viewModel.AvailableAsrEngines.Single(engine => engine.EngineId == "faster-whisper-large-v3-turbo");
        Assert.True(option.IsInstalled);
        Assert.True(viewModel.RequiredRuntimeAssetsReady);
    }

    [Fact]
    public void HeaderModels_RegistersDownloadedSetupAsrFolderWhenInstallRecordIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var asrPath = Path.Combine(paths.UserModels, "asr", "whisper-base");
        Directory.CreateDirectory(asrPath);
        File.WriteAllText(Path.Combine(asrPath, "model.bin"), "asr");
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "vibevoice-asr-gguf", true));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            SelectedModelPresetId = "lightweight",
            SelectedAsrModelId = "whisper-base",
            SelectedReviewModelId = "bonsai-8b-q1-0",
            StorageRoot = paths.UserModels
        });

        var viewModel = new MainWindowViewModel(paths);

        Assert.Equal("whisper-base", viewModel.SelectedAsrEngineId);
        Assert.Equal("Whisper base", viewModel.AsrModel);
        var installed = new InstalledModelRepository(paths).FindInstalledModel("whisper-base");
        Assert.NotNull(installed);
        Assert.Equal(asrPath, installed.FilePath);
    }

    [Fact]
    public void HeaderModels_RegistersDownloadedSetupReviewFileWhenInstallRecordIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var reviewPath = Path.Combine(paths.UserModels, "review", "bonsai-8b-q1-0", "Bonsai-8B-Q1_0.gguf");
        Touch(reviewPath);
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            SelectedModelPresetId = "lightweight",
            SelectedAsrModelId = "whisper-base",
            SelectedReviewModelId = "bonsai-8b-q1-0",
            StorageRoot = paths.UserModels
        });

        var viewModel = new MainWindowViewModel(paths);

        Assert.Equal("Bonsai 8B Q1_0", viewModel.ReviewModel);
        Assert.Equal("bonsai-8b-q1-0", viewModel.SelectedSetupReviewModel?.ModelId);
        var installed = new InstalledModelRepository(paths).FindInstalledModel("bonsai-8b-q1-0");
        Assert.NotNull(installed);
        Assert.Equal(reviewPath, installed.FilePath);
    }

    [Fact]
    public void AsrEngineOption_ToStringReturnsDisplayNameForComboBoxFallbackRendering()
    {
        var option = new AsrEngineOption("whisper-base", "Whisper base");

        Assert.Equal("Whisper base", option.ToString());
    }

    [Fact]
    public void AsrEngineOption_ToStringShowsInstalledStatusWhenInstalled()
    {
        var option = new AsrEngineOption("whisper-base", "Whisper base", IsInstalled: true);

        Assert.Equal("Whisper base (導入済み)", option.ToString());
        Assert.Equal("Whisper base (導入済み)", option.SetupDisplayName);
    }

    [Fact]
    public void SetupInstallSelectedPresetCommand_RemainsEnabledWhenOnlyDiarizationRuntimeIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var installService = new ModelInstallService(paths, new InstalledModelRepository(paths), new ModelVerificationService());
        var asrItem = catalog.Models.First(model => model.ModelId == "whisper-base");
        Directory.CreateDirectory(paths.WhisperBaseModelPath);
        File.WriteAllText(Path.Combine(paths.WhisperBaseModelPath, "model.bin"), "asr");
        installService.RegisterLocalModel(asrItem, paths.WhisperBaseModelPath, "download");
        var reviewItem = catalog.Models.First(model => model.ModelId == "bonsai-8b-q1-0");
        var reviewPath = installService.GetDefaultInstallPath(reviewItem);
        Touch(reviewPath);
        installService.RegisterLocalModel(reviewItem, reviewPath, "download");
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            CurrentStep = SetupStep.AsrModel,
            SetupMode = "lightweight",
            SelectedModelPresetId = "lightweight",
            SelectedAsrModelId = asrItem.ModelId,
            SelectedReviewModelId = reviewItem.ModelId,
            LicenseAccepted = true
        });

        var viewModel = new MainWindowViewModel(paths);

        Assert.True(viewModel.SelectedSetupModelsReady);
        Assert.False(viewModel.SetupDiarizationRuntimeReady);
        Assert.False(viewModel.SelectedSetupConfigurationReady);
        Assert.True(viewModel.SetupInstallSelectedPresetCommand.CanExecute(null));
        Assert.Contains("話者識別runtime", viewModel.SetupPrimaryInstallSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedSetupConfigurationReady_DoesNotRequireCudaReviewRuntimeWithoutDetectedGpu()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var appBaseDirectory = Path.Combine(root, "app");
        Directory.CreateDirectory(Path.Combine(appBaseDirectory, "catalog"));
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "catalog", "model-catalog.json"),
            Path.Combine(appBaseDirectory, "catalog", "model-catalog.json"));
        var paths = new AppPaths(root, root, appBaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var installService = new ModelInstallService(paths, new InstalledModelRepository(paths), new ModelVerificationService());
        var asrItem = catalog.Models.First(model => model.ModelId == "faster-whisper-large-v3");
        var asrPath = installService.GetDefaultInstallPath(asrItem);
        Directory.CreateDirectory(asrPath);
        File.WriteAllText(Path.Combine(asrPath, "model.bin"), "asr");
        installService.RegisterLocalModel(asrItem, asrPath, "download");
        var reviewItem = catalog.Models.First(model => model.ModelId == "gemma-4-e4b-it-q4-k-m");
        var reviewPath = installService.GetDefaultInstallPath(reviewItem);
        Touch(reviewPath);
        installService.RegisterLocalModel(reviewItem, reviewPath, "download");
        CreateFasterWhisperRuntime(paths);
        Touch(paths.LlamaCompletionPath);
        CreateDiarizationRuntime(paths);
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            LicenseAccepted = true,
            CurrentStep = SetupStep.InstallPlan,
            SetupMode = "recommended",
            SelectedModelPresetId = "recommended",
            SelectedAsrModelId = asrItem.ModelId,
            SelectedReviewModelId = reviewItem.ModelId
        });
        var viewModel = new MainWindowViewModel(paths);
        var noGpuRecommendation = new SetupPresetRecommendation(
            "recommended",
            "Recommended",
            "No GPU",
            new SetupHostResources(null, null, NvidiaGpuDetected: false, LogicalProcessorCount: null, "No GPU"));
        SetPrivateField(viewModel, "_setupPresetRecommendation", noGpuRecommendation);

        Assert.False(viewModel.SetupCudaReviewRuntimeRecommended);
        Assert.False(viewModel.SetupCudaReviewRuntimeReady);
        Assert.True(viewModel.SelectedSetupConfigurationReady);
        Assert.False(viewModel.SetupInstallCudaReviewRuntimeCommand.CanExecute(null));
    }

    [Fact]
    public void SelectedSetupConfigurationReady_RequiresAsrCudaRuntimeWithDetectedGpu()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var appBaseDirectory = Path.Combine(root, "app");
        Directory.CreateDirectory(Path.Combine(appBaseDirectory, "catalog"));
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "catalog", "model-catalog.json"),
            Path.Combine(appBaseDirectory, "catalog", "model-catalog.json"));
        var paths = new AppPaths(root, root, appBaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var installService = new ModelInstallService(paths, new InstalledModelRepository(paths), new ModelVerificationService());
        var asrItem = catalog.Models.First(model => model.ModelId == "faster-whisper-large-v3");
        var asrPath = installService.GetDefaultInstallPath(asrItem);
        Directory.CreateDirectory(asrPath);
        File.WriteAllText(Path.Combine(asrPath, "model.bin"), "asr");
        installService.RegisterLocalModel(asrItem, asrPath, "download");
        var reviewItem = catalog.Models.First(model => model.ModelId == "gemma-4-e4b-it-q4-k-m");
        var reviewPath = installService.GetDefaultInstallPath(reviewItem);
        Touch(reviewPath);
        installService.RegisterLocalModel(reviewItem, reviewPath, "download");
        CreateFasterWhisperRuntime(paths);
        Touch(paths.LlamaCompletionPath);
        Touch(Path.Combine(paths.CudaReviewRuntimeDirectory, "ggml-cuda.dll"));
        Touch(Path.Combine(paths.CudaReviewRuntimeDirectory, "cublas64_12.dll"));
        Touch(Path.Combine(paths.CudaReviewRuntimeDirectory, "cublasLt64_12.dll"));
        Touch(Path.Combine(paths.CudaReviewRuntimeDirectory, "cudart64_12.dll"));
        Touch(paths.CudaReviewRuntimeMarkerPath);
        CreateDiarizationRuntime(paths);
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            LicenseAccepted = true,
            CurrentStep = SetupStep.InstallPlan,
            SetupMode = "high_accuracy",
            SelectedModelPresetId = "high_accuracy",
            SelectedAsrModelId = asrItem.ModelId,
            SelectedReviewModelId = reviewItem.ModelId
        });
        var viewModel = new MainWindowViewModel(paths);
        var gpuRecommendation = new SetupPresetRecommendation(
            "high_accuracy",
            "High accuracy",
            "GPU",
            new SetupHostResources(null, 12, NvidiaGpuDetected: true, LogicalProcessorCount: null, "GPU"));
        SetPrivateField(viewModel, "_setupPresetRecommendation", gpuRecommendation);

        Assert.True(viewModel.SetupAsrCudaRuntimeRecommended);
        Assert.False(viewModel.SetupAsrCudaRuntimeReady);
        Assert.True(viewModel.SetupGpuRuntimeRequiredButMissing);
        Assert.False(viewModel.SelectedSetupConfigurationReady);
        Assert.Contains("ASR GPU runtime", viewModel.SetupPrimaryInstallSummary, StringComparison.Ordinal);
        Assert.Contains("GPU runtime", viewModel.SetupWizardModalTitle, StringComparison.Ordinal);
        Assert.True(viewModel.SetupInstallAsrCudaRuntimeCommand.CanExecute(null));
    }

    [Fact]
    public void SelectedSetupConfigurationReady_RequiresCudaReviewRuntimeWithDetectedGpu()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var appBaseDirectory = Path.Combine(root, "app");
        Directory.CreateDirectory(Path.Combine(appBaseDirectory, "catalog"));
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "catalog", "model-catalog.json"),
            Path.Combine(appBaseDirectory, "catalog", "model-catalog.json"));
        var paths = new AppPaths(root, root, appBaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var installService = new ModelInstallService(paths, new InstalledModelRepository(paths), new ModelVerificationService());
        var asrItem = catalog.Models.First(model => model.ModelId == "faster-whisper-large-v3");
        var asrPath = installService.GetDefaultInstallPath(asrItem);
        Directory.CreateDirectory(asrPath);
        File.WriteAllText(Path.Combine(asrPath, "model.bin"), "asr");
        installService.RegisterLocalModel(asrItem, asrPath, "download");
        var reviewItem = catalog.Models.First(model => model.ModelId == "gemma-4-e4b-it-q4-k-m");
        var reviewPath = installService.GetDefaultInstallPath(reviewItem);
        Touch(reviewPath);
        installService.RegisterLocalModel(reviewItem, reviewPath, "download");
        CreateFasterWhisperRuntime(paths);
        Touch(paths.LlamaCompletionPath);
        CreateDiarizationRuntime(paths);
        Touch(Path.Combine(paths.AsrCTranslate2RuntimeDirectory, "cublas64_12.dll"));
        Touch(Path.Combine(paths.AsrCTranslate2RuntimeDirectory, "cublasLt64_12.dll"));
        Touch(Path.Combine(paths.AsrCTranslate2RuntimeDirectory, "cudart64_12.dll"));
        Touch(Path.Combine(paths.AsrCTranslate2RuntimeDirectory, "cudnn64_9.dll"));
        Touch(Path.Combine(paths.AsrRuntimeDirectory, "crispasr.exe"));
        Touch(Path.Combine(paths.AsrRuntimeDirectory, "crispasr.dll"));
        Touch(Path.Combine(paths.AsrRuntimeDirectory, "whisper.dll"));
        Touch(Path.Combine(paths.AsrRuntimeDirectory, "ggml-cuda.dll"));
        Touch(paths.AsrCudaRuntimeMarkerPath);
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            LicenseAccepted = true,
            CurrentStep = SetupStep.InstallPlan,
            SetupMode = "recommended",
            SelectedModelPresetId = "recommended",
            SelectedAsrModelId = asrItem.ModelId,
            SelectedReviewModelId = reviewItem.ModelId
        });
        var viewModel = new MainWindowViewModel(paths);
        var gpuRecommendation = new SetupPresetRecommendation(
            "recommended",
            "Recommended",
            "GPU",
            new SetupHostResources(null, 12, NvidiaGpuDetected: true, LogicalProcessorCount: null, "GPU"));
        SetPrivateField(viewModel, "_setupPresetRecommendation", gpuRecommendation);

        Assert.True(viewModel.SetupCudaReviewRuntimeRecommended);
        Assert.False(viewModel.SetupCudaReviewRuntimeReady);
        Assert.False(viewModel.SelectedSetupConfigurationReady);
        Assert.Contains("Review GPU runtime", viewModel.SetupPrimaryInstallSummary, StringComparison.Ordinal);
        Assert.True(viewModel.SetupInstallCudaReviewRuntimeCommand.CanExecute(null));
    }

    [Fact]
    public void SetupInstallSelectedPresetCommand_AcceptsLicensesAndStartsInstall()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.SetupLicenseAccepted);
        Assert.True(viewModel.SetupInstallSelectedPresetCommand.CanExecute(null));

        viewModel.SetupInstallSelectedPresetCommand.Execute(null);

        Assert.Equal(nameof(SetupStep.InstallPlan), viewModel.SetupCurrentStep);
        Assert.True(viewModel.SetupLicenseAccepted);
        Assert.True(viewModel.IsModelDownloadInProgress);
        Assert.True(viewModel.SetupCancelInstallCommand.CanExecute(null));
        viewModel.SetupCancelInstallCommand.Execute(null);
    }

    [Fact]
    public async Task SetupInstallSelectedPresetCommand_StopsWhenDiarizationRuntimeInstallFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var appBaseDirectory = Path.Combine(root, "app");
        Directory.CreateDirectory(Path.Combine(appBaseDirectory, "catalog"));
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "catalog", "model-catalog.json"),
            Path.Combine(appBaseDirectory, "catalog", "model-catalog.json"));
        var paths = new AppPaths(root, root, appBaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        CreateFasterWhisperRuntime(paths);
        Touch(paths.LlamaCompletionPath);
        CreateAsrCudaRuntime(paths);
        CreateCudaReviewRuntime(paths);

        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var installService = new ModelInstallService(paths, new InstalledModelRepository(paths), new ModelVerificationService());
        var installedModels = new InstalledModelRepository(paths);
        var now = DateTimeOffset.Now;
        var asrItem = catalog.Models.First(model => model.ModelId == "faster-whisper-large-v3-turbo");
        var asrPath = installService.GetDefaultInstallPath(asrItem);
        Directory.CreateDirectory(asrPath);
        Touch(Path.Combine(asrPath, "model.bin"));
        installedModels.UpsertInstalledModel(new InstalledModel(
            asrItem.ModelId,
            asrItem.Role,
            asrItem.EngineId,
            asrItem.DisplayName,
            asrItem.Family,
            null,
            asrPath,
            null,
            null,
            null,
            true,
            asrItem.License.Name,
            "download",
            now,
            now,
            "installed"));
        var reviewItem = catalog.Models.First(model => model.ModelId == "gemma-4-e4b-it-q4-k-m");
        var reviewPath = installService.GetDefaultInstallPath(reviewItem);
        Touch(reviewPath);
        installedModels.UpsertInstalledModel(new InstalledModel(
            reviewItem.ModelId,
            reviewItem.Role,
            reviewItem.EngineId,
            reviewItem.DisplayName,
            reviewItem.Family,
            null,
            reviewPath,
            null,
            null,
            null,
            true,
            reviewItem.License.Name,
            "download",
            now,
            now,
            "installed"));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            LicenseAccepted = true,
            CurrentStep = SetupStep.InstallPlan,
            SetupMode = "recommended",
            SelectedModelPresetId = "recommended",
            SelectedAsrModelId = asrItem.ModelId,
            SelectedReviewModelId = reviewItem.ModelId,
            StorageRoot = paths.UserModels
        });

        var viewModel = new MainWindowViewModel(paths);
        SetPrivateField(viewModel, "_setupPresetRecommendation", new SetupPresetRecommendation(
            "recommended",
            "Recommended",
            "No GPU",
            new SetupHostResources(null, null, NvidiaGpuDetected: false, LogicalProcessorCount: null, "No GPU")));

        var originalPipNoIndex = Environment.GetEnvironmentVariable("PIP_NO_INDEX");
        Environment.SetEnvironmentVariable("PIP_NO_INDEX", "1");
        try
        {
            viewModel.SetupInstallSelectedPresetCommand.Execute(null);
            for (var i = 0; i < 400 && viewModel.IsModelDownloadInProgress; i++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PIP_NO_INDEX", originalPipNoIndex);
        }

        Assert.False(viewModel.IsModelDownloadInProgress);
        Assert.False(viewModel.IsSetupComplete);
        Assert.Equal(nameof(SetupStep.Install), viewModel.SetupCurrentStep);
        Assert.Contains("Failure category:", viewModel.ModelDownloadProgressSummary, StringComparison.Ordinal);
        Assert.Contains("diarize", viewModel.SetupDiarizationRuntimeSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetupWizardInstallPlanItems_ShowSelectedConfigurationBeforeInstall()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            LicenseAccepted = true,
            CurrentStep = SetupStep.InstallPlan
        });

        var viewModel = new MainWindowViewModel(paths);

        Assert.Contains(viewModel.SetupInstallPlanItems, item => item.Name == "文字起こしモデル");
        Assert.Contains(viewModel.SetupInstallPlanItems, item => item.Name == "整文モデル");
        Assert.Contains(viewModel.SetupInstallPlanItems, item => item.Name == "ASR runtime");
        Assert.Contains(viewModel.SetupInstallPlanItems, item => item.Name == "保存先");
        Assert.DoesNotContain(viewModel.SetupInstallPlanItems, item => item.Summary.Contains(paths.UserModels, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SetupCompleteActionText_ChangesToStartWhenSetupIsComplete()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            CurrentStep = SetupStep.Complete,
            LicenseAccepted = true,
            LastSmokeSucceeded = true
        });

        var viewModel = new MainWindowViewModel(paths);

        Assert.Equal("KoeNoteを開始", viewModel.SetupCompleteActionText);
    }

    [Fact]
    public void SetupNextAction_UsesStartWhenCompletedStateIsLeftOnSmokeStep()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var appBaseDirectory = Path.Combine(root, "app");
        Directory.CreateDirectory(Path.Combine(appBaseDirectory, "catalog"));
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "catalog", "model-catalog.json"),
            Path.Combine(appBaseDirectory, "catalog", "model-catalog.json"));
        var paths = new AppPaths(root, root, appBaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        CreateFasterWhisperRuntime(paths);
        CreateDiarizationRuntime(paths);
        CreateAsrCudaRuntime(paths);
        CreateCudaReviewRuntime(paths);
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var installService = new ModelInstallService(paths, new InstalledModelRepository(paths), new ModelVerificationService());
        var installedModels = new InstalledModelRepository(paths);
        var now = DateTimeOffset.Now;
        var asrItem = catalog.Models.First(model => model.ModelId == "whisper-small");
        var asrPath = installService.GetDefaultInstallPath(asrItem);
        Directory.CreateDirectory(asrPath);
        Touch(Path.Combine(asrPath, "model.bin"));
        installedModels.UpsertInstalledModel(new InstalledModel(
            asrItem.ModelId,
            asrItem.Role,
            asrItem.EngineId,
            asrItem.DisplayName,
            asrItem.Family,
            null,
            asrPath,
            null,
            null,
            null,
            true,
            asrItem.License.Name,
            "download",
            now,
            now,
            "installed"));
        var reviewItem = catalog.Models.First(model => model.ModelId == "bonsai-8b-q1-0");
        var reviewPath = installService.GetDefaultInstallPath(reviewItem);
        Touch(reviewPath);
        installedModels.UpsertInstalledModel(new InstalledModel(
            reviewItem.ModelId,
            reviewItem.Role,
            reviewItem.EngineId,
            reviewItem.DisplayName,
            reviewItem.Family,
            null,
            reviewPath,
            null,
            null,
            null,
            true,
            reviewItem.License.Name,
            "download",
            now,
            now,
            "installed"));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            CurrentStep = SetupStep.SmokeTest,
            LastSmokeSucceeded = true,
            LicenseAccepted = true,
            SetupMode = "lightweight",
            SelectedModelPresetId = "lightweight",
            SelectedAsrModelId = asrItem.ModelId,
            SelectedReviewModelId = reviewItem.ModelId,
            StorageRoot = paths.UserModels
        });

        var viewModel = new MainWindowViewModel(paths);
        SetPrivateField(viewModel, "_setupPresetRecommendation", new SetupPresetRecommendation(
            "lightweight",
            "Lightweight",
            "No GPU",
            new SetupHostResources(null, null, NvidiaGpuDetected: false, LogicalProcessorCount: null, "No GPU")));

        Assert.True(viewModel.IsSetupComplete);
        Assert.Equal(nameof(SetupStep.SmokeTest), viewModel.SetupCurrentStep);
        Assert.True(viewModel.SelectedSetupConfigurationReady);
        Assert.Equal("KoeNoteを開始", viewModel.SetupNextActionText);
        Assert.True(viewModel.SetupNextCommand.CanExecute(null));
    }

    [Fact]
    public void SetupWizardModalText_UsesShortPresetFirstFlowCopy()
    {
        var viewModel = CreateViewModel();

        Assert.Equal("モデル構成を選びます", viewModel.SetupWizardModalTitle);
        Assert.Contains("おすすめ構成", viewModel.SetupWizardModalGuide, StringComparison.Ordinal);
        Assert.Equal("完了", viewModel.SetupCompleteActionText);
        Assert.Equal("現在のステップ: モデル構成", viewModel.SetupStatusSummary);
    }

    [Fact]
    public void SetupWizardModalText_OffersInstallActionWhenCompletedSelectionIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            CurrentStep = SetupStep.Complete,
            SetupMode = "ultra_lightweight",
            SelectedModelPresetId = "ultra_lightweight",
            LicenseAccepted = true,
            SelectedAsrModelId = "whisper-base",
            SelectedReviewModelId = "bonsai-8b-q1-0"
        });

        var viewModel = new MainWindowViewModel(paths);

        Assert.True(viewModel.IsSetupComplete);
        Assert.Equal(nameof(SetupStep.Complete), viewModel.SetupCurrentStep);
        Assert.Equal("ダウンロードとインストール", viewModel.SetupNextActionText);
        Assert.True(viewModel.SetupNextCommand.CanExecute(null));
    }

    [Fact]
    public void SetupFailureMessages_ExplainRequiredRuntimeFailures()
    {
        var diarizationMessage = InvokePrivateStatic<string>(
            "BuildDiarizationRuntimeSetupFailureMessage",
            "network unavailable",
            DiarizationRuntimeService.FailureCategoryNetworkUnavailable);
        var cudaMessage = InvokePrivateStatic<string>(
            "BuildCudaReviewRuntimeSetupFailureMessage",
            "network unavailable",
            CudaReviewRuntimeService.FailureCategoryNetworkUnavailable);
        var packageDataMessage = InvokePrivateStatic<string>(
            "BuildDiarizationRuntimeSetupFailureMessage",
            "missing silero_vad.jit",
            DiarizationRuntimeService.FailureCategoryPackageDataMissing);

        Assert.Contains("diarize could not be downloaded", diarizationMessage, StringComparison.Ordinal);
        Assert.Contains("required runtime data is missing", packageDataMessage, StringComparison.Ordinal);
        Assert.Contains("CUDA review runtime could not download", cudaMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("CPU review runtime remains available", cudaMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupInstallCudaReviewRuntimeCommand_RequiresDetectedGpuAndMissingCudaRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = CreatePathsWithoutTernaryRuntime(root);
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            LicenseAccepted = true,
            CurrentStep = SetupStep.InstallPlan,
            SetupMode = "ultra_lightweight",
            SelectedModelPresetId = "ultra_lightweight",
            SelectedAsrModelId = "whisper-base",
            SelectedReviewModelId = "bonsai-8b-q1-0"
        });
        var viewModel = new MainWindowViewModel(paths);
        var noGpuRecommendation = new SetupPresetRecommendation(
            "recommended",
            "Recommended",
            "No GPU",
            new SetupHostResources(null, null, NvidiaGpuDetected: false, LogicalProcessorCount: null, "No GPU"));
        SetPrivateField(viewModel, "_setupPresetRecommendation", noGpuRecommendation);

        Assert.False(viewModel.SetupInstallCudaReviewRuntimeCommand.CanExecute(null));

        var gpuRecommendation = noGpuRecommendation with
        {
            Resources = new SetupHostResources(null, 12, NvidiaGpuDetected: true, LogicalProcessorCount: null, "GPU")
        };
        SetPrivateField(viewModel, "_setupPresetRecommendation", gpuRecommendation);

        Assert.True(viewModel.SetupInstallCudaReviewRuntimeCommand.CanExecute(null));

        Touch(viewModel.Paths.LlamaCompletionPath);
        Touch(Path.Combine(viewModel.Paths.CudaReviewRuntimeDirectory, "ggml-cuda.dll"));
        Touch(Path.Combine(viewModel.Paths.CudaReviewRuntimeDirectory, "cublas64_12.dll"));
        Touch(Path.Combine(viewModel.Paths.CudaReviewRuntimeDirectory, "cublasLt64_12.dll"));
        Touch(Path.Combine(viewModel.Paths.CudaReviewRuntimeDirectory, "cudart64_12.dll"));
        Touch(viewModel.Paths.CudaReviewRuntimeMarkerPath);

        Assert.False(viewModel.SetupInstallCudaReviewRuntimeCommand.CanExecute(null));
        Assert.Equal("導入済み", viewModel.SetupCudaReviewRuntimeActionText);
    }

    [Fact]
    public void SetupInstallAsrCudaRuntimeCommand_RequiresDetectedGpuAndMissingAsrCudaRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = CreatePathsWithoutTernaryRuntime(root);
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            LicenseAccepted = true,
            CurrentStep = SetupStep.InstallPlan,
            SetupMode = "ultra_lightweight",
            SelectedModelPresetId = "ultra_lightweight",
            SelectedAsrModelId = "whisper-base",
            SelectedReviewModelId = "bonsai-8b-q1-0"
        });
        var viewModel = new MainWindowViewModel(paths);
        var noGpuRecommendation = new SetupPresetRecommendation(
            "recommended",
            "Recommended",
            "No GPU",
            new SetupHostResources(null, null, NvidiaGpuDetected: false, LogicalProcessorCount: null, "No GPU"));
        SetPrivateField(viewModel, "_setupPresetRecommendation", noGpuRecommendation);

        Assert.False(viewModel.SetupAsrCudaRuntimeRecommended);
        Assert.False(viewModel.SetupInstallAsrCudaRuntimeCommand.CanExecute(null));

        var gpuRecommendation = noGpuRecommendation with
        {
            Resources = new SetupHostResources(null, 12, NvidiaGpuDetected: true, LogicalProcessorCount: null, "GPU")
        };
        SetPrivateField(viewModel, "_setupPresetRecommendation", gpuRecommendation);

        Assert.True(viewModel.SetupAsrCudaRuntimeRecommended);
        Assert.True(viewModel.SetupInstallAsrCudaRuntimeCommand.CanExecute(null));
        Assert.Contains("ASR GPU runtime", viewModel.SetupPrimaryInstallSummary, StringComparison.Ordinal);

        Touch(Path.Combine(viewModel.Paths.AsrCTranslate2RuntimeDirectory, "cublas64_12.dll"));
        Touch(Path.Combine(viewModel.Paths.AsrCTranslate2RuntimeDirectory, "cublasLt64_12.dll"));
        Touch(Path.Combine(viewModel.Paths.AsrCTranslate2RuntimeDirectory, "cudart64_12.dll"));
        Touch(Path.Combine(viewModel.Paths.AsrCTranslate2RuntimeDirectory, "cudnn64_9.dll"));
        Touch(Path.Combine(viewModel.Paths.AsrRuntimeDirectory, "crispasr.exe"));
        Touch(Path.Combine(viewModel.Paths.AsrRuntimeDirectory, "crispasr.dll"));
        Touch(Path.Combine(viewModel.Paths.AsrRuntimeDirectory, "whisper.dll"));
        Touch(Path.Combine(viewModel.Paths.AsrRuntimeDirectory, "ggml-cuda.dll"));
        Touch(viewModel.Paths.AsrCudaRuntimeMarkerPath);

        Assert.False(viewModel.SetupInstallAsrCudaRuntimeCommand.CanExecute(null));
        Assert.Equal("導入済み", viewModel.SetupAsrCudaRuntimeActionText);
    }

    [Fact]
    public void SettingsReviewModelSelection_DraftsSetupReviewModelUntilNext()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            CurrentStep = SetupStep.ReviewModel,
            SelectedReviewModelId = "llm-jp-4-8b-thinking-q4-k-m"
        });
        var viewModel = new MainWindowViewModel(paths);
        var bonsai = viewModel.SetupReviewModelChoices.Single(entry => entry.ModelId == "bonsai-8b-q1-0");

        viewModel.SelectedSetupReviewModel = bonsai;

        var state = new SetupStateService(paths).Load();
        Assert.Equal("llm-jp-4-8b-thinking-q4-k-m", state.SelectedReviewModelId);
        Assert.NotEqual("bonsai-8b-q1-0", state.SelectedReviewModelId);
        Assert.Equal("bonsai-8b-q1-0", viewModel.SelectedSetupReviewModel?.ModelId);
        Assert.Equal("llm-jp-4-8b-thinking-q4-k-m", viewModel.SelectedSettingsReviewModel?.ModelId);
        Assert.Equal("未設定", viewModel.ReviewModel);

        viewModel.SetupNextCommand.Execute(null);

        state = new SetupStateService(paths).Load();
        Assert.Equal("bonsai-8b-q1-0", state.SelectedReviewModelId);
        Assert.Equal("custom", state.SetupMode);
    }

    [Fact]
    public void SettingsAsrSelection_UsesCommittedEngineWhileSetupPresetIsDrafted()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "whisper-base", true));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            CurrentStep = SetupStep.SetupMode,
            SetupMode = "recommended",
            SelectedModelPresetId = "recommended",
            SelectedAsrModelId = "faster-whisper-large-v3-turbo",
            SelectedReviewModelId = "gemma-4-e4b-it-q4-k-m"
        });
        var viewModel = new MainWindowViewModel(paths);
        var lightweight = viewModel.SetupModelPresetChoices.Single(preset => preset.PresetId == "lightweight");

        viewModel.SelectedSetupModelPreset = lightweight;

        Assert.Equal("whisper-base", viewModel.SelectedAsrEngineId);
        Assert.Equal("whisper-base", viewModel.SelectedSettingsAsrEngine?.EngineId);
        Assert.Equal("whisper-small", viewModel.SelectedSetupAsrModel?.ModelId);
    }

    [Fact]
    public void SettingsPresetSelection_DraftsSetupModelsUntilNext()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            CurrentStep = SetupStep.SetupMode,
            SetupMode = "recommended",
            SelectedModelPresetId = "recommended",
            SelectedAsrModelId = "faster-whisper-large-v3-turbo",
            SelectedReviewModelId = "gemma-4-e4b-it-q4-k-m"
        });
        var viewModel = new MainWindowViewModel(paths);
        var lightweight = viewModel.SetupModelPresetChoices.Single(preset => preset.PresetId == "lightweight");

        viewModel.SelectedSetupModelPreset = lightweight;

        var state = new SetupStateService(paths).Load();
        Assert.Equal("recommended", state.SelectedModelPresetId);
        Assert.Equal("faster-whisper-large-v3-turbo", state.SelectedAsrModelId);
        Assert.Equal("gemma-4-e4b-it-q4-k-m", state.SelectedReviewModelId);
        Assert.Equal("lightweight", viewModel.SelectedSetupModelPreset?.PresetId);
        Assert.Equal("whisper-small", viewModel.SelectedSetupAsrModel?.ModelId);
        Assert.Equal("bonsai-8b-q1-0", viewModel.SelectedSetupReviewModel?.ModelId);
        Assert.NotEqual("whisper-small", viewModel.SelectedAsrEngineId);

        viewModel.SetupNextCommand.Execute(null);

        state = new SetupStateService(paths).Load();
        Assert.Equal("lightweight", state.SelectedModelPresetId);
        Assert.Equal("whisper-small", state.SelectedAsrModelId);
        Assert.Equal("bonsai-8b-q1-0", state.SelectedReviewModelId);
        Assert.Equal("whisper-small", viewModel.SelectedAsrEngineId);
    }

    [Fact]
    public void SetupWizardNext_NotifiesSettingsReviewModelAfterPresetCommit()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            CurrentStep = SetupStep.SetupMode,
            SetupMode = "recommended",
            SelectedModelPresetId = "recommended",
            SelectedAsrModelId = "faster-whisper-large-v3-turbo",
            SelectedReviewModelId = "gemma-4-e4b-it-q4-k-m"
        });
        var viewModel = new MainWindowViewModel(paths);
        var lightweight = viewModel.SetupModelPresetChoices.Single(preset => preset.PresetId == "lightweight");
        var changed = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                changed.Add(args.PropertyName);
            }
        };

        viewModel.SelectedSetupModelPreset = lightweight;
        viewModel.SetupNextCommand.Execute(null);
        viewModel.CloseSetupWizardModalCommand.Execute(null);

        Assert.Contains(nameof(MainWindowViewModel.SelectedSettingsReviewModel), changed);
        Assert.Equal("bonsai-8b-q1-0", viewModel.SelectedSettingsReviewModel?.ModelId);
    }

    [Fact]
    public void SetupWizardClose_DiscardsUncommittedModelSelectionDraft()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            CurrentStep = SetupStep.ReviewModel,
            SelectedReviewModelId = "llm-jp-4-8b-thinking-q4-k-m"
        });
        var viewModel = new MainWindowViewModel(paths);
        var bonsai = viewModel.SetupReviewModelChoices.Single(entry => entry.ModelId == "bonsai-8b-q1-0");

        viewModel.SelectedSetupReviewModel = bonsai;
        viewModel.CloseSetupWizardModalCommand.Execute(null);
        viewModel.OpenSetupCommand.Execute(null);

        var state = new SetupStateService(paths).Load();
        Assert.Equal("llm-jp-4-8b-thinking-q4-k-m", state.SelectedReviewModelId);
        Assert.Equal("llm-jp-4-8b-thinking-q4-k-m", viewModel.SelectedSetupReviewModel?.ModelId);
    }

    [Fact]
    public void SetupWizardRefresh_DoesNotPersistAutomaticPresetRecommendation()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            CurrentStep = SetupStep.SetupMode,
            SetupMode = "recommended",
            SelectedModelPresetId = "recommended",
            SelectedAsrModelId = "faster-whisper-large-v3-turbo",
            SelectedReviewModelId = "gemma-4-e4b-it-q4-k-m"
        });

        _ = new MainWindowViewModel(paths);

        var state = new SetupStateService(paths).Load();
        Assert.Equal("recommended", state.SelectedModelPresetId);
        Assert.Equal("faster-whisper-large-v3-turbo", state.SelectedAsrModelId);
        Assert.Equal("gemma-4-e4b-it-q4-k-m", state.SelectedReviewModelId);
    }

    [Fact]
    public void SelectedAsrEngine_FallsBackToInstalledSelectableModelWhenLegacySettingRemains()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var installService = new ModelInstallService(paths, new InstalledModelRepository(paths), new ModelVerificationService());
        var asrItem = catalog.Models.First(model => model.ModelId == "faster-whisper-large-v3");
        var asrPath = Path.Combine(paths.UserModels, "asr", "faster-whisper-large-v3");
        Directory.CreateDirectory(asrPath);
        File.WriteAllText(Path.Combine(asrPath, "model.bin"), "asr");
        installService.RegisterLocalModel(asrItem, asrPath, "download");
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "legacy-asr-engine", true));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            SelectedAsrModelId = "legacy-asr-engine"
        });

        var viewModel = new MainWindowViewModel(paths);

        Assert.Equal("faster-whisper-large-v3", viewModel.SelectedAsrEngineId);
    }

    [Fact]
    public void FirstRunSummary_ReportsMissingRuntimeAssets()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));

        var viewModel = new MainWindowViewModel(paths);

        Assert.Contains("初回チェック:", viewModel.FirstRunSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("llama-completion", viewModel.FirstRunDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("ASR model", viewModel.FirstRunDetail, StringComparison.Ordinal);
        Assert.Contains("セットアップ、またはモデル導入", viewModel.FirstRunDetail, StringComparison.Ordinal);
        Assert.False(viewModel.RequiredRuntimeAssetsReady);
        Assert.False(viewModel.RunSelectedJobCommand.CanExecute(null));
    }

    [Fact]
    public void FirstRunSummary_DoesNotRequireReviewRuntimeWhenReviewStageIsDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        new AsrSettingsRepository(paths).Save(new AsrSettings(
            string.Empty,
            string.Empty,
            "kotoba-whisper-v2.2-faster",
            false));

        var viewModel = new MainWindowViewModel(paths);

        Assert.False(viewModel.EnableReviewStage);
        Assert.Equal("初回チェック OK", viewModel.FirstRunSummary);
        Assert.DoesNotContain("llama-completion", viewModel.FirstRunDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("Phase 11", viewModel.FirstRunDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("Phase 12", viewModel.FirstRunDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenSetupCommand_ReportsPlaceholderGuidance()
    {
        var viewModel = CreateViewModel();
        viewModel.CloseSetupWizardModalCommand.Execute(null);

        viewModel.OpenSetupCommand.Execute(null);

        Assert.Contains("セットアップウィザード", viewModel.LatestLog, StringComparison.Ordinal);
        Assert.DoesNotContain("Phase 11", viewModel.LatestLog, StringComparison.Ordinal);
        Assert.DoesNotContain("Phase 12", viewModel.LatestLog, StringComparison.Ordinal);
        Assert.True(viewModel.IsSetupWizardModalOpen);
        Assert.False(viewModel.IsDetailPanelOpen);
        Assert.Contains("モデル構成", viewModel.SetupWizardModalTitle, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupWizardModal_OpensOnIncompleteSetupAndCanBeDismissed()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.IsSetupComplete);
        Assert.True(viewModel.IsSetupWizardModalOpen);

        viewModel.CloseSetupWizardModalCommand.Execute(null);

        Assert.False(viewModel.IsSetupWizardModalOpen);
        Assert.False(viewModel.RunSelectedJobCommand.CanExecute(null));
    }

    [Fact]
    public void SetupWizardModal_DoesNotAutoOpenAfterSetupIsComplete()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            CurrentStep = SetupStep.Complete,
            LastSmokeSucceeded = true,
            LicenseAccepted = true
        });

        var viewModel = new MainWindowViewModel(paths);

        Assert.True(viewModel.IsSetupComplete);
        Assert.False(viewModel.IsSetupWizardModalOpen);

        viewModel.OpenSetupCommand.Execute(null);

        Assert.True(viewModel.IsSetupWizardModalOpen);
        Assert.False(viewModel.IsDetailPanelOpen);
    }

    [Fact]
    public void SetupSteps_UseCompactStatusMarksInsteadOfStatusWords()
    {
        var viewModel = CreateViewModel();

        Assert.DoesNotContain(viewModel.SetupSteps, step => step.Status == "done");
        Assert.DoesNotContain(viewModel.SetupSteps, step => step.Status == "current");
        Assert.DoesNotContain(viewModel.SetupSteps, step => step.Status == "pending");
        Assert.Contains(viewModel.SetupSteps, step => step.Status == "●");
    }

    [Fact]
    public void SetupSteps_DoNotShowHiddenModelAndLicenseStepsInShortFlow()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var installService = new ModelInstallService(paths, new InstalledModelRepository(paths), new ModelVerificationService());
        var asrItem = catalog.Models.First(model => model.ModelId == "faster-whisper-large-v3");
        var asrPath = Path.Combine(paths.UserModels, "asr", "faster-whisper-large-v3");
        Directory.CreateDirectory(asrPath);
        File.WriteAllText(Path.Combine(asrPath, "model.bin"), "asr");
        installService.RegisterLocalModel(asrItem, asrPath, "download");
        var reviewItem = catalog.Models.First(model => model.ModelId == "llm-jp-4-8b-thinking-q4-k-m");
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            CurrentStep = SetupStep.InstallPlan,
            LicenseAccepted = true,
            SelectedAsrModelId = asrItem.ModelId,
            SelectedReviewModelId = reviewItem.ModelId
        });

        var viewModel = new MainWindowViewModel(paths);

        Assert.DoesNotContain(viewModel.SetupSteps, step => step.Step == SetupStep.AsrModel);
        Assert.DoesNotContain(viewModel.SetupSteps, step => step.Step == SetupStep.ReviewModel);
        Assert.DoesNotContain(viewModel.SetupSteps, step => step.Step == SetupStep.License);
        Assert.Equal("✓", viewModel.SetupSteps.Single(step => step.Step == SetupStep.SetupMode).Status);
        Assert.Equal(string.Empty, viewModel.SetupSteps.Single(step => step.Step == SetupStep.Install).Status);
    }

    [Fact]
    public void SetupCompleteCommand_ReportsMissingItemWhenFinalCheckFails()
    {
        var viewModel = CreateViewModel();

        viewModel.SetupCompleteCommand.Execute(null);

        Assert.False(viewModel.IsSetupComplete);
        Assert.Contains("不足項目", viewModel.LatestLog, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupRunSmokeCommand_CompletesSetupWhenFinalCheckPasses()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        CreateFasterWhisperRuntime(paths);
        CreateDiarizationRuntime(paths);
        Touch(Path.Combine(paths.AsrCTranslate2RuntimeDirectory, "cublas64_12.dll"));
        Touch(Path.Combine(paths.AsrCTranslate2RuntimeDirectory, "cublasLt64_12.dll"));
        Touch(Path.Combine(paths.AsrCTranslate2RuntimeDirectory, "cudart64_12.dll"));
        Touch(Path.Combine(paths.AsrCTranslate2RuntimeDirectory, "cudnn64_9.dll"));
        Touch(Path.Combine(paths.AsrRuntimeDirectory, "crispasr.exe"));
        Touch(Path.Combine(paths.AsrRuntimeDirectory, "crispasr.dll"));
        Touch(Path.Combine(paths.AsrRuntimeDirectory, "whisper.dll"));
        Touch(Path.Combine(paths.AsrRuntimeDirectory, "ggml-cuda.dll"));
        Touch(paths.AsrCudaRuntimeMarkerPath);
        Touch(Path.Combine(paths.CudaReviewRuntimeDirectory, "ggml-cuda.dll"));
        Touch(Path.Combine(paths.CudaReviewRuntimeDirectory, "cublas64_12.dll"));
        Touch(Path.Combine(paths.CudaReviewRuntimeDirectory, "cublasLt64_12.dll"));
        Touch(Path.Combine(paths.CudaReviewRuntimeDirectory, "cudart64_12.dll"));
        Touch(paths.CudaReviewRuntimeMarkerPath);
        Directory.CreateDirectory(paths.UserModels);

        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var asrItem = catalog.Models.First(model => model.ModelId == "kotoba-whisper-v2.2-faster");
        var reviewItem = catalog.Models.First(model => model.ModelId == "llm-jp-4-8b-thinking-q4-k-m");
        Directory.CreateDirectory(paths.KotobaWhisperFasterModelPath);
        Touch(Path.Combine(paths.KotobaWhisperFasterModelPath, "model.bin"));
        Touch(paths.ReviewModelPath);
        var now = DateTimeOffset.Now;
        var installedModels = new InstalledModelRepository(paths);
        installedModels.UpsertInstalledModel(new InstalledModel(
            asrItem.ModelId,
            asrItem.Role,
            asrItem.EngineId,
            asrItem.DisplayName,
            asrItem.Family,
            null,
            paths.KotobaWhisperFasterModelPath,
            null,
            null,
            null,
            true,
            asrItem.License.Name,
            "download",
            now,
            now,
            "installed"));
        installedModels.UpsertInstalledModel(new InstalledModel(
            reviewItem.ModelId,
            reviewItem.Role,
            reviewItem.EngineId,
            reviewItem.DisplayName,
            reviewItem.Family,
            null,
            paths.ReviewModelPath,
            null,
            null,
            null,
            true,
            reviewItem.License.Name,
            "download",
            now,
            now,
            "installed"));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            CurrentStep = SetupStep.SmokeTest,
            LicenseAccepted = true,
            LastSmokeSucceeded = false,
            SelectedAsrModelId = asrItem.ModelId,
            SelectedReviewModelId = reviewItem.ModelId,
            StorageRoot = paths.UserModels
        });
        var viewModel = new MainWindowViewModel(paths);

        viewModel.SetupRunSmokeCommand.Execute(null);

        Assert.True(viewModel.IsSetupComplete);
        Assert.Equal("Complete", viewModel.SetupCurrentStep);
        Assert.True(viewModel.SetupSmokeChecks.All(check => check.IsOk));
    }

    [Fact]
    public void RunSelectedJobCommand_IsEnabledWhenJobAndRuntimeAssetsAreReady()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        Touch(paths.FasterWhisperScriptPath);
        CreateFasterWhisperRuntime(paths);
        CreateMinimalModelDirectory(paths.KotobaWhisperFasterModelPath);
        Touch(paths.ReviewModelPath);
        RegisterVerifiedModel(
            paths,
            "kotoba-whisper-v2.2-faster",
            "asr",
            "kotoba-whisper-v2.2-faster",
            paths.KotobaWhisperFasterModelPath);
        RegisterVerifiedModel(
            paths,
            "llm-jp-4-8b-thinking-q4-k-m",
            "review",
            "llama-cpp",
            paths.ReviewModelPath);
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "kotoba-whisper-v2.2-faster", true));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            LicenseAccepted = true,
            SelectedAsrModelId = "kotoba-whisper-v2.2-faster",
            SelectedReviewModelId = "llm-jp-4-8b-thinking-q4-k-m"
        });

        var viewModel = new MainWindowViewModel(paths);
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", audioPath, "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];

        Assert.True(viewModel.RequiredRuntimeAssetsReady);
        Assert.True(viewModel.RunSelectedJobCommand.CanExecute(null), viewModel.RunPreflightDetail);
    }

    [Fact]
    public void SettingsReviewModelSelection_KeepsCompletedSetupAndRunCommandEnabledForInstalledModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        Touch(paths.FasterWhisperScriptPath);
        CreateFasterWhisperRuntime(paths);
        CreateMinimalModelDirectory(paths.KotobaWhisperFasterModelPath);
        Touch(paths.ReviewModelPath);
        RegisterVerifiedModel(
            paths,
            "kotoba-whisper-v2.2-faster",
            "asr",
            "kotoba-whisper-v2.2-faster",
            paths.KotobaWhisperFasterModelPath);
        RegisterVerifiedModel(
            paths,
            "llm-jp-4-8b-thinking-q4-k-m",
            "review",
            "llama-cpp",
            paths.ReviewModelPath);
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var bonsaiItem = catalog.Models.Single(model => model.ModelId == "bonsai-8b-q1-0");
        var bonsaiPath = new ModelInstallService(paths, new InstalledModelRepository(paths), new ModelVerificationService())
            .GetDefaultInstallPath(bonsaiItem);
        Touch(bonsaiPath);
        RegisterVerifiedModel(
            paths,
            "bonsai-8b-q1-0",
            "review",
            "llama-cpp",
            bonsaiPath);
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "kotoba-whisper-v2.2-faster", true));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            LicenseAccepted = true,
            SelectedAsrModelId = "kotoba-whisper-v2.2-faster",
            SelectedReviewModelId = "llm-jp-4-8b-thinking-q4-k-m"
        });

        var viewModel = new MainWindowViewModel(paths);
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", audioPath, "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];
        var bonsai = viewModel.SetupReviewModelChoices.Single(entry => entry.ModelId == "bonsai-8b-q1-0");

        viewModel.SelectedSettingsReviewModel = bonsai;

        var state = new SetupStateService(paths).Load();
        Assert.True(state.IsCompleted);
        Assert.Equal("bonsai-8b-q1-0", state.SelectedReviewModelId);
        Assert.True(viewModel.IsSetupComplete);
        Assert.True(viewModel.RunSelectedJobCommand.CanExecute(null), viewModel.RunPreflightDetail);
    }

    [Fact]
    public void SettingsReviewModelSelection_InvalidatesCompletedSetupForUninstalledModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        Touch(paths.FasterWhisperScriptPath);
        CreateFasterWhisperRuntime(paths);
        CreateMinimalModelDirectory(paths.KotobaWhisperFasterModelPath);
        Touch(paths.ReviewModelPath);
        RegisterVerifiedModel(
            paths,
            "kotoba-whisper-v2.2-faster",
            "asr",
            "kotoba-whisper-v2.2-faster",
            paths.KotobaWhisperFasterModelPath);
        RegisterVerifiedModel(
            paths,
            "llm-jp-4-8b-thinking-q4-k-m",
            "review",
            "llama-cpp",
            paths.ReviewModelPath);
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "kotoba-whisper-v2.2-faster", true));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            LicenseAccepted = true,
            SelectedAsrModelId = "kotoba-whisper-v2.2-faster",
            SelectedReviewModelId = "llm-jp-4-8b-thinking-q4-k-m"
        });

        var viewModel = new MainWindowViewModel(paths);
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", audioPath, "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];
        var uninstalled = viewModel.SetupReviewModelChoices.Single(entry => entry.ModelId == "bonsai-8b-q1-0");

        viewModel.SelectedSettingsReviewModel = uninstalled;

        var state = new SetupStateService(paths).Load();
        Assert.False(state.IsCompleted);
        Assert.False(state.LastSmokeSucceeded);
        Assert.Equal(SetupStep.ReviewModel, state.CurrentStep);
        Assert.Equal("bonsai-8b-q1-0", state.SelectedReviewModelId);
        Assert.False(viewModel.RunSelectedJobCommand.CanExecute(null));
    }

    [Fact]
    public void SettingsReviewModelSelection_ResetsSmokeForIncompleteSetupEvenWhenInstalledModelIsReady()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        Touch(paths.FasterWhisperScriptPath);
        CreateFasterWhisperRuntime(paths);
        CreateMinimalModelDirectory(paths.KotobaWhisperFasterModelPath);
        Touch(paths.ReviewModelPath);
        RegisterVerifiedModel(
            paths,
            "kotoba-whisper-v2.2-faster",
            "asr",
            "kotoba-whisper-v2.2-faster",
            paths.KotobaWhisperFasterModelPath);
        RegisterVerifiedModel(
            paths,
            "llm-jp-4-8b-thinking-q4-k-m",
            "review",
            "llama-cpp",
            paths.ReviewModelPath);
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var bonsaiItem = catalog.Models.Single(model => model.ModelId == "bonsai-8b-q1-0");
        var bonsaiPath = new ModelInstallService(paths, new InstalledModelRepository(paths), new ModelVerificationService())
            .GetDefaultInstallPath(bonsaiItem);
        Touch(bonsaiPath);
        RegisterVerifiedModel(
            paths,
            "bonsai-8b-q1-0",
            "review",
            "llama-cpp",
            bonsaiPath);
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = false,
            LastSmokeSucceeded = true,
            LicenseAccepted = true,
            SelectedAsrModelId = "kotoba-whisper-v2.2-faster",
            SelectedReviewModelId = "llm-jp-4-8b-thinking-q4-k-m"
        });

        var viewModel = new MainWindowViewModel(paths);
        var bonsai = viewModel.SetupReviewModelChoices.Single(entry => entry.ModelId == "bonsai-8b-q1-0");

        viewModel.SelectedSettingsReviewModel = bonsai;

        var state = new SetupStateService(paths).Load();
        Assert.False(state.IsCompleted);
        Assert.False(state.LastSmokeSucceeded);
        Assert.Equal(SetupStep.ReviewModel, state.CurrentStep);
        Assert.Equal("bonsai-8b-q1-0", state.SelectedReviewModelId);
    }

    [Fact]
    public void SettingsReviewModelSelection_InvalidatesCompletedSetupWhenNewReviewRuntimeIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.TernaryLlamaCompletionPath);
        Touch(paths.FasterWhisperScriptPath);
        CreateFasterWhisperRuntime(paths);
        CreateMinimalModelDirectory(paths.KotobaWhisperFasterModelPath);
        var ternaryModelPath = Path.Combine(paths.UserModels, "review", "ternary-bonsai-8b-q2-0", "Ternary-Bonsai-8B-Q2_0.gguf");
        var bonsaiModelPath = Path.Combine(paths.UserModels, "review", "bonsai-8b-q1-0", "Bonsai-8B-Q1_0.gguf");
        Touch(ternaryModelPath);
        Touch(bonsaiModelPath);
        RegisterVerifiedModel(
            paths,
            "kotoba-whisper-v2.2-faster",
            "asr",
            "kotoba-whisper-v2.2-faster",
            paths.KotobaWhisperFasterModelPath);
        RegisterVerifiedModel(
            paths,
            "ternary-bonsai-8b-q2-0",
            "review",
            "ternary-bonsai",
            ternaryModelPath);
        RegisterVerifiedModel(
            paths,
            "bonsai-8b-q1-0",
            "review",
            "llama-cpp",
            bonsaiModelPath);
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            LicenseAccepted = true,
            SelectedAsrModelId = "kotoba-whisper-v2.2-faster",
            SelectedReviewModelId = "ternary-bonsai-8b-q2-0"
        });

        var viewModel = new MainWindowViewModel(paths);
        var bonsai = viewModel.SetupReviewModelChoices.Single(entry => entry.ModelId == "bonsai-8b-q1-0");

        viewModel.SelectedSettingsReviewModel = bonsai;

        var state = new SetupStateService(paths).Load();
        Assert.False(state.IsCompleted);
        Assert.False(state.LastSmokeSucceeded);
        Assert.Equal(SetupStep.ReviewModel, state.CurrentStep);
        Assert.Equal("bonsai-8b-q1-0", state.SelectedReviewModelId);
    }

    [Fact]
    public void ReadablePolishingPromptSettings_LoadsSettingsForRuntimeModelFamily()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var repository = new ReadablePolishingPromptSettingsRepository(paths);
        repository.Save(ReadablePolishingPromptSettings.CreateDefault(ReadablePolishingPromptModelFamilies.Bonsai) with
        {
            PresetId = ReadablePolishingPromptPresets.StrongPunctuation,
            AdditionalInstruction = "短い行を自然につないでください。"
        });
        var viewModel = new MainWindowViewModel(paths);

        var settings = InvokePrivate<ReadablePolishingPromptSettings>(
            viewModel,
            "LoadReadablePolishingPromptSettings",
            new LlmRuntimeProfile(
                "test",
                "bonsai-8b-q4-k-m",
                "bonsai",
                "Bonsai 8B",
                "llama-cpp",
                "runtime-llama-cpp",
                "model.gguf",
                "llama-cli.exe",
                8192,
                0,
                null,
                null,
                true,
                "strict",
                TimeSpan.FromMinutes(10)));

        Assert.Equal(ReadablePolishingPromptModelFamilies.Bonsai, settings.ModelFamily);
        Assert.Equal(ReadablePolishingPromptPresets.StrongPunctuation, settings.PresetId);
        Assert.Equal("短い行を自然につないでください。", settings.AdditionalInstruction);
        Assert.Equal(TranscriptPolishingPromptBuilder.BonsaiCompactPromptTemplateId, settings.PromptTemplateId);
    }
}
