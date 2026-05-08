using System.Windows;
using KoeNote.App.Dialogs;

namespace KoeNote.App.Services.Dialogs;

public sealed class ConfirmationDialogService : IConfirmationDialogService
{
    public static ConfirmationDialogService Default { get; } = new();

    public bool Confirm(Window? owner, ConfirmationDialogRequest request)
    {
        if (owner is null)
        {
            return true;
        }

        var dialog = new ConfirmationDialog(request)
        {
            Owner = owner
        };
        return dialog.ShowDialog() == true;
    }
}
