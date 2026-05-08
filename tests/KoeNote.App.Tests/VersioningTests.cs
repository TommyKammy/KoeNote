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
        Assert.Contains("FullyQualifiedName~VersioningTests", script);
        Assert.Contains("release-manifest.json", script);
        Assert.Contains("signing.status", script);
        Assert.Contains("Manifest SHA256 path does not match", script);
        Assert.Contains("Manifest update log path is missing", script);
        Assert.Contains("Manifest requires signing", script);
        Assert.Contains("Bundled Python runtime is required", script);
        Assert.Contains("Release manifest is missing bundled_python_runtime metadata", script);
        Assert.Contains("bundled_python_runtime.required", script);
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
        Assert.Contains("review_runtime_verified", buildScript);
        Assert.Contains("review_runtime = [ordered]@", buildScript);
        Assert.Contains("Review runtime is required for release MSI builds", buildScript);
        Assert.Contains("Review runtime is required but missing from publish output", verificationScript);
        Assert.Contains("Release manifest is missing review_runtime metadata", verificationScript);
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
