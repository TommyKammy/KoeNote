using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Setup;

namespace KoeNote.App.Services.Diagnostics;

public sealed class GpuRuntimeDiagnosticService(
    AppPaths paths,
    ISetupHostResourceProbe? hostResourceProbe = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    private readonly ISetupHostResourceProbe _hostResourceProbe = hostResourceProbe ?? new WindowsSetupHostResourceProbe();

    public GpuRuntimeDiagnosticSnapshot BuildSnapshot()
    {
        var resources = _hostResourceProbe.GetResources();
        var llamaEnvironment = LlamaRuntimeEnvironment.Build(paths);
        return new GpuRuntimeDiagnosticSnapshot(
            DateTimeOffset.Now,
            resources,
            new AsrGpuRuntimeDiagnostic(
                paths.AsrRuntimeDirectory,
                paths.AsrCTranslate2RuntimeDirectory,
                paths.AsrCudaRuntimeMarkerPath,
                File.Exists(paths.AsrCudaRuntimeMarkerPath),
                AsrCudaRuntimeLayout.HasBundledRuntimeFiles(paths.AsrRuntimeDirectory),
                AsrCudaRuntimeLayout.HasNvidiaRuntimeFiles(paths.AsrCTranslate2RuntimeDirectory),
                AsrCudaRuntimeLayout.HasLegacyNvidiaRuntimeFiles(paths),
                AsrCudaRuntimeLayout.HasPackage(paths),
                AsrCudaRuntimeLayout.GetMissingPackageItems(paths)),
            new ReviewGpuRuntimeDiagnostic(
                paths.LlamaCompletionPath,
                File.Exists(paths.LlamaCompletionPath),
                paths.ReviewRuntimeDirectory,
                paths.CudaReviewRuntimeDirectory,
                paths.CudaReviewRuntimeMarkerPath,
                File.Exists(paths.CudaReviewRuntimeMarkerPath),
                CudaReviewRuntimeLayout.HasLegacyNvidiaRuntimeFiles(paths),
                CudaReviewRuntimeLayout.HasPackage(paths),
                CudaReviewRuntimeLayout.GetMissingPackageItems(paths)),
            new LlmRuntimeResolverDiagnostic(
                CudaReviewRuntimeLayout.HasPackage(paths) ? "cuda" : "cpu",
                llamaEnvironment is not null,
                llamaEnvironment is null
                    ? null
                    : llamaEnvironment.GetValueOrDefault(LlamaRuntimeEnvironment.CudaReviewRuntimeDirectoryVariable)));
    }

    public string BuildJson()
    {
        return JsonSerializer.Serialize(BuildSnapshot(), JsonOptions);
    }

    public static string FormatText(GpuRuntimeDiagnosticSnapshot snapshot)
    {
        var builder = new StringBuilder();
        AppendKeyValue(builder, "GeneratedAt", snapshot.GeneratedAt.ToString("o"));
        AppendKeyValue(builder, "NvidiaGpuDetected", snapshot.Host.NvidiaGpuDetected.ToString());
        AppendKeyValue(builder, "HostResources", snapshot.Host.Summary);

        builder.AppendLine();
        builder.AppendLine("### ASR GPU Runtime");
        AppendKeyValue(builder, "HasPackage", snapshot.Asr.HasPackage.ToString());
        AppendKeyValue(builder, "RuntimeDirectory", snapshot.Asr.RuntimeDirectory);
        AppendKeyValue(builder, "CTranslate2RuntimeDirectory", snapshot.Asr.CTranslate2RuntimeDirectory);
        AppendKeyValue(builder, "MarkerPath", snapshot.Asr.MarkerPath);
        AppendKeyValue(builder, "MarkerExists", snapshot.Asr.MarkerExists.ToString());
        AppendKeyValue(builder, "BundledRuntimeFilesReady", snapshot.Asr.BundledRuntimeFilesReady.ToString());
        AppendKeyValue(builder, "NvidiaRuntimeFilesReady", snapshot.Asr.NvidiaRuntimeFilesReady.ToString());
        AppendKeyValue(builder, "LegacyNvidiaRuntimeFilesPresent", snapshot.Asr.LegacyNvidiaRuntimeFilesPresent.ToString());
        AppendMissingItems(builder, snapshot.Asr.MissingItems);

        builder.AppendLine();
        builder.AppendLine("### Review GPU Runtime");
        AppendKeyValue(builder, "HasPackage", snapshot.Review.HasPackage.ToString());
        AppendKeyValue(builder, "LlamaCompletionPath", snapshot.Review.LlamaCompletionPath);
        AppendKeyValue(builder, "LlamaCompletionExists", snapshot.Review.LlamaCompletionExists.ToString());
        AppendKeyValue(builder, "ReviewRuntimeDirectory", snapshot.Review.ReviewRuntimeDirectory);
        AppendKeyValue(builder, "CudaRuntimeDirectory", snapshot.Review.CudaRuntimeDirectory);
        AppendKeyValue(builder, "MarkerPath", snapshot.Review.MarkerPath);
        AppendKeyValue(builder, "MarkerExists", snapshot.Review.MarkerExists.ToString());
        AppendKeyValue(builder, "LegacyNvidiaRuntimeFilesPresent", snapshot.Review.LegacyNvidiaRuntimeFilesPresent.ToString());
        AppendMissingItems(builder, snapshot.Review.MissingItems);

        builder.AppendLine();
        builder.AppendLine("### Runtime Resolver");
        AppendKeyValue(builder, "ReviewBackendMode", snapshot.Resolver.ReviewBackendMode);
        AppendKeyValue(builder, "LlamaRuntimeEnvironmentReady", snapshot.Resolver.LlamaRuntimeEnvironmentReady.ToString());
        AppendKeyValue(builder, "CudaReviewRuntimeDirectory", snapshot.Resolver.CudaReviewRuntimeDirectory ?? "(none)");
        return builder.ToString();
    }

    private static void AppendMissingItems(StringBuilder builder, IReadOnlyList<string> missingItems)
    {
        if (missingItems.Count == 0)
        {
            builder.AppendLine("MissingItems: (none)");
            return;
        }

        builder.AppendLine("MissingItems:");
        foreach (var item in missingItems)
        {
            builder.Append("  - ").AppendLine(item);
        }
    }

    private static void AppendKeyValue(StringBuilder builder, string key, string value)
    {
        builder.Append(key).Append(": ").AppendLine(value);
    }
}

