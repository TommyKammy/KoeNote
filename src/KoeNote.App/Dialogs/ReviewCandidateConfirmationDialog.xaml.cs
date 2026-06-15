using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using KoeNote.App.Services.Dialogs;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Dialogs;

public partial class ReviewCandidateConfirmationDialog : Window
{
    private ReviewCandidateConfirmationDialogViewModel ViewModel =>
        (ReviewCandidateConfirmationDialogViewModel)DataContext;

    public ReviewCandidateConfirmationDialog(ReviewCandidateConfirmationRequest request)
    {
        InitializeComponent();
        DataContext = new ReviewCandidateConfirmationDialogViewModel(request);
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
        decision();
    }
}

public sealed class ReviewCandidateConfirmationDialogViewModel : INotifyPropertyChanged
{
    private readonly ReviewCandidateConfirmationRequest _request;
    private ReviewCandidateConfirmationDialogItem? _selectedItem;
    private string _manualEditText = string.Empty;
    private string _operationErrorText = string.Empty;

    public ReviewCandidateConfirmationDialogViewModel(ReviewCandidateConfirmationRequest request)
    {
        _request = request;
        TotalCount = request.Candidates.Count;
        LeadText = $"{request.JobTitle} の整文候補を確認してから、話者名確認へ進みます。";
        Items = new ObservableCollection<ReviewCandidateConfirmationDialogItem>(
            request.Candidates.Select(static candidate => new ReviewCandidateConfirmationDialogItem(candidate)));
        SelectedItem = Items.FirstOrDefault();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string LeadText { get; }

    public int TotalCount { get; }

    public int AcceptedCount { get; private set; }

    public int RejectedCount { get; private set; }

    public int EditedCount { get; private set; }

    public ObservableCollection<ReviewCandidateConfirmationDialogItem> Items { get; }

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
            ManualEditText = value?.SuggestedText ?? string.Empty;
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

    public string CandidateListTitle => $"候補一覧 ({PendingCount}/{TotalCount})";

    public string PendingCountText => $"未処理 {PendingCount}";

    public string AcceptedCountText => $"採用 {AcceptedCount}";

    public string RejectedCountText => $"却下 {RejectedCount}";

    public string EditedCountText => $"手修正 {EditedCount}";

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

            var index = Items.IndexOf(SelectedItem);
            return index < 0 ? string.Empty : $"{index + 1} / {PendingCount}";
        }
    }

    public string FooterText => CanContinue
        ? "すべての整文候補を確認済みです。次に話者名確認へ進めます。"
        : SelectedItem is null
            ? "未処理の整文候補があります。次に確認する候補を選択してください。"
            : "未処理の整文候補があります。採用、却下、または手修正を選んでください。";

    public bool CanOperate => SelectedItem is not null;

    public bool CanApplyManualEdit => CanOperate && !string.IsNullOrWhiteSpace(ManualEditText);

    public bool CanGoPrevious
    {
        get
        {
            var index = SelectedItem is null ? -1 : Items.IndexOf(SelectedItem);
            return index > 0;
        }
    }

    public bool CanGoNext
    {
        get
        {
            if (SelectedItem is null)
            {
                return Items.Count > 0;
            }

            var index = Items.IndexOf(SelectedItem);
            return index >= 0 && index + 1 < Items.Count;
        }
    }

    public bool CanContinue => Items.Count == 0;

    public void SelectPrevious()
    {
        var index = SelectedItem is null ? -1 : Items.IndexOf(SelectedItem);
        if (index > 0)
        {
            SelectedItem = Items[index - 1];
        }
    }

    public void SelectNext()
    {
        if (SelectedItem is null)
        {
            if (Items.Count > 0)
            {
                SelectedItem = Items[0];
            }

            return;
        }

        var index = Items.IndexOf(SelectedItem);
        if (index >= 0 && index + 1 < Items.Count)
        {
            SelectedItem = Items[index + 1];
        }
    }

    public bool AcceptSelected()
    {
        return ApplyDecision(static (operations, draftId, _) => operations.AcceptDraft(draftId), manualText: null, accepted: true);
    }

    public bool RejectSelected()
    {
        return ApplyDecision(static (operations, draftId, _) => operations.RejectDraft(draftId), manualText: null, rejected: true);
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
            ManualEditText,
            edited: true);
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
        Func<IReviewCandidateConfirmationOperations, string, string?, ReviewOperationResult> operation,
        string? manualText,
        bool accepted = false,
        bool rejected = false,
        bool edited = false)
    {
        if (!CanOperate)
        {
            return false;
        }

        var selected = SelectedItem!;
        ReviewOperationResult result;
        try
        {
            OperationErrorText = string.Empty;
            result = operation(_request.Operations, selected.DraftId, manualText);
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
        if (accepted)
        {
            AcceptedCount++;
        }
        else if (rejected)
        {
            RejectedCount++;
        }
        else if (edited)
        {
            EditedCount++;
        }

        RefreshRemainingSegmentText(selected, result);
        Items.Remove(selected);
        SelectedItem = null;
        OperationErrorText = postCommitWarning ?? string.Empty;
        RefreshStateProperties();
        return true;
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

    private void RefreshRemainingSegmentText(ReviewCandidateConfirmationDialogItem selected, ReviewOperationResult result)
    {
        if (result.FinalText is null)
        {
            return;
        }

        foreach (var item in Items)
        {
            if (!ReferenceEquals(item, selected) &&
                string.Equals(item.SegmentId, result.SegmentId, StringComparison.Ordinal))
            {
                item.CurrentText = result.FinalText;
            }
        }
    }

    private void RefreshStateProperties()
    {
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(CandidateListTitle));
        OnPropertyChanged(nameof(PendingCountText));
        OnPropertyChanged(nameof(AcceptedCountText));
        OnPropertyChanged(nameof(RejectedCountText));
        OnPropertyChanged(nameof(EditedCountText));
        OnPropertyChanged(nameof(DetailTitle));
        OnPropertyChanged(nameof(DetailSubtitle));
        OnPropertyChanged(nameof(CurrentPositionText));
        OnPropertyChanged(nameof(FooterText));
        OnPropertyChanged(nameof(CanOperate));
        OnPropertyChanged(nameof(CanApplyManualEdit));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanContinue));
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
