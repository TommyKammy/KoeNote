using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using KoeNote.App.Services.Dialogs;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Dialogs;

public partial class ReviewCandidateConfirmationDialog : Window
{
    private readonly DispatcherTimer _decisionCooldownTimer;

    private ReviewCandidateConfirmationDialogViewModel ViewModel =>
        (ReviewCandidateConfirmationDialogViewModel)DataContext;

    public ReviewCandidateConfirmationDialog(ReviewCandidateConfirmationRequest request)
    {
        InitializeComponent();
        DataContext = new ReviewCandidateConfirmationDialogViewModel(request);
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

public enum ReviewCandidateConfirmationFilter
{
    Pending,
    Decided,
    All
}

public enum ReviewCandidateDecisionKind
{
    Pending,
    Accepted,
    Rejected,
    Edited
}

public sealed class ReviewCandidateConfirmationDialogViewModel : INotifyPropertyChanged
{
    private readonly ReviewCandidateConfirmationRequest _request;
    private ReviewCandidateConfirmationDialogItem? _selectedItem;
    private string _manualEditText = string.Empty;
    private string _operationErrorText = string.Empty;
    private bool _isDecisionInputBlocked;
    private ReviewCandidateConfirmationFilter _filter = ReviewCandidateConfirmationFilter.Pending;

    public ReviewCandidateConfirmationDialogViewModel(ReviewCandidateConfirmationRequest request)
    {
        _request = request;
        TotalCount = request.Candidates.Count;
        LeadText = $"{request.JobTitle} の整文候補を確認してから、話者名確認へ進みます。";
        Items = new ObservableCollection<ReviewCandidateConfirmationDialogItem>(
            request.Candidates.Select(static candidate => new ReviewCandidateConfirmationDialogItem(candidate)));
        DecidedItems = [];
        DisplayItems = [];
        RefreshDisplayItems(Items.FirstOrDefault());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string LeadText { get; }

    public int TotalCount { get; }

    public int AcceptedCount { get; private set; }

    public int RejectedCount { get; private set; }

    public int EditedCount { get; private set; }

    public ObservableCollection<ReviewCandidateConfirmationDialogItem> Items { get; }

    public ObservableCollection<ReviewCandidateConfirmationDialogItem> DecidedItems { get; }

    public ObservableCollection<ReviewCandidateConfirmationDialogItem> DisplayItems { get; }

    public ReviewCandidateConfirmationDialogItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (ReferenceEquals(_selectedItem, value))
            {
                return;
            }

            _selectedItem = value;
            RefreshManualEditText(value);
            OperationErrorText = string.Empty;
            RefreshStateProperties();
            OnPropertyChanged();
        }
    }

    public string ManualEditText
    {
        get => _manualEditText;
        set
        {
            if (string.Equals(_manualEditText, value, StringComparison.Ordinal))
            {
                return;
            }

            _manualEditText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanAcceptSelected));
            OnPropertyChanged(nameof(CanRejectSelected));
            OnPropertyChanged(nameof(CanApplyManualEdit));
        }
    }

    public string OperationErrorText
    {
        get => _operationErrorText;
        private set
        {
            if (string.Equals(_operationErrorText, value, StringComparison.Ordinal))
            {
                return;
            }

            _operationErrorText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasOperationError));
        }
    }

    public bool HasOperationError => !string.IsNullOrWhiteSpace(OperationErrorText);

    public int PendingCount => Items.Count;

    public int DecidedCount => DecidedItems.Count;

    public string CandidateListTitle => $"候補一覧 ({PendingCount}/{TotalCount})";

    public string PendingCountText => $"未処理 {PendingCount}";

    public string DecidedCountText => $"処理済み {DecidedCount}";

    public string AcceptedCountText => $"採用 {AcceptedCount}";

    public string RejectedCountText => $"却下 {RejectedCount}";

    public string EditedCountText => $"手修正 {EditedCount}";

    public bool IsPendingFilterActive => _filter == ReviewCandidateConfirmationFilter.Pending;

    public bool IsDecidedFilterActive => _filter == ReviewCandidateConfirmationFilter.Decided;

    public bool IsAllFilterActive => _filter == ReviewCandidateConfirmationFilter.All;

    public string DetailTitle => SelectedItem is null
        ? Items.Count == 0 ? "候補はありません" : "候補を選択してください"
        : SelectedItem.IssueType;

    public string DetailSubtitle => SelectedItem is null
        ? Items.Count == 0 ? "すべての候補を確認しました。" : "一覧から確認する候補を選んでください。"
        : $"{SelectedItem.TimestampText} / {SelectedItem.SpeakerText}";

    public string CurrentPositionText
    {
        get
        {
            if (SelectedItem is null)
            {
                return string.Empty;
            }

            var index = DisplayItems.IndexOf(SelectedItem);
            return index < 0 ? string.Empty : $"{index + 1} / {DisplayItems.Count}";
        }
    }

    public string FooterText => CanContinue
        ? "すべての整文候補を確認しました。次に話者名確認へ進めます。"
        : SelectedItem is null
            ? "未処理の整文候補があります。次に確認する候補を選択してください。"
            : "未処理の整文候補があります。採用、却下、または手修正を選んでください。";

    public bool IsDecisionInputBlocked
    {
        get => _isDecisionInputBlocked;
        private set
        {
            if (_isDecisionInputBlocked == value)
            {
                return;
            }

            _isDecisionInputBlocked = value;
            OnPropertyChanged();
            RefreshStateProperties();
        }
    }

    public bool CanOperate => SelectedItem is not null && !IsDecisionInputBlocked;

    public bool CanAcceptSelected => CanOperate && SelectedItem?.DecisionKind != ReviewCandidateDecisionKind.Accepted;

    public bool CanRejectSelected => CanOperate && SelectedItem?.DecisionKind != ReviewCandidateDecisionKind.Rejected;

    public bool CanApplyManualEdit => CanOperate && !string.IsNullOrWhiteSpace(ManualEditText);

    public bool CanGoPrevious
    {
        get
        {
            var index = SelectedItem is null ? -1 : DisplayItems.IndexOf(SelectedItem);
            return index > 0;
        }
    }

    public bool CanGoNext
    {
        get
        {
            if (SelectedItem is null)
            {
                return DisplayItems.Count > 0;
            }

            var index = DisplayItems.IndexOf(SelectedItem);
            return index >= 0 && index + 1 < DisplayItems.Count;
        }
    }

    public bool CanContinue => Items.Count == 0;

    public void SetFilter(ReviewCandidateConfirmationFilter filter)
    {
        if (_filter == filter)
        {
            RefreshFilterStateProperties();
            return;
        }

        _filter = filter;
        RefreshDisplayItems(SelectedItem);
        RefreshFilterStateProperties();
        RefreshStateProperties();
    }

    public void EndDecisionInputCooldown()
    {
        IsDecisionInputBlocked = false;
    }

    public void SelectPrevious()
    {
        var index = SelectedItem is null ? -1 : DisplayItems.IndexOf(SelectedItem);
        if (index > 0)
        {
            SelectedItem = DisplayItems[index - 1];
        }
    }

    public void SelectNext()
    {
        if (SelectedItem is null)
        {
            if (DisplayItems.Count > 0)
            {
                SelectedItem = DisplayItems[0];
            }

            return;
        }

        var index = DisplayItems.IndexOf(SelectedItem);
        if (index >= 0 && index + 1 < DisplayItems.Count)
        {
            SelectedItem = DisplayItems[index + 1];
        }
    }

    public bool AcceptSelected()
    {
        if (!CanAcceptSelected)
        {
            return false;
        }

        return ApplyDecision(
            static (operations, draftId, _) => operations.AcceptDraft(draftId),
            static (operations, draftId, _) => operations.ChangeToAccepted(draftId),
            manualText: null,
            ReviewCandidateDecisionKind.Accepted);
    }

    public bool RejectSelected()
    {
        if (!CanRejectSelected)
        {
            return false;
        }

        return ApplyDecision(
            static (operations, draftId, _) => operations.RejectDraft(draftId),
            static (operations, draftId, _) => operations.ChangeToRejected(draftId),
            manualText: null,
            ReviewCandidateDecisionKind.Rejected);
    }

    public bool ApplyManualEdit()
    {
        if (!CanApplyManualEdit)
        {
            return false;
        }

        return ApplyDecision(
            static (operations, draftId, manualText) =>
                operations.ApplyManualEdit(draftId, manualText ?? string.Empty, "整文候補確認"),
            static (operations, draftId, manualText) =>
                operations.ChangeToManualEdit(draftId, manualText ?? string.Empty, "整文候補確認"),
            ManualEditText,
            ReviewCandidateDecisionKind.Edited);
    }

    public ReviewCandidateConfirmationResult CreateResult(ReviewCandidateConfirmationOutcome outcome)
    {
        return new ReviewCandidateConfirmationResult(
            outcome,
            TotalCount,
            AcceptedCount,
            RejectedCount,
            EditedCount,
            PendingCount);
    }

    private bool ApplyDecision(
        Func<IReviewCandidateConfirmationOperations, string, string?, ReviewOperationResult> pendingOperation,
        Func<IReviewCandidateConfirmationOperations, string, string?, ReviewOperationResult> decidedOperation,
        string? manualText,
        ReviewCandidateDecisionKind decisionKind)
    {
        if (!CanOperate)
        {
            return false;
        }

        var selected = SelectedItem!;
        var wasPending = selected.IsPending;
        var previousDecisionKind = selected.DecisionKind;
        ReviewOperationResult result;
        try
        {
            OperationErrorText = string.Empty;
            result = wasPending
                ? pendingOperation(_request.Operations, selected.DraftId, manualText)
                : decidedOperation(_request.Operations, selected.DraftId, manualText);
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            OperationErrorText = $"整文候補の保存に失敗しました: {exception.Message}";
            return false;
        }
        catch (Exception exception)
        {
            OperationErrorText = $"整文候補の保存中に予期しないエラーが発生しました: {exception.Message}";
            return false;
        }

        var selectedSuggestionText = manualText ?? selected.SuggestedText;
        var postCommitWarning = TryRecordDecision(selected, result, selectedSuggestionText);
        ApplyDecisionCountDelta(previousDecisionKind, decisionKind);

        var selectedIndex = Items.IndexOf(selected);
        var displayFinalText = result.FinalText ?? selected.CurrentText;
        RefreshRemainingSegmentText(selected, displayFinalText);
        selected.MarkDecided(decisionKind, result.FinalText ?? (decisionKind == ReviewCandidateDecisionKind.Rejected
            ? selected.CurrentText
            : selectedSuggestionText));
        RefreshManualEditText(selected);
        if (wasPending)
        {
            Items.Remove(selected);
            DecidedItems.Add(selected);
            var nextPending = selectedIndex >= 0 && selectedIndex < Items.Count
                ? Items[selectedIndex]
                : Items.LastOrDefault();
            RefreshDisplayItems(nextPending);
            IsDecisionInputBlocked = nextPending is not null;
        }
        else
        {
            RefreshDisplayItems(selected);
            IsDecisionInputBlocked = true;
        }

        OperationErrorText = postCommitWarning ?? string.Empty;
        RefreshStateProperties();
        return true;
    }

    private void ApplyDecisionCountDelta(ReviewCandidateDecisionKind previousDecisionKind, ReviewCandidateDecisionKind nextDecisionKind)
    {
        DecrementDecisionCount(previousDecisionKind);
        IncrementDecisionCount(nextDecisionKind);
    }

    private void IncrementDecisionCount(ReviewCandidateDecisionKind decisionKind)
    {
        switch (decisionKind)
        {
            case ReviewCandidateDecisionKind.Accepted:
                AcceptedCount++;
                break;
            case ReviewCandidateDecisionKind.Rejected:
                RejectedCount++;
                break;
            case ReviewCandidateDecisionKind.Edited:
                EditedCount++;
                break;
        }
    }

    private void DecrementDecisionCount(ReviewCandidateDecisionKind decisionKind)
    {
        switch (decisionKind)
        {
            case ReviewCandidateDecisionKind.Accepted:
                AcceptedCount--;
                break;
            case ReviewCandidateDecisionKind.Rejected:
                RejectedCount--;
                break;
            case ReviewCandidateDecisionKind.Edited:
                EditedCount--;
                break;
        }
    }

    private string? TryRecordDecision(
        ReviewCandidateConfirmationDialogItem selected,
        ReviewOperationResult result,
        string selectedSuggestionText)
    {
        if (_request.RecordDecision is null)
        {
            return null;
        }

        try
        {
            _request.RecordDecision(selected.Draft, result, selectedSuggestionText);
            return null;
        }
        catch (Exception exception)
        {
            return $"整文候補は保存済みですが、補正メモリの更新に失敗しました: {exception.Message}";
        }
    }

    private void RefreshManualEditText(ReviewCandidateConfirmationDialogItem? item)
    {
        ManualEditText = item is null
            ? string.Empty
            : item.HasDecision
                ? !string.IsNullOrWhiteSpace(item.FinalText) ? item.FinalText : item.CurrentText
                : item.SuggestedText;
    }

    private void RefreshRemainingSegmentText(ReviewCandidateConfirmationDialogItem selected, string finalText)
    {
        foreach (var item in Items)
        {
            if (!ReferenceEquals(item, selected) &&
                string.Equals(item.SegmentId, selected.SegmentId, StringComparison.Ordinal))
            {
                item.CurrentText = finalText;
            }
        }
    }

    private void RefreshDisplayItems(ReviewCandidateConfirmationDialogItem? preferredSelection)
    {
        DisplayItems.Clear();
        if (_filter is ReviewCandidateConfirmationFilter.Pending or ReviewCandidateConfirmationFilter.All)
        {
            foreach (var item in Items)
            {
                DisplayItems.Add(item);
            }
        }

        if (_filter is ReviewCandidateConfirmationFilter.Decided or ReviewCandidateConfirmationFilter.All)
        {
            foreach (var item in DecidedItems)
            {
                DisplayItems.Add(item);
            }
        }

        SelectedItem = preferredSelection is not null && DisplayItems.Contains(preferredSelection)
            ? preferredSelection
            : DisplayItems.FirstOrDefault();
        OnPropertyChanged(nameof(DisplayItems));
    }

    private void RefreshStateProperties()
    {
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(DecidedCount));
        OnPropertyChanged(nameof(CandidateListTitle));
        OnPropertyChanged(nameof(PendingCountText));
        OnPropertyChanged(nameof(DecidedCountText));
        OnPropertyChanged(nameof(AcceptedCountText));
        OnPropertyChanged(nameof(RejectedCountText));
        OnPropertyChanged(nameof(EditedCountText));
        OnPropertyChanged(nameof(DetailTitle));
        OnPropertyChanged(nameof(DetailSubtitle));
        OnPropertyChanged(nameof(CurrentPositionText));
        OnPropertyChanged(nameof(FooterText));
        OnPropertyChanged(nameof(CanOperate));
        OnPropertyChanged(nameof(CanAcceptSelected));
        OnPropertyChanged(nameof(CanRejectSelected));
        OnPropertyChanged(nameof(CanApplyManualEdit));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanContinue));
    }

    private void RefreshFilterStateProperties()
    {
        OnPropertyChanged(nameof(IsPendingFilterActive));
        OnPropertyChanged(nameof(IsDecidedFilterActive));
        OnPropertyChanged(nameof(IsAllFilterActive));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ReviewCandidateConfirmationDialogItem(ReviewCandidateConfirmationItem item)
    : INotifyPropertyChanged
{
    private string _currentText = string.IsNullOrWhiteSpace(item.CurrentText)
        ? item.Draft.OriginalText
        : item.CurrentText.Trim();
    private ReviewCandidateDecisionKind _decisionKind = ReviewCandidateDecisionKind.Pending;
    private string _finalText = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public KoeNote.App.Models.CorrectionDraft Draft => item.Draft;

    public string DraftId => item.Draft.DraftId;

    public string SegmentId => item.Draft.SegmentId;

    public string IssueType => item.Draft.IssueType;

    public string CurrentText
    {
        get => _currentText;
        set
        {
            if (string.Equals(_currentText, value, StringComparison.Ordinal))
            {
                return;
            }

            _currentText = value;
            OnPropertyChanged();
        }
    }

    public string SuggestedText => item.Draft.SuggestedText;

    public string Reason => item.Draft.Reason;

    public bool IsPending => _decisionKind == ReviewCandidateDecisionKind.Pending;

    public bool HasDecision => !IsPending;

    public ReviewCandidateDecisionKind DecisionKind => _decisionKind;

    public string DecisionStatusText => _decisionKind switch
    {
        ReviewCandidateDecisionKind.Accepted => "採用済み",
        ReviewCandidateDecisionKind.Rejected => "却下済み",
        ReviewCandidateDecisionKind.Edited => "手修正済み",
        _ => "未処理"
    };

    public string FinalText => HasDecision ? _finalText : string.Empty;

    public string SpeakerText => string.IsNullOrWhiteSpace(item.SpeakerName)
        ? "話者未設定"
        : item.SpeakerName.Trim();

    public string ConfidenceText => item.Draft.Confidence.ToString("P0", CultureInfo.InvariantCulture);

    public string TimestampText => (item.StartSeconds, item.EndSeconds) switch
    {
        (double start, double end) => $"{FormatTimestamp(start)} - {FormatTimestamp(end)}",
        (double start, null) => FormatTimestamp(start),
        _ => $"Segment {item.Draft.SegmentId}"
    };

    public void MarkDecided(ReviewCandidateDecisionKind decisionKind, string finalText)
    {
        _decisionKind = decisionKind;
        _finalText = finalText;
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(HasDecision));
        OnPropertyChanged(nameof(DecisionKind));
        OnPropertyChanged(nameof(DecisionStatusText));
        OnPropertyChanged(nameof(FinalText));
    }

    private static string FormatTimestamp(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
