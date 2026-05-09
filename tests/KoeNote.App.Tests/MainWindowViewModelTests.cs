using System.Collections;
using System.Windows.Input;
using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Setup;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Transcript;
using KoeNote.App.Services.Updates;
using KoeNote.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void AppVersionDisplay_UsesAssemblyVersionForHeader()
    {
        var viewModel = CreateViewModel();

        Assert.Matches(@"^v\d+\.\d+\.\d+", viewModel.AppVersionDisplay);
        Assert.StartsWith("KoeNote ", viewModel.AppVersionToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void ZoomCommands_AdjustAndPersistMainContentZoom()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        var first = new MainWindowViewModel(paths);

        Assert.Equal(1.0, first.MainContentZoomScale);
        Assert.Equal("100%", first.MainContentZoomPercentText);

        first.ZoomInCommand.Execute(null);
        Assert.Equal(1.1, first.MainContentZoomScale, 3);
        Assert.Equal("110%", first.MainContentZoomPercentText);
        Assert.True(first.ZoomOutCommand.CanExecute(null));

        var second = new MainWindowViewModel(paths);
        Assert.Equal(1.1, second.MainContentZoomScale, 3);

        second.ResetZoomCommand.Execute(null);
        Assert.Equal(1.0, second.MainContentZoomScale);
        Assert.False(second.ResetZoomCommand.CanExecute(null));
    }

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
    public void HeaderModels_ShowUnsetWhenSelectedModelsAreNotInstalled()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedAsrEngineId = "faster-whisper-large-v3-turbo";

        Assert.Equal("未設定", viewModel.AsrModel);
        Assert.Equal("未設定", viewModel.ReviewModel);
    }

    [Fact]
    public void HeaderModels_ShowInstalledSelectedModels()
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
        var reviewPath = installService.GetDefaultInstallPath(reviewItem);
        Touch(reviewPath);
        installService.RegisterLocalModel(reviewItem, reviewPath, "download");
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, asrItem.EngineId));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            SelectedAsrModelId = asrItem.ModelId,
            SelectedReviewModelId = reviewItem.ModelId
        });

        var viewModel = new MainWindowViewModel(paths);

        Assert.Equal("faster-whisper large-v3", viewModel.AsrModel);
        Assert.Equal("llm-jp 4 8B thinking Q4_K_M", viewModel.ReviewModel);
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
            SelectedReviewModelId = reviewItem.ModelId
        });

        var viewModel = new MainWindowViewModel(paths);

        Assert.True(viewModel.SelectedSetupModelsReady);
        Assert.False(viewModel.SetupDiarizationRuntimeReady);
        Assert.False(viewModel.SelectedSetupConfigurationReady);
        Assert.True(viewModel.SetupInstallSelectedPresetCommand.CanExecute(null));
        Assert.Contains("話者識別ランタイム", viewModel.SetupPrimaryInstallSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupInstallCudaReviewRuntimeCommand_RequiresDetectedGpuAndMissingCudaRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var viewModel = new MainWindowViewModel(CreatePathsWithoutTernaryRuntime(root));
        var noGpuRecommendation = new SetupPresetRecommendation(
            "recommended",
            "Recommended",
            "No GPU",
            new SetupHostResources(null, null, NvidiaGpuDetected: false, "No GPU"));
        SetPrivateField(viewModel, "_setupPresetRecommendation", noGpuRecommendation);

        Assert.False(viewModel.SetupInstallCudaReviewRuntimeCommand.CanExecute(null));

        var gpuRecommendation = noGpuRecommendation with
        {
            Resources = new SetupHostResources(null, 12, NvidiaGpuDetected: true, "GPU")
        };
        SetPrivateField(viewModel, "_setupPresetRecommendation", gpuRecommendation);

        Assert.True(viewModel.SetupInstallCudaReviewRuntimeCommand.CanExecute(null));

        Touch(viewModel.Paths.LlamaCompletionPath);
        Touch(Path.Combine(viewModel.Paths.ReviewRuntimeDirectory, "ggml-cuda.dll"));
        Touch(viewModel.Paths.CudaReviewRuntimeMarkerPath);

        Assert.False(viewModel.SetupInstallCudaReviewRuntimeCommand.CanExecute(null));
        Assert.Equal("導入済み", viewModel.SetupCudaReviewRuntimeActionText);
    }

    [Fact]
    public void SettingsReviewModelSelection_UpdatesSetupReviewModel()
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
        Assert.Equal("bonsai-8b-q1-0", state.SelectedReviewModelId);
        Assert.Equal("custom", state.SetupMode);
    }

    [Fact]
    public void HeaderModels_ShowUnsetWhenInstalledModelFileIsMissing()
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
        Directory.Delete(asrPath, recursive: true);
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, asrItem.EngineId));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            SelectedAsrModelId = asrItem.ModelId
        });

        var viewModel = new MainWindowViewModel(paths);

        Assert.Equal("未設定", viewModel.AsrModel);
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
    public void DeleteSelectedModelFilesCommand_IsDisabledWhileJobOrModelDownloadIsRunning()
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
        var viewModel = new MainWindowViewModel(paths);
        viewModel.SelectedModelCatalogEntry = viewModel.ModelCatalogEntries.Single(entry => entry.ModelId == asrItem.ModelId);

        Assert.True(viewModel.DeleteSelectedModelFilesCommand.CanExecute(null));

        SetPrivateProperty(viewModel, nameof(MainWindowViewModel.IsRunInProgress), true);
        Assert.False(viewModel.DeleteSelectedModelFilesCommand.CanExecute(null));

        SetPrivateProperty(viewModel, nameof(MainWindowViewModel.IsRunInProgress), false);
        SetPrivateProperty(viewModel, nameof(MainWindowViewModel.IsModelDownloadInProgress), true);
        Assert.False(viewModel.DeleteSelectedModelFilesCommand.CanExecute(null));
    }

    [Fact]
    public void JobSearchText_FiltersJobsByTitleFileNameAndStatus()
    {
        var viewModel = CreateViewModel();
        viewModel.Jobs.Clear();
        viewModel.Jobs.Add(new JobSummary("job-001", "企画会議", "planning.wav", @"C:\audio\planning.wav", "登録済み", 0, 0, DateTimeOffset.Now));
        viewModel.Jobs.Add(new JobSummary("job-002", "レビュー会議", "review.wav", @"C:\audio\review.wav", "キャンセル済み", 10, 0, DateTimeOffset.Now));

        viewModel.JobSearchText = "キャンセル";

        var jobs = ViewItems<JobSummary>(viewModel.FilteredJobs);
        var job = Assert.Single(jobs);
        Assert.Equal("job-002", job.JobId);

        viewModel.JobSearchText = "planning";

        jobs = ViewItems<JobSummary>(viewModel.FilteredJobs);
        job = Assert.Single(jobs);
        Assert.Equal("job-001", job.JobId);
    }

    [Fact]
    public void Constructor_DoesNotInjectSampleSegments()
    {
        var viewModel = CreateViewModel();

        Assert.Empty(viewModel.Segments);
        Assert.Empty(ViewItems<TranscriptSegmentPreview>(viewModel.FilteredSegments));
    }

    [Fact]
    public async Task DeleteAndRestoreJob_UpdatesActiveAndHistoryLists()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        var viewModel = new MainWindowViewModel(new AppPaths(root, root, AppContext.BaseDirectory));
        var job = viewModel.RegisterAudioFile(audioPath);
        new JobRepository(viewModel.Paths).MarkPreprocessSucceeded(job, Path.Combine(root, "normalized.wav"));
        var confirmations = 0;
        viewModel.ConfirmAction = (_, _) =>
        {
            confirmations++;
            return true;
        };

        viewModel.DeleteJobCommand.Execute(job);
        for (var i = 0; i < 20 && viewModel.DeletedJobs.Count == 0; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.Empty(viewModel.Jobs);
        var deleted = Assert.Single(viewModel.DeletedJobs);
        Assert.True(deleted.IsDeleted);
        Assert.StartsWith("履歴 1 件 / ", viewModel.DeletedJobCountSummary, StringComparison.Ordinal);

        viewModel.RestoreJobCommand.Execute(deleted);
        for (var i = 0; i < 20 && viewModel.Jobs.Count == 0; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.Empty(viewModel.DeletedJobs);
        Assert.Single(viewModel.Jobs);
        Assert.Equal(job.JobId, viewModel.SelectedJob?.JobId);
        Assert.Equal(1, confirmations);
    }

    [Fact]
    public async Task DeleteJobCommand_CancelledConfirmationDoesNotMoveJob()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        var viewModel = new MainWindowViewModel(new AppPaths(root, root, AppContext.BaseDirectory));
        var job = viewModel.RegisterAudioFile(audioPath);
        viewModel.ConfirmAction = (_, _) => false;

        viewModel.DeleteJobCommand.Execute(job);
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        Assert.Single(viewModel.Jobs);
        Assert.Empty(viewModel.DeletedJobs);
    }

    [Fact]
    public async Task DeleteJobCommand_UpdatesDeletedHistoryStorageSummary()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        var viewModel = new MainWindowViewModel(new AppPaths(root, root, AppContext.BaseDirectory));
        var job = viewModel.RegisterAudioFile(audioPath);
        new JobRepository(viewModel.Paths).MarkPreprocessSucceeded(job, Path.Combine(root, "normalized.wav"));
        var jobDirectory = Path.Combine(viewModel.Paths.Jobs, job.JobId);
        Directory.CreateDirectory(jobDirectory);
        File.WriteAllText(Path.Combine(jobDirectory, "artifact.txt"), "artifact");
        viewModel.ConfirmAction = (_, _) => true;

        viewModel.DeleteJobCommand.Execute(job);
        for (var i = 0; i < 20 && viewModel.DeletedJobs.Count == 0; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        var deleted = Assert.Single(viewModel.DeletedJobs);
        Assert.True(deleted.StorageBytes >= "artifact".Length);
        Assert.False(viewModel.DeletedJobCountSummary.EndsWith("/ 0 B", StringComparison.Ordinal), viewModel.DeletedJobCountSummary);
    }

    [Fact]
    public async Task DeleteJobCommand_UnstartedRegisteredJobDoesNotMoveToHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        var viewModel = new MainWindowViewModel(new AppPaths(root, root, AppContext.BaseDirectory));
        var job = viewModel.RegisterAudioFile(audioPath);
        viewModel.ConfirmAction = (_, message) =>
        {
            Assert.Contains("履歴に残さず", message, StringComparison.Ordinal);
            return true;
        };

        viewModel.DeleteJobCommand.Execute(job);
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        Assert.Empty(viewModel.Jobs);
        Assert.Empty(viewModel.DeletedJobs);
        Assert.Empty(new JobRepository(viewModel.Paths).LoadRecent());
        Assert.Empty(new JobRepository(viewModel.Paths).LoadDeleted());
    }

    [Fact]
    public void StageStatuses_StartWithReadablePendingStatus()
    {
        var viewModel = CreateViewModel();

        Assert.All(viewModel.StageStatuses.Take(3), stage => Assert.Equal("未開始", stage.Status));
        Assert.Equal("スキップ", viewModel.StageStatuses.Last().Status);
    }

    [Fact]
    public void SegmentSearchAndSpeakerFilter_CombinePredicates()
    {
        var viewModel = CreateViewModel();
        viewModel.Segments.Add(new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.810",
            "Speaker_0",
            "今日は会議の議事録を作成するために音声認識をテストしています。",
            "候補なし"));
        viewModel.Segments.Add(new TranscriptSegmentPreview(
            "00:03:21.400",
            "00:03:27.800",
            "Speaker_1",
            "この仕様はサーバーの右側で処理します。",
            "候補なし"));

        viewModel.SegmentSearchText = "サーバー";
        viewModel.SelectedSpeakerFilter = "Speaker_1";

        var segments = ViewItems<TranscriptSegmentPreview>(viewModel.FilteredSegments);
        var segment = Assert.Single(segments);
        Assert.Equal("Speaker_1", segment.Speaker);
        Assert.Contains("サーバー", segment.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AsrSettings_RestoreAfterRecreatingViewModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        var first = new MainWindowViewModel(paths)
        {
            AsrContextText = "製品開発会議",
            AsrHotwordsText = "KoeNote\r\nRTX 3060",
            EnableReviewStage = false
        };
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        var second = new MainWindowViewModel(paths);

        Assert.Equal(first.AsrContextText, second.AsrContextText);
        Assert.Equal(first.AsrHotwordsText, second.AsrHotwordsText);
        Assert.False(second.EnableReviewStage);
    }

    [Fact]
    public void ImportDomainPresetFromFile_UpdatesAsrSettingsInViewModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        var presetPath = Path.Combine(root, "preset.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(presetPath, """
            {
              "schema_version": 1,
              "display_name": "産科・産後ケア研究プリセット",
              "asr_context": "産後ケア研究のインタビューです。",
              "hotwords": ["産後ケア", "助産師"]
            }
            """);
        var viewModel = new MainWindowViewModel(paths)
        {
            AsrContextText = "既存設定",
            AsrHotwordsText = "産後ケア"
        };

        viewModel.ImportDomainPresetFromFile(presetPath);

        Assert.True(viewModel.HasLoadedDomainPreset);
        Assert.Contains("産科・産後ケア研究プリセット", viewModel.LoadedDomainPresetSummary, StringComparison.Ordinal);
        Assert.Contains("産後ケア研究のインタビューです。", viewModel.LoadedDomainPresetDetails, StringComparison.Ordinal);
        Assert.Contains("助産師", viewModel.LoadedDomainPresetDetails, StringComparison.Ordinal);
        Assert.Equal("既存設定", viewModel.AsrContextText);
        Assert.Equal("産後ケア", viewModel.AsrHotwordsText);

        viewModel.ApplyLoadedDomainPresetCommand.Execute(null);

        Assert.Contains("既存設定", viewModel.AsrContextText, StringComparison.Ordinal);
        Assert.Contains("産後ケア研究のインタビューです。", viewModel.AsrContextText, StringComparison.Ordinal);
        Assert.Equal(
            ["産後ケア", "助産師"],
            viewModel.AsrHotwordsText.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("プリセットをインポートしました", viewModel.LatestLog, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportDomainPresetFromFile_AppliesSpeakerAliasesToSelectedJob()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "こんにちは")
        ]);
        var presetPath = Path.Combine(root, "preset.json");
        File.WriteAllText(presetPath, """
            {
              "schema_version": 1,
              "speaker_aliases": [
                {
                  "speaker_id": "Speaker_0",
                  "display_name": "聞き手"
                }
              ]
            }
            """);
        var viewModel = new MainWindowViewModel(paths);

        viewModel.ImportDomainPresetFromFile(presetPath);

        Assert.True(viewModel.HasLoadedDomainPreset);
        Assert.Equal("Speaker_0", Assert.Single(viewModel.Segments).Speaker);

        viewModel.ApplyLoadedDomainPresetCommand.Execute(null);

        Assert.Equal("聞き手", Assert.Single(viewModel.Segments).Speaker);
        Assert.Contains("話者別名: 1件追加", viewModel.LatestLog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewQueue_LoadsSelectedDraftAndAcceptCommandAdvances()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "ミギワ"),
            new TranscriptSegment("segment-002", job.JobId, 2, 3, "Speaker_1", "公正")
        ]);
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", job.JobId, "segment-001", "意味不明語", "ミギワ", "右側", "候補", 0.8),
            new CorrectionDraft("draft-002", job.JobId, "segment-002", "同音異義語", "公正", "構成", "候補", 0.7)
        ]);

        var viewModel = new MainWindowViewModel(paths);

        Assert.Equal("draft-001", viewModel.SelectedCorrectionDraftId);
        Assert.Equal("1 / 2", viewModel.DraftPositionText);
        Assert.Equal("segment-001", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal(viewModel.SelectedSegment?.Text, viewModel.SelectedSegmentEditText);
        Assert.True(viewModel.AcceptDraftCommand.CanExecute(null));
        var initialFocusRequest = viewModel.ReviewSegmentFocusRequestId;
        viewModel.SegmentSearchText = "hidden-segment";
        Assert.Empty(ViewItems<TranscriptSegmentPreview>(viewModel.FilteredSegments));

        viewModel.FocusSelectedDraftSegmentCommand.Execute(null);

        Assert.Empty(viewModel.SegmentSearchText);
        Assert.Contains(viewModel.SelectedSegment, ViewItems<TranscriptSegmentPreview>(viewModel.FilteredSegments));

        viewModel.AcceptDraftCommand.Execute(null);
        Assert.False(viewModel.SelectNextDraftCommand.CanExecute(null));
        for (var i = 0; i < 20 && viewModel.SelectedCorrectionDraftId == "draft-001"; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.True(
            viewModel.SelectedCorrectionDraftId == "draft-002",
            $"Expected draft-002 but was {viewModel.SelectedCorrectionDraftId}. InProgress: {viewModel.IsReviewOperationInProgress}. LatestLog: {viewModel.LatestLog}");
        Assert.Equal("1 / 1", viewModel.DraftPositionText);
        Assert.Equal("segment-002", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal(viewModel.SelectedSegment?.Text, viewModel.SelectedSegmentEditText);
        Assert.True(viewModel.ReviewSegmentFocusRequestId > initialFocusRequest);
        Assert.True(
            viewModel.SelectedJobUnreviewedDrafts == 1,
            $"Expected one pending draft but saw {viewModel.SelectedJobUnreviewedDrafts}. LatestLog: {viewModel.LatestLog}");

        viewModel.AcceptDraftCommand.Execute(null);
        for (var i = 0; i < 20 && viewModel.SelectedCorrectionDraftId == "draft-002"; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.Empty(viewModel.SelectedCorrectionDraftId);
        Assert.Equal(0, viewModel.SelectedJobUnreviewedDrafts);
        Assert.Equal("整文完了", viewModel.SelectedJob?.Status);
        Assert.Equal(100, viewModel.SelectedJob?.ProgressPercent);
        Assert.Equal(4, viewModel.StageStatuses.Count);
        Assert.Equal("要約", viewModel.StageStatuses.Last().Name);
    }

    [Fact]
    public void ReviewPane_StartsEmptyWhenThereAreNoDrafts()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.HasReviewDraft);
        Assert.Equal("候補なし", viewModel.ReviewIssueType);
        Assert.Empty(viewModel.OriginalText);
        Assert.Empty(viewModel.SuggestedText);
        Assert.Equal("整文候補はありません。", viewModel.ReviewReason);
        Assert.Equal(0, viewModel.Confidence);
        Assert.Equal("0 / 0", viewModel.DraftPositionText);
    }

    [Fact]
    public async Task AcceptDraft_RemembersSuggestedFragmentInsteadOfWholeSegment()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "今日は旧サービス名を確認します")
        ]);
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", job.JobId, "segment-001", "wording", "旧サービス名", "KoeNote", "suggestion", 0.8)
        ]);

        var viewModel = new MainWindowViewModel(paths);
        viewModel.AcceptDraftCommand.Execute(null);
        for (var i = 0; i < 20 && viewModel.SelectedCorrectionDraftId == "draft-001"; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT wrong_text, correct_text
            FROM correction_memory;
            """;

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("旧サービス名", reader.GetString(0));
        Assert.Equal("KoeNote", reader.GetString(1));
    }

    [Fact]
    public void ExportSelectedJobToFolder_RecordsErrorWithoutThrowing()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "raw")
        ]);

        var viewModel = new MainWindowViewModel(paths);

        viewModel.ExportSelectedJobToFolder(string.Empty);

        Assert.StartsWith("整文の出力に失敗しました:", viewModel.ExportWarning, StringComparison.Ordinal);
        Assert.StartsWith("整文の出力に失敗しました:", viewModel.LatestLog, StringComparison.Ordinal);
        Assert.Contains(viewModel.Logs, entry => entry.Stage == "export" && entry.Level == "error");
    }

    [Fact]
    public void FormatExportCommands_EnableWhenSelectedJobHasSegments()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "raw")
        ]);

        var viewModel = new MainWindowViewModel(paths);

        Assert.True(viewModel.ExportTxtCommand.CanExecute(null));
        Assert.True(viewModel.ExportJsonCommand.CanExecute(null));
        Assert.True(viewModel.ExportSrtCommand.CanExecute(null));
        Assert.True(viewModel.ExportDocxCommand.CanExecute(null));
    }

    [Fact]
    public void PostProcessMenuItems_DisableWhenSelectedJobHasNoTranscriptSegments()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));

        var viewModel = new MainWindowViewModel(paths);

        Assert.False(viewModel.CanRunPostReview);
        Assert.False(viewModel.CanRunPostSummary);
        Assert.False(viewModel.CanRunPostReviewAndSummary);
    }

    [Fact]
    public void PostProcessMenuItems_EnableWhenSelectedJobHasTranscriptSegments()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "raw")
        ]);

        var viewModel = new MainWindowViewModel(paths);

        Assert.True(viewModel.CanRunPostReview);
        Assert.True(viewModel.CanRunPostSummary);
        Assert.True(viewModel.CanRunPostReviewAndSummary);
    }

    [Theory]
    [InlineData(nameof(MainWindowViewModel.RunPostReviewCommand), PostProcessMode.ReviewOnly)]
    [InlineData(nameof(MainWindowViewModel.RunPostSummaryCommand), PostProcessMode.SummaryOnly)]
    [InlineData(nameof(MainWindowViewModel.RunPostReviewAndSummaryCommand), PostProcessMode.ReviewAndSummary)]
    public void PostProcessCommands_RecordRequestedMode(string commandName, PostProcessMode expectedMode)
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "raw")
        ]);
        var viewModel = new MainWindowViewModel(paths);
        var command = (ICommand)typeof(MainWindowViewModel).GetProperty(commandName)!.GetValue(viewModel)!;

        Assert.True(command.CanExecute(null));

        command.Execute(null);

        Assert.Equal(expectedMode, viewModel.LastRequestedPostProcessMode);
    }

    [Fact]
    public void PostProcessOverwriteConfirmation_CancelsWhenReviewOutputExistsAndUserDeclines()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "raw")
        ]);
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", job.JobId, "segment-001", "wording", "raw", "fixed", "reason", 0.8)
        ]);
        var viewModel = new MainWindowViewModel(paths);
        var promptSeen = false;
        viewModel.ConfirmAction = (_, message) =>
        {
            promptSeen = message.Contains("整文", StringComparison.Ordinal);
            return false;
        };

        var result = InvokePrivate<bool>(
            viewModel,
            "ConfirmOverwriteExistingPostProcessOutputs",
            PostProcessMode.ReviewOnly,
            job.JobId);

        Assert.False(result);
        Assert.True(promptSeen);
    }

    [Fact]
    public void PostProcessOverwriteConfirmation_CoversSummaryOutputForCombinedRun()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "raw")
        ]);
        new TranscriptDerivativeRepository(paths).Save(new TranscriptDerivativeSaveRequest(
            job.JobId,
            TranscriptDerivativeKinds.Summary,
            TranscriptDerivativeFormats.Markdown,
            "# summary",
            TranscriptDerivativeSourceKinds.Raw,
            "hash",
            "segment-001..segment-001",
            null,
            "model",
            "prompt",
            "profile"));
        var viewModel = new MainWindowViewModel(paths);
        var promptSeen = false;
        viewModel.ConfirmAction = (_, message) =>
        {
            promptSeen = message.Contains("要約", StringComparison.Ordinal);
            return true;
        };

        var result = InvokePrivate<bool>(
            viewModel,
            "ConfirmOverwriteExistingPostProcessOutputs",
            PostProcessMode.ReviewAndSummary,
            job.JobId);

        Assert.True(result);
        Assert.True(promptSeen);
    }

    [Fact]
    public void IncludeExportTimestamps_CanBeToggled()
    {
        var viewModel = CreateViewModel();

        viewModel.IncludeExportTimestamps = false;

        Assert.False(viewModel.IncludeExportTimestamps);
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
        Assert.Contains("KoeNote", viewModel.SetupWizardModalTitle, StringComparison.Ordinal);
    }

    [Fact]
    public void HeaderNavigationCommands_SelectExpectedLogPanelTabs()
    {
        var viewModel = CreateViewModel();

        viewModel.OpenSettingsCommand.Execute(null);
        Assert.Equal(0, viewModel.SelectedLogPanelTabIndex);
        Assert.Equal(0, viewModel.SelectedDetailPanelTabIndex);
        Assert.Equal("設定", viewModel.DetailPanelTitle);
        Assert.True(viewModel.IsDetailPanelOpen);

        viewModel.ShowModelCatalogCommand.Execute(null);
        Assert.Equal(2, viewModel.SelectedLogPanelTabIndex);
        Assert.Equal(2, viewModel.SelectedDetailPanelTabIndex);
        Assert.Equal("モデル", viewModel.DetailPanelTitle);

        viewModel.OpenLogsCommand.Execute(null);
        Assert.Equal(3, viewModel.SelectedLogPanelTabIndex);
        Assert.Equal(3, viewModel.SelectedDetailPanelTabIndex);
        Assert.Equal("ログ", viewModel.DetailPanelTitle);

        viewModel.OpenSetupCommand.Execute(null);
        Assert.True(viewModel.IsSetupWizardModalOpen);
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
    public void SetupSteps_DoNotMarkReviewModelReadyWhenOnlyLicenseIsAccepted()
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
            CurrentStep = SetupStep.License,
            LicenseAccepted = true,
            SelectedAsrModelId = asrItem.ModelId,
            SelectedReviewModelId = reviewItem.ModelId
        });

        var viewModel = new MainWindowViewModel(paths);

        Assert.Equal("✓", viewModel.SetupSteps.Single(step => step.Step == SetupStep.AsrModel).Status);
        Assert.Equal(string.Empty, viewModel.SetupSteps.Single(step => step.Step == SetupStep.ReviewModel).Status);
        Assert.Equal("✓", viewModel.SetupSteps.Single(step => step.Step == SetupStep.License).Status);
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

        viewModel.CloseDetailPanelCommand.Execute(null);
        Assert.False(viewModel.IsDetailPanelOpen);
    }

    [Fact]
    public void PlayPauseAudioCommand_IsDisabledWhenNoPlayableAudioIsSelected()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.PlayPauseAudioCommand.CanExecute(null));
        Assert.Empty(viewModel.SelectedJobPlaybackPath);
    }

    [Fact]
    public void PlayPauseAudioCommand_UsesSelectedJobSourceAudioWhenNormalizedAudioIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        var viewModel = new MainWindowViewModel(new AppPaths(root, root, AppContext.BaseDirectory));
        var job = new JobSummary("job-001", "meeting", "meeting.wav", audioPath, "registered", 0, 0, DateTimeOffset.Now);

        viewModel.Jobs.Add(job);
        viewModel.SelectedJob = job;

        Assert.True(viewModel.PlayPauseAudioCommand.CanExecute(null));
        Assert.Equal(audioPath, viewModel.SelectedJobPlaybackPath);
        Assert.Equal("\uE768", viewModel.PlayPauseAudioIcon);
        Assert.Equal("00:00 / 00:00", viewModel.PlaybackTimeDisplay);
    }

    [Fact]
    public void PlaybackRate_UpdatesSelectedSpeed()
    {
        var viewModel = CreateViewModel();

        viewModel.PlaybackRate = 1.5;

        Assert.Equal(1.5, viewModel.PlaybackRate);
        Assert.Contains(2.0, viewModel.PlaybackRates);
    }

    [Fact]
    public void SkipToNextSegmentCommand_SelectsNextSegmentStart()
    {
        var viewModel = CreateViewModel();
        var first = new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.000",
            "Speaker_0",
            "first",
            "",
            "segment-001",
            StartSeconds: 0,
            EndSeconds: 5);
        var second = new TranscriptSegmentPreview(
            "00:00:05.000",
            "00:00:10.000",
            "Speaker_1",
            "second",
            "",
            "segment-002",
            StartSeconds: 5,
            EndSeconds: 10);
        viewModel.Segments.Add(first);
        viewModel.Segments.Add(second);
        viewModel.PlaybackPositionSeconds = 1;

        Assert.True(viewModel.SkipToNextSegmentCommand.CanExecute(null));

        viewModel.SkipToNextSegmentCommand.Execute(null);

        Assert.Equal("segment-002", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal(5, viewModel.PlaybackPositionSeconds, 2);
        Assert.Equal(1, viewModel.TranscriptAutoScrollRequestId);
    }

    [Fact]
    public void SkipToPreviousSegmentCommand_RestartsCurrentSegmentBeforeMovingPrevious()
    {
        var viewModel = CreateViewModel();
        var first = new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.000",
            "Speaker_0",
            "first",
            "",
            "segment-001",
            StartSeconds: 0,
            EndSeconds: 5);
        var second = new TranscriptSegmentPreview(
            "00:00:05.000",
            "00:00:10.000",
            "Speaker_1",
            "second",
            "",
            "segment-002",
            StartSeconds: 5,
            EndSeconds: 10);
        viewModel.Segments.Add(first);
        viewModel.Segments.Add(second);
        viewModel.PlaybackPositionSeconds = 7;

        viewModel.SkipToPreviousSegmentCommand.Execute(null);

        Assert.Equal("segment-002", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal(5, viewModel.PlaybackPositionSeconds, 2);

        viewModel.SkipToPreviousSegmentCommand.Execute(null);

        Assert.Equal("segment-001", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal(0, viewModel.PlaybackPositionSeconds, 2);
    }

    [Fact]
    public void StageStatuses_EndWithSummaryStage()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(4, viewModel.StageStatuses.Count);
        Assert.Equal("要約", viewModel.StageStatuses.Last().Name);
        Assert.False(viewModel.StageStatuses.Last().ShowConnectorAfter);
        Assert.DoesNotContain(viewModel.StageStatuses, stage => stage.Name == "レビュー");
    }

    [Fact]
    public void Constructor_ShowsReviewStageSkippedWhenSavedSettingIsOff()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        new AsrSettingsRepository(paths).Save(new AsrSettings(
            "context",
            "term",
            "kotoba-whisper-v2.2-faster",
            false));

        var viewModel = new MainWindowViewModel(paths);
        var reviewStage = viewModel.StageStatuses.Single(stage => stage.IsToggleable);

        Assert.False(viewModel.EnableReviewStage);
        Assert.Equal("スキップ", reviewStage.Status);
        Assert.Contains("スキップ", reviewStage.ToggleToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void SummaryStageToggleText_ReflectsSummaryStageSetting()
    {
        var viewModel = CreateViewModel();

        viewModel.EnableSummaryStage = true;

        Assert.Equal("要約 ON", viewModel.SummaryStageToggleText);
        Assert.Contains("スキップ", viewModel.SummaryStageToggleToolTip, StringComparison.Ordinal);

        viewModel.EnableSummaryStage = false;

        Assert.Equal("要約 OFF", viewModel.SummaryStageToggleText);
        Assert.Contains("実行", viewModel.SummaryStageToggleToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void SummaryStageToggle_UpdatesStageStatusForHeaderToggle()
    {
        var viewModel = CreateViewModel();
        var summaryStage = viewModel.StageStatuses.Last();

        Assert.False(viewModel.EnableSummaryStage);
        Assert.Equal("スキップ", summaryStage.Status);
        Assert.True(summaryStage.IsSkipped);

        viewModel.EnableSummaryStage = true;

        Assert.Equal("未開始", summaryStage.Status);
        Assert.False(summaryStage.IsSkipped);

        viewModel.EnableSummaryStage = false;

        Assert.Equal("スキップ", summaryStage.Status);
        Assert.True(summaryStage.IsSkipped);
    }

    [Fact]
    public void ReviewStageSkippedRunUpdate_UsesLocalizedSkippedStatus()
    {
        var viewModel = CreateViewModel();
        var reviewStage = viewModel.StageStatuses.Single(stage => stage.IsToggleable);
        var method = typeof(MainWindowViewModel).GetMethod(
            "ApplyRunUpdate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        method?.Invoke(viewModel, [
            new JobRunUpdate(
                JobRunStage.Review,
                JobRunStageState.Skipped,
                100)
        ]);

        Assert.Equal("スキップ", reviewStage.Status);
        Assert.True(reviewStage.IsSkipped);
    }

    [Fact]
    public void StageRunUpdate_MarksStageDoingEvenWhenProgressIsZero()
    {
        var viewModel = CreateViewModel();
        var method = typeof(MainWindowViewModel).GetMethod(
            "ApplyRunUpdate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        method?.Invoke(viewModel, [
            new JobRunUpdate(
                JobRunStage.Summary,
                JobRunStageState.Running,
                0)
        ]);

        var summaryStage = viewModel.StageStatuses.Last();
        Assert.True(summaryStage.IsRunning);
        Assert.True(summaryStage.IsDoing);
        Assert.False(summaryStage.IsPending);
    }

    [Fact]
    public void StageRunUpdate_DoesNotMarkRunningStageDoneAtOneHundredPercent()
    {
        var viewModel = CreateViewModel();
        var method = typeof(MainWindowViewModel).GetMethod(
            "ApplyRunUpdate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        method?.Invoke(viewModel, [
            new JobRunUpdate(
                JobRunStage.Summary,
                JobRunStageState.Running,
                100)
        ]);

        var summaryStage = viewModel.StageStatuses.Last();
        Assert.True(summaryStage.IsRunning);
        Assert.True(summaryStage.IsDoing);
        Assert.False(summaryStage.IsDone);
    }

    [Fact]
    public void StageRunUpdate_DoesNotMarkSkippedStageDone()
    {
        var viewModel = CreateViewModel();
        var method = typeof(MainWindowViewModel).GetMethod(
            "ApplyRunUpdate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        method?.Invoke(viewModel, [
            new JobRunUpdate(
                JobRunStage.Summary,
                JobRunStageState.Skipped,
                100)
        ]);

        var summaryStage = viewModel.StageStatuses.Last();
        Assert.True(summaryStage.IsSkipped);
        Assert.False(summaryStage.IsDone);
        Assert.False(summaryStage.IsDoing);
    }

    [Fact]
    public void SelectingSegment_SeeksPlaybackPositionToSegmentStart()
    {
        var viewModel = CreateViewModel();
        var segment = new TranscriptSegmentPreview(
            "00:01:12.340",
            "00:01:20.000",
            "Speaker_0",
            "target",
            "",
            "segment-001",
            StartSeconds: 72.34,
            EndSeconds: 80);

        viewModel.SelectedSegment = segment;

        Assert.Equal(72.34, viewModel.PlaybackPositionSeconds, 2);
    }

    [Fact]
    public void PlaybackPosition_SelectsVisibleSegmentWhenAutoScrollIsEnabled()
    {
        var viewModel = CreateViewModel();
        viewModel.Segments.Add(new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.000",
            "Speaker_0",
            "first",
            "",
            "segment-001",
            StartSeconds: 0,
            EndSeconds: 5));
        viewModel.Segments.Add(new TranscriptSegmentPreview(
            "00:00:05.000",
            "00:00:10.000",
            "Speaker_1",
            "second",
            "",
            "segment-002",
            StartSeconds: 5,
            EndSeconds: 10));

        viewModel.PlaybackPositionSeconds = 6;
        Assert.Null(viewModel.SelectedSegment);

        viewModel.IsTranscriptAutoScrollEnabled = true;

        Assert.Equal("segment-002", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal(1, viewModel.TranscriptAutoScrollRequestId);
        Assert.Equal(6, viewModel.PlaybackPositionSeconds, 2);
    }

    [Fact]
    public void PlaybackPosition_DoesNotAutoSelectFilteredOutSegment()
    {
        var viewModel = CreateViewModel();
        viewModel.Segments.Add(new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.000",
            "Speaker_0",
            "visible first",
            "",
            "segment-001",
            StartSeconds: 0,
            EndSeconds: 5));
        viewModel.Segments.Add(new TranscriptSegmentPreview(
            "00:00:05.000",
            "00:00:10.000",
            "Speaker_1",
            "hidden second",
            "",
            "segment-002",
            StartSeconds: 5,
            EndSeconds: 10));

        viewModel.SegmentSearchText = "visible";
        viewModel.IsTranscriptAutoScrollEnabled = true;
        viewModel.PlaybackPositionSeconds = 6;

        Assert.Equal("segment-001", viewModel.SelectedSegment?.SegmentId);
    }

    [Fact]
    public void PlaybackAutoScroll_DoesNotChangeSelectedReviewDraft()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 5, "Speaker_0", "first"),
            new TranscriptSegment("segment-002", job.JobId, 5, 10, "Speaker_1", "second")
        ]);
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", job.JobId, "segment-001", "wording", "first", "1st", "suggestion", 0.8),
            new CorrectionDraft("draft-002", job.JobId, "segment-002", "wording", "second", "2nd", "suggestion", 0.8)
        ]);
        var viewModel = new MainWindowViewModel(paths);

        Assert.Equal("draft-001", viewModel.SelectedCorrectionDraftId);

        viewModel.IsTranscriptAutoScrollEnabled = true;
        viewModel.PlaybackPositionSeconds = 6;

        Assert.Equal("segment-002", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal("draft-001", viewModel.SelectedCorrectionDraftId);
    }

    [Fact]
    public void PlaybackAutoScroll_DoesNotReplacePendingSegmentEdit()
    {
        var viewModel = CreateViewModel();
        var first = new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.000",
            "Speaker_0",
            "first",
            "",
            "segment-001",
            StartSeconds: 0,
            EndSeconds: 5);
        var second = new TranscriptSegmentPreview(
            "00:00:05.000",
            "00:00:10.000",
            "Speaker_1",
            "second",
            "",
            "segment-002",
            StartSeconds: 5,
            EndSeconds: 10);
        viewModel.Segments.Add(first);
        viewModel.Segments.Add(second);
        viewModel.SelectedSegment = first;
        viewModel.SelectedSegmentEditText = "unsaved edit";

        viewModel.IsTranscriptAutoScrollEnabled = true;
        viewModel.PlaybackPositionSeconds = 6;

        Assert.Equal("segment-001", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal("unsaved edit", viewModel.SelectedSegmentEditText);
    }

    [Fact]
    public void InlineSegmentEditCommand_ActivatesAndCancelRestoresSegmentText()
    {
        var viewModel = CreateViewModel();
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", @"C:\audio\meeting.wav", "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];
        var segment = new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.000",
            "Speaker_0",
            "first",
            "",
            "segment-001",
            StartSeconds: 0,
            EndSeconds: 5);
        viewModel.Segments.Add(segment);

        Assert.True(viewModel.BeginSegmentInlineEditCommand.CanExecute(segment));
        viewModel.BeginSegmentInlineEditCommand.Execute(segment);

        Assert.True(viewModel.IsSegmentInlineEditActive);
        Assert.Equal(segment, viewModel.SelectedSegment);
        Assert.Equal("first", viewModel.SelectedSegmentEditText);

        viewModel.SelectedSegmentEditText = "edited";
        viewModel.CancelSegmentInlineEditCommand.Execute(null);

        Assert.False(viewModel.IsSegmentInlineEditActive);
        Assert.Equal("first", viewModel.SelectedSegmentEditText);
    }

    [Fact]
    public void PlaybackAutoScroll_DoesNotReplaceActiveInlineSegmentEdit()
    {
        var viewModel = CreateViewModel();
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", @"C:\audio\meeting.wav", "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];
        var first = new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.000",
            "Speaker_0",
            "first",
            "",
            "segment-001",
            StartSeconds: 0,
            EndSeconds: 5);
        var second = new TranscriptSegmentPreview(
            "00:00:05.000",
            "00:00:10.000",
            "Speaker_1",
            "second",
            "",
            "segment-002",
            StartSeconds: 5,
            EndSeconds: 10);
        viewModel.Segments.Add(first);
        viewModel.Segments.Add(second);
        viewModel.BeginSegmentInlineEditCommand.Execute(first);

        viewModel.IsTranscriptAutoScrollEnabled = true;
        viewModel.PlaybackPositionSeconds = 6;

        Assert.True(viewModel.IsSegmentInlineEditActive);
        Assert.Equal("segment-001", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal("first", viewModel.SelectedSegmentEditText);
    }

    [Fact]
    public void InlineSegmentEdit_AutoSavesWhenSelectingAnotherSegment()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        var repository = new TranscriptSegmentRepository(paths);
        repository.SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 5, "Speaker_0", "first"),
            new TranscriptSegment("segment-002", job.JobId, 5, 10, "Speaker_1", "second")
        ]);
        var viewModel = new MainWindowViewModel(paths);
        var first = viewModel.Segments.Single(segment => segment.SegmentId == "segment-001");
        var second = viewModel.Segments.Single(segment => segment.SegmentId == "segment-002");

        viewModel.BeginSegmentInlineEditCommand.Execute(first);
        viewModel.SelectedSegmentEditText = "edited first";
        viewModel.SelectedSegment = second;

        Assert.False(viewModel.IsSegmentInlineEditActive);
        Assert.Equal("segment-002", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal("edited first", repository.ReadPreviews(job.JobId).Single(segment => segment.SegmentId == "segment-001").Text);
    }

    [Fact]
    public void InlineSegmentEdit_DoesNotChangeSelectionWhenAutoSaveCannotCommit()
    {
        var viewModel = CreateViewModel();
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", @"C:\audio\meeting.wav", "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];
        var first = new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.000",
            "Speaker_0",
            "first",
            "",
            "segment-001",
            StartSeconds: 0,
            EndSeconds: 5);
        var second = new TranscriptSegmentPreview(
            "00:00:05.000",
            "00:00:10.000",
            "Speaker_1",
            "second",
            "",
            "segment-002",
            StartSeconds: 5,
            EndSeconds: 10);
        viewModel.Segments.Add(first);
        viewModel.Segments.Add(second);

        viewModel.BeginSegmentInlineEditCommand.Execute(first);
        viewModel.SelectedSegmentEditText = "   ";
        viewModel.SelectedSegment = second;

        Assert.True(viewModel.IsSegmentInlineEditActive);
        Assert.Equal("segment-001", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal("   ", viewModel.SelectedSegmentEditText);
    }

    [Fact]
    public void InlineSegmentEdit_DoesNotOverwriteTextWhenBeginEditCannotChangeSelection()
    {
        var viewModel = CreateViewModel();
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", @"C:\audio\meeting.wav", "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];
        var first = new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.000",
            "Speaker_0",
            "first",
            "",
            "segment-001",
            StartSeconds: 0,
            EndSeconds: 5);
        var second = new TranscriptSegmentPreview(
            "00:00:05.000",
            "00:00:10.000",
            "Speaker_1",
            "second",
            "",
            "segment-002",
            StartSeconds: 5,
            EndSeconds: 10);
        viewModel.Segments.Add(first);
        viewModel.Segments.Add(second);

        viewModel.BeginSegmentInlineEditCommand.Execute(first);
        viewModel.SelectedSegmentEditText = "   ";
        viewModel.BeginSegmentInlineEditCommand.Execute(second);

        Assert.True(viewModel.IsSegmentInlineEditActive);
        Assert.Equal("segment-001", viewModel.SelectedSegment?.SegmentId);
        Assert.Equal("   ", viewModel.SelectedSegmentEditText);
    }

    [Fact]
    public void RevertSegmentEditCommand_RestoresCommittedSegmentEdit()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        var repository = new TranscriptSegmentRepository(paths);
        repository.SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 5, "Speaker_0", "first")
        ]);
        new TranscriptEditService(paths).ApplySegmentEdit(job.JobId, "segment-001", "edited first");
        var viewModel = new MainWindowViewModel(paths);
        var segment = Assert.Single(viewModel.Segments);

        Assert.Equal("edited first", segment.Text);
        Assert.True(viewModel.RevertSegmentEditCommand.CanExecute(segment));
        viewModel.RevertSegmentEditCommand.Execute(segment);

        Assert.Equal("first", repository.ReadPreviews(job.JobId).Single().Text);
        Assert.Equal("first", viewModel.SelectedSegment?.Text);
        Assert.Contains("戻しました", viewModel.LatestLog, StringComparison.Ordinal);
    }

    [Fact]
    public void RunSelectedJobCommand_IsEnabledWhenJobAndRuntimeAssetsAreReady()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        Touch(paths.FasterWhisperScriptPath);
        CreateFasterWhisperRuntime(paths);
        Directory.CreateDirectory(paths.KotobaWhisperFasterModelPath);
        Touch(paths.ReviewModelPath);
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
        Assert.True(viewModel.RunSelectedJobCommand.CanExecute(null));
    }

    [Fact]
    public async Task RunSelectedJobAsync_StopsBeforeRunWhenSourceAudioIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.FasterWhisperScriptPath);
        CreateFasterWhisperRuntime(paths);
        Directory.CreateDirectory(paths.KotobaWhisperFasterModelPath);
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "kotoba-whisper-v2.2-faster", false));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            LicenseAccepted = true,
            SelectedAsrModelId = "kotoba-whisper-v2.2-faster"
        });
        var missingAudioPath = Path.Combine(root, "missing.wav");
        var viewModel = new MainWindowViewModel(paths);
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", missingAudioPath, "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];

        Assert.False(viewModel.RunSelectedJobCommand.CanExecute(null));
        await viewModel.RunSelectedJobAsync();

        Assert.False(viewModel.IsRunInProgress);
        Assert.True(viewModel.IsSetupWizardModalOpen);
        Assert.Contains("音声ファイルが見つかりません", viewModel.LatestLog, StringComparison.Ordinal);
        Assert.Contains(missingAudioPath, viewModel.RunPreflightDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunSelectedJobAsync_RechecksFfmpegFileBeforeRun()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.FasterWhisperScriptPath);
        CreateFasterWhisperRuntime(paths);
        Directory.CreateDirectory(paths.KotobaWhisperFasterModelPath);
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "kotoba-whisper-v2.2-faster", false));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            LicenseAccepted = true,
            SelectedAsrModelId = "kotoba-whisper-v2.2-faster"
        });
        var viewModel = new MainWindowViewModel(paths);
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", audioPath, "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];
        File.Delete(paths.FfmpegPath);

        await viewModel.RunSelectedJobAsync();

        Assert.False(viewModel.IsRunInProgress);
        Assert.True(viewModel.IsSetupWizardModalOpen);
        Assert.Contains("ffmpeg が見つかりません", viewModel.LatestLog, StringComparison.Ordinal);
        Assert.Contains(paths.FfmpegPath, viewModel.RunPreflightDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void RunSelectedJobCommand_RequiresReviewAssetsWhenReviewStageIsEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.FasterWhisperScriptPath);
        CreateFasterWhisperRuntime(paths);
        Directory.CreateDirectory(paths.KotobaWhisperFasterModelPath);
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "kotoba-whisper-v2.2-faster", true));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            LicenseAccepted = true
        });

        var viewModel = new MainWindowViewModel(paths);
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", audioPath, "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];

        Assert.True(viewModel.EnableReviewStage);
        Assert.True(viewModel.RequiredRuntimeAssetsReady);
        Assert.False(viewModel.ReviewStageAssetsReady);
        Assert.False(viewModel.RunSelectedJobCommand.CanExecute(null));
        Assert.Contains("整文ランタイムまたは整文モデルが不足しています", viewModel.RunPreflightDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void RunSelectedJobCommand_DoesNotRequireReviewModelWhenReviewStageIsDisabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.FasterWhisperScriptPath);
        CreateFasterWhisperRuntime(paths);
        Directory.CreateDirectory(paths.KotobaWhisperFasterModelPath);
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "kotoba-whisper-v2.2-faster", false));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            LicenseAccepted = true
        });

        var viewModel = new MainWindowViewModel(paths);
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", audioPath, "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];

        Assert.False(viewModel.EnableReviewStage);
        Assert.True(viewModel.RequiredRuntimeAssetsReady);
        Assert.True(viewModel.RunSelectedJobCommand.CanExecute(null));
    }

    [Fact]
    public void RunSelectedJobCommand_AcceptsInstalledUserReviewModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        Touch(paths.FasterWhisperScriptPath);
        CreateFasterWhisperRuntime(paths);
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var installService = new ModelInstallService(paths, new InstalledModelRepository(paths), new ModelVerificationService());
        var asrItem = catalog.Models.First(model => model.ModelId == "faster-whisper-large-v3");
        var asrPath = Path.Combine(paths.UserModels, "asr", "faster-whisper-large-v3");
        Directory.CreateDirectory(asrPath);
        File.WriteAllText(Path.Combine(asrPath, "model.bin"), "asr");
        installService.RegisterLocalModel(asrItem, asrPath, "download");
        var reviewItem = catalog.Models.First(model => model.ModelId == "llm-jp-4-8b-thinking-q4-k-m");
        var reviewPath = installService.GetDefaultInstallPath(reviewItem);
        Touch(reviewPath);
        installService.RegisterLocalModel(reviewItem, reviewPath, "download");
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "faster-whisper-large-v3"));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            LicenseAccepted = true,
            SelectedAsrModelId = asrItem.ModelId,
            SelectedReviewModelId = reviewItem.ModelId
        });

        var viewModel = new MainWindowViewModel(paths);
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", audioPath, "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];

        Assert.True(viewModel.RequiredRuntimeAssetsReady);
        Assert.True(viewModel.RunSelectedJobCommand.CanExecute(null));
    }

    [Fact]
    public void RunSelectedJobCommand_RepairsHiddenTernaryReviewModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = CreatePathsWithoutTernaryRuntime(root);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        Touch(paths.FasterWhisperScriptPath);
        CreateFasterWhisperRuntime(paths);
        Directory.CreateDirectory(paths.KotobaWhisperFasterModelPath);
        Touch(Path.Combine(paths.KotobaWhisperFasterModelPath, "model.bin"));
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var installService = new ModelInstallService(paths, new InstalledModelRepository(paths), new ModelVerificationService());
        var asrItem = catalog.Models.First(model => model.ModelId == "kotoba-whisper-v2.2-faster");
        installService.RegisterLocalModel(asrItem, paths.KotobaWhisperFasterModelPath, "download");
        var reviewItem = catalog.Models.First(model => model.ModelId == "ternary-bonsai-8b-q2-0");
        var reviewPath = installService.GetDefaultInstallPath(reviewItem);
        Touch(reviewPath);
        installService.RegisterLocalModel(reviewItem, reviewPath, "download");
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "kotoba-whisper-v2.2-faster", false, true));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            LicenseAccepted = true,
            SelectedAsrModelId = "kotoba-whisper-v2.2-faster",
            SelectedReviewModelId = reviewItem.ModelId
        });

        var viewModel = new MainWindowViewModel(paths);
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", audioPath, "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];

        Assert.False(viewModel.SelectedSetupModelsReady);
        Assert.Equal("gemma-4-e4b-it-q4-k-m", viewModel.SelectedSetupReviewModel?.ModelId);
        Assert.True(viewModel.SetupTernaryReviewRuntimeReady);
        Assert.False(viewModel.SelectedSetupConfigurationReady);
        Assert.False(viewModel.ReviewStageAssetsReady);
        Assert.False(viewModel.RunSelectedJobCommand.CanExecute(null));
        Assert.DoesNotContain("Ternary review runtime", viewModel.SetupPrimaryInstallSummary, StringComparison.Ordinal);
        Assert.DoesNotContain(paths.TernaryLlamaCompletionPath, viewModel.RunPreflightDetail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunSelectedJobCommand_UsesPresetReviewModelWhenSelectedReviewModelIsEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        Touch(paths.FasterWhisperScriptPath);
        CreateFasterWhisperRuntime(paths);
        Directory.CreateDirectory(paths.WhisperSmallModelPath);
        var audioPath = Path.Combine(root, "meeting.wav");
        Touch(audioPath);
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var installService = new ModelInstallService(paths, new InstalledModelRepository(paths), new ModelVerificationService());
        var reviewItem = catalog.Models.First(model => model.ModelId == "bonsai-8b-q1-0");
        var reviewPath = installService.GetDefaultInstallPath(reviewItem);
        Touch(reviewPath);
        installService.RegisterLocalModel(reviewItem, reviewPath, "download");
        new AsrSettingsRepository(paths).Save(new AsrSettings(string.Empty, string.Empty, "whisper-small", true));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            LicenseAccepted = true,
            SelectedModelPresetId = "lightweight",
            SelectedAsrModelId = "whisper-small",
            SelectedReviewModelId = string.Empty
        });

        var viewModel = new MainWindowViewModel(paths);
        viewModel.Jobs.Add(new JobSummary("job-001", "meeting", "meeting.wav", audioPath, "registered", 0, 0, DateTimeOffset.Now));
        viewModel.SelectedJob = viewModel.Jobs[0];

        Assert.True(viewModel.ReviewStageAssetsReady);
        Assert.True(viewModel.RunSelectedJobCommand.CanExecute(null));
    }

    [Fact]
    public void InstallVerifiedUpdateCommand_IsEnabledOnlyWhenInstallerIsReadyAndNoJobIsRunning()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.InstallVerifiedUpdateCommand.CanExecute(null));

        SetPrivateProperty(viewModel, nameof(MainWindowViewModel.VerifiedUpdateInstallerPath), @"C:\updates\KoeNote.msi");

        Assert.True(viewModel.InstallVerifiedUpdateCommand.CanExecute(null));

        SetPrivateProperty(viewModel, nameof(MainWindowViewModel.IsRunInProgress), true);

        Assert.False(viewModel.InstallVerifiedUpdateCommand.CanExecute(null));
    }

    [Fact]
    public void UpToDateUpdateCheck_ClearsPreviouslyVerifiedInstaller()
    {
        var viewModel = CreateViewModel();
        SetPrivateProperty(viewModel, nameof(MainWindowViewModel.VerifiedUpdateInstallerPath), @"C:\updates\KoeNote.msi");
        Assert.True(viewModel.CanShowInstallUpdateAction);

        InvokePrivate(
            viewModel,
            "ApplyUpdateCheckResult",
            new UpdateCheckResult(
                true,
                false,
                false,
                "0.14.0",
                null,
                "KoeNote is up to date (0.14.0)."),
            true);

        Assert.Empty(viewModel.VerifiedUpdateInstallerPath);
        Assert.Empty(viewModel.UpdateDownloadProgressText);
        Assert.False(viewModel.CanShowInstallUpdateAction);
        Assert.False(viewModel.InstallVerifiedUpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task InstallVerifiedUpdateCommand_DisablesInstallAfterStartingInstaller()
    {
        var viewModel = CreateViewModel();
        var installerPath = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "KoeNote.msi");
        var launcher = new RecordingUpdateInstallerLauncher();
        var shutdownRequested = false;
        Action requestShutdown = () => shutdownRequested = true;
        SetPrivateField(viewModel, "_updateInstallerLauncher", launcher);
        SetPrivateField(viewModel, "_shutdownApplication", requestShutdown);
        SetPrivateProperty(viewModel, nameof(MainWindowViewModel.VerifiedUpdateInstallerPath), installerPath);

        viewModel.InstallVerifiedUpdateCommand.Execute(null);
        await Task.Delay(50);

        Assert.Equal(installerPath, launcher.StartedInstallerPath);
        Assert.Empty(viewModel.VerifiedUpdateInstallerPath);
        Assert.False(viewModel.CanShowInstallUpdateAction);
        Assert.False(viewModel.InstallVerifiedUpdateCommand.CanExecute(null));
        Assert.Contains("Installer started", viewModel.UpdateDownloadProgressText, StringComparison.Ordinal);
        Assert.True(shutdownRequested);
    }

    private static MainWindowViewModel CreateViewModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        return new MainWindowViewModel(new AppPaths(root, root, AppContext.BaseDirectory));
    }

    private static AppPaths CreatePathsWithoutTernaryRuntime(string root)
    {
        var appBase = Path.Combine(root, "app");
        Directory.CreateDirectory(Path.Combine(appBase, "catalog"));
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "catalog", "model-catalog.json"),
            Path.Combine(appBase, "catalog", "model-catalog.json"));
        return new AppPaths(root, root, appBase);
    }

    private static List<T> ViewItems<T>(IEnumerable view)
    {
        return view.Cast<T>().ToList();
    }

    private static void SetPrivateProperty<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(propertyName)
            ?? throw new InvalidOperationException($"Property not found: {propertyName}");
        property.SetValue(target, value);
    }

    private static void SetPrivateField<T>(object target, string fieldName, T value)
    {
        var field = target.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field not found: {fieldName}");
        field.SetValue(target, value);
    }

    private static void InvokePrivate(object target, string methodName, params object[] arguments)
    {
        var method = target.GetType().GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method not found: {methodName}");
        method.Invoke(target, arguments);
    }

    private static T InvokePrivate<T>(object target, string methodName, params object[] arguments)
    {
        var method = target.GetType().GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method not found: {methodName}");
        return (T)method.Invoke(target, arguments)!;
    }

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
    }

    private static void CreateFasterWhisperRuntime(AppPaths paths)
    {
        Directory.CreateDirectory(Path.Combine(paths.AsrPythonEnvironment, "Lib", "site-packages", "faster_whisper-1.2.1.dist-info"));
    }

    private sealed class RecordingUpdateInstallerLauncher : IUpdateInstallerLauncher
    {
        public string? StartedInstallerPath { get; private set; }

        public UpdateInstallerLaunchResult Launch(string installerPath, string? expectedSha256 = null)
        {
            StartedInstallerPath = installerPath;
            return new UpdateInstallerLaunchResult(installerPath, DateTimeOffset.Now, "SHA256 verified download", false);
        }
    }
}
