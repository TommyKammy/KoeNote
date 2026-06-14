using KoeNote.App.Dialogs;
using KoeNote.App.Models;
using KoeNote.App.Services.Dialogs;
using KoeNote.App.Services.Review;

namespace KoeNote.App.UiIntegrationTests;

public sealed class ReviewCandidateConfirmationDialogViewModelTests
{
    [Fact]
    public void Decisions_UpdateCountsAndAllowContinueAfterAllCandidatesAreResolved()
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
        Assert.False(viewModel.CanContinue);
        Assert.Equal("fixed one", viewModel.ManualEditText);

        viewModel.AcceptSelected();

        Assert.Equal(1, viewModel.AcceptedCount);
        Assert.Equal(2, viewModel.PendingCount);
        Assert.Equal("draft-002", viewModel.SelectedItem?.DraftId);
        Assert.Equal(["accept:draft-001"], operations.Decisions);

        viewModel.ManualEditText = " manual two ";
        viewModel.ApplyManualEdit();

        Assert.Equal(1, viewModel.EditedCount);
        Assert.Equal(1, viewModel.PendingCount);
        Assert.Equal("draft-003", viewModel.SelectedItem?.DraftId);
        Assert.Equal(" manual two ", operations.ManualTextByDraft["draft-002"]);

        viewModel.RejectSelected();

        Assert.Equal(1, viewModel.RejectedCount);
        Assert.Equal(0, viewModel.PendingCount);
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
    }

    [Fact]
    public void DecisionCooldown_BlocksRepeatedActionAgainstAutoSelectedCandidate()
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
        viewModel.BeginDecisionInputCooldown();

        Assert.False(viewModel.AcceptSelected());
        Assert.Equal(1, viewModel.AcceptedCount);
        Assert.Equal(1, viewModel.PendingCount);
        Assert.Equal("draft-002", viewModel.SelectedItem?.DraftId);
        Assert.Equal(["accept:draft-001"], operations.Decisions);
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
