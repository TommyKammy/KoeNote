using KoeNote.App.Services.Models;

namespace KoeNote.App.Services.Setup;

internal sealed class SetupPresetRecommendationService(
    ModelCatalogService modelCatalogService,
    ISetupHostResourceProbe hostResourceProbe)
{
    public SetupPresetRecommendation GetRecommendation()
    {
        var resources = hostResourceProbe.GetResources();
        var presetId = ChoosePresetId(resources);
        var preset = FindPreset(presetId) ?? FindPreset("recommended") ?? BuildFallbackPreset(presetId);
        return new SetupPresetRecommendation(
            preset.PresetId,
            preset.DisplayName,
            BuildDetail(preset.PresetId, resources),
            resources);
    }

    private IReadOnlyList<ModelQualityPreset> GetModelPresets()
    {
        return modelCatalogService.LoadBuiltInCatalog().Presets ?? [];
    }

    private ModelQualityPreset? FindPreset(string presetId)
    {
        return GetModelPresets()
            .FirstOrDefault(preset => preset.PresetId.Equals(presetId, StringComparison.OrdinalIgnoreCase));
    }

    private static ModelQualityPreset BuildFallbackPreset(string presetId)
    {
        return new ModelQualityPreset(
            presetId,
            presetId,
            presetId,
            "Model presets are not available.",
            "faster-whisper-large-v3-turbo",
            "llm-jp-4-8b-thinking-q4-k-m",
            [],
            []);
    }

    private static string ChoosePresetId(SetupHostResources resources)
    {
        var totalMemoryGb = resources.TotalMemoryBytes is { } bytes
            ? bytes / 1024d / 1024d / 1024d
            : (double?)null;

        if (!resources.NvidiaGpuDetected ||
            resources.MaxGpuMemoryGb is < 6 ||
            totalMemoryGb is < 12)
        {
            return "ultra_lightweight";
        }

        if (resources.MaxGpuMemoryGb >= 8 && totalMemoryGb >= 24)
        {
            return "high_accuracy";
        }

        return "recommended";
    }

    private static string BuildDetail(string presetId, SetupHostResources resources)
    {
        var reason = presetId switch
        {
            "lightweight" => "GPU/VRAM または RAM が控えめな環境として、Whisper base と Bonsai 8B の軽量構成を推奨します。",
            "high_accuracy" => "十分な VRAM と RAM が見込めるため、日本語精度を優先する高精度構成も選べます。",
            _ => "標準的な GPU/RAM が見込めるため、速度と品質のバランスを取る推奨構成を選びます。"
        };
        return $"{resources.Summary}。{reason}";
    }
}
