using KoeNote.App.Models;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Services.Dialogs;

public sealed record ReviewCandidateConfirmationRequest(
    string JobTitle,
    IReadOnlyList<ReviewCandidateConfirmationItem> Candidates,
    IReviewCandidateConfirmationOperations Operations);

public sealed record ReviewCandidateConfirmationItem(
    CorrectionDraft Draft,
    double? StartSeconds = null,
    double? EndSeconds = null,
    string? SpeakerName = null,
    string? CurrentText = null);

public interface IReviewCandidateConfirmationOperations
{
    ReviewOperationResult AcceptDraft(string draftId);

    ReviewOperationResult RejectDraft(string draftId);

    ReviewOperationResult ApplyManualEdit(string draftId, string finalText, string? manualNote = null);
}

public sealed class ReviewCandidateConfirmationOperationAdapter(ReviewOperationService reviewOperationService)
    : IReviewCandidateConfirmationOperations
{
    public ReviewOperationResult AcceptDraft(string draftId)
    {
        return reviewOperationService.AcceptDraft(draftId);
    }

    public ReviewOperationResult RejectDraft(string draftId)
    {
        return reviewOperationService.RejectDraft(draftId);
    }

    public ReviewOperationResult ApplyManualEdit(string draftId, string finalText, string? manualNote = null)
    {
        return reviewOperationService.ApplyManualEdit(draftId, finalText, manualNote);
    }
}

public enum ReviewCandidateConfirmationOutcome
{
    Continue,
    Defer,
    Cancel
}

public sealed record ReviewCandidateConfirmationResult(
    ReviewCandidateConfirmationOutcome Outcome,
    int TotalCount,
    int AcceptedCount,
    int RejectedCount,
    int EditedCount,
    int RemainingPendingCount)
{
    public static ReviewCandidateConfirmationResult Defer(ReviewCandidateConfirmationRequest request)
    {
        return new ReviewCandidateConfirmationResult(
            ReviewCandidateConfirmationOutcome.Defer,
            request.Candidates.Count,
            0,
            0,
            0,
            request.Candidates.Count);
    }
}
