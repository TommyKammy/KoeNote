using System.Text.Json.Serialization;
using KoeNote.App.Models;
using KoeNote.App.Services.Models;

namespace KoeNote.App.Services.Setup;

public enum SetupStep
{
    Welcome,
    EnvironmentCheck,
    SetupMode,
    AsrModel,
    ReviewModel,
    Storage,
    License,
    Install,
    SmokeTest,
    Complete
}

public sealed record SetupState(
    [property: JsonPropertyName("is_completed")] bool IsCompleted,
    [property: JsonPropertyName("current_step")] SetupStep CurrentStep,
    [property: JsonPropertyName("setup_mode")] string SetupMode,
    [property: JsonPropertyName("selected_asr_model_id")] string? SelectedAsrModelId,
    [property: JsonPropertyName("selected_review_model_id")] string? SelectedReviewModelId,
    [property: JsonPropertyName("storage_root")] string? StorageRoot,
    [property: JsonPropertyName("license_accepted")] bool LicenseAccepted,
    [property: JsonPropertyName("last_smoke_succeeded")] bool LastSmokeSucceeded,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt)
{
    public static SetupState Default(string storageRoot)
    {
        return new SetupState(
            IsCompleted: false,
            CurrentStep: SetupStep.Welcome,
            SetupMode: "guided",
            SelectedAsrModelId: null,
            SelectedReviewModelId: null,
            StorageRoot: storageRoot,
            LicenseAccepted: false,
            LastSmokeSucceeded: false,
            UpdatedAt: DateTimeOffset.Now);
    }
}

public sealed record SetupStepItem(SetupStep Step, string Title, string Status);

public sealed record SetupEnvironmentCheck(string Name, bool IsOk, string Detail);

public sealed record SetupSmokeCheck(string Name, bool IsOk, string Detail);

public sealed record SetupSmokeResult(bool IsSucceeded, IReadOnlyList<SetupSmokeCheck> Checks, string ReportPath);

public sealed record SetupInstallResult(bool IsSucceeded, string Message, IReadOnlyList<InstalledModel> InstalledModels);

public sealed record SetupModelAudit(
    string ModelId,
    bool ChecksumKnown,
    string ChecksumDetail,
    bool ManifestKnown,
    string ManifestDetail,
    bool LicenseKnown,
    string LicenseDetail);

public sealed record SetupExistingDataItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("exists")] bool Exists,
    [property: JsonPropertyName("detail")] string Detail);

public sealed record SetupReport(
    [property: JsonPropertyName("generated_at")] DateTimeOffset GeneratedAt,
    [property: JsonPropertyName("setup_state")] SetupState SetupState,
    [property: JsonPropertyName("environment")] IReadOnlyList<StatusItem> Environment,
    [property: JsonPropertyName("existing_data")] IReadOnlyList<SetupExistingDataItem> ExistingData,
    [property: JsonPropertyName("selected_models")] IReadOnlyList<InstalledModel> SelectedModels,
    [property: JsonPropertyName("checks")] IReadOnlyList<SetupSmokeCheck> Checks,
    [property: JsonPropertyName("is_complete")] bool IsComplete,
    [property: JsonPropertyName("messages")] IReadOnlyList<string> Messages);
