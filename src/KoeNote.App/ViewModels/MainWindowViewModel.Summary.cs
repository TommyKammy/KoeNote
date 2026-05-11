using System.IO;
using KoeNote.App.Services.Transcript;
using Microsoft.Win32;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void LoadSummaryForSelectedJob()
    {
        if (SelectedJob is null)
        {
            IsSummaryStale = false;
            SummaryContent = string.Empty;
            SummaryStatus = "要約はまだありません。";
            return;
        }

        var summary = _transcriptDerivativeRepository.ReadLatestSuccessful(SelectedJob.JobId, TranscriptDerivativeKinds.Summary);
        if (summary is null)
        {
            IsSummaryStale = false;
            SummaryContent = string.Empty;
            SummaryStatus = "要約はまだありません。";
            return;
        }

        SummaryContent = summary.Content;
        IsSummaryStale = _transcriptDerivativeRepository.IsStale(summary);
        SummaryStatus = IsSummaryStale
            ? "古い要約があります。再実行すると更新できます。"
            : $"要約済み: {summary.UpdatedAt:yyyy/MM/dd HH:mm}";
    }

    private void LoadReadablePolishedForSelectedJob()
    {
        if (SelectedJob is null)
        {
            ReadablePolishedContent = string.Empty;
            ReadablePolishedStatus = "読みやすく整文はまだ生成されていません。";
            return;
        }

        var derivative = _transcriptDerivativeRepository.ReadLatestSuccessful(
            SelectedJob.JobId,
            TranscriptDerivativeKinds.Polished);
        if (derivative is null)
        {
            ReadablePolishedContent = string.Empty;
            ReadablePolishedStatus = "読みやすく整文はまだ生成されていません。";
            return;
        }

        if (!TranscriptPolishingOutputNormalizer.IsUsableDocument(derivative.Content, out var reason))
        {
            ReadablePolishedContent = string.Empty;
            ReadablePolishedStatus = $"読みやすく整文の最新結果は破損しているため表示できません。再生成してください。({reason})";
            return;
        }

        ReadablePolishedContent = TranscriptPolishingOutputNormalizer.Normalize(derivative.Content);
        ReadablePolishedStatus = _transcriptDerivativeRepository.IsStale(derivative)
            ? "古い読みやすく整文があります。再生成すると更新できます。"
            : $"読みやすく整文済み: {derivative.UpdatedAt:yyyy/MM/dd HH:mm}";
    }

    private Task CopyReadablePolishedContentAsync()
    {
        if (!HasReadablePolishedContent)
        {
            return Task.CompletedTask;
        }

        System.Windows.Clipboard.SetText(ReadablePolishedContent);
        LatestLog = "読みやすく整文をクリップボードにコピーしました。";
        return Task.CompletedTask;
    }

    private Task ExportSummaryMarkdownAsync()
    {
        return ExportSummaryAsync("Markdown (*.md)|*.md", "md", "markdown");
    }

    private Task ExportSummaryTextAsync()
    {
        return ExportSummaryAsync("Text document (*.txt)|*.txt", "txt", "text");
    }

    private Task ExportSummaryAsync(string filter, string extension, string formatName)
    {
        if (SelectedJob is null || string.IsNullOrWhiteSpace(SummaryContent))
        {
            return Task.CompletedTask;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export summary",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"{Path.GetFileNameWithoutExtension(SelectedJob.FileName)}.summary.{extension}",
            Filter = filter,
            FilterIndex = 1,
            DefaultExt = extension,
            InitialDirectory = GetOpenableExportFolder()
        };

        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        var expectedExtension = "." + extension;
        var outputPath = string.Equals(Path.GetExtension(dialog.FileName), expectedExtension, StringComparison.OrdinalIgnoreCase)
            ? dialog.FileName
            : Path.ChangeExtension(dialog.FileName, extension);
        File.WriteAllText(outputPath, SummaryContent);
        LastExportFolder = Path.GetDirectoryName(outputPath) ?? LastExportFolder;
        LatestLog = $"Exported summary {formatName}: {outputPath}";
        _jobLogRepository.AddEvent(SelectedJob.JobId, "export", "info", $"Exported summary {formatName} to {outputPath}");
        RefreshLogs();
        return Task.CompletedTask;
    }

    private bool CanExportSummaryMarkdown()
    {
        return SelectedJob is not null && !IsRunInProgress && !string.IsNullOrWhiteSpace(SummaryContent);
    }
}
