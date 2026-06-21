using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using KoeNote.App.Services.Audio;
using KoeNote.App.Services.Dialogs;
using KoeNote.App.Services.Review;
using KoeNote.App.ViewModels;

namespace KoeNote.App.Dialogs;

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
    private readonly AudioPreviewPlaybackController _playbackController;
    private readonly ReviewCandidateConfirmationListProjection _listProjection = new();
    private readonly ReviewCandidateConfirmationStatePresenter _statePresenter = new();
    private readonly DispatcherTimer _playbackTimer;
    private ReviewCandidateConfirmationPresentationState _state = ReviewCandidateConfirmationPresentationState.Empty;
    private ReviewCandidateConfirmationDialogItem? _selectedItem;
    private string _manualEditText = string.Empty;
    private string _operationErrorText = string.Empty;
    private bool _isDecisionInputBlocked;
    private ReviewCandidateConfirmationFilter _filter = ReviewCandidateConfirmationFilter.Pending;

    public ReviewCandidateConfirmationDialogViewModel(ReviewCandidateConfirmationRequest request)
        : this(request, new ReviewCandidateConfirmationNoOpAudioPlaybackService())
    {
    }

    public ReviewCandidateConfirmationDialogViewModel(
        ReviewCandidateConfirmationRequest request,
        IAudioPlaybackService audioPlaybackService)
    {
        _request = request;
        _playbackController = new AudioPreviewPlaybackController(audioPlaybackService);
        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _playbackTimer.Tick += (_, _) => RefreshPlayback();
        TogglePreviewCommand = new RelayCommand(TogglePreviewAsync, () => CanPlaySelectedPreview);
        TotalCount = request.Candidates.Count;
        LeadText = $"{request.JobTitle} の整文候補を確認してから、話者名確認へ進みます。";
        Items = new ObservableCollection<ReviewCandidateConfirmationDialogItem>(
            request.Candidates.Select(static candidate => new ReviewCandidateConfirmationDialogItem(candidate)));
        DecidedItems = [];
        DisplayItems = [];
        RefreshDisplayItems(Items.FirstOrDefault());
        RefreshStateProperties();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string LeadText { get; }

    public string? AudioPath => _request.AudioPath;

    public int TotalCount { get; }

    public int AcceptedCount { get; private set; }

    public int RejectedCount { get; private set; }

    public int EditedCount { get; private set; }

    public ObservableCollection<ReviewCandidateConfirmationDialogItem> Items { get; }

    public ObservableCollection<ReviewCandidateConfirmationDialogItem> DecidedItems { get; }

    public ObservableCollection<ReviewCandidateConfirmationDialogItem> DisplayItems { get; }

    public ICommand TogglePreviewCommand { get; }

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
            StopPlayback();
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
            RefreshProjectedState();
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

    public int PendingCount => _state.PendingCount;

    public int DecidedCount => _state.DecidedCount;

    public string CandidateListTitle => _state.CandidateListTitle;

    public string PendingCountText => _state.PendingCountText;

    public string DecidedCountText => _state.DecidedCountText;

    public string AcceptedCountText => _state.AcceptedCountText;

    public string RejectedCountText => _state.RejectedCountText;

    public string EditedCountText => _state.EditedCountText;

    public bool IsPendingFilterActive => _state.IsPendingFilterActive;

    public bool IsDecidedFilterActive => _state.IsDecidedFilterActive;

    public bool IsAllFilterActive => _state.IsAllFilterActive;

    public string DetailTitle => _state.DetailTitle;

    public string DetailSubtitle => _state.DetailSubtitle;

    public string CurrentPositionText => _state.CurrentPositionText;

    public string FooterText => _state.FooterText;

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

    public bool CanOperate => _state.CanOperate;

    public bool CanAcceptSelected => _state.CanAcceptSelected;

    public bool CanRejectSelected => _state.CanRejectSelected;

    public bool CanApplyManualEdit => _state.CanApplyManualEdit;

    public bool CanPlaySelectedPreview => _state.CanPlaySelectedPreview;

    public bool IsPreviewPlaying => _state.IsPreviewPlaying;

    public string PlayIcon => _state.PlayIcon;

    public string PreviewPlaybackStatusText => _state.PreviewPlaybackStatusText;

    public double PreviewPlaybackProgressPercent => _state.PreviewPlaybackProgressPercent;

    public bool CanGoPrevious => _state.CanGoPrevious;

    public bool CanGoNext => _state.CanGoNext;

    public bool CanContinue => _state.CanContinue;

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

    public Task TogglePreviewAsync()
    {
        if (SelectedItem?.PlaybackPreview is not { } preview)
        {
            StopPlayback();
            return Task.CompletedTask;
        }

        var isPlaying = _playbackController.Toggle(AudioPath, preview);
        if (isPlaying)
        {
            _playbackTimer.Start();
        }
        else
        {
            _playbackTimer.Stop();
        }

        RefreshPlaybackStateProperties();
        return Task.CompletedTask;
    }

    public void StopPlayback()
    {
        _playbackTimer.Stop();
        _playbackController.Stop();
        RefreshPlaybackStateProperties();
    }

    public void ClosePlayback()
    {
        _playbackTimer.Stop();
        _playbackController.Close();
        RefreshPlaybackStateProperties();
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
        StopPlayback();
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

    private void RefreshPlayback()
    {
        var isPlaying = _playbackController.Refresh();
        if (!isPlaying)
        {
            _playbackTimer.Stop();
        }

        RefreshPlaybackStateProperties();
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
        SelectedItem = _listProjection.Refresh(DisplayItems, Items, DecidedItems, _filter, preferredSelection);
        OnPropertyChanged(nameof(DisplayItems));
    }

    private void RefreshStateProperties()
    {
        RefreshProjectedState();
        foreach (var propertyName in ReviewCandidateConfirmationStatePresenter.StatePropertyNames)
        {
            OnPropertyChanged(propertyName);
        }

        RefreshTogglePreviewCommandState();
    }

    private void RefreshPlaybackStateProperties()
    {
        RefreshProjectedState();
        foreach (var propertyName in ReviewCandidateConfirmationStatePresenter.PlaybackStatePropertyNames)
        {
            OnPropertyChanged(propertyName);
        }

        RefreshTogglePreviewCommandState();
    }

    private void RefreshProjectedState()
    {
        _state = _statePresenter.Create(new ReviewCandidateConfirmationStateInput(
            Items,
            DecidedItems,
            DisplayItems,
            SelectedItem,
            _filter,
            TotalCount,
            AcceptedCount,
            RejectedCount,
            EditedCount,
            IsDecisionInputBlocked,
            ManualEditText,
            _playbackController.CanPlay(AudioPath),
            _playbackController.IsPlaying,
            _playbackController.ProgressPercent));
    }

    private void RefreshTogglePreviewCommandState()
    {
        if (TogglePreviewCommand is RelayCommand command)
        {
            command.RaiseCanExecuteChanged();
        }
    }

    private void RefreshFilterStateProperties()
    {
        RefreshProjectedState();
        foreach (var propertyName in ReviewCandidateConfirmationStatePresenter.FilterStatePropertyNames)
        {
            OnPropertyChanged(propertyName);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed class ReviewCandidateConfirmationNoOpAudioPlaybackService : IAudioPlaybackService
{
    public event EventHandler? PlaybackStateChanged;

    public bool IsPlaying => false;

    public string? CurrentPath => null;

    public TimeSpan Position => TimeSpan.Zero;

    public TimeSpan Duration => TimeSpan.Zero;

    public bool Toggle(string audioPath)
    {
        return false;
    }

    public bool Open(string audioPath)
    {
        return false;
    }

    public void Seek(TimeSpan position)
    {
    }

    public void SetPlaybackRate(double rate)
    {
    }

    public void SetVolume(double volume)
    {
    }

    public void Stop()
    {
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
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

    public AudioPreviewRange? PlaybackPreview => (item.StartSeconds, item.EndSeconds) switch
    {
        (double start, double end) => new AudioPreviewRange(start, end, item.Draft.DraftId),
        _ => null
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
