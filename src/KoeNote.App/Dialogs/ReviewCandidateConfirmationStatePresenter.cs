namespace KoeNote.App.Dialogs;

internal sealed class ReviewCandidateConfirmationStatePresenter
{
    public static readonly IReadOnlyList<string> StatePropertyNames =
    [
        nameof(ReviewCandidateConfirmationDialogViewModel.PendingCount),
        nameof(ReviewCandidateConfirmationDialogViewModel.DecidedCount),
        nameof(ReviewCandidateConfirmationDialogViewModel.CandidateListTitle),
        nameof(ReviewCandidateConfirmationDialogViewModel.PendingCountText),
        nameof(ReviewCandidateConfirmationDialogViewModel.DecidedCountText),
        nameof(ReviewCandidateConfirmationDialogViewModel.AcceptedCountText),
        nameof(ReviewCandidateConfirmationDialogViewModel.RejectedCountText),
        nameof(ReviewCandidateConfirmationDialogViewModel.EditedCountText),
        nameof(ReviewCandidateConfirmationDialogViewModel.DetailTitle),
        nameof(ReviewCandidateConfirmationDialogViewModel.DetailSubtitle),
        nameof(ReviewCandidateConfirmationDialogViewModel.CurrentPositionText),
        nameof(ReviewCandidateConfirmationDialogViewModel.FooterText),
        nameof(ReviewCandidateConfirmationDialogViewModel.CanOperate),
        nameof(ReviewCandidateConfirmationDialogViewModel.CanAcceptSelected),
        nameof(ReviewCandidateConfirmationDialogViewModel.CanRejectSelected),
        nameof(ReviewCandidateConfirmationDialogViewModel.CanApplyManualEdit),
        nameof(ReviewCandidateConfirmationDialogViewModel.CanPlaySelectedPreview),
        nameof(ReviewCandidateConfirmationDialogViewModel.IsPreviewPlaying),
        nameof(ReviewCandidateConfirmationDialogViewModel.PlayIcon),
        nameof(ReviewCandidateConfirmationDialogViewModel.PreviewPlaybackStatusText),
        nameof(ReviewCandidateConfirmationDialogViewModel.PreviewPlaybackProgressPercent),
        nameof(ReviewCandidateConfirmationDialogViewModel.CanGoPrevious),
        nameof(ReviewCandidateConfirmationDialogViewModel.CanGoNext),
        nameof(ReviewCandidateConfirmationDialogViewModel.CanContinue)
    ];

    public static readonly IReadOnlyList<string> PlaybackStatePropertyNames =
    [
        nameof(ReviewCandidateConfirmationDialogViewModel.CanPlaySelectedPreview),
        nameof(ReviewCandidateConfirmationDialogViewModel.IsPreviewPlaying),
        nameof(ReviewCandidateConfirmationDialogViewModel.PlayIcon),
        nameof(ReviewCandidateConfirmationDialogViewModel.PreviewPlaybackStatusText),
        nameof(ReviewCandidateConfirmationDialogViewModel.PreviewPlaybackProgressPercent)
    ];

    public static readonly IReadOnlyList<string> FilterStatePropertyNames =
    [
        nameof(ReviewCandidateConfirmationDialogViewModel.IsPendingFilterActive),
        nameof(ReviewCandidateConfirmationDialogViewModel.IsDecidedFilterActive),
        nameof(ReviewCandidateConfirmationDialogViewModel.IsAllFilterActive)
    ];

    public ReviewCandidateConfirmationPresentationState Create(ReviewCandidateConfirmationStateInput input)
    {
        var pendingCount = input.PendingItems.Count;
        var decidedCount = input.DecidedItems.Count;
        var selectedItem = input.SelectedItem;
        var currentPositionText = selectedItem is null
            ? string.Empty
            : FormatCurrentPosition(input.DisplayItems, selectedItem);
        var canOperate = selectedItem is not null && !input.IsDecisionInputBlocked;
        var canContinue = pendingCount == 0;
        var canPlaySelectedPreview = selectedItem?.PlaybackPreview is not null && input.CanPlaySelectedPreviewAudio;

        return new ReviewCandidateConfirmationPresentationState(
            pendingCount,
            decidedCount,
            $"候補一覧 ({pendingCount}/{input.TotalCount})",
            $"未処理 {pendingCount}",
            $"処理済み {decidedCount}",
            $"採用 {input.AcceptedCount}",
            $"却下 {input.RejectedCount}",
            $"手修正 {input.EditedCount}",
            input.Filter == ReviewCandidateConfirmationFilter.Pending,
            input.Filter == ReviewCandidateConfirmationFilter.Decided,
            input.Filter == ReviewCandidateConfirmationFilter.All,
            selectedItem is null
                ? pendingCount == 0 ? "候補はありません" : "候補を選択してください"
                : selectedItem.IssueType,
            selectedItem is null
                ? pendingCount == 0 ? "すべての候補を確認しました。" : "一覧から確認する候補を選んでください。"
                : $"{selectedItem.TimestampText} / {selectedItem.SpeakerText}",
            currentPositionText,
            canContinue
                ? "すべての整文候補を確認しました。次に話者名確認へ進めます。"
                : selectedItem is null
                    ? "未処理の整文候補があります。次に確認する候補を選択してください。"
                    : "未処理の整文候補があります。採用、却下、または手修正を選んでください。",
            canOperate,
            canOperate && selectedItem?.DecisionKind != ReviewCandidateDecisionKind.Accepted,
            canOperate && selectedItem?.DecisionKind != ReviewCandidateDecisionKind.Rejected,
            canOperate && !string.IsNullOrWhiteSpace(input.ManualEditText),
            canPlaySelectedPreview,
            input.IsPreviewPlaying,
            input.IsPreviewPlaying ? "\uE769" : "\uE768",
            canPlaySelectedPreview
                ? input.IsPreviewPlaying ? "再生中" : "音声確認"
                : "音声なし",
            input.PreviewPlaybackProgressPercent,
            CanGoPrevious(input.DisplayItems, selectedItem),
            CanGoNext(input.DisplayItems, selectedItem),
            canContinue);
    }

