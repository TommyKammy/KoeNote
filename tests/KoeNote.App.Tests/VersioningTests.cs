using System.Xml.Linq;

namespace KoeNote.App.Tests;

public sealed class VersioningTests
{
    [Fact]
    public void DirectoryBuildProps_DefinesSingleVersionPrefix()
    {
        var repoRoot = FindRepoRoot();
        var props = XDocument.Load(Path.Combine(repoRoot, "Directory.Build.props"));

        var versionPrefix = props.Descendants("VersionPrefix").Single().Value;

        Assert.Matches(@"^\d+\.\d+\.\d+$", versionPrefix);
    }

    [Fact]
    public void InstallerProject_DefaultsProductVersionToVersionPrefix()
    {
        var repoRoot = FindRepoRoot();
        var wixProject = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.Installer", "KoeNote.Installer.wixproj"));

        Assert.Contains("<ProductVersion Condition=\"'$(ProductVersion)' == ''\">$(VersionPrefix)</ProductVersion>", wixProject);
    }

    [Fact]
    public void BuildMsiScript_DoesNotHardcodeProductVersionDefault()
    {
        var repoRoot = FindRepoRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "scripts", "phase13", "Build-KoeNoteMsi.ps1"));

        Assert.Contains("[string]$ProductVersion = \"\"", script);
        Assert.Contains("Directory.Build.props", script);
        Assert.Contains("MSI-compatible numeric x.y.z format", script);
        Assert.DoesNotContain("[string]$ProductVersion = \"0.13.0\"", script);
    }

    [Fact]
    public void BuildMsiScript_UsesVersionedReleaseArtifactName()
    {
        var repoRoot = FindRepoRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "scripts", "phase13", "Build-KoeNoteMsi.ps1"));

        Assert.Contains("$OutputName = \"$ProductName-v$ProductVersion-$RuntimeIdentifier\"", script);
        Assert.Contains("$msiArtifactPath = Join-Path $installerOut \"$OutputName.msi\"", script);
        Assert.DoesNotContain("Sort-Object LastWriteTime -Descending", script);
    }

    [Fact]
    public void BuildMsiScript_HasCodeSigningHook()
    {
        var repoRoot = FindRepoRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "scripts", "phase13", "Build-KoeNoteMsi.ps1"));

        Assert.Contains("KOENOTE_SIGNTOOL_PATH", script);
        Assert.Contains("KOENOTE_SIGN_CERT_SHA1", script);
        Assert.Contains("Invoke-CodeSigningIfConfigured", script);
        Assert.Contains("[switch]$RequireCodeSigning", script);
        Assert.Contains("Code signing is required", script);
        Assert.Contains("code_signing_failed", script);
    }

    [Fact]
    public void BuildMsiScript_WritesUpdateLog()
    {
        var repoRoot = FindRepoRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "scripts", "phase13", "Build-KoeNoteMsi.ps1"));

        Assert.Contains("artifacts\\logs\\updates", script);
        Assert.Contains("release_build_started", script);
        Assert.Contains("release_artifact_completed", script);
        Assert.Contains("UpdateLogPath", script);
    }

    [Fact]
    public void BuildMsiScript_WritesReleaseManifest()
    {
        var repoRoot = FindRepoRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "scripts", "phase13", "Build-KoeNoteMsi.ps1"));

        Assert.Contains("$OutputName.release-manifest.json", script);
        Assert.Contains("schema_version = 1", script);
        Assert.Contains("signing = [ordered]@", script);
        Assert.Contains("release_manifest_written", script);
        Assert.Contains("ManifestPath", script);
    }

    [Fact]
    public void BuildMsiScript_RequiresBundledPythonRuntimeForRelease()
    {
        var repoRoot = FindRepoRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "scripts", "phase13", "Build-KoeNoteMsi.ps1"));

        Assert.Contains("-RequireBundledPythonRuntime", script);
        Assert.Contains("bundled_python_runtime_verified", script);
        Assert.Contains("bundled_python_runtime = [ordered]@", script);
        Assert.Contains("Bundled Python runtime is required for release MSI builds", script);
    }

    [Fact]
    public void ReleaseVerificationScript_ValidatesCiReleaseArtifacts()
    {
        var repoRoot = FindRepoRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "scripts", "phase13", "Test-KoeNoteReleaseVerification.ps1"));

        Assert.Contains("Test-KoeNoteArtifactIntegrity.ps1", script);
        Assert.Contains("Test-KoeNoteReleasePayloadGuard.ps1", script);
        Assert.Contains("FullyQualifiedName~VersioningTests", script);
        Assert.Contains("release-manifest.json", script);
        Assert.Contains("signing.status", script);
        Assert.Contains("Manifest SHA256 path does not match", script);
        Assert.Contains("Manifest update log path is missing", script);
        Assert.Contains("Manifest requires signing", script);
        Assert.Contains("Bundled Python runtime is required", script);
        Assert.Contains("Release manifest is missing bundled_python_runtime metadata", script);
        Assert.Contains("bundled_python_runtime.required", script);
        Assert.Contains("gpu_ready_runtime", script);
        Assert.Contains("nvidia_redistributables_included", script);
        Assert.Contains("redistrib_12.9.0.json", script);
        Assert.Contains("redistrib_9.22.0.json", script);
        Assert.DoesNotContain("koenote-cuda-asr-runtime.zip", script);
        Assert.DoesNotContain("koenote-cuda-review-runtime.zip", script);
        Assert.DoesNotContain("Required CUDA release asset is missing or unreachable", script);
    }

    [Fact]
    public void BuildMsiScript_RequiresReviewRuntimeForRelease()
    {
        var repoRoot = FindRepoRoot();
        var buildScript = File.ReadAllText(Path.Combine(repoRoot, "scripts", "phase13", "Build-KoeNoteMsi.ps1"));
        var verificationScript = File.ReadAllText(Path.Combine(repoRoot, "scripts", "phase13", "Test-KoeNoteReleaseVerification.ps1"));
        var publishScript = File.ReadAllText(Path.Combine(repoRoot, "scripts", "phase10", "Publish-KoeNote.ps1"));
        var ternaryRuntimeService = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "Services", "Review", "TernaryReviewRuntimeService.cs"));

        Assert.Contains("-RequireReviewRuntime", buildScript);
        Assert.Contains("-RequireGpuReadyRuntime", buildScript);
        Assert.Contains("review_runtime_verified", buildScript);
        Assert.Contains("review_gpu_bridge_verified", buildScript);
        Assert.Contains("asr_gpu_runtime_verified", buildScript);
        Assert.Contains("review_runtime = [ordered]@", buildScript);
        Assert.Contains("gpu_ready_runtime = [ordered]@", buildScript);
        Assert.Contains("Review runtime is required for release MSI builds", buildScript);
        Assert.Contains("Review runtime is required but missing from publish output", verificationScript);
        Assert.Contains("Review GPU bridge is required but missing from publish output", verificationScript);
        Assert.Contains("ASR GPU-ready runtime is required but missing from publish output", verificationScript);
        Assert.Contains("Release manifest is missing review_runtime metadata", verificationScript);
        Assert.Contains("Release manifest is missing gpu_ready_runtime metadata", verificationScript);
        Assert.Contains("review_runtime.required", verificationScript);
        Assert.DoesNotContain("Ternary review runtime is required for release MSI builds", buildScript);
        Assert.Contains("ternary_review_runtime_skipped", buildScript);
        Assert.Contains("ternary_review_runtime = [ordered]@", buildScript);
        Assert.Contains("required = $false", buildScript);
        Assert.Contains("present = [bool]$ternaryReviewRuntimePresent", buildScript);
        Assert.Contains("ternaryReviewRuntimeTag = \"prism-b8846-d104cf1\"", buildScript);
        Assert.Contains("source_url = $ternaryReviewRuntimeSourceUrl", buildScript);
        Assert.Contains("Release manifest is missing ternary_review_runtime metadata", verificationScript);
        Assert.Contains("ternary_review_runtime.required", verificationScript);
        Assert.Contains("required as false", verificationScript);
        Assert.Contains("ternary_review_runtime.present", verificationScript);
        Assert.Contains("expectedTernaryReviewRuntimeTag = \"prism-b8846-d104cf1\"", verificationScript);
        Assert.Contains("ternary_review_runtime.tag", verificationScript);
        Assert.Contains("ternary_review_runtime.source_url", verificationScript);
        Assert.Contains("prism-b8846-d104cf1", ternaryRuntimeService);
        Assert.Contains("[switch]$RequireReviewRuntime", publishScript);
        Assert.Contains("[switch]$RequireGpuReadyRuntime", publishScript);
        Assert.Contains("[switch]$IncludeTernaryReviewRuntime", publishScript);
    }

    [Fact]
    public void ReleasePayloadGuard_BlocksNvidiaRedistributablesAndHostPythonPackages()
    {
        var repoRoot = FindRepoRoot();
        var guardScript = File.ReadAllText(Path.Combine(repoRoot, "scripts", "phase13", "Test-KoeNoteReleasePayloadGuard.ps1"));
        var buildScript = File.ReadAllText(Path.Combine(repoRoot, "scripts", "phase13", "Build-KoeNoteMsi.ps1"));
        var verificationScript = File.ReadAllText(Path.Combine(repoRoot, "scripts", "phase13", "Test-KoeNoteReleaseVerification.ps1"));

        Assert.Contains("cublas*.dll", guardScript);
        Assert.Contains("cudart*.dll", guardScript);
        Assert.Contains("cudnn*.dll", guardScript);
        Assert.Contains("NVIDIA redistributable runtime file is not allowed", guardScript);
        Assert.Contains("\"artifact_tool_v2\"", guardScript);
        Assert.Contains("\"pandas\"", guardScript);
        Assert.Contains("\"numpy\"", guardScript);
        Assert.Contains("\"lxml\"", guardScript);
        Assert.Contains("\"PIL\"", guardScript);
        Assert.Contains("\"openpyxl\"", guardScript);
        Assert.Contains("\"pdf2image\"", guardScript);
        Assert.Contains("\"pypdf\"", guardScript);
        Assert.Contains("\"reportlab\"", guardScript);
        Assert.Contains("MaxReviewRuntimeMB = 700", guardScript);
        Assert.Contains("MaxAsrRuntimeMB = 180", guardScript);
        Assert.Contains("MaxBundledPythonMB = 120", guardScript);
        Assert.Contains("Release payload guard failed", guardScript);
        Assert.Contains("release_payload_guard_verified", buildScript);
        Assert.Contains("Test-KoeNoteReleasePayloadGuard.ps1", buildScript);
        Assert.Contains("Test-KoeNoteReleasePayloadGuard.ps1", verificationScript);
    }

    [Fact]
    public void PublishScript_FiltersNormalReleaseRuntimePayload()
    {
        var repoRoot = FindRepoRoot();
        var publishScript = File.ReadAllText(Path.Combine(repoRoot, "scripts", "phase10", "Publish-KoeNote.ps1"));
        var appProject = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.App", "KoeNote.App.csproj"));

        Assert.Contains("Copy-FilteredDirectory", publishScript);
        Assert.Contains("IncludeTernaryReviewRuntime", publishScript);
        Assert.Contains("RequireGpuReadyRuntime", publishScript);
        Assert.Contains("Lib\\site-packages\\artifact_tool_v2*", publishScript);
        Assert.Contains("Lib\\site-packages\\pandas*", publishScript);
        Assert.Contains("Lib\\site-packages\\numpy*", publishScript);
        Assert.Contains("Lib\\site-packages\\lxml*", publishScript);
        Assert.Contains("ggml-cuda*.dll", publishScript);
        Assert.Contains("cublas*.dll", publishScript);
        Assert.Contains("cudart*.dll", publishScript);
        Assert.Contains("cudnn*.dll", publishScript);
        Assert.Contains("crispasr*.exe", publishScript);
        Assert.Contains("whisper.dll", publishScript);
        Assert.Contains("<KoeNoteIncludeRuntimeToolsInProjectPublish>false</KoeNoteIncludeRuntimeToolsInProjectPublish>", appProject);
        Assert.Contains("Condition=\"Exists('..\\..\\tools\\ffmpeg.exe')\"", appProject);
        Assert.Contains("'$(KoeNoteIncludeRuntimeToolsInProjectPublish)' == 'true' And Exists('..\\..\\tools\\python')", appProject);
        Assert.Contains("'$(KoeNoteIncludeRuntimeToolsInProjectPublish)' == 'true' And Exists('..\\..\\tools\\review')", appProject);
        Assert.Contains("'$(KoeNoteIncludeRuntimeToolsInProjectPublish)' == 'true' And Exists('..\\..\\tools\\review-ternary')", appProject);
    }

    [Fact]
    public void LatestJsonScript_CreatesDistributionManifest()
    {
        var repoRoot = FindRepoRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "scripts", "phase13", "New-KoeNoteLatestJson.ps1"));

        Assert.Contains("ReleaseManifestPath", script);
        Assert.Contains("BaseDownloadUrl", script);
        Assert.Contains("latest.json", script);
        Assert.Contains("msi_url", script);
        Assert.Contains("sha256_url", script);
        Assert.Contains("mandatory", script);
        Assert.Contains("BaseDownloadUrl must be an absolute HTTPS URL", script);
        Assert.Contains("ReleaseNotesUrl must be an absolute HTTPS URL", script);
    }

    [Fact]
    public void Installer_ClosesRunningKoeNoteBeforeUpdate()
    {
        var repoRoot = FindRepoRoot();
        var productWxs = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.Installer", "Product.wxs"));

        Assert.Contains("util:CloseApplication", productWxs);
        Assert.Contains("Target=\"KoeNote.App.exe\"", productWxs);
        Assert.Contains("PromptToContinue=\"no\"", productWxs);
    }

    [Fact]
    public void Installer_RequiresExplicitPropertyForQuietAllDataCleanup()
    {
        var repoRoot = FindRepoRoot();
        var productWxs = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.Installer", "Product.wxs"));
        var wixProject = File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.Installer", "KoeNote.Installer.wixproj"));
        var buildScript = File.ReadAllText(Path.Combine(repoRoot, "scripts", "phase13", "Build-KoeNoteMsi.ps1"));
        var smokeScript = File.ReadAllText(Path.Combine(repoRoot, "scripts", "phase13", "Test-KoeNoteMsiSmoke.ps1"));

        Assert.DoesNotContain("RunKoeNoteCleanupUi", productWxs);
        Assert.DoesNotContain("RunKoeNoteCleanupQuiet\"", productWxs);
        Assert.DoesNotContain("CleanupUiCommand", wixProject);
        Assert.DoesNotContain("CleanupUiCommand", buildScript);
        Assert.DoesNotContain("CleanupQuietCommand", wixProject);
        Assert.DoesNotContain("CleanupQuietCommand", buildScript);
        Assert.Contains("RunKoeNoteCleanupQuietAll", productWxs);
        Assert.Contains("KOENOTE_CLEANUP_ALL_DATA=&quot;1&quot;", productWxs);
        Assert.Contains("KOENOTE_UNINSTALL_MODE=&quot;ALL&quot;", productWxs);
        Assert.Contains("KOENOTE_CLEANUP_APPDATA_ROOT", productWxs);
        Assert.Contains("--appdata-root", productWxs);
        Assert.DoesNotContain("KOENOTE_CLEANUP_ALL_DATA &lt;&gt; &quot;1&quot;", productWxs);
        Assert.DoesNotContain("UILevel &lt; 3 AND KOENOTE_CLEANUP_ALL_DATA", productWxs);
        Assert.Contains("KoeNoteCleanupChoiceDlg", productWxs);
        Assert.Contains("KOENOTE_UNINSTALL_MODE", productWxs);
        Assert.Contains("ARPSYSTEMCOMPONENT", productWxs);
        Assert.Contains("Name=\"DisplayName\"", productWxs);
        Assert.Contains("MsiExec.exe /I[ProductCode]", productWxs);
        Assert.Contains("MsiExec.exe /I", File.ReadAllText(Path.Combine(repoRoot, "src", "KoeNote.Cleanup", "ArpMetadataWriter.cs")));
        Assert.Contains("CleanupQuietAllCommand", wixProject);
        Assert.Contains("--quiet --all", wixProject);
        Assert.Contains("[switch]$TestAllDataCleanup", smokeScript);
        Assert.Contains("KOENOTE_CLEANUP_APPDATA_ROOT", smokeScript);
        Assert.Contains("must open MSI maintenance UI with /I", smokeScript);
        Assert.Contains("[string]$CleanupQuietAllCommand = \"--quiet --all\"", buildScript);
        Assert.Contains("-p:CleanupQuietAllCommand=\"$CleanupQuietAllCommand\"", buildScript);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KoeNote.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate KoeNote repository root.");
    }
}
