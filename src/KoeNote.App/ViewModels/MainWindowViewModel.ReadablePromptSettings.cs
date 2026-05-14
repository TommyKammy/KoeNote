using System.Collections.ObjectModel;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.ViewModels;

public sealed record ReadablePolishingPromptModelFamilyOption(string ModelFamily, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record ReadablePolishingPromptPresetOption(string PresetId, string DisplayName, string Description)
{
    public override string ToString() => DisplayName;
}

public sealed partial class MainWindowViewModel
{
    private bool _isLoadingReadablePolishingPromptSettings;
    private ReadablePolishingPromptModelFamilyOption? _selectedReadablePolishingPromptModelFamily;
    private ReadablePolishingPromptPresetOption? _selectedReadablePolishingPromptPreset;
    private string _readablePolishingPromptAdditionalInstruction = string.Empty;
    private bool _readablePolishingPromptUseCustomPrompt;
    private string _readablePolishingPromptCustomPrompt = string.Empty;
    private string _readablePolishingPromptPreviewText = string.Empty;
    private string _readablePolishingPromptSettingsStatus = "読みやすく整文プロンプト設定は未保存です。";

    public ObservableCollection<ReadablePolishingPromptModelFamilyOption> ReadablePolishingPromptModelFamilyOptions { get; } =
    [
        new(ReadablePolishingPromptModelFamilies.Gemma, "Gemma 4"),
        new(ReadablePolishingPromptModelFamilies.Bonsai, "Bonsai 8B"),
        new(ReadablePolishingPromptModelFamilies.LlmJp, "llm-jp")
    ];

    public ObservableCollection<ReadablePolishingPromptPresetOption> ReadablePolishingPromptPresetOptions { get; } =
    [
        new(ReadablePolishingPromptPresets.Standard, "標準", "既定の安全な読みやすさ補正を使います。"),
        new(ReadablePolishingPromptPresets.StrongPunctuation, "句読点を強める", "短い行の連続を自然な文に結合し、句読点を積極的に補います。"),
        new(ReadablePolishingPromptPresets.Faithful, "原文に忠実", "言い換えを抑え、句読点と軽い読みやすさ補正を中心にします。"),
        new(ReadablePolishingPromptPresets.MeetingMinutes, "会議録", "決定事項、担当者、日付、数値を発話どおりに扱う方針を強めます。"),
        new(ReadablePolishingPromptPresets.LectureSeminar, "講義・セミナー", "説明の細切れ行を自然な文章へつなぐ方針を強めます。")
    ];

    public ReadablePolishingPromptModelFamilyOption? SelectedReadablePolishingPromptModelFamily
    {
        get => _selectedReadablePolishingPromptModelFamily;
        set
        {
            if (SetField(ref _selectedReadablePolishingPromptModelFamily, value) && !_isLoadingReadablePolishingPromptSettings)
            {
                LoadReadablePolishingPromptSettingsForSelectedFamily();
            }
        }
    }

    public ReadablePolishingPromptPresetOption? SelectedReadablePolishingPromptPreset
    {
        get => _selectedReadablePolishingPromptPreset;
        set
        {
            if (SetField(ref _selectedReadablePolishingPromptPreset, value))
            {
                OnPropertyChanged(nameof(ReadablePolishingPromptPresetDescription));
            }
        }
    }

    public string ReadablePolishingPromptPresetDescription =>
        SelectedReadablePolishingPromptPreset?.Description ?? string.Empty;

    public string ReadablePolishingPromptAdditionalInstruction
    {
        get => _readablePolishingPromptAdditionalInstruction;
        set => SetField(ref _readablePolishingPromptAdditionalInstruction, value ?? string.Empty);
    }

    public bool ReadablePolishingPromptUseCustomPrompt
    {
        get => _readablePolishingPromptUseCustomPrompt;
        set
        {
            if (SetField(ref _readablePolishingPromptUseCustomPrompt, value))
            {
                OnPropertyChanged(nameof(IsReadablePolishingPromptPresetEnabled));
            }
        }
    }

    public bool IsReadablePolishingPromptPresetEnabled => !ReadablePolishingPromptUseCustomPrompt;

    public string ReadablePolishingPromptCustomPrompt
    {
        get => _readablePolishingPromptCustomPrompt;
        set => SetField(ref _readablePolishingPromptCustomPrompt, value ?? string.Empty);
    }

    public string ReadablePolishingPromptSettingsStatus
    {
        get => _readablePolishingPromptSettingsStatus;
        private set => SetField(ref _readablePolishingPromptSettingsStatus, value);
    }

    public string ReadablePolishingPromptPreviewText
    {
        get => _readablePolishingPromptPreviewText;
        private set => SetField(ref _readablePolishingPromptPreviewText, value);
    }

    public string ReadablePolishingPromptActiveModelFamilySummary
    {
        get
        {
            var option = FindReadablePolishingPromptModelFamilyOption(ResolveActiveReadablePolishingPromptModelFamily());
            return $"現在の整文モデルで使われる設定: {option.DisplayName}";
        }
    }

    private void InitializeReadablePolishingPromptSettings()
    {
        _isLoadingReadablePolishingPromptSettings = true;
        try
        {
            SelectedReadablePolishingPromptModelFamily = FindReadablePolishingPromptModelFamilyOption(
                ResolveActiveReadablePolishingPromptModelFamily());
        }
        finally
        {
            _isLoadingReadablePolishingPromptSettings = false;
        }

        LoadReadablePolishingPromptSettingsForSelectedFamily();
    }

    private void LoadReadablePolishingPromptSettingsForSelectedFamily()
    {
        var modelFamily = SelectedReadablePolishingPromptModelFamily?.ModelFamily ?? ReadablePolishingPromptModelFamilies.Gemma;
        var settings = _readablePolishingPromptSettingsRepository.Load(modelFamily).Settings;
        ApplyReadablePolishingPromptSettings(settings);
        ReadablePolishingPromptSettingsStatus = $"{SelectedReadablePolishingPromptModelFamily?.DisplayName ?? modelFamily} の設定を読み込みました。";
    }

    private void ApplyReadablePolishingPromptSettings(ReadablePolishingPromptSettings settings)
    {
        var normalized = settings.Normalize();
        _isLoadingReadablePolishingPromptSettings = true;
        try
        {
            SelectedReadablePolishingPromptPreset = ReadablePolishingPromptPresetOptions
                .FirstOrDefault(option => option.PresetId.Equals(normalized.PresetId, StringComparison.OrdinalIgnoreCase)) ??
                ReadablePolishingPromptPresetOptions.First(option => option.PresetId == ReadablePolishingPromptPresets.Standard);
            ReadablePolishingPromptAdditionalInstruction = normalized.AdditionalInstruction;
            ReadablePolishingPromptUseCustomPrompt = normalized.UseCustomPrompt;
            ReadablePolishingPromptCustomPrompt = normalized.CustomPrompt;
        }
        finally
        {
            _isLoadingReadablePolishingPromptSettings = false;
        }
    }

    private Task SaveReadablePolishingPromptSettings()
    {
        var modelFamily = SelectedReadablePolishingPromptModelFamily?.ModelFamily ?? ReadablePolishingPromptModelFamilies.Gemma;
        var presetId = SelectedReadablePolishingPromptPreset?.PresetId ?? ReadablePolishingPromptPresets.Standard;
        var normalizedFamily = ReadablePolishingPromptModelFamilies.Normalize(modelFamily);
        var settings = new ReadablePolishingPromptSettings(
            normalizedFamily,
            presetId,
            ReadablePolishingPromptAdditionalInstruction,
            ReadablePolishingPromptUseCustomPrompt,
            ReadablePolishingPromptCustomPrompt,
            ReadablePolishingPromptSettings.ResolveDefaultPromptTemplateId(normalizedFamily),
            TranscriptPolishingPromptBuilder.PromptVersion).Normalize();

        _readablePolishingPromptSettingsRepository.Save(settings);
        ApplyReadablePolishingPromptSettings(settings);
        ReadablePolishingPromptSettingsStatus = $"{SelectedReadablePolishingPromptModelFamily?.DisplayName ?? normalizedFamily} の設定を保存しました。";
        return Task.CompletedTask;
    }

    private Task ResetReadablePolishingPromptSettings()
    {
        var modelFamily = SelectedReadablePolishingPromptModelFamily?.ModelFamily ?? ReadablePolishingPromptModelFamilies.Gemma;
        _readablePolishingPromptSettingsRepository.Reset(modelFamily);
        LoadReadablePolishingPromptSettingsForSelectedFamily();
        ReadablePolishingPromptSettingsStatus = $"{SelectedReadablePolishingPromptModelFamily?.DisplayName ?? modelFamily} の設定を標準に戻しました。";
        return Task.CompletedTask;
    }

    private Task SelectActiveReadablePolishingPromptModelFamily()
    {
        var option = FindReadablePolishingPromptModelFamilyOption(ResolveActiveReadablePolishingPromptModelFamily());
        SelectedReadablePolishingPromptModelFamily = option;
        RefreshReadablePolishingPromptPreview();
        ReadablePolishingPromptSettingsStatus = $"{option.DisplayName} の設定を表示しています。";
        return Task.CompletedTask;
    }

    private void RefreshReadablePolishingPromptPreview()
    {
        var modelFamily = SelectedReadablePolishingPromptModelFamily?.ModelFamily ?? ReadablePolishingPromptModelFamilies.Gemma;
        var normalizedFamily = ReadablePolishingPromptModelFamilies.Normalize(modelFamily);
        var settings = new ReadablePolishingPromptSettings(
            normalizedFamily,
            SelectedReadablePolishingPromptPreset?.PresetId ?? ReadablePolishingPromptPresets.Standard,
            ReadablePolishingPromptAdditionalInstruction,
            ReadablePolishingPromptUseCustomPrompt,
            ReadablePolishingPromptCustomPrompt,
            ReadablePolishingPromptSettings.ResolveDefaultPromptTemplateId(normalizedFamily),
            TranscriptPolishingPromptBuilder.PromptVersion).Normalize();

        if (settings.UseCustomPrompt)
        {
            ReadablePolishingPromptPreviewText = $"""
                カスタムプロンプト:
                {settings.CustomPrompt}

                自動で追加される必須ルール:
                - 入力された speaker block だけを処理します。
                - 話者、タイムスタンプ、順序を保持します。
                - 入力にない事実や推測を追加しません。
                - BEGIN_BLOCK / END_BLOCK 形式を維持します。
                """;
            return;
        }

        var presetInstruction = ReadablePolishingPromptPresetInstructions.Resolve(settings);
        var presetText = string.IsNullOrWhiteSpace(presetInstruction)
            ? "標準プリセット: モデル別の既定テンプレートを使います。追加のプリセット指示はありません。"
            : presetInstruction.Trim();
        var additionalText = string.IsNullOrWhiteSpace(settings.AdditionalInstruction)
            ? "追加指示: なし"
            : $"追加指示:{Environment.NewLine}{settings.AdditionalInstruction}";

        ReadablePolishingPromptPreviewText = $"""
            対象モデル: {FindReadablePolishingPromptModelFamilyOption(settings.ModelFamily).DisplayName}
            プリセット: {SelectedReadablePolishingPromptPreset?.DisplayName ?? ReadablePolishingPromptPresets.Standard}
            テンプレート: {settings.PromptTemplateId}

            プリセット定義:
            {presetText}

            {additionalText}
            """;
    }

    private string ResolveActiveReadablePolishingPromptModelFamily()
    {
        try
        {
            var catalog = _modelCatalogService.LoadBuiltInCatalog();
            var modelId = ResolveEffectiveReviewModelId(catalog);
            var catalogItem = catalog.Models.FirstOrDefault(model =>
                model.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase));
            return ReadablePolishingPromptModelFamilies.ResolveForModel(modelId, catalogItem?.Family);
        }
        catch
        {
            return ReadablePolishingPromptModelFamilies.Gemma;
        }
    }

    private ReadablePolishingPromptModelFamilyOption FindReadablePolishingPromptModelFamilyOption(string modelFamily)
    {
        var normalized = ReadablePolishingPromptModelFamilies.Normalize(modelFamily);
        return ReadablePolishingPromptModelFamilyOptions.FirstOrDefault(option =>
            option.ModelFamily.Equals(normalized, StringComparison.OrdinalIgnoreCase)) ??
            ReadablePolishingPromptModelFamilyOptions.First(option =>
                option.ModelFamily == ReadablePolishingPromptModelFamilies.Gemma);
    }
}
