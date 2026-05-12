namespace KoeNote.Cleanup;

public sealed record CleanupPlan(
    bool RemoveLogs,
    bool RemoveDownloads,
    bool RemoveUserModels,
    bool RemoveMachineModels,
    bool RemoveUserData,
    bool RemoveAllData = false)
{
    public static CleanupPlan AppOnly { get; } = new(
        RemoveLogs: false,
        RemoveDownloads: false,
        RemoveUserModels: false,
        RemoveMachineModels: false,
        RemoveUserData: false);

    public static CleanupPlan AllData { get; } = AppOnly with { RemoveAllData = true };
}

public sealed record CleanupTarget(string Label, string Path, bool IsDirectory, bool Remove);

public sealed record CleanupActionResult(string Path, bool Removed, string Message);

public sealed record CleanupResult(IReadOnlyList<CleanupActionResult> Actions)
{
    public bool Succeeded => Actions.All(static action =>
        action.Removed ||
        (!action.Message.StartsWith("Failed:", StringComparison.Ordinal) &&
            !action.Message.Contains("失敗", StringComparison.Ordinal)));

    public string ToConsoleText()
    {
        return string.Join(Environment.NewLine, Actions.Select(static action => $"{(action.Removed ? "削除" : "保持")}: {action.Path} ({action.Message})"));
    }
}
