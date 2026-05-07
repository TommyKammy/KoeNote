using KoeNote.App.Services;
using KoeNote.App.Services.Diarization;

namespace KoeNote.App.Tests;

public sealed class PythonRuntimeResolverTests
{
    [Theory]
    [InlineData(3, 9, true)]
    [InlineData(3, 11, true)]
    [InlineData(3, 12, true)]
    [InlineData(3, 8, false)]
    [InlineData(3, 13, false)]
    [InlineData(3, 14, false)]
    public void IsSupportedVersion_AllowsOnlyTorchCompatiblePythonRange(
        int major,
        int minor,
        bool expected)
    {
        Assert.Equal(expected, PythonRuntimeResolver.IsSupportedVersion(new Version(major, minor)));
    }

    [Fact]
    public void AppPaths_ProvidesManagedDiarizationPythonLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);

        Assert.EndsWith(Path.Combine("KoeNote", "python-envs", "diarization"), paths.DiarizationPythonEnvironment);
        Assert.EndsWith(Path.Combine("KoeNote", "python-envs", "diarization", "Scripts", "python.exe"), paths.DiarizationPythonPath);
    }

    [Fact]
    public void DiarizationRuntimeLayout_DetectsManagedVenvPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        Directory.CreateDirectory(Path.Combine(paths.DiarizationPythonEnvironment, "Lib", "site-packages", "diarize"));

        Assert.True(DiarizationRuntimeLayout.HasManagedPackage(paths));
        Assert.True(DiarizationRuntimeLayout.HasPackage(paths));
    }
}
