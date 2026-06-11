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
public sealed class MainWindowViewModelModelTests : MainWindowViewModelTestBase
{
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
        Assert.Equal(
            "faster-whisper large-v3 (導入済み)",
            viewModel.AvailableAsrEngines.Single(engine => engine.EngineId == asrItem.EngineId).SetupDisplayName);
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
}
