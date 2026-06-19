using System.Collections.ObjectModel;
using KoeNote.App.Dialogs;
using KoeNote.App.Models;
using KoeNote.App.Services.Dialogs;

namespace KoeNote.App.Tests;

public sealed class ReviewCandidateConfirmationListProjectionTests
{
    [Fact]
    public void Refresh_ShowsPendingAndKeepsPreferredSelectionWhenVisible()
    {
        var projection = new ReviewCandidateConfirmationListProjection();
        var pending = new ObservableCollection<ReviewCandidateConfirmationDialogItem>
        {
            CreateItem("draft-001"),
            CreateItem("draft-002")
        };
        var decided = new ObservableCollection<ReviewCandidateConfirmationDialogItem>
        {
            CreateDecidedItem("draft-003")
        };
        var displayItems = new ObservableCollection<ReviewCandidateConfirmationDialogItem>();

        var selected = projection.Refresh(
            displayItems,
            pending,
            decided,
            ReviewCandidateConfirmationFilter.Pending,
            pending[1]);

        Assert.Same(pending[1], selected);
        Assert.Equal(["draft-001", "draft-002"], displayItems.Select(static item => item.DraftId));
    }

    [Fact]
    public void Refresh_ShowsDecidedOrAllAndFallsBackToFirstVisibleItem()
    {
        var projection = new ReviewCandidateConfirmationListProjection();
        var pending = new ObservableCollection<ReviewCandidateConfirmationDialogItem>
        {
            CreateItem("draft-001")
        };
        var decided = new ObservableCollection<ReviewCandidateConfirmationDialogItem>
        {
            CreateDecidedItem("draft-002"),
            CreateDecidedItem("draft-003")
        };
        var displayItems = new ObservableCollection<ReviewCandidateConfirmationDialogItem>();

        var selectedDecided = projection.Refresh(
            displayItems,
            pending,
            decided,
            ReviewCandidateConfirmationFilter.Decided,
            pending[0]);

        Assert.Same(decided[0], selectedDecided);
        Assert.Equal(["draft-002", "draft-003"], displayItems.Select(static item => item.DraftId));

        var selectedAll = projection.Refresh(
            displayItems,
            pending,
            decided,
            ReviewCandidateConfirmationFilter.All,
            decided[1]);

        Assert.Same(decided[1], selectedAll);
        Assert.Equal(["draft-001", "draft-002", "draft-003"], displayItems.Select(static item => item.DraftId));
    }

    private static ReviewCandidateConfirmationDialogItem CreateDecidedItem(string draftId)
    {
        var item = CreateItem(draftId);
        item.MarkDecided(ReviewCandidateDecisionKind.Accepted, "accepted text");
        return item;
    }

    private static ReviewCandidateConfirmationDialogItem CreateItem(string draftId)
    {
        return new ReviewCandidateConfirmationDialogItem(new ReviewCandidateConfirmationItem(
            new CorrectionDraft(
                draftId,
                "job-001",
                $"segment-{draftId}",
                "表記ゆれ",
                "original text",
                "suggested text",
                "候補理由",
                0.8)));
    }
}
