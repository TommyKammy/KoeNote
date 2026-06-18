using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using KoeNote.App.Services.Audio;
using KoeNote.App.Services.Dialogs;

namespace KoeNote.App.Dialogs;

public partial class ReviewCandidateConfirmationDialog : Window
{
    private readonly DispatcherTimer _decisionCooldownTimer;

    private ReviewCandidateConfirmationDialogViewModel ViewModel =>
        (ReviewCandidateConfirmationDialogViewModel)DataContext;

    public ReviewCandidateConfirmationDialog(ReviewCandidateConfirmationRequest request)
        : this(request, new AudioPlaybackService())
    {
    }

    public ReviewCandidateConfirmationDialog(
        ReviewCandidateConfirmationRequest request,
        IAudioPlaybackService audioPlaybackService)
    {
        InitializeComponent();
        DataContext = new ReviewCandidateConfirmationDialogViewModel(request, audioPlaybackService);
        _decisionCooldownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _decisionCooldownTimer.Tick += (_, _) =>
        {
            _decisionCooldownTimer.Stop();
            ViewModel.EndDecisionInputCooldown();
        };
    }

    public ReviewCandidateConfirmationResult? Result { get; private set; }

    public ReviewCandidateConfirmationResult CreateCancelResult()
    {
        return ViewModel.CreateResult(ReviewCandidateConfirmationOutcome.Cancel);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ViewModel.StopPlayback();
    }

    private void OnPreviousClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectPrevious();
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectNext();
    }

    private void OnAcceptClick(object sender, RoutedEventArgs e)
    {
        RunDecision(ViewModel.AcceptSelected);
    }

    private void OnRejectClick(object sender, RoutedEventArgs e)
    {
        RunDecision(ViewModel.RejectSelected);
    }

    private void OnApplyManualEditClick(object sender, RoutedEventArgs e)
    {
        RunDecision(ViewModel.ApplyManualEdit);
    }

    private void OnShowPendingClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SetFilter(ReviewCandidateConfirmationFilter.Pending);
    }

    private void OnShowDecidedClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SetFilter(ReviewCandidateConfirmationFilter.Decided);
    }

    private void OnShowAllClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SetFilter(ReviewCandidateConfirmationFilter.All);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = ViewModel.CreateResult(ReviewCandidateConfirmationOutcome.Cancel);
        DialogResult = false;
    }

    private void OnDeferClick(object sender, RoutedEventArgs e)
    {
        Result = ViewModel.CreateResult(ReviewCandidateConfirmationOutcome.Defer);
        DialogResult = true;
    }

    private void OnContinueClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanContinue)
        {
            return;
        }

        Result = ViewModel.CreateResult(ReviewCandidateConfirmationOutcome.Continue);
        DialogResult = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            OnCancelClick(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && ViewModel.CanContinue)
        {
            OnContinueClick(sender, e);
            e.Handled = true;
        }
    }

    private void RunDecision(Func<bool> decision)
    {
        if (!decision())
        {
            return;
        }

        _decisionCooldownTimer.Stop();
        _decisionCooldownTimer.Start();
    }
}
