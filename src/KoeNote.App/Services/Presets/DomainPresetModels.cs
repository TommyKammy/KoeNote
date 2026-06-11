using System.IO;
using System.Text.Json.Serialization;

namespace KoeNote.App.Services.Presets;

internal sealed record DomainPreset(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("preset_id")] string? PresetId,
    [property: JsonPropertyName("display_name")] string? DisplayNameValue,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("domain")] string? Domain,
    [property: JsonPropertyName("asr_context")] string? AsrContext,
    [property: JsonPropertyName("hotwords")] IReadOnlyList<string>? HotwordsValue,
    [property: JsonPropertyName("correction_memory")] IReadOnlyList<CorrectionMemoryPresetEntry>? CorrectionMemoryValue,
    [property: JsonPropertyName("speaker_aliases")] IReadOnlyList<SpeakerAliasPresetEntry>? SpeakerAliasesValue,
    [property: JsonPropertyName("review_guidelines")] IReadOnlyList<string>? ReviewGuidelinesValue)
{
    private const int SupportedSchemaVersion = 1;

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(DisplayNameValue)
        ? PresetId ?? Domain ?? "domain preset"
        : DisplayNameValue.Trim();

    [JsonIgnore]
    public string? NormalizedPresetId => string.IsNullOrWhiteSpace(PresetId)
        ? null
        : PresetId.Trim();

    [JsonIgnore]
    public string GuidelinePresetId => NormalizedPresetId ?? DisplayName;

    [JsonIgnore]
    public IReadOnlyList<string> Hotwords => HotwordsValue ?? [];

    [JsonIgnore]
    public IReadOnlyList<CorrectionMemoryPresetEntry> CorrectionMemory => CorrectionMemoryValue ?? [];

    [JsonIgnore]
    public IReadOnlyList<SpeakerAliasPresetEntry> SpeakerAliases => SpeakerAliasesValue ?? [];

    [JsonIgnore]
    public IReadOnlyList<string> ReviewGuidelines => ReviewGuidelinesValue ?? [];

    public void Validate()
    {
        if (SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidDataException($"未対応のプリセット schema_version です: {SchemaVersion}");
        }

        if (string.IsNullOrWhiteSpace(AsrContext) &&
            !Hotwords.Any(static hotword => !string.IsNullOrWhiteSpace(hotword)) &&
            !CorrectionMemory.Any(static entry => !string.IsNullOrWhiteSpace(entry.WrongText) && !string.IsNullOrWhiteSpace(entry.CorrectText)) &&
            !SpeakerAliases.Any(static entry => !string.IsNullOrWhiteSpace(entry.SpeakerId) && !string.IsNullOrWhiteSpace(entry.DisplayName)) &&
            !ReviewGuidelines.Any(static guideline => !string.IsNullOrWhiteSpace(guideline)))
        {
            throw new InvalidDataException("プリセットには asr_context、hotwords、correction_memory、speaker_aliases、review_guidelines のいずれかが必要です。");
        }
    }
}

internal sealed record CorrectionMemoryPresetEntry(
    [property: JsonPropertyName("wrong_text")] string? WrongText,
    [property: JsonPropertyName("correct_text")] string? CorrectText,
    [property: JsonPropertyName("issue_type")] string? IssueType,
    [property: JsonPropertyName("scope")] string? Scope);

internal sealed record SpeakerAliasPresetEntry(
    [property: JsonPropertyName("job_id")] string? JobId,
    [property: JsonPropertyName("speaker_id")] string? SpeakerId,
    [property: JsonPropertyName("display_name")] string? DisplayName);
