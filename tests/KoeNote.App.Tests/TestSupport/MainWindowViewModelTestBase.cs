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

public abstract class MainWindowViewModelTestBase
{
    protected static RunReadyViewModelFixture CreateRunReadyViewModel(
        bool enableReviewStage,
        FakeTranscriptPolishingRuntime polishingRuntime)
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();

        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        CreateFasterWhisperRuntime(paths);
        Directory.CreateDirectory(paths.KotobaWhisperFasterModelPath);
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
        new AsrSettingsRepository(paths).Save(new AsrSettings(
            string.Empty,
            string.Empty,
            "kotoba-whisper-v2.2-faster",
            enableReviewStage));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            LicenseAccepted = true,
            SelectedAsrModelId = "kotoba-whisper-v2.2-faster",
            SelectedReviewModelId = "llm-jp-4-8b-thinking-q4-k-m"
        });

        var job = new JobRepository(paths).CreateFromAudio(audioPath);
        var segments = new[]
        {
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "raw text")
        };
        var asrStageRunner = new SavingAsrStageRunnerStub(paths, segments);
        var reviewStageRunner = new ReviewStageRunnerStub();
        var viewModel = new MainWindowViewModel(paths);
        SetPrivateField(
            viewModel,
            "_jobRunCoordinator",
            new JobRunCoordinator(
                new PreprocessStageRunnerStub("normalized.wav"),
                asrStageRunner,
                reviewStageRunner,
                new SummaryStageRunnerStub()));
        SetPrivateField(
            viewModel,
            "_readablePolishingStageRunner",
            new ReadablePolishingStageRunner(
                paths,
                new JobRepository(paths),
                new StageProgressRepository(paths),
                new JobLogRepository(paths),
                new InstalledModelRepository(paths),
                new SetupStateService(paths),
                new TranscriptPolishingService(
                    new TranscriptReadRepository(paths),
                    new TranscriptDerivativeRepository(paths),
                    polishingRuntime),
                new ReadablePolishingPromptSettingsRepository(paths)));
        viewModel.SelectedJob = viewModel.Jobs.Single(item => item.JobId == job.JobId);
        return new RunReadyViewModelFixture(viewModel, polishingRuntime, reviewStageRunner);
    }

    protected sealed record RunReadyViewModelFixture(
        MainWindowViewModel ViewModel,
        FakeTranscriptPolishingRuntime PolishingRuntime,
        ReviewStageRunnerStub ReviewStageRunner);

    protected sealed class PreprocessStageRunnerStub(string normalizedAudioPath) : IPreprocessStageRunner
    {
        public Task<string?> RunAsync(
            JobSummary job,
            Action<JobRunUpdate> report,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(normalizedAudioPath);
        }
    }

    protected sealed class SavingAsrStageRunnerStub(AppPaths paths, IReadOnlyList<TranscriptSegment> segments) : IAsrStageRunner
    {
        public Task<IReadOnlyList<TranscriptSegment>?> RunAsync(
            JobSummary job,
            string normalizedAudioPath,
            AsrSettings asrSettings,
            Action<JobRunUpdate> report,
            CancellationToken cancellationToken)
        {
            new TranscriptSegmentRepository(paths).SaveSegments(segments);
            report(new JobRunUpdate(
                JobRunStage.Asr,
                JobRunStageState.Succeeded,
                100,
                Segments: segments,
                RefreshJobViews: true));
            return Task.FromResult<IReadOnlyList<TranscriptSegment>?>(segments);
        }
    }

    protected sealed class ReviewStageRunnerStub : IReviewStageRunner
    {
        public bool RunWasCalled { get; private set; }

        public bool SkipWasCalled { get; private set; }

        public Task<bool> RunAsync(
            JobSummary job,
            IReadOnlyList<TranscriptSegment> segments,
            Action<JobRunUpdate> report,
            CancellationToken cancellationToken)
        {
            RunWasCalled = true;
            report(new JobRunUpdate(
                JobRunStage.Review,
                JobRunStageState.Succeeded,
                100,
                Drafts: [],
                RefreshJobViews: true));
            return Task.FromResult(true);
        }

        public void Skip(JobSummary job, Action<JobRunUpdate> report)
        {
            SkipWasCalled = true;
            report(new JobRunUpdate(
                JobRunStage.Review,
                JobRunStageState.Skipped,
                100,
                ClearReviewPreview: true));
        }
    }

    protected sealed class SummaryStageRunnerStub : ISummaryStageRunner
    {
        public Task RunAsync(
            JobSummary job,
            Action<JobRunUpdate> report,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void Skip(JobSummary job, Action<JobRunUpdate> report, string reason)
        {
        }
    }

    protected sealed class FakeTranscriptPolishingRuntime(string content, Exception? exception = null) : ITranscriptPolishingRuntime
    {
        public bool WasCalled { get; private set; }

        public List<string> SeenSegmentTexts { get; } = [];

        public List<string> SeenSpeakerNames { get; } = [];

        public Task<TranscriptPolishingChunkResult> PolishChunkAsync(
            TranscriptPolishingOptions options,
            TranscriptPolishingChunk chunk,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            SeenSegmentTexts.AddRange(chunk.Segments.Select(static segment => segment.Text));
            SeenSpeakerNames.AddRange(chunk.Segments.Select(static segment => segment.Speaker));
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(new TranscriptPolishingChunkResult(chunk, content, TimeSpan.FromSeconds(1)));
        }
    }

    protected static MainWindowViewModel CreateViewModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        return new MainWindowViewModel(new AppPaths(root, root, AppContext.BaseDirectory));
    }

    protected static AppPaths CreatePathsWithoutTernaryRuntime(string root)
    {
        var appBase = Path.Combine(root, "app");
        Directory.CreateDirectory(Path.Combine(appBase, "catalog"));
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "catalog", "model-catalog.json"),
            Path.Combine(appBase, "catalog", "model-catalog.json"));
        return new AppPaths(root, root, appBase);
    }

    protected static List<T> ViewItems<T>(IEnumerable view)
    {
        return view.Cast<T>().ToList();
    }

    protected static void SetPrivateProperty<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(propertyName)
            ?? throw new InvalidOperationException($"Property not found: {propertyName}");
        property.SetValue(target, value);
    }

    protected static void SetPrivateField<T>(object target, string fieldName, T value)
    {
        var field = target.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field not found: {fieldName}");
        field.SetValue(target, value);
    }

    protected static void InvokePrivate(object target, string methodName, params object[] arguments)
    {
        var method = target.GetType().GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method not found: {methodName}");
        method.Invoke(target, arguments);
    }

    protected static T InvokePrivate<T>(object target, string methodName, params object[] arguments)
    {
        var method = target.GetType().GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method not found: {methodName}");
        return (T)method.Invoke(target, arguments)!;
    }

    protected static T InvokePrivateStatic<T>(string methodName, params object[] arguments)
    {
        var method = typeof(MainWindowViewModel).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method not found: {methodName}");
        return (T)method.Invoke(null, arguments)!;
    }

    protected static XDocument ReadZipXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        Assert.NotNull(entry);
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    protected static Dictionary<string, string> ReadInlineStringCells(XDocument worksheet)
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return worksheet
            .Descendants(main + "c")
            .Where(cell => cell.Attribute("r") is not null)
            .ToDictionary(
                cell => cell.Attribute("r")!.Value,
                cell => string.Concat(cell.Descendants(main + "t").Select(static text => text.Value)));
    }

    protected static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
    }

    protected static void RegisterVerifiedModel(
        AppPaths paths,
        string modelId,
        string role,
        string engineId,
        string filePath)
    {
        new InstalledModelRepository(paths).UpsertInstalledModel(new InstalledModel(
            modelId,
            role,
            engineId,
            modelId,
            Family: null,
            Version: null,
            filePath,
            ManifestPath: null,
            SizeBytes: 0,
            Sha256: null,
            Verified: true,
            LicenseName: "test",
            SourceType: "test",
            InstalledAt: DateTimeOffset.Now,
            LastVerifiedAt: DateTimeOffset.Now,
            Status: "installed"));
    }

    protected static void CreateFasterWhisperRuntime(AppPaths paths)
    {
        Touch(paths.AsrPythonPath);
        Touch(paths.FasterWhisperScriptPath);
        Directory.CreateDirectory(Path.Combine(paths.AsrPythonEnvironment, "Lib", "site-packages", "faster_whisper-1.2.1.dist-info"));
    }

    protected static void CreateDiarizationRuntime(AppPaths paths)
    {
        Directory.CreateDirectory(Path.Combine(paths.DiarizationPythonEnvironment, "Lib", "site-packages", "diarize-0.1.2.dist-info"));
        Touch(Path.Combine(paths.DiarizationPythonEnvironment, "Lib", "site-packages", "silero_vad", "data", "silero_vad.jit"));
    }

    protected sealed class RecordingUpdateInstallerLauncher : IUpdateInstallerLauncher
    {
        public string? StartedInstallerPath { get; private set; }

        public UpdateInstallerLaunchResult Launch(string installerPath, string? expectedSha256 = null)
        {
            StartedInstallerPath = installerPath;
            return new UpdateInstallerLaunchResult(installerPath, DateTimeOffset.Now, "SHA256 verified download", false);
        }
    }
}
