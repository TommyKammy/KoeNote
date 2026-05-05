using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private Task ImportDomainPresetAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "専門領域プリセットを選択",
            Filter = "KoeNote プリセット (*.json)|*.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        ImportDomainPresetFromFile(dialog.FileName);
        return Task.CompletedTask;
    }

    public void ImportDomainPresetFromFile(string presetPath)
    {
        try
        {
            SaveAsrSettings();
            var result = _domainPresetImportService.Import(presetPath, SelectedJob?.JobId);
            var settings = _asrSettingsRepository.Load();
            AsrContextText = settings.ContextText;
            AsrHotwordsText = settings.HotwordsText;
            ReloadSegmentsForSelectedJob(SelectedSegment?.SegmentId);
            LatestLog = result.Summary;
            RefreshDomainPresetHistory();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        {
            LatestLog = $"Preset import failed: {exception.Message}";
        }
    }

    private Task ClearSelectedDomainPresetAsync()
    {
        if (SelectedDomainPresetImport is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            SaveAsrSettings();
            var result = _domainPresetImportService.ClearImport(SelectedDomainPresetImport.ImportId);
            var settings = _asrSettingsRepository.Load();
            AsrContextText = settings.ContextText;
            AsrHotwordsText = settings.HotwordsText;
            ReloadSegmentsForSelectedJob(SelectedSegment?.SegmentId);
            RefreshDomainPresetHistory();
            LatestLog = result.Summary;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        {
            LatestLog = $"Preset clear failed: {exception.Message}";
        }

        return Task.CompletedTask;
    }

    private void RefreshDomainPresetHistory()
    {
        var selectedImportId = SelectedDomainPresetImport?.ImportId;
        DomainPresetImports.Clear();
        foreach (var item in _domainPresetImportService.LoadHistory())
        {
            DomainPresetImports.Add(item);
        }

        SelectedDomainPresetImport = DomainPresetImports.FirstOrDefault(item => item.ImportId == selectedImportId)
            ?? DomainPresetImports.FirstOrDefault(static item => item.DeactivatedAt is null)
            ?? DomainPresetImports.FirstOrDefault();
    }
}
