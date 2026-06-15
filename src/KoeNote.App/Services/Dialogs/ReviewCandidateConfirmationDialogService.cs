using System.Windows;
using KoeNote.App.Dialogs;

namespace KoeNote.App.Services.Dialogs;

public sealed class ReviewCandidateConfirmationDialogService
{
    public static ReviewCandidateConfirmationDialogService Default { get; } = new();

    public ReviewCandidateConfirmationResult? Confirm(Window? owner, ReviewCandidateConfirmationRequest request)
    {
        if (owner is null)
        {
            return ReviewCandidateConfirmationResult.Defer(request);
        }

        var dialog = new ReviewCandidateConfirmationDialog(request)
        {
            Owner = owner
        };
        dialog.ShowDialog();
        return dialog.Result ?? dialog.CreateCancelResult();
    }
}
