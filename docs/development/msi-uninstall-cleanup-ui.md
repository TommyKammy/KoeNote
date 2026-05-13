# MSI uninstall cleanup UI migration

## Problem

Windows Settings starts KoeNote uninstall through MSI with a basic UI level. Launching `KoeNoteCleanup.exe` as a separate interactive custom action can place the cleanup window behind the standard uninstaller, which makes the flow feel disconnected and easy to miss.

## Direction

The MSI must own the uninstall choice UI. The standalone cleanup window remains useful from the Start Menu shortcut, but MSI uninstall should not launch it as a separate process.

The MSI uninstall choice should offer:

- `アプリのみ削除`
- `アプリと KoeNote 関連データをすべて削除`

The interactive UI uses `KOENOTE_UNINSTALL_MODE=ALL` for the all-data option. Quiet uninstall can request the same cleanup with `KOENOTE_CLEANUP_ALL_DATA=1`.

## Execution Policy

- App-only uninstall removes the MSI payload and does not run a cleanup custom action.
- All-data uninstall runs `KoeNoteCleanup.exe --quiet --all` from a custom action.
- Quiet uninstall remains app-only by default.
- Quiet all-data uninstall requires `KOENOTE_CLEANUP_ALL_DATA=1`.
- `%USERPROFILE%\Documents\KoeNote\Exports` remains outside the cleanup roots.

For isolated MSI smoke tests, `KOENOTE_CLEANUP_APPDATA_ROOT`, `KOENOTE_CLEANUP_LOCALAPPDATA_ROOT`, and `KOENOTE_CLEANUP_PROGRAMDATA_ROOT` can override cleanup roots. These properties are intentionally undocumented for end users.

## Phase 2 State

`RunKoeNoteCleanupUi` is removed from the MSI execute sequence. This avoids the background-window behavior while preserving `KoeNoteCleanup.exe --quiet --all` as the deletion engine for the MSI-integrated choice.

The MSI now includes `KoeNoteCleanupChoiceDlg` in the maintenance/remove UI flow. Windows Apps should enter the maintenance UI through an ARP `UninstallString` of `MsiExec.exe /I[ProductCode]`, while `QuietUninstallString` keeps using `/X[ProductCode] /qn`.

Windows Installer's automatic ARP entry is hidden with `ARPSYSTEMCOMPONENT=1`, and KoeNote writes its own HKCU Apps entry. This is needed because the automatic MSI entry uses `/X[ProductCode]` and can bypass the maintenance UI.

## Next Phase

Smoke test the generated MSI from Windows Settings and verify:

- The maintenance/remove flow shows `KoeNoteCleanupChoiceDlg` in the foreground.
- App-only remove keeps KoeNote data.
- All-data remove sets `KOENOTE_UNINSTALL_MODE=ALL` and removes KoeNote data roots.
- Quiet uninstall remains app-only unless `KOENOTE_CLEANUP_ALL_DATA=1` is passed.