    private static string FormatCurrentPosition(
        IReadOnlyList<ReviewCandidateConfirmationDialogItem> displayItems,
        ReviewCandidateConfirmationDialogItem selectedItem)
    {
        var index = IndexOf(displayItems, selectedItem);
        return index < 0 ? string.Empty : $"{index + 1} / {displayItems.Count}";
    }

    private static bool CanGoPrevious(
        IReadOnlyList<ReviewCandidateConfirmationDialogItem> displayItems,
        ReviewCandidateConfirmationDialogItem? selectedItem)
    {
        var index = selectedItem is null ? -1 : IndexOf(displayItems, selectedItem);
        return index > 0;
    }

    private static bool CanGoNext(
        IReadOnlyList<ReviewCandidateConfirmationDialogItem> displayItems,
        ReviewCandidateConfirmationDialogItem? selectedItem)
    {
        if (selectedItem is null)
        {
            return displayItems.Count > 0;
        }

        var index = IndexOf(displayItems, selectedItem);
        return index >= 0 && index + 1 < displayItems.Count;
    }

    private static int IndexOf(
        IReadOnlyList<ReviewCandidateConfirmationDialogItem> items,
        ReviewCandidateConfirmationDialogItem selectedItem)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], selectedItem))
            {
                return index;
            }
        }

        return -1;
    }
}

internal sealed record ReviewCandidateConfirmationStateInput(
    IReadOnlyList<ReviewCandidateConfirmationDialogItem> PendingItems,
    IReadOnlyList<ReviewCandidateConfirmationDialogItem> DecidedItems,
    IReadOnlyList<ReviewCandidateConfirmationDialogItem> DisplayItems,
    ReviewCandidateConfirmationDialogItem? SelectedItem,
    ReviewCandidateConfirmationFilter Filter,
    int TotalCount,
    int AcceptedCount,
    int RejectedCount,
    int EditedCount,
    bool IsDecisionInputBlocked,
    string ManualEditText,
    bool CanPlaySelectedPreviewAudio,
    bool IsPreviewPlaying,
    double PreviewPlaybackProgressPercent);

internal sealed record ReviewCandidateConfirmationPresentationState(
    int PendingCount,
    int DecidedCount,
    string CandidateListTitle,
    string PendingCountText,
    string DecidedCountText,
    string AcceptedCountText,
    string RejectedCountText,
    string EditedCountText,
    bool IsPendingFilterActive,
    bool IsDecidedFilterActive,
    bool IsAllFilterActive,
    string DetailTitle,
    string DetailSubtitle,
    string CurrentPositionText,
    string FooterText,
    bool CanOperate,
    bool CanAcceptSelected,
    bool CanRejectSelected,
    bool CanApplyManualEdit,
    bool CanPlaySelectedPreview,
    bool IsPreviewPlaying,
    string PlayIcon,
    string PreviewPlaybackStatusText,
    double PreviewPlaybackProgressPercent,
    bool CanGoPrevious,
    bool CanGoNext,
    bool CanContinue)
{
    public static ReviewCandidateConfirmationPresentationState Empty { get; } = new(
        0,
        0,
        "候補一覧 (0/0)",
        "未処理 0",
        "処理済み 0",
        "採用 0",
        "却下 0",
        "手修正 0",
        true,
        false,
        false,
        "候補はありません",
        "すべての候補を確認しました。",
        string.Empty,
        "すべての整文候補を確認しました。次に話者名確認へ進めます。",
        false,
        false,
        false,
        false,
        false,
        false,
        "\uE768",
        "音声なし",
        0,
        false,
        false,
        true);
}
