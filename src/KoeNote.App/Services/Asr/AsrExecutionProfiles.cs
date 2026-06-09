namespace KoeNote.App.Services.Asr;

public sealed record AsrExecutionProfile(
    string ProfileId,
    string DisplayName,
    string Device,
    string ComputeType,
    bool IsGpu,
    string Detail);

public static class AsrExecutionProfiles
{
    public const string CudaFloat16 = "cuda-float16";
    public const string CudaInt8Float16 = "cuda-int8-float16";
    public const string CudaFloat32 = "cuda-float32";
    public const string Auto = "auto";
    public const string CpuInt8 = "cpu-int8";

    public static readonly AsrExecutionProfile Default = new(
        CudaFloat16,
        "GPU CUDA float16",
        "cuda",
        "float16",
        true,
        "NVIDIA GPU向けの標準設定です。");

    public static IReadOnlyList<AsrExecutionProfile> All { get; } =
    [
        Default,
        new(
            CudaInt8Float16,
            "GPU CUDA int8_float16",
            "cuda",
            "int8_float16",
            true,
            "GPU負荷を下げた再試行向け設定です。"),
        new(
            CudaFloat32,
            "GPU CUDA float32",
            "cuda",
            "float32",
            true,
            "互換性優先のGPU設定です。"),
        new(
            Auto,
            "自動",
            "auto",
            "auto",
            false,
            "faster-whisperに実行環境の自動判定を任せます。"),
        new(
            CpuInt8,
            "CPU int8",
            "cpu",
            "int8",
            false,
            "GPUを使わない明示的なCPU設定です。")
    ];

    public static AsrExecutionProfile Resolve(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return Default;
        }

        return All.FirstOrDefault(profile =>
            profile.ProfileId.Equals(profileId.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Default;
    }

    public static string Normalize(string? profileId)
    {
        return Resolve(profileId).ProfileId;
    }

    public static IReadOnlyList<AsrExecutionProfile> BuildNativeCrashRetryLadder(string? profileId)
    {
        var primary = Resolve(profileId);
        if (!primary.IsGpu)
        {
            return [primary];
        }

        var retry = Resolve(CudaInt8Float16);
        return primary.ProfileId.Equals(retry.ProfileId, StringComparison.OrdinalIgnoreCase)
            ? [primary]
            : [primary, retry];
    }
}
