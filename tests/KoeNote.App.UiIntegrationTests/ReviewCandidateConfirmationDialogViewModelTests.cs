using KoeNote.App.Dialogs;
using KoeNote.App.Models;
using KoeNote.App.Services.Dialogs;
using KoeNote.App.Services.Review;

namespace KoeNote.App.UiIntegrationTests;

public sealed class ReviewCandidateConfirmationDialogViewModelTests
{
    [Fact]
    public void Decisions_UpdateCountsHistoryAndAllowContinueAfterAllCandidatesAreResolved()
    {
        var operations = new FakeReviewCandidateOperations();
        var recordedDecisions = new List<string>();
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [
                CreateCandidate("draft-001", "segment-001", "raw one", "fixed one"),
                CreateCandidate("draft-002", "segment-002", "raw two", "fixed two"),
                CreateCandidate("draft-003", "segment-003", "raw three", "fixed three")
            ],
            operations)
        {
            RecordDecision = (draft, result, selectedText) =>
                recordedDecisions.Add($"{draft.DraftId}:{result.Action}:{selectedText}")
        });

        Assert.Equal(3, viewModel.PendingCount);
        Assert.Equal(0, viewModel.DecidedCount);
        Assert.False(viewModel.CanContinue);
        Assert.Equal("fixed one", viewModel.ManualEditText);

        Assert.True(viewModel.AcceptSelected());

        Assert.Equal(1, viewModel.AcceptedCount);
        Assert.Equal(2, viewModel.PendingCount);
        Assert.Equal(1, viewModel.DecidedCount);
        Assert.Equal("draft-002", viewModel.SelectedItem?.DraftId);
        Assert.False(viewModel.CanOperate);
        Assert.Equal(["accept:draft-001"], operations.Decisions);
        Assert.Equal("採用済み", viewModel.DecidedItems[0].DecisionStatusText);
        Assert.Equal("accepted text", viewModel.DecidedItems[0].FinalText);

        viewModel.EndDecisionInputCooldown();
        viewModel.ManualEditText = " manual two ";
        Assert.True(viewModel.ApplyManualEdit());

        Assert.Equal(1, viewModel.EditedCount);
        Assert.Equal(1, viewModel.PendingCount);
        Assert.Equal(2, viewModel.DecidedCount);
        Assert.Equal("draft-003", viewModel.SelectedItem?.DraftId);
        Assert.Equal(" manual two ", operations.ManualTextByDraft["draft-002"]);

        viewModel.EndDecisionInputCooldown();
        Assert.True(viewModel.RejectSelected());

        Assert.Equal(1, viewModel.RejectedCount);
        Assert.Equal(0, viewModel.PendingCount);
        Assert.Equal(3, viewModel.DecidedCount);
        Assert.True(viewModel.CanContinue);
        Assert.Null(viewModel.SelectedItem);
        Assert.Equal(ReviewCandidateConfirmationOutcome.Continue, viewModel.CreateResult(
            ReviewCandidateConfirmationOutcome.Continue).Outcome);
        Assert.Equal(
            [
                "draft-001:accepted:fixed one",
                "draft-002:manual_edit: manual two ",
                "draft-003:rejected:fixed three"
            ],
            recordedDecisions);

        viewModel.SetFilter(ReviewCandidateConfirmationFilter.Decided);

        Assert.Equal(3, viewModel.DisplayItems.Count);
        Assert.Equal("draft-001", viewModel.SelectedItem?.DraftId);
        Assert.Equal("採用済み", viewModel.SelectedItem?.DecisionStatusText);
        Assert.Equal("accepted text", viewModel.SelectedItem?.FinalText);
    }

    [Fact]
    public void Decision_AutoSelectsNextCandidateAndBlocksImmediateRepeat()
    {
        var operations = new FakeReviewCandidateOperations();
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [
                CreateCandidate("draft-001", "segment-001", "raw one", "fixed one"),
                CreateCandidate("draft-002", "segment-002", "raw two", "fixed two")
            ],
            operations));

        Assert.True(viewModel.AcceptSelected());

        Assert.Equal("draft-002", viewModel.SelectedItem?.DraftId);
        Assert.False(viewModel.AcceptSelected());
        Assert.Equal(1, viewModel.AcceptedCount);
        Assert.Equal(1, viewModel.PendingCount);
        Assert.Equal(["accept:draft-001"], operations.Decisions);

        viewModel.EndDecisionInputCooldown();

        Assert.True(viewModel.AcceptSelected());
        Assert.Equal(2, viewModel.AcceptedCount);
    }

    [Fact]
    public void Filter_CanShowPendingDecidedAndAllCandidates()
    {
        var operations = new FakeReviewCandidateOperations();
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [
                CreateCandidate("draft-001", "segment-001", "raw one", "fixed one"),
                CreateCandidate("draft-002", "segment-002", "raw two", "fixed two")
            ],
            operations));

        Assert.True(viewModel.AcceptSelected());

        viewModel.SetFilter(ReviewCandidateConfirmationFilter.Pending);
        Assert.Single(viewModel.DisplayItems);
        Assert.Equal("draft-002", viewModel.SelectedItem?.DraftId);

        viewModel.SetFilter(ReviewCandidateConfirmationFilter.Decided);
        Assert.Single(viewModel.DisplayItems);
        Assert.Equal("draft-001", viewModel.SelectedItem?.DraftId);
        Assert.False(viewModel.CanOperate);

        viewModel.SetFilter(ReviewCandidateConfirmationFilter.All);
        Assert.Equal(2, viewModel.DisplayItems.Count);
    }

    [Fact]
    public void PostCommitRecordDecisionFailure_DoesNotLeaveCommittedDraftPending()
    {
        var operations = new FakeReviewCandidateOperations();
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [CreateCandidate("draft-001", "segment-001", "raw one", "fixed one")],
            operations)
        {
            RecordDecision = (_, _, _) => throw new InvalidOperationException("memory unavailable")
        });

        Assert.True(viewModel.AcceptSelected());

        Assert.Equal(1, viewModel.AcceptedCount);
        Assert.Equal(0, viewModel.PendingCount);
        Assert.Equal(1, viewModel.DecidedCount);
        Assert.True(viewModel.CanContinue);
        Assert.Contains("補正メモリの更新に失敗しました", viewModel.OperationErrorText, StringComparison.Ordinal);
        Assert.Equal(["accept:draft-001"], operations.Decisions);
    }

    [Fact]
    public void SameSegmentRemainingCandidate_UsesFinalTextFromPreviousDecision()
    {
        var operations = new FakeReviewCandidateOperations();
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [
                CreateCandidate("draft-001", "segment-001", "raw one", "fixed one"),
                CreateCandidate("draft-002", "segment-001", "raw two", "fixed two")
            ],
            operations));

        Assert.True(viewModel.AcceptSelected());

        Assert.Equal("draft-002", viewModel.SelectedItem?.DraftId);
        Assert.Equal("accepted text", viewModel.SelectedItem?.CurrentText);
    }

    private static ReviewCandidateConfirmationItem CreateCandidate(
        string draftId,
        string segmentId,
        string originalText,
        string suggestedText)
    {
        return new ReviewCandidateConfirmationItem(
            new CorrectionDraft(
                draftId,
                "job-001",
                segmentId,
                "表記ゆれ",
                originalText,
                suggestedText,
                "候補理由",
                0.8),
            StartSeconds: 1,
            EndSeconds: 2,
            SpeakerName: "Speaker_0",
            CurrentText: originalText);
    }

    private sealed class FakeReviewCandidateOperations : IReviewCandidateConfirmationOperations
    {
        public List<string> Decisions { get; } = [];

        public Dictionary<string, string> ManualTextByDraft { get; } = new(StringComparer.Ordinal);

        public ReviewOperationResult AcceptDraft(string draftId)
        {
            Decisions.Add($"accept:{draftId}");
            return CreateResult(draftId, "accepted", "accepted text");
        }

        public ReviewOperationResult RejectDraft(string draftId)
        {
            Decisions.Add($"reject:{draftId}");
            return CreateResult(draftId, "rejected", null);
        }

        public ReviewOperationResult ApplyManualEdit(string draftId, string finalText, string? manualNote = null)
        {
            Decisions.Add($"manual:{draftId}");
            ManualTextByDraft[draftId] = finalText;
            return CreateResult(draftId, "manual_edit", finalText);
        }

        private static ReviewOperationResult CreateResult(string draftId, string action, string? finalText)
        {
            return new ReviewOperationResult("job-001", "segment-001", draftId, action, finalText, 0);
        }
    }
}
