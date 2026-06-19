using System.Collections.ObjectModel;

namespace KoeNote.App.Dialogs;

internal sealed class ReviewCandidateConfirmationListProjection
{
    public ReviewCandidateConfirmationDialogItem? Refresh(
        ObservableCollection<ReviewCandidateConfirmationDialogItem> displayItems,
        IReadOnlyCollection<ReviewCandidateConfirmationDialogItem> pendingItems,
        IReadOnlyCollection<ReviewCandidateConfirmationDialogItem> decidedItems,
        ReviewCandidateConfirmationFilter filter,
        ReviewCandidateConfirmationDialogItem? preferredSelection)
    {
        displayItems.Clear();
        AppendVisibleItems(displayItems, pendingItems, decidedItems, filter);

        return preferredSelection is not null && displayItems.Contains(preferredSelection)
            ? preferredSelection
            : displayItems.FirstOrDefault();
    }

    private static void AppendVisibleItems(
        ObservableCollection<ReviewCandidateConfirmationDialogItem> displayItems,
        IReadOnlyCollection<ReviewCandidateConfirmationDialogItem> pendingItems,
        IReadOnlyCollection<ReviewCandidateConfirmationDialogItem> decidedItems,
        ReviewCandidateConfirmationFilter filter)
    {
        if (filter is ReviewCandidateConfirmationFilter.Pending or ReviewCandidateConfirmationFilter.All)
        {
            foreach (var item in pendingItems)
            {
                displayItems.Add(item);
            }
        }

        if (filter is ReviewCandidateConfirmationFilter.Decided or ReviewCandidateConfirmationFilter.All)
        {
            foreach (var item in decidedItems)
            {
                displayItems.Add(item);
            }
        }
    }
}
