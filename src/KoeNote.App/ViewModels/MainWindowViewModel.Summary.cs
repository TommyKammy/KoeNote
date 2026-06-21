using System.Globalization;
using System.IO;
using KoeNote.App.Models;
using KoeNote.App.Services.Clipboard;
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

        var summary = _transcriptDerivativeRepository.ReadLatestDisplayable(SelectedJob.JobId, TranscriptDerivativeKinds.Summary);
        if (summary is null)
        {
            IsSummaryStale = false;
            SummaryContent = string.Empty;
            SummaryStatus = "要約はまだありません。";
            return;
        }

        SummaryContent = summary.Content;
        IsSummaryStale = string.Equals(summary.Status, TranscriptDerivativeStatuses.Stale, StringComparison.Ordinal) ||
            _transcriptDerivativeRepository.IsStale(summary);
        SummaryStatus = IsSummaryStale
            ? "古い要約があります。再実行すると更新できます。"
            : $"要約済み: {summary.UpdatedAt:yyyy/MM/dd HH:mm}";
    }

    private void LoadReadablePolishedForSelectedJob(bool confirmDiscardEdits = true)
    {
        if (confirmDiscardEdits && !ConfirmDiscardReadableDocumentEditsIfNeeded())
        {
            return;
        }

        ResetReadableDocumentEditState();
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

        var content = IsManualReadableDerivative(derivative)
            ? TranscriptPolishingOutputNormalizer.NormalizePreservedDocument(derivative.Content)
            : TranscriptPolishingOutputNormalizer.Normalize(derivative.Content);
        var isUsable = IsManualReadableDerivative(derivative)
            ? TranscriptPolishingOutputNormalizer.IsUsablePreservedDocument(content, out var reason)
            : TranscriptPolishingOutputNormalizer.IsUsableDocument(content, out reason);
        if (!isUsable)
        {
            ReadablePolishedContent = string.Empty;
            ReadablePolishedStatus = $"整文の最新結果は破損しているため表示できません。再生成してください。({reason})";
            return;
        }

        ReadablePolishedContent = content;
        ReadablePolishedStatus = _transcriptDerivativeRepository.IsStale(derivative)
            ? "古い整文があります。再生成すると更新できます。"
            : $"整文済み: {derivative.UpdatedAt:yyyy/MM/dd HH:mm}";
    }

    public bool BeginReadableDocumentEdit()
    {
        if (!CanEditReadableDocument)
        {
            return false;
        }

        _readableDocumentEditedTexts.Clear();
        _readableDocumentEditedTexts.AddRange(ReadableDocumentBlocks.Select(static block => block.Text));
        IsReadableDocumentEditMode = true;
        HasReadableDocumentUnsavedEdits = false;
        return true;
    }

    public void UpdateReadableDocumentEditedText(int blockIndex, string text)
    {
        if (!IsReadableDocumentEditMode ||
            blockIndex < 0 ||
            blockIndex >= _readableDocumentEditedTexts.Count)
        {
            return;
        }

        if (string.Equals(_readableDocumentEditedTexts[blockIndex], text, StringComparison.Ordinal))
        {
            return;
        }

        _readableDocumentEditedTexts[blockIndex] = text;
        HasReadableDocumentUnsavedEdits = true;
        ReadableDocumentEditRevision++;
    }

    public string GetReadableDocumentEditedText(int blockIndex, string fallback)
    {
        if (!IsReadableDocumentEditMode ||
            blockIndex < 0 ||
            blockIndex >= _readableDocumentEditedTexts.Count)
        {
            return fallback;
        }

        return _readableDocumentEditedTexts[blockIndex];
    }

    public void MarkReadableDocumentEditDirty()
    {
        if (IsReadableDocumentEditMode)
        {
            HasReadableDocumentUnsavedEdits = true;
        }
    }

    public bool DiscardReadableDocumentEdits()
    {
        if (!IsReadableDocumentEditMode)
        {
            return false;
        }

        ResetReadableDocumentEditState();
        return true;
    }

    public bool SaveReadableDocumentEdits()
    {
        return SaveReadableDocumentEdits(_readableDocumentEditedTexts);
    }

    public bool SaveReadableDocumentEdits(IReadOnlyList<string> editedBlockTexts)
    {
        ArgumentNullException.ThrowIfNull(editedBlockTexts);
        if (SelectedJob is null ||
            !IsReadableDocumentEditMode ||
            IsRunInProgress ||
            IsReadablePolishingInProgress ||
            editedBlockTexts.Count != ReadableDocumentBlocks.Count)
        {
            return false;
        }

        var content = ReadableDocumentBlockSerializer.Serialize(
            ReadableDocumentBlocks.ToArray(),
            editedBlockTexts);
        if (!TranscriptPolishingOutputNormalizer.IsUsablePreservedDocument(content, out var reason))
        {
            LatestLog = $"整文の保存に失敗しました。内容を確認してください。({reason})";
            return false;
        }

        if (string.Equals(content, ReadablePolishedContent, StringComparison.Ordinal))
        {
            ResetReadableDocumentEditState();
            return true;
        }

        var jobId = SelectedJob.JobId;
        var derivative = _transcriptDerivativeRepository.ReadLatestSuccessful(jobId, TranscriptDerivativeKinds.Polished);
        var wasDerivativeStale = derivative is not null && _transcriptDerivativeRepository.IsStale(derivative);
        var sourceTranscriptHash = derivative is not null && wasDerivativeStale
            ? derivative.SourceTranscriptHash
            : _transcriptDerivativeRepository.ComputeCurrentRawTranscriptHash(jobId);
        var savedDerivative = _transcriptDerivativeRepository.Save(new TranscriptDerivativeSaveRequest(
            jobId,
            TranscriptDerivativeKinds.Polished,
            derivative?.ContentFormat ?? TranscriptDerivativeFormats.PlainText,
            content,
            derivative?.SourceKind ?? TranscriptDerivativeSourceKinds.Raw,
            sourceTranscriptHash,
            derivative?.SourceSegmentRange,
            null,
            derivative?.ModelId,
            derivative?.PromptVersion ?? "manual-edit",
            "manual-edit",
            derivative?.Status ?? TranscriptDerivativeStatuses.Succeeded,
            derivative?.ErrorMessage));
        savedDerivative = SaveEditedReadableDerivativeChunks(savedDerivative, derivative, ReadableDocumentBlocks, editedBlockTexts);

        ReadablePolishedContent = content;
        ReadablePolishedStatus = wasDerivativeStale
            ? "古い整文があります。再生成すると更新できます。"
            : $"整文を手修正しました: {savedDerivative.UpdatedAt:yyyy/MM/dd HH:mm}";
        ResetReadableDocumentEditState();
        RefreshSummaryAfterReadableDocumentChanged(jobId);
        LatestLog = "整文の手修正を保存しました。";
        return true;
    }

    private TranscriptDerivative SaveEditedReadableDerivativeChunks(
        TranscriptDerivative savedDerivative,
        TranscriptDerivative? sourceDerivative,
        IReadOnlyList<ReadableDocumentBlock> readableBlocks,
        IReadOnlyList<string> editedBlockTexts)
    {
        if (sourceDerivative is null)
        {
            return savedDerivative;
        }

        var sourceChunks = _transcriptDerivativeRepository.ReadChunks(sourceDerivative.DerivativeId)
            .Where(static chunk => string.Equals(chunk.Status, TranscriptDerivativeStatuses.Succeeded, StringComparison.Ordinal))
            .OrderBy(static chunk => chunk.ChunkIndex)
            .ToArray();
        if (sourceChunks.Length == 0)
        {
            return savedDerivative;
        }

        var savedChunkIds = new List<string>(sourceChunks.Length);
        var readableBlockIndex = 0;
        foreach (var chunk in sourceChunks)
        {
            var chunkBlocks = ReadableDocumentBlockBuilder.Build(chunk.Content);
            if (chunkBlocks.Count == 0)
            {
                continue;
            }

            var chunkText = ReadableDocumentBlockSerializer.Serialize(
                chunkBlocks,
                chunkBlocks.Select(block =>
                {
                    return TryGetEditedReadableBlockText(block, readableBlocks, editedBlockTexts, ref readableBlockIndex, out var editedText)
                        ? editedText
                        : block.Text;
                }).ToArray());
            if (!TranscriptPolishingOutputNormalizer.IsUsablePreservedDocument(chunkText, out _))
            {
                continue;
            }

            var chunkId = BuildManualReadableChunkId(savedDerivative.DerivativeId, chunk.ChunkIndex);
            _transcriptDerivativeRepository.SaveChunk(new TranscriptDerivativeChunkSaveRequest(
                savedDerivative.DerivativeId,
                savedDerivative.JobId,
                chunk.ChunkIndex,
                chunk.SourceKind,
                chunk.SourceSegmentIds,
                chunk.SourceStartSeconds,
                chunk.SourceEndSeconds,
                savedDerivative.SourceTranscriptHash,
                savedDerivative.ContentFormat,
                chunkText,
                savedDerivative.ModelId,
                savedDerivative.PromptVersion,
                savedDerivative.GenerationProfile,
                chunk.Status,
                chunk.ErrorMessage,
                chunkId));
            savedChunkIds.Add(chunkId);
        }

        return savedChunkIds.Count == 0
            ? savedDerivative
            : _transcriptDerivativeRepository.Save(new TranscriptDerivativeSaveRequest(
                savedDerivative.JobId,
                savedDerivative.Kind,
                savedDerivative.ContentFormat,
                savedDerivative.Content,
                savedDerivative.SourceKind,
                savedDerivative.SourceTranscriptHash,
                savedDerivative.SourceSegmentRange,
                string.Join(",", savedChunkIds),
                savedDerivative.ModelId,
                savedDerivative.PromptVersion,
                savedDerivative.GenerationProfile,
                savedDerivative.Status,
                savedDerivative.ErrorMessage,
                savedDerivative.DerivativeId));
    }

    private static string BuildReadableBlockKey(ReadableDocumentBlock block)
    {
        if (!block.HasTimeRange)
        {
            return string.Empty;
        }

        return string.Join(
            "|",
            block.Speaker.Trim(),
            block.TimeRange.Trim(),
            block.StartSeconds?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
            block.EndSeconds?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private static bool TryGetEditedReadableBlockText(
        ReadableDocumentBlock sourceBlock,
        IReadOnlyList<ReadableDocumentBlock> blocks,
        IReadOnlyList<string> editedTexts,
        ref int blockIndex,
        out string editedText)
    {
        var sourceKey = BuildReadableBlockKey(sourceBlock);
        for (var index = Math.Max(0, blockIndex); index < blocks.Count && index < editedTexts.Count; index++)
        {
            if (IsSameReadableSourceBlock(sourceBlock, sourceKey, blocks[index]))
            {
                blockIndex = index + 1;
                editedText = editedTexts[index];
                return true;
            }
        }

        editedText = string.Empty;
        return false;
    }

    private static bool IsSameReadableSourceBlock(
        ReadableDocumentBlock sourceBlock,
        string sourceKey,
        ReadableDocumentBlock candidateBlock)
    {
        if (sourceKey.Length > 0)
        {
            return string.Equals(sourceKey, BuildReadableBlockKey(candidateBlock), StringComparison.Ordinal);
        }

        return !candidateBlock.HasTimeRange &&
            string.Equals(sourceBlock.Speaker, candidateBlock.Speaker, StringComparison.Ordinal) &&
            string.Equals(sourceBlock.TimeRange, candidateBlock.TimeRange, StringComparison.Ordinal) &&
            string.Equals(sourceBlock.Text, candidateBlock.Text, StringComparison.Ordinal);
    }

    private static string BuildManualReadableChunkId(string derivativeId, int chunkIndex)
    {
        return $"{derivativeId}-chunk-{chunkIndex:000}";
    }

    private void ResetReadableDocumentEditState()
    {
        _readableDocumentEditedTexts.Clear();
        HasReadableDocumentUnsavedEdits = false;
        IsReadableDocumentEditMode = false;
        ReadableDocumentEditRevision++;
    }

    private bool ConfirmDiscardReadableDocumentEditsIfNeeded()
    {
        if (!HasReadableDocumentUnsavedEdits)
        {
            return true;
        }

        var confirmed = ConfirmAction(
            "整文の未保存編集を破棄",
            "整文に未保存の編集があります。現在の操作を続けると編集内容は破棄されます。続けますか？");
        if (!confirmed)
        {
            LatestLog = "整文の未保存編集を保持しました。保存または破棄してから操作を続けてください。";
            return false;
        }

        return true;
    }

    public bool ConfirmDiscardReadableDocumentEditsForClose()
    {
        return ConfirmDiscardReadableDocumentEditsIfNeeded();
    }

    private bool ConfirmAndResetReadableDocumentEditsIfNeeded()
    {
        if (!ConfirmDiscardReadableDocumentEditsIfNeeded())
        {
            return false;
        }

        if (IsReadableDocumentEditMode)
        {
            ResetReadableDocumentEditState();
        }

        return true;
    }

    private static bool IsManualReadableDerivative(TranscriptDerivative derivative)
    {
        return string.Equals(derivative.GenerationProfile, "manual-edit", StringComparison.Ordinal);
    }

    private void UpdateReadableDocumentEditCommandStates()
    {
        if (BeginReadableDocumentEditCommand is RelayCommand beginCommand)
        {
            beginCommand.RaiseCanExecuteChanged();
        }

        if (SaveReadableDocumentEditCommand is RelayCommand saveCommand)
        {
            saveCommand.RaiseCanExecuteChanged();
        }

        if (DiscardReadableDocumentEditCommand is RelayCommand discardCommand)
        {
            discardCommand.RaiseCanExecuteChanged();
        }
    }

    private Task CopyReadablePolishedContentAsync()
    {
        if (!HasReadablePolishedContent)
        {
            return Task.CompletedTask;
        }

        var result = ClipboardHelper.TrySetText(ReadablePolishedContent);
        if (!result.IsSucceeded)
        {
            ReportClipboardCopyFailure("整文", result);
            return Task.CompletedTask;
        }

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
            IsRunInProgress ||
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
        var derivative = _transcriptDerivativeRepository.ReadLatestSuccessful(jobId, TranscriptDerivativeKinds.Polished);
        var wasDerivativeStale = derivative is not null && _transcriptDerivativeRepository.IsStale(derivative);
        var speakerIds = FindSpeakerIdsByDisplayName(jobId, normalizedCurrentSpeaker);
        foreach (var speakerId in speakerIds)
        {
            _transcriptEditService.ApplySpeakerAlias(
                jobId,
                speakerId,
                normalizedReplacementSpeaker,
                recordHistory: false);
        }

        if (derivative is not null)
        {
            var sourceTranscriptHash = wasDerivativeStale
                ? derivative.SourceTranscriptHash
                : _transcriptDerivativeRepository.ComputeCurrentRawTranscriptHash(jobId);
            var savedDerivative = _transcriptDerivativeRepository.Save(new TranscriptDerivativeSaveRequest(
                derivative.JobId,
                derivative.Kind,
                derivative.ContentFormat,
                renameResult.Content,
                derivative.SourceKind,
                sourceTranscriptHash,
                derivative.SourceSegmentRange,
                derivative.SourceChunkIds,
                derivative.ModelId,
                derivative.PromptVersion,
                derivative.GenerationProfile,
                derivative.Status,
                derivative.ErrorMessage,
                derivative.DerivativeId));
            SaveRenamedReadableDerivativeChunks(
                derivative.DerivativeId,
                normalizedCurrentSpeaker,
                normalizedReplacementSpeaker,
                sourceTranscriptHash);
            ReadablePolishedStatus = wasDerivativeStale
                ? "古い整文があります。再生成すると更新できます。"
                : $"整文済み: {savedDerivative.UpdatedAt:yyyy/MM/dd HH:mm}";
        }

        ReadablePolishedContent = renameResult.Content;
        if (speakerIds.Count > 0)
        {
            ReloadSegmentsForSelectedJob(selectedSegmentId);
        }

        RefreshSummaryAfterReadableDocumentChanged(jobId);
        LatestLog = $"整文の話者名を更新しました: {normalizedCurrentSpeaker} -> {normalizedReplacementSpeaker}";
        return true;
    }

    private void SaveRenamedReadableDerivativeChunks(
        string derivativeId,
        string currentSpeaker,
        string replacementSpeaker,
        string sourceTranscriptHash)
    {
        foreach (var chunk in _transcriptDerivativeRepository.ReadChunks(derivativeId))
        {
            var chunkRenameResult = ReadableDocumentSpeakerRenamer.Rename(
                chunk.Content,
                currentSpeaker,
                replacementSpeaker);
            if (!chunkRenameResult.Changed)
            {
                continue;
            }

            _transcriptDerivativeRepository.SaveChunk(new TranscriptDerivativeChunkSaveRequest(
                chunk.DerivativeId,
                chunk.JobId,
                chunk.ChunkIndex,
                chunk.SourceKind,
                chunk.SourceSegmentIds,
                chunk.SourceStartSeconds,
                chunk.SourceEndSeconds,
                sourceTranscriptHash,
                chunk.ContentFormat,
                chunkRenameResult.Content,
                chunk.ModelId,
                chunk.PromptVersion,
                chunk.GenerationProfile,
                chunk.Status,
                chunk.ErrorMessage,
                chunk.ChunkId));
        }
    }

    private void RefreshSummaryAfterReadableDocumentChanged(string jobId)
    {
        var summary = _transcriptDerivativeRepository.ReadLatestDisplayable(jobId, TranscriptDerivativeKinds.Summary);
        if (summary is not null &&
            string.Equals(summary.SourceKind, TranscriptDerivativeSourceKinds.Polished, StringComparison.Ordinal))
        {
            _transcriptDerivativeRepository.Save(new TranscriptDerivativeSaveRequest(
                summary.JobId,
                summary.Kind,
                summary.ContentFormat,
                summary.Content,
                summary.SourceKind,
                summary.SourceTranscriptHash,
                summary.SourceSegmentRange,
                summary.SourceChunkIds,
                summary.ModelId,
                summary.PromptVersion,
                summary.GenerationProfile,
                TranscriptDerivativeStatuses.Stale,
                summary.ErrorMessage,
                summary.DerivativeId));
        }

        LoadSummaryForSelectedJob();
    }

    private IReadOnlyList<string> FindSpeakerIdsByDisplayName(string jobId, string speakerDisplayName)
    {
        var speakerIds = Segments
            .Where(segment => string.Equals(segment.Speaker, speakerDisplayName, StringComparison.Ordinal))
            .Select(static segment => segment.SpeakerId)
            .Where(static speakerId => !string.IsNullOrWhiteSpace(speakerId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (speakerIds.Count > 0)
        {
            return speakerIds;
        }

        return _transcriptSegmentRepository.ReadPreviews(jobId)
            .Where(segment => string.Equals(segment.Speaker, speakerDisplayName, StringComparison.Ordinal))
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
