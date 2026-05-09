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

        if (!resources.NvidiaGpuDetected)
        {
            return IsCapableCpuOnlyHost(totalMemoryGb, resources.LogicalProcessorCount)
                ? "lightweight"
                : "ultra_lightweight";
        }

        if (resources.MaxGpuMemoryGb is not null and < 6 ||
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

    private static bool IsCapableCpuOnlyHost(double? totalMemoryGb, int? logicalProcessorCount)
    {
        if (totalMemoryGb is null)
        {
            return false;
        }

        return totalMemoryGb >= 12 &&
            (logicalProcessorCount >= 6 ||
                (logicalProcessorCount is null && totalMemoryGb >= 16));
    }

    private static string BuildDetail(string presetId, SetupHostResources resources)
    {
        var reason = presetId switch
        {
            "ultra_lightweight" => "CPU/RAM または GPU 条件が控えめな環境として、Whisper base と Bonsai 8B の超軽量構成を推奨します。",
            "lightweight" => "GPUなしでも CPU/RAM に余裕があるため、Whisper small と Bonsai 8B の軽量構成を推奨します。",
            "high_accuracy" => "十分な VRAM と RAM が見込めるため、日本語精度を優先する高精度構成も選べます。",
            _ => "標準的な GPU/RAM が見込めるため、速度と品質のバランスを取る推奨構成を選びます。"
        };
        return $"{resources.Summary}。{reason}";
    }
}
