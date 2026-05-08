using System.Windows;

namespace KoeNote.App.Services.Dialogs;

public interface IConfirmationDialogService
{
    bool Confirm(Window? owner, ConfirmationDialogRequest request);
}
