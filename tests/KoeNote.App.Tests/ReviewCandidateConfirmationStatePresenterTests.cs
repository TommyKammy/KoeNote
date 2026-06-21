using KoeNote.App.Dialogs;
using KoeNote.App.Models;
using KoeNote.App.Services.Dialogs;

namespace KoeNote.App.Tests;

public sealed class ReviewCandidateConfirmationStatePresenterTests
{
    [Fact]
    public void Create_ProjectsCountsSelectionNavigationAndActionState()
    {
        var presenter = new ReviewCandidateConfirmationStatePresenter();
        var first = CreateItem("draft-001", "segment-001");
        var second = CreateItem("draft-002", "segment-002");
        var decided = CreateItem("draft-003", "segment-003");
        decided.MarkDecided(ReviewCandidateDecisionKind.Accepted, "accepted text");

        var state = presenter.Create(new ReviewCandidateConfirmationStateInput(
            [first, second],
            [decided],
            [first, second],
            second,
            ReviewCandidateConfirmationFilter.Pending,
            TotalCount: 3,
            AcceptedCount: 1,
            RejectedCount: 0,
            EditedCount: 0,
            IsDecisionInputBlocked: false,
            ManualEditText: "manual text",
            CanPlaySelectedPreviewAudio: true,
            IsPreviewPlaying: false,
            PreviewPlaybackProgressPercent: 25));

        Assert.Equal(2, state.PendingCount);
        Assert.Equal(1, state.DecidedCount);
        Assert.Equal("候補一覧 (2/3)", state.CandidateListTitle);
        Assert.Equal("2 / 2", state.CurrentPositionText);
        Assert.True(state.CanOperate);
        Assert.True(state.CanAcceptSelected);
        Assert.True(state.CanRejectSelected);
        Assert.True(state.CanApplyManualEdit);
        Assert.True(state.CanGoPrevious);
        Assert.False(state.CanGoNext);
        Assert.False(state.CanContinue);
        Assert.True(state.CanPlaySelectedPreview);
        Assert.Equal("音声確認", state.PreviewPlaybackStatusText);
        Assert.Equal(25, state.PreviewPlaybackProgressPercent);
    }

    [Fact]
    public void Create_ProjectsCompletedEmptyState()
    {
        var presenter = new ReviewCandidateConfirmationStatePresenter();

        var state = presenter.Create(new ReviewCandidateConfirmationStateInput(
            [],
            [],
            [],
            SelectedItem: null,
            ReviewCandidateConfirmationFilter.Decided,
            TotalCount: 0,
            AcceptedCount: 0,
            RejectedCount: 0,
            EditedCount: 0,
            IsDecisionInputBlocked: false,
            ManualEditText: string.Empty,
            CanPlaySelectedPreviewAudio: false,
            IsPreviewPlaying: false,
            PreviewPlaybackProgressPercent: 0));

        Assert.Equal("候補はありません", state.DetailTitle);
        Assert.Equal("すべての候補を確認しました。", state.DetailSubtitle);
        Assert.Equal("すべての整文候補を確認しました。次に話者名確認へ進めます。", state.FooterText);
        Assert.True(state.IsDecidedFilterActive);
        Assert.False(state.CanOperate);
        Assert.False(state.CanGoPrevious);
        Assert.False(state.CanGoNext);
        Assert.True(state.CanContinue);
        Assert.Equal("音声なし", state.PreviewPlaybackStatusText);
    }

    private static ReviewCandidateConfirmationDialogItem CreateItem(string draftId, string segmentId)
    {
        return new ReviewCandidateConfirmationDialogItem(new ReviewCandidateConfirmationItem(
            new CorrectionDraft(
                draftId,
                "job-001",
                segmentId,
                "表記ゆれ",
                "original text",
                "suggested text",
                "候補理由",
                0.8),
            StartSeconds: 1,
            EndSeconds: 2));
    }
}
