using Microsoft.Win32;

namespace KoeNote.Cleanup;

internal static class ArpMetadataWriter
{
    public static bool TryHandle(string[] args)
    {
        if (args.Length == 0 || !args[0].Equals("--set-arp-metadata", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (args.Length < 4)
        {
            return true;
        }

        var productCode = args[1].Trim();
        var installLocation = args[2].Trim();
        var displayIcon = args[3].Trim();
        var interactiveUninstall = $"MsiExec.exe /I{productCode}";
        var quietUninstall = $"MsiExec.exe /X{productCode} /qn";

        foreach (var key in OpenExistingUninstallKeys(productCode))
        {
            using (key)
            {
                key.SetValue("InstallLocation", installLocation, RegistryValueKind.String);
                key.SetValue("DisplayIcon", displayIcon, RegistryValueKind.String);
                key.SetValue("UninstallString", interactiveUninstall, RegistryValueKind.String);
                key.SetValue("QuietUninstallString", quietUninstall, RegistryValueKind.String);
            }
        }

        return true;
    }

    private static IEnumerable<RegistryKey> OpenExistingUninstallKeys(string productCode)
    {
        const string uninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        var relativeKey = $@"{uninstallRoot}\{productCode}";
        var wow6432RelativeKey = $@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{productCode}";

        foreach (var key in new[]
        {
            Registry.CurrentUser.OpenSubKey(relativeKey, writable: true),
            Registry.LocalMachine.OpenSubKey(relativeKey, writable: true),
            Registry.LocalMachine.OpenSubKey(wow6432RelativeKey, writable: true)
        })
        {
            if (key is not null)
            {
                yield return key;
            }
        }
    }
}