public sealed record GpuRuntimeDiagnosticSnapshot(
    DateTimeOffset GeneratedAt,
    SetupHostResources Host,
    AsrGpuRuntimeDiagnostic Asr,
    ReviewGpuRuntimeDiagnostic Review,
    LlmRuntimeResolverDiagnostic Resolver);

public sealed record AsrGpuRuntimeDiagnostic(
    string RuntimeDirectory,
    string CTranslate2RuntimeDirectory,
    string MarkerPath,
    bool MarkerExists,
    bool BundledRuntimeFilesReady,
    bool NvidiaRuntimeFilesReady,
    bool LegacyNvidiaRuntimeFilesPresent,
    bool HasPackage,
    IReadOnlyList<string> MissingItems);

public sealed record ReviewGpuRuntimeDiagnostic(
    string LlamaCompletionPath,
    bool LlamaCompletionExists,
    string ReviewRuntimeDirectory,
    string CudaRuntimeDirectory,
    string MarkerPath,
    bool MarkerExists,
    bool LegacyNvidiaRuntimeFilesPresent,
    bool HasPackage,
    IReadOnlyList<string> MissingItems);

public sealed record LlmRuntimeResolverDiagnostic(
    string ReviewBackendMode,
    bool LlamaRuntimeEnvironmentReady,
    string? CudaReviewRuntimeDirectory);
