using System.Windows;
using KoeNote.App.Dialogs;

namespace KoeNote.App.Services.Dialogs;

public sealed class SpeakerNameConfirmationDialogService
{
    public static SpeakerNameConfirmationDialogService Default { get; } = new();

    public SpeakerNameConfirmationResult? Confirm(Window? owner, SpeakerNameConfirmationRequest request)
    {
        if (owner is null)
        {
            return SpeakerNameConfirmationResult.FromRequest(request);
        }

        var dialog = new SpeakerNameConfirmationDialog(request)
        {
            Owner = owner
        };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }
}
