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
            ReadablePolishedStatus = "整文はまだ生成されていません。";
            return;
        }

        var derivative = _transcriptDerivativeRepository.ReadLatestSuccessful(
            SelectedJob.JobId,
            TranscriptDerivativeKinds.Polished);
        if (derivative is null)
        {
            ReadablePolishedContent = string.Empty;
            ReadablePolishedStatus = "整文はまだ生成されていません。";
            return;
        }

        if (!TranscriptPolishingOutputNormalizer.IsUsableDocument(derivative.Content, out var reason))
        {
            ReadablePolishedContent = string.Empty;
            ReadablePolishedStatus = $"整文の最新結果は破損しているため表示できません。再生成してください。({reason})";
            return;
        }

        ReadablePolishedContent = TranscriptPolishingOutputNormalizer.Normalize(derivative.Content);
        ReadablePolishedStatus = _transcriptDerivativeRepository.IsStale(derivative)
            ? "古い整文があります。再生成すると更新できます。"
            : $"整文済み: {derivative.UpdatedAt:yyyy/MM/dd HH:mm}";
    }

    private Task CopyReadablePolishedContentAsync()
    {
        if (!HasReadablePolishedContent)
        {
            return Task.CompletedTask;
        }

        System.Windows.Clipboard.SetText(ReadablePolishedContent);
        LatestLog = "整文をクリップボードにコピーしました。";
        return Task.CompletedTask;
    }

    private Task ExportSummaryMarkdownAsync()
    {
        return ExportSummaryAsync("Markdown (*.md)|*.md", "md", "markdown");
    }

    private void RefreshReadableDocumentBlocks()
    {
        ReadableDocumentBlocks.Clear();
        foreach (var block in ReadableDocumentBlockBuilder.Build(ReadablePolishedContent))
        {
            ReadableDocumentBlocks.Add(block);
        }
    }

    public bool RenameReadableDocumentSpeaker(string currentSpeaker, string replacementSpeaker)
    {
        if (SelectedJob is null ||
            string.IsNullOrWhiteSpace(currentSpeaker) ||
            string.IsNullOrWhiteSpace(replacementSpeaker) ||
            !HasReadablePolishedContent)
        {
            return false;
        }

        var normalizedCurrentSpeaker = currentSpeaker.Trim();
        var normalizedReplacementSpeaker = replacementSpeaker.Trim();
        var renameResult = ReadableDocumentSpeakerRenamer.Rename(
            ReadablePolishedContent,
            normalizedCurrentSpeaker,
            normalizedReplacementSpeaker);
        if (!renameResult.Changed)
        {
            return false;
        }

        var jobId = SelectedJob.JobId;
        var selectedSegmentId = SelectedSegment?.SegmentId;
        var speakerIds = FindSpeakerIdsByDisplayName(jobId, normalizedCurrentSpeaker);
        foreach (var speakerId in speakerIds)
        {
            _transcriptEditService.ApplySpeakerAlias(jobId, speakerId, normalizedReplacementSpeaker);
        }

        var derivative = _transcriptDerivativeRepository.ReadLatestSuccessful(jobId, TranscriptDerivativeKinds.Polished);
        if (derivative is not null)
        {
            var savedDerivative = _transcriptDerivativeRepository.Save(new TranscriptDerivativeSaveRequest(
                derivative.JobId,
                derivative.Kind,
                derivative.ContentFormat,
                renameResult.Content,
                derivative.SourceKind,
                _transcriptDerivativeRepository.ComputeCurrentRawTranscriptHash(jobId),
                derivative.SourceSegmentRange,
                derivative.SourceChunkIds,
                derivative.ModelId,
                derivative.PromptVersion,
                derivative.GenerationProfile,
                derivative.Status,
                derivative.ErrorMessage,
                derivative.DerivativeId));
            ReadablePolishedStatus = $"整文済み: {savedDerivative.UpdatedAt:yyyy/MM/dd HH:mm}";
        }

        ReadablePolishedContent = renameResult.Content;
        if (speakerIds.Count > 0)
        {
            ReloadSegmentsForSelectedJob(selectedSegmentId);
        }

        LatestLog = $"整文の話者名を更新しました: {normalizedCurrentSpeaker} -> {normalizedReplacementSpeaker}";
        return true;
    }

    private IReadOnlyList<string> FindSpeakerIdsByDisplayName(string jobId, string speakerDisplayName)
    {
        var speakerIds = Segments
            .Where(segment =>
                string.Equals(segment.Speaker, speakerDisplayName, StringComparison.Ordinal) ||
                string.Equals(segment.SpeakerId, speakerDisplayName, StringComparison.OrdinalIgnoreCase))
            .Select(static segment => segment.SpeakerId)
            .Where(static speakerId => !string.IsNullOrWhiteSpace(speakerId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (speakerIds.Count > 0)
        {
            return speakerIds;
        }

        return _transcriptSegmentRepository.ReadPreviews(jobId)
            .Where(segment =>
                string.Equals(segment.Speaker, speakerDisplayName, StringComparison.Ordinal) ||
                string.Equals(segment.SpeakerId, speakerDisplayName, StringComparison.OrdinalIgnoreCase))
            .Select(static segment => segment.SpeakerId)
            .Where(static speakerId => !string.IsNullOrWhiteSpace(speakerId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
