using System.Runtime.CompilerServices;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]

namespace KoeNote.App.UiIntegrationTests;

internal static class UiIntegrationTestAssembly
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // WPF's font cache initialization reads windir; some CI/test shells only expose SystemRoot.
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        if (string.IsNullOrWhiteSpace(systemRoot))
        {
            return;
        }

        SetIfMissing("windir", systemRoot);
        SetIfMissing("WINDIR", systemRoot);
    }

    private static void SetIfMissing(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
