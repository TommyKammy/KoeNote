namespace KoeNote.App.Services.SystemStatus;

public sealed record StatusBarInfo(
    string DiskFreeSummary,
    string MemorySummary,
    string CpuSummary,
    string GpuUsageSummary);
