using System.IO;

namespace KoeNote.Cleanup;

public sealed class CleanupService(CleanupPaths paths)
{
    public IReadOnlyList<CleanupTarget> BuildTargets(CleanupPlan plan)
    {
        if (plan.RemoveAllData)
        {
            return
            [
                new("KoeNote roaming data", paths.UserRoot, true, true),
                new("KoeNote local data", paths.LocalRoot, true, true),
                new("KoeNote machine data", paths.MachineRoot, true, true)
            ];
        }

        return
        [
            new("一時ファイルとログ", paths.Logs, true, plan.RemoveLogs),
            new("一時モデルダウンロード", paths.ModelDownloads, true, plan.RemoveDownloads),
            new("任意の Python パッケージ", paths.PythonPackages, true, plan.RemoveDownloads),
            new("ダウンロード済みユーザーモデル", paths.UserModels, true, plan.RemoveUserModels),
            new("共有マシンモデル", paths.MachineModels, true, plan.RemoveMachineModels),
            new("ジョブフォルダー", paths.Jobs, true, plan.RemoveUserData),
            new("ジョブデータベース", paths.DatabasePath, false, plan.RemoveUserData),
            new("設定", paths.SettingsPath, false, plan.RemoveUserData),
            new("セットアップ状態", paths.SetupStatePath, false, plan.RemoveUserData),
            new("セットアップレポート", paths.SetupReportPath, false, plan.RemoveUserData)
        ];
    }

    public CleanupResult Execute(CleanupPlan plan, bool dryRun)
    {
        var actions = new List<CleanupActionResult>();

        foreach (var target in BuildTargets(plan))
        {
            if (!target.Remove)
            {
                actions.Add(new CleanupActionResult(target.Path, false, $"{target.Label}: 未選択"));
                continue;
            }

            if (!IsSafeKnownPath(target.Path))
            {
                actions.Add(new CleanupActionResult(target.Path, false, $"{target.Label}: 失敗: KoeNote のデータ領域外のパスです"));
                continue;
            }

            if (!Exists(target))
            {
                actions.Add(new CleanupActionResult(target.Path, false, $"{target.Label}: 既に存在しません"));
                continue;
            }

            if (dryRun)
            {
                actions.Add(new CleanupActionResult(target.Path, false, $"{target.Label}: プレビューのみ"));
                continue;
            }

            try
            {
                if (target.IsDirectory)
                {
                    Directory.Delete(target.Path, recursive: true);
                }
                else
                {
                    File.Delete(target.Path);
                }

                actions.Add(new CleanupActionResult(target.Path, true, $"{target.Label}: 削除しました"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                actions.Add(new CleanupActionResult(target.Path, false, $"{target.Label}: 失敗: {ex.Message}"));
            }
        }

        RemoveEmptyKoeNoteDataDirectories(dryRun, actions);
        return new CleanupResult(actions);
    }

    private static bool Exists(CleanupTarget target)
    {
        return target.IsDirectory ? Directory.Exists(target.Path) : File.Exists(target.Path);
    }

    private bool IsSafeKnownPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return IsUnder(fullPath, Path.Combine(paths.AppDataRoot, "KoeNote")) ||
            IsUnder(fullPath, Path.Combine(paths.LocalAppDataRoot, "KoeNote")) ||
            IsUnder(fullPath, Path.Combine(paths.ProgramDataRoot, "KoeNote"));
    }

    private static bool IsUnder(string path, string root)
    {
        var fullRoot = Path.GetFullPath(root);
        if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            fullRoot += Path.DirectorySeparatorChar;
        }

        return path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private void RemoveEmptyKoeNoteDataDirectories(bool dryRun, List<CleanupActionResult> actions)
    {
        foreach (var directory in new[]
        {
            Path.Combine(paths.LocalAppDataRoot, "KoeNote"),
            Path.Combine(paths.AppDataRoot, "KoeNote"),
            Path.Combine(paths.ProgramDataRoot, "KoeNote")
        })
        {
            try
            {
                if (dryRun || !Directory.Exists(directory) || Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    continue;
                }

                Directory.Delete(directory);
                actions.Add(new CleanupActionResult(directory, true, "空のデータフォルダーを削除しました"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                actions.Add(new CleanupActionResult(directory, false, $"失敗: {ex.Message}"));
            }
        }
    }
}
